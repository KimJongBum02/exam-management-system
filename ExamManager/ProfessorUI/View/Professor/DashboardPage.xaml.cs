using System.Windows;
using System.Windows.Controls;

namespace ProfessorUI.View.Professor
{
    public partial class DashboardPage : UserControl
    {
        public DashboardPage()
        {
            InitializeComponent();
            var ctx = UiContext.Instance;
            DataContext = ctx;
            StudentTable.ItemsSource = ctx.Overview.Students;
            PriorityList.ItemsSource = ctx.Overview.Priorities;
        }

        private void StartWizard_Click(object sender, RoutedEventArgs e)
            => ShellWindow.From(this)?.Navigate(new ExamPrepPage(), 1);

        // 강의실 큰 모니터에 올려 둘 현황판을 띄운다.
        // 교수 창과 별개로 떠서 다른 모니터로 옮길 수 있다.
        private void OpenMonitorBoard_Click(object sender, RoutedEventArgs e)
            => MonitorBoardWindow.ShowSingle(Window.GetWindow(this));
    }
}
