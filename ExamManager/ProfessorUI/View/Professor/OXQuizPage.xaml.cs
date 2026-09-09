using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ProfessorUI.Service;

namespace ProfessorUI.View.Professor
{
    // OX 퀴즈는 시험 단계와 별개라 메뉴가 항상 열려 있다.
    //
    // 문제는 그 자리에서 써서 바로 낸다. 모아 두지 않으므로 목록도 저장도 없다.
    // 출제와 응답 기록은 QuizSessionService 가 맡고, 이 화면은 그것을 보여 준다.
    public partial class OXQuizPage : UserControl
    {
        private readonly UiContext _ctx = UiContext.Instance;
        private readonly QuizSessionService _session = QuizSessionService.Instance;

        public OXQuizPage()
        {
            InitializeComponent();

            DataContext = _ctx;

            _ctx.Quiz.PropertyChanged += OnQuizEdited;
            _session.Rounds.CollectionChanged += (_, _) => RefreshResults();
            _session.ResponseReceived += OnResponseReceived;
            ServerControl.StateChanged += UpdateAskState;

            // 화면은 메뉴를 옮길 때마다 새로 만들어지므로, 떠날 때 구독을 반드시 푼다.
            Unloaded += (_, _) =>
            {
                _ctx.Quiz.PropertyChanged -= OnQuizEdited;
                _session.ResponseReceived -= OnResponseReceived;
                ServerControl.StateChanged -= UpdateAskState;
            };

            RefreshResults();
            UpdateAskState();
        }

        // ── 출제 ──────────────────────────────────────────────

        private void AskQuiz_Click(object sender, RoutedEventArgs e)
        {
            string question = _ctx.Quiz.CurrentQuestion?.Trim() ?? string.Empty;
            bool? answer = _ctx.Quiz.CurrentAnswer;
            if (question.Length == 0 || !answer.HasValue) return;

            if (!_session.Ask(question, answer.Value))
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
            bool serverOpen = ServerControl.IsRunning;
            int connected = _ctx.Overview.ConnectedCount;
            bool hasQuestion = !string.IsNullOrWhiteSpace(_ctx.Quiz.CurrentQuestion)
                               && _ctx.Quiz.CurrentAnswer.HasValue;

            SendQuizButton.IsEnabled = serverOpen && connected > 0 && hasQuestion;

            SendHint.Text =
                !serverOpen ? "서버가 닫혀 있습니다. 먼저 서버를 열어 주십시오."
                : connected == 0 ? "접속한 학생이 없습니다."
                : !hasQuestion ? "문제 내용과 정답을 모두 채워 주십시오."
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
                RoundAnswer.Text = "-";
                RoundCorrect.Text = "0";
                RoundWrong.Text = "0";
                RoundMissed.Text = "0";
                ResponseTable.ItemsSource = null;
            }
            else
            {
                RoundLabel.Text = $"방금 낸 문제 · {round.AskedAt} 출제 · 대상 {round.TargetCount}명";
                RoundQuestion.Text = round.Question;
                RoundAnswer.Text = round.CorrectAnswerText;
                RoundCorrect.Text = round.CorrectCount.ToString();
                RoundWrong.Text = round.WrongCount.ToString();
                RoundMissed.Text = round.MissedCount.ToString();

                ResponseTable.ItemsSource = round.Responses;
                ApplyResponseSort();
            }

            TallyTable.ItemsSource = _session.BuildTally();
            TallyHint.Text = _session.Rounds.Count == 0
                ? "이번 세션에서 낸 문제가 없습니다."
                : $"이번 세션에서 낸 {_session.Rounds.Count}문제를 합친 것입니다.";
        }

        // 오답 → 미응답 → 정답 순. 교수가 봐야 할 학생이 위로 온다.
        // 응답이 들어오는 동안 줄이 튀는 것이 싫으면 체크를 풀어 학번순으로 둘 수 있다.
        private void ApplyResponseSort()
        {
            var view = CollectionViewSource.GetDefaultView(ResponseTable.ItemsSource);
            if (view == null) return;

            using (view.DeferRefresh())
            {
                view.SortDescriptions.Clear();
                if (SortWrongFirst.IsChecked == true)
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
                $"지금까지 낸 {_session.Rounds.Count}문제의 응답 기록을 화면에서 비웁니다.\n" +
                $"저장된 파일은 지워지지 않습니다.\n\n{QuizSessionService.SessionFolder}",
                "기록 비우기", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            _session.ClearSession();
            RefreshResults();
        }
    }
}
