using ExamManager.Shared;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ProfessorUI.Service;
using ProfessorUI.ViewModel;

namespace ProfessorUI.View.Professor
{
    // 학생 PC 를 1:1 로 보고 조작하는 창.
    //
    // 화면·입력은 윈도우 내장 Desktop Sharing 이 다 한다. 이 창은 그 뷰어(RDPViewer, ActiveX)를
    // WindowsFormsHost 에 얹고 학생이 보내 준 접속 문자열로 접속할 뿐이다.
    // 학생 쪽 준비(잠금 해제·공유 세션·방화벽)는 StudentUI 의 RemoteControlService 가 한다.
    //
    // 이 파일의 바깥 진입점(창 이벤트·패킷 알림)은 예외를 밖으로 내보내지 않는다.
    // 교수 앱에는 전역 예외 방어가 없어서, 여기서 새어 나간 예외 하나면 앱이 통째로 죽고
    // 서버가 닫혀 학생 전원이 끊긴다. 원격이 안 되는 것과 수업이 끊기는 것은 무게가 다르다.
    public partial class RemoteControlWindow : Window
    {
        // rdpviewerax.dll 의 RDPViewer. AxHost 는 중괄호 없는 CLSID 문자열을 받는다.
        private const string ViewerClsid = "32be5ed2-5c86-480f-a914-0ff8885a1b3f";

        // CTRL_LEVEL_INTERACTIVE (Windows SDK rdpencomapi.idl). 2 는 보기 전용이다.
        private const int ControlLevelInteractive = 3;

        // 한/영(VK_HANGUL 0x15)·한자(VK_HANJA 0x19)는 보통 키가 아니라 IME 키다(winuser.h 에서
        // VK_KANA·VK_JUNJA 와 같은 묶음). 두 가지 이유로 화면 공유로는 학생 PC 까지 가지 않는다 —
        //   ① IMM32 가 포커스 창의 IME 를 바꾸는 데 이 키를 그 PC 에서 먹어 버린다
        //   ② 전용 키의 스캔코드(한/영 0xF2 · 한자 0xF1)는 RDP 가 나르는 표준 범위 밖이고, 뗌이 없는 키다
        // 그래서 저수준 훅으로 IMM 보다 먼저 가로채 우리 패킷으로 보내고, 키는 삼킨다.
        // 삼키지 않으면 학생 PC 는 그대로인데 교수 PC 의 한글 상태만 몰래 바뀐다.
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
        private const int VkHangul = 0x15, VkHanja = 0x19;

        // 교수는 한 번에 한 명만 조작한다. 두 학생을 동시에 만지면 어느 창에 입력하는지 헷갈린다.
        private static RemoteControlWindow? _current;
        private static string _currentName = string.Empty;
        private static bool _requesting;
        private static string _requestingName = string.Empty;

        private readonly StudentScreenViewModel _item;
        private readonly string _sessionId;
        private readonly string _connectionString;
        private readonly string _password;
        private ViewerHost? _viewer;

        private readonly LowLevelKeyboardProc _keyProc;   // GC 가 수거하지 않도록 필드에 보관한다
        private readonly string _defaultHint;             // 상단바 안내 원문(XAML 한 곳에서만 쓴다)
        private IntPtr _keyHook = IntPtr.Zero;
        private IntPtr _handle = IntPtr.Zero;
        private DispatcherTimer? _hintTimer;

        /// <summary>원격 제어 창이 떠 있는지. 상태 전환 버튼이 먼저 끊어야 할지 판단할 때 쓴다.</summary>
        public static bool IsOpen => _current != null;

