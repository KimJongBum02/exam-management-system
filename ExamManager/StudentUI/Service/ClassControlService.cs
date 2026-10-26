using ExamManager.Shared;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using NetworkLib;

namespace StudentUI.Service
{
    // 교수의 상태 전환 명령(ClassModeChange)에 따라 학생 PC를 세 상태로 바꾼다.
    //   자유(Free)   아무 제한 없음. 로그인 직후 기본값이다.
    //   통제(Locked) 키보드·마우스를 OS 레벨에서 전부 막고 어두운 전체화면 덮개를 띄운다(수업 경청).
    //   시험(Exam)   시험 창(StudentExamWindow)이 스스로 전체화면 덮개가 된다.
    //                여기서는 바탕화면 탈출키(Alt+Tab·Win·Alt+F4 등)만 막고, 그 창의 버튼은 누를 수 있게 둔다.
    //
    // 이 서비스는 '입력 차단'과 '통제 덮개'만 맡는다. 시험 창을 덮개로 바꾸는 일은
    // StudentExamWindow 가 ModeChanged 를 듣고 스스로 한다(창 모양은 그 창이 안다).
    //
    // 기존 KeyboardLockService 는 '개별 학생 키보드 잠금' 토글 전용이라 건드리지 않고 따로 둔다.
    //
    // [테스트용] 한 PC에서 교수·학생 앱을 함께 띄워 시험할 때, 통제·시험으로 들어가면
    // 교수 쪽을 조작하기 어렵다. Ctrl+Esc 로 그 PC만 자유로 되돌린다.
    public class ClassControlService
    {
        public static ClassControlService Instance { get; } = new();

        /// <summary>현재 상태. 로그인 직후 StudentExamWindow 가 초기 모양을 맞추는 데도 쓴다.</summary>
        public ClassMode CurrentMode { get; private set; } = ClassMode.Free;

        /// <summary>상태가 바뀌면 화면 스레드에서 알린다. StudentExamWindow 가 덮개 전환에 쓴다.</summary>
        public event Action<ClassMode>? ModeChanged;

        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL    = 14;
        private const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;

        // 가상 키 코드. 저수준 훅은 좌/우를 구분해 주므로 둘 다 본다.
        private const int VK_CONTROL = 0x11, VK_MENU = 0x12;
        private const int VK_TAB = 0x09, VK_ESCAPE = 0x1B, VK_F4 = 0x73;
        private const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;
        private const int VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3;
        private const int VK_LMENU = 0xA4, VK_RMENU = 0xA5;

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        // GC 가 수거하지 않도록 인스턴스 필드에 대리자를 보관한다.
        private readonly HookProc _kbProc;
        private readonly HookProc _mouseProc;
        private IntPtr _kbHook = IntPtr.Zero;
        private IntPtr _mouseHook = IntPtr.Zero;

        // 통제 상태에서만 띄우는 어두운 덮개. 시험 상태는 시험 창이 덮개가 되므로 이걸 쓰지 않는다.
        private View.Shared.ClassControlOverlayWindow? _overlay;

        // 지금 눌려 있는 키 모음. 조합키(Alt+Tab 등)와 테스트 탈출(Ctrl+Esc) 판정에 쓴다.
        private readonly HashSet<int> _downKeys = new();

        private ClassControlService()
        {
            _kbProc = KeyboardHook;
            _mouseProc = MouseHook;
        }

        public void Start()
        {
            NetworkService.Instance.PacketReceived += OnPacketReceived;
            // 교수 앱이 꺼지거나 연결이 끊기면 그 PC가 잠긴 채 남지 않도록 자유로 되돌린다.
            NetworkService.Instance.Disconnected += _ => ReleaseAll();
        }

        private void OnPacketReceived(PacketType type, IntPtr payload, uint payloadLen)
        {
            if (type != PacketType.ClassModeChange || payloadLen < 1) return;

            var mode = (ClassMode)Marshal.ReadByte(payload);
            // 네이티브 수신 스레드에서 올라오므로 화면 일은 화면 스레드로 넘긴다.
            // 기다리는 Invoke 는 쓰지 않는다(KeyboardLockService 와 같은 데드락 이유).
            UiThread.Post(() => ApplyMode(mode));
        }

