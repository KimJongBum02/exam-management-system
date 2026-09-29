using System.Windows;

namespace ExamManager.Shared
{
    // 두 앱의 주 창(교수 MainWindow, 학생 StudentExamWindow)이 같은 크기 규칙을 쓰도록 한 곳에서 정한다.
    // 처음에는 최대화로 뜨고, 복원하면 화면 가운데에 이 크기로 온다. 이보다 작게 줄일 수는 없다.
    // 크기를 바꿀 때는 여기만 고친다.
    public static class UiWindow
    {
        private const double RestoreWidth = 1180;
        private const double RestoreHeight = 720;

        // 주 창 생성자에서 InitializeComponent 뒤에 부른다.
        public static void ApplyMainWindowSize(Window window)
        {
            window.Width = window.MinWidth = RestoreWidth;
            window.Height = window.MinHeight = RestoreHeight;

            // 처음부터 최대화로 뜨는 창에는 CenterScreen 이 먹지 않아, 복원하면 화면 왼쪽 위로 간다.
            // 복원했을 때 화면 가운데에 오도록 자리를 직접 잡아 둔다.
            var area = SystemParameters.WorkArea;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = area.Left + (area.Width - RestoreWidth) / 2;
            window.Top = area.Top + (area.Height - RestoreHeight) / 2;
            window.WindowState = WindowState.Maximized;
        }
    }
}
