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
    }
}