        // 화면 스레드에서만 부른다.
        // 창을 띄우고 내리는 사이 메시지가 돌며, 교수가 빠르게 모드를 바꾸면 다음 ApplyMode 가
        // 재진입할 수 있다. 중첩 실행하면 띄우는 중인 창을 다른 분기가 건드려 죽으므로,
        // 재진입분은 쌓아 두고 지금 처리가 끝난 뒤 이어서 적용한다.
        private bool _applying;
        private ClassMode? _pendingMode;

        private void ApplyMode(ClassMode mode)
        {
            if (_applying) { _pendingMode = mode; return; }
            _applying = true;
            try
            {
                while (true)
                {
                    ApplyModeCore(mode);
                    if (_pendingMode is not ClassMode next) break;
                    _pendingMode = null;
                    mode = next;
                }
            }
            finally { _applying = false; }
        }

        // 같은 모드를 다시 받아도 안전하다(설치는 멱등).
        private void ApplyModeCore(ClassMode mode)
        {
            // 시험에 처음 들어가고(직전이 시험이 아니고) 아직 시작 전이면, 저장 유예 카운트다운부터 한다.
            // 유예가 끝나면 FinishExamGrace 가 자유 때 켠 프로그램을 정리하고 진짜 시험 잠금+덮개를 건다.
            if (mode == ClassMode.Exam && CurrentMode != ClassMode.Exam && !ExamStarted())
            {
                BeginExamGrace();
                return;
            }

            CancelExamGrace();   // 유예 중 다른 모드로 바뀌면 유예를 접는다
            CurrentMode = mode;
            switch (mode)
            {
                case ClassMode.Locked:
                    SetKeyboardHook(true);   // 모든 키 차단(훅 콜백이 모드로 분기)
                    SetMouseHook(true);      // 마우스도 전부 차단
                    ShowOverlay();
                    break;

                case ClassMode.Exam:
                    SetKeyboardHook(true);   // 바탕화면 탈출키만 차단
                    SetMouseHook(false);     // 덮개(시험 창) 안 클릭은 허용
                    HideOverlay();
                    break;

                default: // Free
                    SetKeyboardHook(false);
                    SetMouseHook(false);
                    HideOverlay();
                    break;
            }

            ModeChanged?.Invoke(mode);
        }

        // 강제종료·연결끊김·앱종료에서 불린다. 어느 스레드에서 불러도 된다.
        // 앱이 끝나는 중이면 UiThread 가 넘기지 않지만, 훅은 프로세스에 딸린 것이라
        // 프로세스가 사라지면 윈도우가 거둬 간다 — 입력이 잠긴 채 남지는 않는다.
        public void ReleaseAll()
        {
            UiThread.Post(() => ApplyMode(ClassMode.Free));
        }

        // ── 시험 진입 유예(저장 기회 + 프로그램 정리) ──────────────────────────
        // 덮개가 올라가면 Alt+Tab 이 막혀 학생이 자기 앱에 못 가므로, 덮개 전에 유예를 둔다.
        private const int GraceSeconds = 15;
        private DispatcherTimer? _graceTimer;
        private View.Shared.ExamStartCountWindow? _graceWindow;
        private int _graceLeft;

        private static bool ExamStarted()
            => ExamTimeStore.Instance.IsRunning || ExamTimeStore.Instance.IsFinished;

        // 유예 시작: 입력을 풀어 바탕화면을 쓸 수 있게 두고(저장) 카운트다운 배너를 띄운다.
        private void BeginExamGrace()
        {
            if (_graceTimer != null) return;   // 이미 유예 중

            // 유예 동안은 자유처럼 둔다 — 덮개·잠금 없이 PIP 만. 통제에서 넘어왔다면 그 잠금도 푼다.
            CurrentMode = ClassMode.Free;
            SetKeyboardHook(false);
            SetMouseHook(false);
            HideOverlay();
            ModeChanged?.Invoke(ClassMode.Free);

            _graceLeft = GraceSeconds;
            _graceWindow = new View.Shared.ExamStartCountWindow();
            _graceWindow.SetSeconds(_graceLeft);
            _graceWindow.Show();

            _graceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _graceTimer.Tick += OnGraceTick;
            _graceTimer.Start();
        }

        private void OnGraceTick(object? sender, EventArgs e)
        {
            _graceLeft--;
            if (_graceLeft > 0) { _graceWindow?.SetSeconds(_graceLeft); return; }
            FinishExamGrace();
        }

