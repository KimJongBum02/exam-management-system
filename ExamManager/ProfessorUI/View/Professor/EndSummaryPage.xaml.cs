using System.Windows;
using System.Windows.Controls;
using ProfessorUI.Service;

namespace ProfessorUI.View.Professor
{
    public partial class EndSummaryPage : UserControl
    {
        private readonly UiContext _ctx = UiContext.Instance;

        public EndSummaryPage()
        {
            InitializeComponent();

            DataContext = _ctx;
            EndTable.ItemsSource = _ctx.Overview.Students;
        }

        private void BackToEnd_Click(object sender, RoutedEventArgs e)
            => ShellWindow.From(this)?.Navigate(new ExamEndPage(), 4);

        // 모든 과정이 끝났으니 다음 시험을 위해 상태를 되돌린다.
        // 단계가 Waiting 이 되면서 시험 관리 / 종료 및 정산 메뉴가 다시 잠긴다.
        private void RestartSession_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "시험 단계와 학생별 진행 상태를 초기화합니다.\n걷은 답안 파일은 그대로 남습니다. 계속하시겠습니까?",
                "새 시험 준비", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            foreach (var student in _ctx.Overview.Students)
            {
                student.IsSelected = false;
                student.IsFileReceived = false;
                student.IsAnswerSubmitted = false;
                student.IsApproved = false;
                student.IsCleanupFailed = false;
                student.IsCleanupDone = false;
                student.Status = student.IsConnected ? "대기" : "미접속";
            }

            AlertStore.Instance.Clear();
            FileDeployState.IsFileDistributed = false;
            ExamState.CurrentPhase = NetworkLib.ExamPhase.Waiting;

            ShellWindow.From(this)?.Navigate(new DashboardPage(), 0);
        }
    }
}
