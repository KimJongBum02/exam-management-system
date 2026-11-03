using System.Windows;
using System.Windows.Input;

// 두 앱의 창 크기 · 자리잡기 · 끌어 옮기기를 한 규칙으로 맞추는 파일입니다.

namespace ExamManager.Shared
{
    public static class UiWindow
    {
        private const double RestoreWidth = 1180;
        private const double RestoreHeight = 720;
        private const double PipMargin = 16;      // PIP 창을 화면 가장자리에서 띄워 두는 간격입니다

        // 주 창(교수 MainWindow · 학생 ExamWindow)의 크기와 첫 자리를 담당합니다.
        // 창 생성자의 InitializeComponent 뒤에 부릅니다.
        public static void ApplyMainWindowSize(Window window)
        {
            window.Width = window.MinWidth = RestoreWidth;
            window.Height = window.MinHeight = RestoreHeight;

            // 최대화로 뜨는 창에는 CenterScreen 이 먹지 않습니다. 복원했을 때 가운데로 오도록 직접 잡아 둡니다.
            var area = SystemParameters.WorkArea;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = area.Left + (area.Width - RestoreWidth) / 2;
            window.Top = area.Top + (area.Height - RestoreHeight) / 2;
            window.WindowState = WindowState.Maximized;
        }

        // 제목줄 없는 작은 창(PIP)을 화면 오른쪽 위에 두고, 화면 밖으로 나가지 않게 하는 일을 담당합니다.
        public static void PinTopRight(Window window)
        {
            window.Loaded += (_, _) =>
            {
                window.Left = SystemParameters.WorkArea.Right - window.ActualWidth - PipMargin;
                window.Top = SystemParameters.WorkArea.Top + PipMargin;
            };

            // 알림 · 채팅 칸을 펼치면 카드가 아래로 자랍니다. 아래쪽에 끌어다 놓은 카드는
            // 그 칸이 화면 밖으로 나가 보이지 않으므로 넘치는 만큼 끌어올립니다.
            window.SizeChanged += (_, _) =>
            {
                double overflow = window.Top + window.ActualHeight - SystemParameters.WorkArea.Bottom;
                if (overflow > 0) window.Top -= overflow;
            };
        }

        // 제목줄 없는 창을 몸통으로 끌어 옮기는 일을 담당합니다. MouseLeftButtonDown 에서 부릅니다.
        public static void Drag(Window window)
        {
            // 떼어진 뒤 들어오면 DragMove 가 예외를 던져 앱이 끝납니다.
            // 이벤트 인자의 ButtonState 는 여기서 늘 Pressed 라 막지 못하므로 지금 눌림 상태를 봅니다.
            if (Mouse.LeftButton == MouseButtonState.Pressed) window.DragMove();
        }
    }
}
