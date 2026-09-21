using System.Linq;
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
            => ShellWindow.From(this)?.Navigate(new ExamEndPage(), 3);

        // 모든 과정이 끝났으니 다음 시험을 위해 상태를 되돌린다.
        // 단계가 Waiting 이 되면서 시험 관리 / 종료 및 정산 메뉴가 다시 잠긴다.
        private void RestartSession_Click(object sender, RoutedEventArgs e)
        {
            // 종료 및 정산은 먼저 낸 학생을 승인하려고 시험 중에도 열린다.
            // 시험 중에 초기화하면 아직 푸는 학생들이 있는데 시험 단계가 대기로 돌아가므로 막는다.
            if (ExamState.CurrentPhase < NetworkLib.ExamPhase.SubmitRequested)
            {
                MessageBox.Show("시험이 아직 진행 중입니다. 시험 관리 화면에서 시험을 종료한 뒤 초기화하십시오.",
                    "새 시험 준비", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 승인이 남아 있으면 초기화하지 않는다.
            // 초기화는 학생별 기록을 모두 지우므로, 승인을 기다리는 학생이 있으면
            // 누가 남았는지 알 길이 사라진다.
            int waiting = _ctx.Overview.Students.Count(s => s.CanApprove);
            if (waiting > 0)
            {
                MessageBox.Show(
                    $"아직 승인하지 않은 학생이 {waiting}명 있습니다.\n" +
                    "아래 표에서 확인하고, 답안 수집 화면에서 모두 승인한 뒤에 초기화하십시오.",
                    "새 시험 준비", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 답안을 내지 않은 학생은 승인 자체가 안 되므로 초기화를 막지는 않는다.
            // 다만 그대로 지워지는 것이라 몇 명이 그런지 미리 알린다.
            int notCollected = _ctx.Overview.Students.Count(s => !s.IsAnswerSubmitted);
            string note = notCollected > 0
                ? $"\n\n답안을 내지 않은 학생 {notCollected}명은 승인할 수 없어 그대로 초기화됩니다."
                : string.Empty;

            var confirm = MessageBox.Show(
                "시험 단계와 학생별 진행 상태를 초기화합니다.\n걷은 답안 파일은 그대로 남습니다." + note +
                "\n계속하시겠습니까?",
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

            // 접속 중인 학생들에게도 시험 초기화(대기 상태)를 알린다.
            NetworkService.Instance.Broadcast(
                NetworkLib.PacketType.ExamPhaseChange,
                NetworkLib.ExamPhasePayload.Encode(NetworkLib.ExamPhase.Waiting, "시험이 초기화되었습니다."));

            ShellWindow.From(this)?.Navigate(new DashboardPage(), 0);
        }
    }
}
