using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NetworkLib;
using ProfessorUI.Service;

namespace ProfessorUI.View.Professor
{
    public partial class SecurityPolicyPage : UserControl
    {
        private readonly UiContext _ctx = UiContext.Instance;

        public SecurityPolicyPage()
        {
            InitializeComponent();

            DataContext = _ctx;
            AlertSummary.ItemsSource = _ctx.Overview.Alerts.Take(3).ToList();

            bool started = ExamState.IsExamStarted;
            PolicyStateText.Text = started ? "시험 진행 중 · 정책 활성" : "시험 전 · 시작 시 함께 전달";

            // 시험 전에는 시험 시작이 목록을 함께 보내므로 따로 보낼 필요가 없다.
            ApplyButton.IsEnabled = started && _ctx.Server.IsRunning;
            ApplyHint.Text = started
                ? "시험 중 목록을 바꾸면 이 버튼을 눌러야 학생 PC에 반영됩니다."
                : "시험 전에는 시험 시작 실행 때 목록이 함께 전달됩니다.";
        }

        // 지금 목록을 접속 중인 학생 전원에게 보낸다.
        // 시험 도중 감시 목록을 갈아 끼우는 유일한 경로다.
        private void ApplyPolicy_Click(object sender, RoutedEventArgs e)
        {
            NetworkService.Instance.Broadcast(
                PacketType.ProcessListUpdate,
                ProcessListPayload.Encode(ProgramControlStore.WhiteList, ProgramControlStore.BlackList));

            MessageBox.Show(
                $"허용 {ProgramControlStore.WhiteList.Count}개 · 금지 {ProgramControlStore.BlackList.Count}개를 학생 PC에 전달했습니다.",
                "보안 정책 적용", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowAllAlerts_Click(object sender, RoutedEventArgs e)
            => ShellWindow.From(this)?.Navigate(new ExamManagePage(), 2);
    }
}
