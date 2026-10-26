using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ExamManager.Shared;
using NetworkLib;
using StudentUI.ViewModel;

namespace StudentUI.View.StudentExamView
{
    public partial class StudentExamWindow : Window
    {
        public StudentExamWindow()
        {
            InitializeComponent();

            // 이 큰 창은 '시험' 상태일 때만 화면에 뜬다(시작 전=덮개, 시작 후=일반 작업 창).
            // 자유·통제에서는 숨어 있고, 자유에서는 작은 PIP 창(FreePipWindow)이 대신 떠 있다.
            // 로그인 직후 큰 창이 번쩍이지 않도록, 보이지 않게(최소화) 시작한다.
            ShowActivated = false;
            WindowState = WindowState.Minimized;
            Width = 1180; Height = 720;

            // 상태 전환(자유·통제·시험) + 시험 시작/종료에 따라 덮개·작업창·숨김·PIP 을 전환한다.
            Service.ClassControlService.Instance.ModeChanged += OnClassModeChanged;
            Closed += (_, _) => Service.ClassControlService.Instance.ModeChanged -= OnClassModeChanged;
            Service.ExamTimeStore.Instance.PropertyChanged += OnExamTimeChanged;
            Closed += (_, _) => Service.ExamTimeStore.Instance.PropertyChanged -= OnExamTimeChanged;
            // 자유 PIP 창도 큰 창이 닫힐 때 함께 정리한다.
            Closed += (_, _) => { _freePip?.Close(); _freePip = null; };
            // 로그인 직후 현재 상태(기본 자유)에 맞춰 화면을 잡는다.
            Loaded += (_, _) => UpdatePresentation();

            // 교수의 채팅·공지가 오면 채팅 버튼을 깜빡인다.
            SharedChatViewModel.Instance.MessageArrived += OnMessageArrived;
            Closed += (_, _) => SharedChatViewModel.Instance.MessageArrived -= OnMessageArrived;

            // 채팅창처럼 새 말이 오면 맨 아래로 따라 내려간다.
            SharedChatViewModel.Instance.Messages.CollectionChanged += OnChatMessagesChanged;
            Closed += (_, _) => SharedChatViewModel.Instance.Messages.CollectionChanged -= OnChatMessagesChanged;

            // 떠 있는 시간 창은 시험 화면에 딸린 것이라 같이 닫는다.
            Closed += (_, _) => { _pip?.Close(); _pip = null; };

            // 창 크기가 바뀔 때(최대화·복원·끌어서 줄이기) 알림·채팅 패널을 놓는 방식을 다시 정한다.
            SizeChanged += (_, _) => PlaceSideDrawer();

            // 채팅 패널을 열면 바로 칠 수 있게 입력칸에 커서를 둔다.
            DataContextChanged += (_, e) =>
            {
                if (e.OldValue is INotifyPropertyChanged oldVm) oldVm.PropertyChanged -= OnViewModelChanged;
                if (e.NewValue is INotifyPropertyChanged newVm) newVm.PropertyChanged += OnViewModelChanged;
            };
            Closed += (_, _) =>
            {
                if (DataContext is INotifyPropertyChanged vm) vm.PropertyChanged -= OnViewModelChanged;
            };
        }

