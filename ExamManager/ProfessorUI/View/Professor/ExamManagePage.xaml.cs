using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ProfessorUI.ViewModel;

namespace ProfessorUI.View.Professor
{
    // 시험 진행 중 허브 화면. 경고 상세 / 보안 정책 / 종료 화면으로 갈라진다.
    public partial class ExamManagePage : UserControl
    {
        private readonly UiContext _ctx = UiContext.Instance;
        private readonly ICollectionView _studentView;

        public ExamManagePage()
        {
            InitializeComponent();

            DataContext = _ctx;

            // 검색·필터는 원본 목록을 건드리지 않고 보기만 걸러 낸다.
            _studentView = CollectionViewSource.GetDefaultView(_ctx.Overview.Students);
            _studentView.Filter = MatchesFilter;
            StudentTable.ItemsSource = _studentView;

            AlertList.ItemsSource = _ctx.Overview.Alerts;
            ChatList.ItemsSource = _ctx.Chat.RecentMessages;
        }

        private bool MatchesFilter(object item)
        {
            if (item is not StudentItemViewModel student) return false;

            string keyword = SearchBox?.Text?.Trim() ?? string.Empty;
            if (keyword.Length > 0 &&
                !student.StudentId.Contains(keyword) &&
                !student.Name.Contains(keyword))
                return false;

            string conn = SelectedText(ConnFilter);
            if (conn == "접속 중" && !student.IsConnected) return false;
            if (conn == "미접속" && student.IsConnected) return false;

            string state = SelectedText(StateFilter);
            if (state.Length > 0 && state != "전체" && student.Status != state) return false;

            return true;
        }

        private static string SelectedText(ComboBox? box)
            => (box?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;

        private void Filter_Changed(object sender, RoutedEventArgs e) => _studentView?.Refresh();

        private void AlertDetail_Click(object sender, RoutedEventArgs e)
        {
            // 누른 카드의 경고를 상세 화면으로 넘긴다.
            var alert = (sender as FrameworkElement)?.DataContext as Service.AlertItem;
            ShellWindow.From(this)?.Navigate(new CheatAlertDetailPage(alert), 2);
        }

        private void SecurityPolicy_Click(object sender, RoutedEventArgs e)
            => ShellWindow.From(this)?.Navigate(new SecurityPolicyPage(), 3);

        // 시험 종료 실행. 학생 PC의 감시를 멈추는 신호까지 AnswerCollectViewModel 이 보낸다.
        // 교수가 확인 창에서 취소하면 단계가 그대로이므로 화면도 옮기지 않는다.
        private void EndExam_Click(object sender, RoutedEventArgs e)
        {
            var command = _ctx.AnswerCollect.EndExamCommand;
            if (!command.CanExecute(null)) return;

            command.Execute(null);

            if (Service.ExamState.CurrentPhase >= NetworkLib.ExamPhase.SubmitRequested)
                ShellWindow.From(this)?.Navigate(new ExamEndPage(), 4);
        }
    }
}