        // 유예 끝: 자유 때 켠 프로그램을 정리하고, 진짜 시험 잠금+덮개를 건다.
        private void FinishExamGrace()
        {
            CancelExamGrace();
            ForegroundCleanup.KillUserWindows();

            // 다시 유예로 빠지지 않도록 먼저 시험으로 바꾼 뒤, 가드 경유로 잠금+덮개를 적용한다.
            CurrentMode = ClassMode.Exam;
            ApplyMode(ClassMode.Exam);
        }

        private void CancelExamGrace()
        {
            if (_graceTimer != null)
            {
                _graceTimer.Stop();
                _graceTimer.Tick -= OnGraceTick;
                _graceTimer = null;
            }
            _graceWindow?.Close();
            _graceWindow = null;
        }

        // 통제 덮개. 만들었다 닫기를 반복하면(투명 창) 간헐적으로 렌더링 크래시가 나므로,
        // 한 번만 만들어 두고 보이기/숨기기만 한다.
        private void ShowOverlay()
        {
            if (_overlay == null)
            {
                _overlay = new View.Shared.ClassControlOverlayWindow();
                _overlay.Show();
            }
            else
            {
                _overlay.Visibility = System.Windows.Visibility.Visible;
            }
        }

        private void HideOverlay()
        {
            if (_overlay != null)
                _overlay.Visibility = System.Windows.Visibility.Hidden;
        }

        private void SetKeyboardHook(bool want)
        {
            if (want && _kbHook == IntPtr.Zero)
                _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, ModuleHandle(), 0);
            else if (!want && _kbHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_kbHook);
                _kbHook = IntPtr.Zero;
                _downKeys.Clear();
            }
        }

        private void SetMouseHook(bool want)
        {
            if (want && _mouseHook == IntPtr.Zero)
                _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, ModuleHandle(), 0);
            else if (!want && _mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
        }

        private static IntPtr ModuleHandle()
        {
            IntPtr hMod = GetModuleHandle(null);
            if (hMod == IntPtr.Zero)
            {
                using var curProcess = Process.GetCurrentProcess();
                using var curModule = curProcess.MainModule;
                hMod = GetModuleHandle(curModule?.ModuleName ?? "");
            }
            return hMod;
        }

        private IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vk = Marshal.ReadInt32(lParam);
                int msg = (int)wParam;
                bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;

                if (isDown) _downKeys.Add(vk);
                else if (msg == WM_KEYUP || msg == WM_SYSKEYUP) _downKeys.Remove(vk);

                // [테스트 탈출] Ctrl+Esc → 이 PC만 자유로 (모드 무관).
                if (isDown && vk == VK_ESCAPE && AnyDown(VK_LCONTROL, VK_RCONTROL, VK_CONTROL))
                {
                    ReleaseAll();
                    return (IntPtr)1;
                }

                // 통제: 모든 키를 막는다.
                if (CurrentMode == ClassMode.Locked)
                    return (IntPtr)1;

                // 시험: 바탕화면으로 빠져나가는 키만 막는다. 나머지는 그대로 통과(창 조작 허용).
                if (CurrentMode == ClassMode.Exam && IsExamEscapeKey(vk, isDown))
                    return (IntPtr)1;
            }
            return CallNextHookEx(_kbHook, nCode, wParam, lParam);
        }

        // 시험 덮개 중 막을 '바탕화면 탈출' 키.
        private bool IsExamEscapeKey(int vk, bool isDown)
        {
            // 시작 메뉴는 Win 키를 뗄 때 열리므로 눌림·뗌 모두 막는다.
            if (vk == VK_LWIN || vk == VK_RWIN) return true;
            if (!isDown) return false;

            bool alt = AnyDown(VK_LMENU, VK_RMENU, VK_MENU);
            bool ctrl = AnyDown(VK_LCONTROL, VK_RCONTROL, VK_CONTROL);
            if (vk == VK_TAB && alt) return true;              // Alt+Tab
            if (vk == VK_ESCAPE && (ctrl || alt)) return true; // Ctrl+Esc(시작), Alt+Esc(창 전환)
            if (vk == VK_F4 && alt) return true;               // Alt+F4(덮개 닫기)
            return false;
        }

        private IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // 통제에서만 설치된다 — 설치돼 있으면 이동·클릭·휠까지 모든 마우스 입력을 막는다.
            if (nCode >= 0) return (IntPtr)1;
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private bool AnyDown(params int[] vks)
        {
            foreach (int vk in vks)
                if (_downKeys.Contains(vk)) return true;
            return false;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