        // 새 말풍선이 쌓이면 맨 아래를 보여 준다. 줄이 그려진 뒤라야 끝까지 내려간다.
        // (Loaded 우선순위라 UiThread.Post 로 바꾸면 안 된다 — 그리기 전에 실행돼 엉뚱한 위치에 멈춘다)
        private void OnChatMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;
            Dispatcher.BeginInvoke(new Action(() => ChatScroll.ScrollToEnd()),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // 남은 시간을 항상 위에 뜨는 작은 창으로 띄운다. 이미 떠 있으면 닫는다.
        private ExamTimerPipWindow? _pip;

        private void OnTogglePip(object sender, RoutedEventArgs e)
        {
            if (_pip is { Visibility: Visibility.Visible }) { _pip.Visibility = Visibility.Hidden; return; }
            ShowExamPip();
        }

        // 타이머 + '답안 제출 · 시험 종료' 플로팅 위젯. 만들었다 닫기를 반복하면(투명 창)
        // 간헐적으로 렌더링 크래시가 날 수 있어, 한 번만 만들고 보이기/숨기기만 한다.
        // 주인 창을 두지 않는다 — 주인을 두면 시험 창을 내렸을 때 이 창도 같이 사라져 뜻이 없다.
        private void ShowExamPip()
        {
            if (_pip == null)
            {
                var submit = (DataContext as StudentExamViewModel)?.SubmitAnswerCommand;
                _pip = new ExamTimerPipWindow(submit);
                _pip.Closed += (_, _) => _pip = null;
                _pip.Show();
            }
            else
            {
                _pip.Visibility = Visibility.Visible;
            }
        }

        private void HideExamPip()
        {
            if (_pip != null) _pip.Visibility = Visibility.Hidden;
        }

        // 패널이 보이게 된 뒤라야 입력칸이 포커스를 받는다. (Loaded 우선순위 — UiThread.Post 로 바꾸면 안 된다)
        private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StudentExamViewModel.IsChatOpen) && sender is StudentExamViewModel { IsChatOpen: true })
                Dispatcher.BeginInvoke(new Action(() => ChatInput.Focus()), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // 알림·채팅 패널이 본문을 밀어도 본문이 이 폭은 남아야 표가 제 모양을 유지하고 세로 스크롤도 생기지 않는다.
        // 이보다 좁으면 표의 설명 칸이 눌려 줄이 늘어나고, 1440 화면에서는 그만큼 본문이 길어져 스크롤이 생긴다.
        // 본문을 가운데 한 줄(MaxWidth 1180)로 모은 뒤의 값 — 1180 + 좌우 여백 32×2.
        private const double MinMainWidthWithDrawer = 1244;

        private static readonly System.Windows.Media.Effects.DropShadowEffect DrawerShadow = new()
        {
            BlurRadius = 24, ShadowDepth = 0, Opacity = 0.18, Color = System.Windows.Media.Colors.Black,
        };

        // 창이 넓으면 패널을 본문 옆에 둔다(본문과 함께 보인다).
        // 좁으면 본문 위에 겹쳐 띄운다. 본문 폭이 그대로라 표 모양이 흐트러지지 않고, 닫으면 가린 부분이 다시 보인다.
        private void PlaceSideDrawer()
        {
            // 창 테두리를 뺀 안쪽 폭으로 따진다
            double inner = (Content as FrameworkElement)?.ActualWidth ?? ActualWidth;
            bool overlay = inner - SideDrawer.Width < MinMainWidthWithDrawer;

            System.Windows.Controls.Grid.SetColumn(SideDrawer, overlay ? 0 : 1);
            SideDrawer.HorizontalAlignment = overlay ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
            System.Windows.Controls.Panel.SetZIndex(SideDrawer, overlay ? 1 : 0);
            SideDrawer.Effect = overlay ? DrawerShadow : null;
        }

        // 채팅 창을 열어 두고 있으면 이미 보이므로 깜빡이지 않는다.
        private void OnMessageArrived()
        {
            if (DataContext is StudentExamViewModel { IsChatOpen: true }) return;
            UiSignal.Blink(ChatButton, BackgroundProperty, UiSignal.MessageSoftColor, times: 4);
        }

        // 화면 전환에 의한 닫힘이면 true — 이때는 종료 확인창을 띄우지 않는다.
        public bool IsNavigating { get; set; }

        // 창을 닫으면 앱이 그대로 종료되고 수신한 시험 파일 상태도 사라지므로,
        // 사용자가 직접 닫을 때만(전환이 아닐 때) 실수 방지 확인을 한 번 한다.
        protected override void OnClosing(CancelEventArgs e)
        {
            if (IsNavigating)
            {
                base.OnClosing(e);
                return;
            }

            var answer = MessageBox.Show(
                "시험 화면을 닫으면 프로그램이 종료됩니다.\n정말 종료하시겠습니까?",
                "종료 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
                e.Cancel = true;

            base.OnClosing(e);
        }

        private FreePipWindow? _freePip;

        private void OnClassModeChanged(ClassMode mode) => UpdatePresentation();

        private void OnExamTimeChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 교수가 '시험 시작'을 누르면(IsRunning) 덮개를 걷어 작업 창으로, 종료(IsFinished)돼도 작업 창을 유지한다.
            if (e.PropertyName is nameof(Service.ExamTimeStore.IsRunning) or nameof(Service.ExamTimeStore.IsFinished))
                UpdatePresentation();
        }

        // 지금 상태 + 시험 진행 여부에 맞춰 큰 창의 모습을 정한다.
        //   자유            → 숨김 + 작은 PIP 창
        //   통제            → 숨김(서비스의 어두운 덮개가 가린다)
        //   시험, 시작 전    → 전체화면 덮개(대기 화면, 파일은 받는다)
        //   시험, 시작 후    → 일반 작업 창(작업표시줄·바탕화면 복귀, 파일 열고 제출)
        private void UpdatePresentation()
        {
            switch (Service.ClassControlService.Instance.CurrentMode)
            {
                case ClassMode.Exam:
                    if (ExamStarted()) PresentExamWorking();
                    else PresentCover();
                    break;
                case ClassMode.Locked:
                    PresentHidden();
                    break;
                default:
                    PresentFree();
                    break;
            }

            // 시험이 실제 진행 중일 때만(시작 후~종료 전) 바탕화면 위에 타이머+제출 위젯을 띄운다.
            // 그러면 학생은 창을 내리고 바탕화면에서 응시하다가 여기서 바로 답안을 낼 수 있다.
            bool wantPip = Service.ClassControlService.Instance.CurrentMode == ClassMode.Exam
                           && Service.ExamTimeStore.Instance.IsRunning;
            if (wantPip) ShowExamPip();
            else HideExamPip();
        }

        // 교수가 '시험 시작'을 누른 뒤인지(진행 중이거나 이미 끝나 제출 대기인지).
        private static bool ExamStarted()
            => Service.ExamTimeStore.Instance.IsRunning || Service.ExamTimeStore.Instance.IsFinished;

        // 자유: 큰 창을 숨기고 작은 PIP 창만 띄운다.
        // 숨어 있는 동안 WindowState 를 Normal 로 정착시켜 둔다 — 덮개로 넘어갈 때
        // Minimized→Normal 복원이 비동기로 걸려 전체화면 크기를 덮어쓰는 것을 막는다.
        private void PresentFree()
        {
            Topmost = false;
            WindowState = WindowState.Normal;
            Visibility = Visibility.Hidden;
            ShowFreePip();
        }

        // 통제: 큰 창도 PIP 도 숨긴다. 화면은 서비스가 띄우는 어두운 덮개가 가린다.
        private void PresentHidden()
        {
            Topmost = false;
            WindowState = WindowState.Normal;
            Visibility = Visibility.Hidden;
            _freePip?.Hide();
        }

        // 시험 덮개로. 테두리를 없애고 모니터 전체 크기로 직접 맞춘 뒤 맨 위로 띄운다.
        // WindowState(최대화·최소화)를 건드리지 않아, 시험↔자유를 여러 번 오가도 상태가 꼬이지 않는다.
        // (예전엔 최대화 토글 + WM_GETMINMAXINFO 를 썼는데, 재진입 때 예외가 나 앱이 통째로 죽었다)
        private void PresentCover()
        {
            _freePip?.Hide();
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Normal;
            ApplyFullScreenBounds();        // 숨김 상태에서 이미 Normal 이면 깜빡임 없이 바로 전체화면
            Topmost = true;
            Visibility = Visibility.Visible;
            ApplyFullScreenBounds();        // 최대화 등에서 온 경우, 보인 뒤 복원 크기를 다시 덮어쓴다
            Activate();
        }

        // 시험 진행: 일반 작업 창으로 되돌린다. 작업표시줄·바탕화면이 보여 학생이 파일을 열어 시험을 치르고,
        // 창을 최소화했다가 다시 띄워 '답안 제출'을 누를 수 있다(기존 시험 흐름 그대로).
        private void PresentExamWorking()
        {
            _freePip?.Hide();
            Topmost = false;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Maximized;
            Visibility = Visibility.Visible;
            Activate();
        }

        // 창을 지금 모니터 전체(작업표시줄 자리 포함)에 딱 맞춘다.
        // 물리 픽셀을 DPI 로 나눠 WPF 단위로 바꾼다(다중 모니터 대응은 테스트 범위 밖).
        private void ApplyFullScreenBounds()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return;

            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref mi)) return;

            double dpiX = 1, dpiY = 1;
            if (PresentationSource.FromVisual(this)?.CompositionTarget is { } ct)
            {
                System.Windows.Media.Matrix m = ct.TransformToDevice;
                dpiX = m.M11;
                dpiY = m.M22;
            }

            Left = mi.rcMonitor.left / dpiX;
            Top = mi.rcMonitor.top / dpiY;
            Width = (mi.rcMonitor.right - mi.rcMonitor.left) / dpiX;
            Height = (mi.rcMonitor.bottom - mi.rcMonitor.top) / dpiY;
        }

        private void ShowFreePip()
        {
            if (_freePip == null)
            {
                if (DataContext is not StudentExamViewModel vm) return;
                _freePip = new FreePipWindow(vm.Student);
                _freePip.Closed += (_, _) => _freePip = null;
            }
            _freePip.Show();
            _freePip.Activate();
        }

        private const int MONITOR_DEFAULTTONEAREST = 0x2;

        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
    }
}
