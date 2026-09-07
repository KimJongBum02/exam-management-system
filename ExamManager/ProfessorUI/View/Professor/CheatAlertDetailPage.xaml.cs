using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ProfessorUI.Service;

namespace ProfessorUI.View.Professor
{
    public partial class CheatAlertDetailPage : UserControl
    {
        private readonly AlertItem? _alert;

        // 목록에서 고른 경고를 받는다. 넘어온 것이 없으면 가장 최근 경고를 보여 준다.
        public CheatAlertDetailPage(AlertItem? alert = null)
        {
            InitializeComponent();

            _alert = alert ?? AlertStore.Instance.Alerts.FirstOrDefault();
            DataContext = _alert;

            if (_alert == null)
            {
                AckButton.IsEnabled = false;
                return;
            }

            // 같은 학생이 여러 번 걸렸을 수 있으므로 그 학생 것만 모아 보여 준다.
            HistoryTable.ItemsSource = AlertStore.Instance.Alerts
                .Where(a => a.StudentId == _alert.StudentId)
                .ToList();
        }

        private void Acknowledge_Click(object sender, RoutedEventArgs e)
        {
            if (_alert == null) return;

            _alert.IsAcknowledged = true;
            AckButton.IsEnabled = false;
        }

        private void BackToManage_Click(object sender, RoutedEventArgs e)
            => ShellWindow.From(this)?.Navigate(new ExamManagePage(), 2);
    }
}
