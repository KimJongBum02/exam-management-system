using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace ProfessorUI.Common
{
    // 창 제목줄을 우리가 그린 것으로 바꾼다.
    //
    // WindowStyle="None" 으로 제목줄을 떼어 내면 크기 조절·화면 가장자리에 붙이기(스냅)·
    // 더블클릭 최대화가 모두 사라져 하나하나 직접 만들어야 한다.
    // WindowChrome 은 제목줄의 '그림'만 우리에게 넘기고 창을 옮기고 키우는 일은 윈도가 그대로 맡으므로
    // 이쪽을 쓴다. (학생 UI 의 작은 창들은 크기를 바꾸지 않아 WindowStyle="None" 으로 되어 있다)
    //
    // 제목줄 안에 둔 버튼에는 WindowChrome.IsHitTestVisibleInChrome="True" 를 줘야 눌린다 —
    // 그러지 않으면 그 자리가 '끌어서 창 옮기기' 영역이라 눌림이 먹히지 않는다.
    public static class AppChrome
    {
        // 창에 붙이면 최소화·최대화·닫기 버튼이 쓰는 명령을 이어 주고,
        // 둥근 모서리·테두리·최대화 여백을 맞춰 준다.
        // 제목줄 모양은 Styles.xaml 의 AppWindow 스타일이 맡는다.
        public static readonly DependencyProperty ManagedProperty =
            DependencyProperty.RegisterAttached(
                "Managed", typeof(bool), typeof(AppChrome), new PropertyMetadata(false, OnManagedChanged));

        public static void SetManaged(DependencyObject target, bool value) => target.SetValue(ManagedProperty, value);
        public static bool GetManaged(DependencyObject target) => (bool)target.GetValue(ManagedProperty);

        // ── 최대화했을 때만 들어가는 여백 ──
        //
        // 윈도는 창을 최대화할 때 테두리 두께만큼 일부러 화면 밖으로 넘겨 잡는다.
        // 제목줄이 윈도 것일 때는 윈도가 알아서 그 안쪽에 그려 주지만, 우리가 직접 그리면
        // 가장자리에 둔 것(창 단추)이 그만큼 잘린다.
        //
        // 그렇다고 늘 여백을 주면 창을 줄였을 때 그 여백이 빈 틈으로 남는다.
        // 그래서 최대화일 때만 넘치는 만큼 넣고, 그 밖에는 0 으로 둔다.
        // 넘침은 화면 배율마다 달라서(100%에서 8, 200%에서 13 픽셀) 값을 박지 않고 그때그때 구한다.
        //
        // 창 바깥쪽 요소가 Margin 으로 받아 쓴다 — MainWindow.xaml·Styles.xaml 참고.
        // (최대화 크기 자체를 작업 영역에 맞추는 WM_GETMINMAXINFO 방법은 WPF WindowChrome 이
        //  값을 덮어써서 먹지 않는다. 2026-10-07 측정으로 확인.)
        public static readonly DependencyProperty MaximizedPaddingProperty =
            DependencyProperty.RegisterAttached(
                "MaximizedPadding", typeof(Thickness), typeof(AppChrome),
                new PropertyMetadata(default(Thickness)));

        public static Thickness GetMaximizedPadding(DependencyObject target)
            => (Thickness)target.GetValue(MaximizedPaddingProperty);

        // ── 창 테두리를 우리가 그릴지 ──
        //
        // 윈도 11 은 창 모서리를 둥글게 그리고 테두리도 직접 그려 준다(아래 ApplySystemChrome).
        // 그쪽이 되면 우리는 0 으로 두고 윈도가 그린 것을 쓴다 — 모서리 곡선을 정확히 따라가기 때문이다.
        // 우리가 WPF 로 그리면 제목줄의 흰 바탕이 둥근 모서리를 덮어 모서리에서 선이 끊긴다
        // (2026-10-07 화면으로 확인).
        //
        // 윈도 10 에서는 두 요청이 모두 실패하므로 그때만 1 로 두고 각진 테두리를 직접 그린다.
        public static readonly DependencyProperty OutlineThicknessProperty =
            DependencyProperty.RegisterAttached(
                "OutlineThickness", typeof(Thickness), typeof(AppChrome),
                new PropertyMetadata(new Thickness(1)));

        public static Thickness GetOutlineThickness(DependencyObject target)
            => (Thickness)target.GetValue(OutlineThicknessProperty);

        private static void OnManagedChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
        {
            if (target is not Window window || e.NewValue is not true) return;

            Bind(window, SystemCommands.MinimizeWindowCommand, () => SystemCommands.MinimizeWindow(window));
            Bind(window, SystemCommands.MaximizeWindowCommand, () => SystemCommands.MaximizeWindow(window));
            Bind(window, SystemCommands.RestoreWindowCommand, () => SystemCommands.RestoreWindow(window));
            Bind(window, SystemCommands.CloseWindowCommand, () => SystemCommands.CloseWindow(window));

            // 창 크기 상태가 바뀔 때마다, 그리고 다른 배율의 모니터로 옮겨갈 때마다 다시 잰다.
            // Loaded 도 있어야 한다 — 처음부터 최대화로 뜨는 창(UiWindow.ApplyMainWindowSize)은
            // 상태가 '바뀐' 적이 없어 StateChanged 가 한 번도 불리지 않는다.
            window.SourceInitialized += (_, _) => ApplySystemChrome(window);
            window.Loaded += (_, _) => { ApplySystemChrome(window); UpdateMaximizedPadding(window); };
            window.StateChanged += (_, _) => UpdateMaximizedPadding(window);
            window.DpiChanged += (_, _) => UpdateMaximizedPadding(window);
        }

        private static void Bind(Window window, RoutedCommand command, Action run)
            => window.CommandBindings.Add(new CommandBinding(command, (_, _) => run()));

        private static void UpdateMaximizedPadding(Window window)
        {
            if (window.WindowState != WindowState.Maximized)
            {
                window.SetValue(MaximizedPaddingProperty, default(Thickness));
                return;
            }

            // GetSystemMetrics 는 실제 화면 픽셀로 주고 WPF 여백은 배율을 뺀 값을 쓰므로 나눠 준다
            var dpi = VisualTreeHelper.GetDpi(window);
            double padded = GetSystemMetrics(SM_CXPADDEDBORDER);
            double x = (GetSystemMetrics(SM_CXSIZEFRAME) + padded) / dpi.DpiScaleX;
            double y = (GetSystemMetrics(SM_CYSIZEFRAME) + padded) / dpi.DpiScaleY;
            window.SetValue(MaximizedPaddingProperty, new Thickness(x, y, x, y));
        }

        // 둥근 모서리와 테두리를 윈도에 맡긴다. 두 번 불러도 괜찮다.
        private static void ApplySystemChrome(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            int round = DWMWCP_ROUND;
            DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

            int color = OutlineColorRef;
            bool windowsDrawsIt =
                DwmSetWindowAttribute(handle, DWMWA_BORDER_COLOR, ref color, sizeof(int)) == 0;
            window.SetValue(OutlineThicknessProperty, new Thickness(windowsDrawsIt ? 0 : 1));
        }

        private const int SM_CXSIZEFRAME = 32;
        private const int SM_CYSIZEFRAME = 33;
        private const int SM_CXPADDEDBORDER = 92;

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;   // 윈도 11 부터
        private const int DWMWA_BORDER_COLOR = 34;               // 윈도 11 부터
        private const int DWMWCP_ROUND = 2;

        // Styles.xaml 의 BorderStrong(#2A2D31)과 같은 색. COLORREF 라 바이트 순서가 0x00BBGGRR 이다
        private const int OutlineColorRef = 0x00312D2A;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
    }
}
