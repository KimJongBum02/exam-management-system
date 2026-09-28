using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ProfessorUI.Service;
using ProfessorUI.Common;

namespace ProfessorUI.View.Professor
{
    // 설문 — 퀴즈 메뉴(QuizTab)의 한 탭이다. 정답 없이 학생이 O·X 중 무엇을 골랐는지 모은다.
    //
    // 문제는 그 자리에서 써서 바로 낸다. 모아 두지 않으므로 목록도 저장도 없다.
    // 출제와 응답 기록은 SurveyService 가 맡고, 이 화면은 그것을 보여 준다.
    public partial class SurveyWindow : UserControl
    {
        private readonly UiContext _ctx = UiContext.Instance;
        private readonly SurveyService _session = SurveyService.Instance;

        public SurveyWindow()
        {
            InitializeComponent();

            DataContext = _ctx;

            _ctx.Survey.PropertyChanged += OnQuizEdited;
            _session.Rounds.CollectionChanged += (_, _) => RefreshResults();
            _session.ResponseReceived += OnResponseReceived;
            ServerService.StateChanged += UpdateAskState;

            // 화면은 메뉴를 옮길 때마다 새로 만들어지므로, 떠날 때 구독을 반드시 푼다.
            Unloaded += (_, _) =>
            {
                _ctx.Survey.PropertyChanged -= OnQuizEdited;
                _session.ResponseReceived -= OnResponseReceived;
                ServerService.StateChanged -= UpdateAskState;
            };

            RefreshResults();
            UpdateAskState();
        }

        // ── 출제 ──────────────────────────────────────────────

        private void AskQuiz_Click(object sender, RoutedEventArgs e)
        {
            string question = _ctx.Survey.CurrentQuestion?.Trim() ?? string.Empty;
            if (question.Length == 0) return;

            if (!_session.Ask(question))
            {
                MessageBox.Show("접속한 학생이 없어 문제를 보내지 못했습니다.", "출제",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            RefreshResults();
            UpdateAskState();
        }

        // 출제는 학생이 붙어 있어야 의미가 있다. 시험 중인지 여부는 보지 않는다 —
        // 이 기능의 본래 목적이 수업 중 이해도 확인이기 때문이다.
        private void UpdateAskState()
        {
            bool serverOpen = ServerService.IsRunning;
            int connected = _ctx.Overview.ConnectedCount;
            bool hasQuestion = !string.IsNullOrWhiteSpace(_ctx.Survey.CurrentQuestion);

            SendQuizButton.IsEnabled = serverOpen && connected > 0 && hasQuestion;

            SendHint.Text =
                !serverOpen ? "서버가 닫혀 있습니다. 먼저 서버를 열어 주십시오."
                : connected == 0 ? "접속한 학생이 없습니다."
                : !hasQuestion ? "문제 내용을 채워 주십시오."
                : $"접속 중인 학생 {connected}명에게 바로 전달됩니다.";
        }

        private void OnQuizEdited(object? sender, PropertyChangedEventArgs e) => UpdateAskState();

        // ── 출제 현황 ─────────────────────────────────────────

        private void OnResponseReceived(string studentId) => RefreshResults();

        private void RefreshResults()
        {
            var round = _session.CurrentRound;

            if (round == null)
            {
                RoundLabel.Text = "방금 낸 문제";
                RoundQuestion.Text = "아직 낸 문제가 없습니다.";
                RoundO.Text = "0";
                RoundX.Text = "0";
                RoundMissed.Text = "0";
                ResponseTable.ItemsSource = null;
            }
            else
            {
                RoundLabel.Text = $"방금 낸 문제 · {round.AskedAt} 출제 · 대상 {round.TargetCount}명";
                RoundQuestion.Text = round.Question;
                RoundO.Text = round.OCount.ToString();
                RoundX.Text = round.XCount.ToString();
                RoundMissed.Text = round.MissedCount.ToString();

                ResponseTable.ItemsSource = round.Responses;
                ApplyResponseSort();
            }

            TallyTable.ItemsSource = _session.BuildTally();
            TallyHint.Text = _session.Rounds.Count == 0
                ? "이번 세션에서 낸 문제가 없습니다."
                : $"이번 세션에서 낸 {_session.Rounds.Count}문제를 합친 것입니다.";
        }

        // O → X → 미응답 순. 고른 답끼리 모여 명단처럼 보인다.
        // 응답이 들어오는 동안 줄이 튀는 것이 싫으면 체크를 풀어 학번순으로 둘 수 있다.
        private void ApplyResponseSort()
        {
            var view = CollectionViewSource.GetDefaultView(ResponseTable.ItemsSource);
            if (view == null) return;

            using (view.DeferRefresh())
            {
                view.SortDescriptions.Clear();
                if (SortByAnswer.IsChecked == true)
                    view.SortDescriptions.Add(new SortDescription("SortRank", ListSortDirection.Ascending));
                view.SortDescriptions.Add(new SortDescription("StudentId", ListSortDirection.Ascending));
            }
        }

        private void SortToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (ResponseTable?.ItemsSource != null) ApplyResponseSort();
        }

        private void ClearSession_Click(object sender, RoutedEventArgs e)
        {
            if (_session.Rounds.Count == 0) return;

            var confirm = MessageBox.Show(
                $"지금까지 낸 {_session.Rounds.Count}문제의 응답 기록을 화면에서 비웁니다.\n설문은 파일로 남기지 않으므로 비운 기록은 되살릴 수 없습니다.",
                "기록 비우기", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            _session.ClearSession();
            RefreshResults();
        }
    }
}