        /// <summary>그 학생에게 원격 제어를 요청하고, 답이 오면 창을 띄운다. 화면 스레드에서 호출한다.</summary>
        public static void Start(StudentScreenViewModel item)
        {
            if (_current != null)
            {
                MessageBox.Show($"이미 {_currentName} 학생을 원격 제어하고 있습니다.\n한 번에 한 명만 조작할 수 있습니다.",
                                "원격 제어", MessageBoxButton.OK, MessageBoxImage.Information);
                _current.Activate();
                return;
            }
            if (_requesting)                  // 답을 기다리는 중에 또 누른 경우
            {
                MessageBox.Show($"{_requestingName} 학생 PC 의 응답을 기다리고 있습니다.\n" +
                                "잠시 뒤에 다시 시도해 주세요.",
                                "원격 제어", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!item.IsConnected)
            {
                MessageBox.Show($"{item.DisplayName} 학생 PC 와 연결이 끊어져 있습니다.\n" +
                                "연결이 돌아온 뒤에 다시 시도해 주세요.",
                                "원격 제어", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 학생은 요청을 받으면 키보드·마우스 잠금을 먼저 푼다(그래야 교수 입력이 들어간다).
            // 교수 화면의 잠금 표시도 지금 같이 내린다 — 실제로는 풀렸는데 잠긴 것으로 기억하면,
            // 다음에 [키보드 잠금]을 눌렀을 때 토글이 거꾸로 돌아 해제 명령이 나간다.
            item.IsKeyboardLocked = false;
            ScreenBoardViewModel.Instance.CheckAllLockState();

            var service = RemoteControlService.Instance;
            _requesting = true;
            _requestingName = item.DisplayName;

            void Cleanup()
            {
                _requesting = false;
                _requestingName = string.Empty;
                service.InvitationReceived -= OnInvitation;
                service.RequestTimedOut -= OnTimedOut;
                service.Failed -= OnFailed;
            }

            void OnInvitation(string sessionId, string connectionString, string password)
            {
                if (sessionId != item.SessionId) return;
                Cleanup();

                // 창을 만들다 터져도 교수 앱은 살아 있어야 한다.
                try { new RemoteControlWindow(item, connectionString, password).Show(); }
                catch (Exception ex) { Fail(item.DisplayName, "원격 제어 창을 열지 못했습니다.", ex); }
            }

            void OnTimedOut(string sessionId)
            {
                if (sessionId != item.SessionId) return;
                Cleanup();
                MessageBox.Show($"{item.DisplayName} 학생 PC 가 10초 안에 답하지 않아 원격 제어를 취소했습니다.\n" +
                                "학생 PC 에 남은 원격 제어 상태는 해제했으니 다시 시도해도 됩니다.\n\n" +
                                "계속 실패하면 학생 프로그램이 최신판인지, 연결이 살아 있는지 확인해 주세요.",
                                "원격 제어", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // 학생 쪽이 왜 못 했는지는 교수만 볼 수 있다. 그대로 띄워 준다.
            void OnFailed(string sessionId, string reason)
            {
                if (sessionId != item.SessionId) return;
                Cleanup();
                MessageBox.Show($"{item.DisplayName} 학생 PC 에서 원격 제어를 시작하지 못했습니다.\n\n{reason}",
                                "원격 제어", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            service.InvitationReceived += OnInvitation;
            service.RequestTimedOut += OnTimedOut;
            service.Failed += OnFailed;

            try
            {
                service.Request(item.SessionId);
            }
            catch (Exception ex)
            {
                Cleanup();
                Fail(item.DisplayName, "원격 제어 요청을 보내지 못했습니다.", ex);
            }
        }

        /// <summary>떠 있으면 닫는다. 상태 전환(통제·시험)처럼 원격과 함께 둘 수 없는 조작 앞에서 부른다.</summary>
        public static void CloseIfOpen(string reason)
        {
            if (_current == null) return;

            string name = _currentName;
            try { _current.Close(); }
            catch (Exception ex) { Debug.WriteLine($"[RemoteControl] 창을 닫지 못했습니다: {ex.Message}"); }

            MessageBox.Show($"{name} 학생의 원격 제어를 끊었습니다.\n{reason}",
                            "원격 제어", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private RemoteControlWindow(StudentScreenViewModel item, string connectionString, string password)
        {
            InitializeComponent();

            _keyProc = OnKeyEvent;
            _defaultHint = HintText.Text;

            _item = item;
            _sessionId = item.SessionId;
            _connectionString = connectionString;
            _password = password;

            Title = $"원격 제어 – {item.DisplayName}";
            NameText.Text = $"{item.DisplayName} 학생 PC 를 원격 제어하고 있습니다";
            _current = this;
            _currentName = item.DisplayName;

            // 학생이 끊기면(앱 종료·네트워크 끊김) 이 창은 멈춘 그림만 띄운 채 남는다. 알리고 닫는다.
            _item.PropertyChanged += OnItemChanged;

            // 창이 열린 뒤에도 학생 쪽 실패가 올 수 있다(조작 권한 부여 실패 등).
            // 그때 교수 화면은 '보이는데 안 눌리는' 상태라, 알려 주지 않으면 원인을 알 길이 없다.
            RemoteControlService.Instance.Failed += OnFailed;

            Loaded += (_, _) =>
            {
                Safely("원격 화면을 준비하지 못했습니다", () =>
                {
                    _viewer = new ViewerHost();
                    ViewerArea.Child = _viewer;

                    // ActiveX 는 호스트가 창 핸들을 만든 뒤에야 쓸 수 있다. 배치가 끝난 다음으로 미룬다.
                    Dispatcher.BeginInvoke(new Action(Connect), DispatcherPriority.Background);
                });

                // 화면이 안 떠도 한/영은 걸어 둔다(반대도 마찬가지). 둘은 서로 딸린 일이 아니다.
                Safely("한/영 키 가로채기를 걸지 못했습니다", InstallImeKeyHook);
            };

            Closed += (_, _) =>
            {
                _current = null;
                _item.PropertyChanged -= OnItemChanged;
                RemoteControlService.Instance.Failed -= OnFailed;

                // 훅은 교수 PC 의 모든 키를 지나간다. 창을 닫은 뒤에도 남으면 한/영이
                // 교수 앱 어디서도 안 먹는 상태가 된다. 반드시 뗀다.
                _hintTimer?.Stop();
                _hintTimer = null;
                if (_keyHook != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_keyHook);
                    _keyHook = IntPtr.Zero;
                }

                // 하나가 실패해도 나머지는 해야 한다. 특히 학생 쪽 공유 세션 닫기를 빠뜨리면
                // 그 PC 가 화면을 계속 내어 준 채로 남는다.
                Safely("원격 화면을 끊지 못했습니다", () => Call(_viewer!.Ocx, "Disconnect"));

                // 학생이 이미 끊겼으면 보낼 길이 없다. 그때는 학생 앱이 스스로 정리한다(Disconnected → Release).
                if (!_item.IsConnected) return;

                try { RemoteControlService.Instance.Stop(_sessionId); }
                catch (Exception ex)
                {
                    // 조용히 넘기면 그 PC 는 화면을 계속 내어 주고 "원격 제어 중" 배너도 그대로 남는다.
                    Debug.WriteLine($"[RemoteControl] 종료를 알리지 못했습니다: {ex}");
                    MessageBox.Show($"{_item.DisplayName} 학생 PC 에 종료를 알리지 못했습니다.\n" +
                                    "그 PC 가 원격 제어 상태로 남아 있을 수 있습니다. 학생에게 프로그램을 다시 시작하도록 안내해 주세요.",
                                    "원격 제어", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
        }

        private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(StudentScreenViewModel.IsConnected)) return;
            if (_item.IsConnected) return;

            // StatusText 에 적어도 창이 곧 닫혀 교수는 보지 못한다. 닫고 나서 분명히 알린다.
            Safely("끊긴 학생의 원격 창을 닫지 못했습니다", () =>
            {
                Close();
                MessageBox.Show($"{_item.DisplayName} 학생 PC 와 연결이 끊어져 원격 제어를 멈췄습니다.\n" +
                                "학생 PC 의 전원과 네트워크를 확인해 주세요.",
                                "원격 제어", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }

        // 창이 열린 뒤에 학생 쪽에서 온 실패. 창을 닫고 이유를 그대로 보여 준다.
        private void OnFailed(string sessionId, string reason)
        {
            if (sessionId != _sessionId) return;

            Safely("실패 안내를 띄우지 못했습니다", () =>
            {
                Close();
                MessageBox.Show($"{_item.DisplayName} 학생 PC 의 원격 제어가 중단되었습니다.\n\n{reason}",
                                "원격 제어", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }

        private void Connect()
        {
            try
            {
                object ocx = _viewer!.Ocx;
                // 학생 화면이 1:1 크기로 그려져 창 밖으로 넘친다. 창에 맞춰 줄인다.
                Set(ocx, "SmartSizing", true);
                Call(ocx, "Connect", _connectionString, "교수", _password);
                StatusText.Visibility = Visibility.Collapsed;

                // 뷰어 쪽에서도 조작 권한을 요청해 둔다. 실제 허용은 학생 쪽이 참석자의
                // ControlLevel 을 3으로 올려 주면서 이뤄지고, 못 올리면 학생 쪽이 교수에게 알린다.
                // 접속이 막 시작된 직후라 아직 못 받을 수 있는데, 그건 실패가 아니라 순서 문제다.
                try { Call(ocx, "RequestControl", ControlLevelInteractive); }
                catch (Exception ex) { Debug.WriteLine($"[RemoteControl] 조작 권한 요청은 넘어간다: {(ex.InnerException ?? ex).Message}"); }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"연결하지 못했습니다.\n{(ex.InnerException ?? ex).Message}";
            }
        }

        // ── 한/영 · 한자 ─────────────────────────────────────────────────
        private void InstallImeKeyHook()
        {
            _handle = new WindowInteropHelper(this).Handle;

            IntPtr hMod = GetModuleHandle(null);
            _keyHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyProc, hMod, 0);
            if (_keyHook != IntPtr.Zero) return;

            // 못 걸면 한/영이 조용히 안 먹는 상태가 된다. 대신 쓸 방법까지 적어 준다 —
            // 오른쪽 Alt 는 평범한 스캔코드라 화면 공유만으로도 학생 PC 까지 간다.
            int err = Marshal.GetLastWin32Error();
            Debug.WriteLine($"[RemoteControl] 한/영 훅을 걸지 못했습니다 (Error {err})");
            HintText.Text = "한/영 키 전달을 걸지 못했습니다. 오른쪽 Alt 로 한글·영문을 바꿔 주세요.";
        }

        // 교수 PC 의 모든 키가 이 함수를 지나간다. 여기서 무거운 일을 하면(윈도우 기준 300ms)
        // 운영체제가 훅을 떼어 버리고, 그러면 한/영이 조용히 다시 안 먹는다.
        // 그래서 판단만 하고 전송은 화면 스레드 줄에 세워 둔 뒤 바로 돌려준다.
        private IntPtr OnKeyEvent(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // 이 창이 맨 앞일 때만 가로챈다. 아니면 교수 앱의 다른 입력칸에서 한글을 못 쓰게 된다.
            if (nCode >= 0 && GetForegroundWindow() == _handle)
            {
                int vk = Marshal.ReadInt32(lParam);   // KBDLLHOOKSTRUCT 의 첫 항목이 vkCode 다
                if (vk == VkHangul || vk == VkHanja)
                {
                    // 한/영은 원래 뗌이 없는 키지만, 와도 같이 삼켜야 교수 PC 의 IME 가 바뀌지 않는다.
                    int msg = (int)wParam;
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                        UiThread.Post(() => SendImeKey(vk == VkHanja));

                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(_keyHook, nCode, wParam, lParam);
        }

        private void SendImeKey(bool hanja)
        {
            string name = hanja ? "한자" : "한/영";
            try
            {
                RemoteControlService.Instance.SendImeKey(_sessionId, hanja);
                ShowHint($"{name} 키를 학생 PC 로 보냈습니다.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RemoteControl] {name} 키를 보내지 못했습니다: {ex}");
                ShowHint($"{name} 키를 보내지 못했습니다. 오른쪽 Alt 로 바꿔 보세요.");
            }
        }

        // 한/영을 눌러도 교수 화면에는 아무 변화가 없다 — 바뀌는 것은 학생 PC 의 IME 다.
        // 아무 반응이 없으면 키가 먹은 것인지 알 수 없으므로, 보냈다는 사실을 상단바에 띄운다.
        private void ShowHint(string text)
        {
            HintText.Text = text;

            _hintTimer?.Stop();
            _hintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _hintTimer.Tick += (_, _) =>
            {
                _hintTimer?.Stop();
                _hintTimer = null;
                HintText.Text = _defaultHint;
            };
            _hintTimer.Start();
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        private void Close_Click(object sender, RoutedEventArgs e) => Safely("창을 닫지 못했습니다", Close);

        // 창 이벤트 안에서 터진 예외가 밖으로 나가면 교수 앱이 죽는다. 여기서 끊고 기록만 남긴다.
        private void Safely(string what, Action action)
        {
            try { action(); }
            catch (Exception ex) { Debug.WriteLine($"[RemoteControl] {what}: {(ex.InnerException ?? ex).Message}"); }
        }

        private static void Fail(string studentName, string what, Exception ex)
        {
            Debug.WriteLine($"[RemoteControl] {what} ({studentName}): {ex}");
            MessageBox.Show($"{studentName} 학생을 원격 제어하지 못했습니다.\n{what}\n\n{(ex.InnerException ?? ex).Message}",
                            "원격 제어", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // RDPViewer 를 WinForms 컨트롤로 감싼다. 타입 라이브러리(tlbimp) 없이 CLSID 만으로 얹힌다.
        private class ViewerHost : System.Windows.Forms.AxHost
        {
            public ViewerHost() : base(ViewerClsid) { }
            public object Ocx => GetOcx()
                ?? throw new InvalidOperationException("원격 화면 컨트롤이 아직 준비되지 않았습니다.");
        }

        // COM 은 dynamic 이 아니라 리플렉션으로 부른다(학생 쪽 RemoteControlService 와 같은 이유).
        private static object? Call(object target, string name, params object?[] args)
            => target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args);

        private static void Set(object target, string name, object? value)
            => target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, new[] { value });
    }
}
