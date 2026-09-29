using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ProfessorUI.Common;
using ProfessorUI.Model;
using ProfessorUI.Service;
using ProfessorUI.ViewModel;

namespace ProfessorUI.View.Professor
{
    // 수업 확인 퀴즈 — 퀴즈 메뉴(QuizTab)의 한 탭이다.
    //
    // OX·N지선다 문제를 섞어 여러 개 써 두었다가 한 번에 낸다. 학생은 문제 창에서 답을 고르고
    // [답안 제출]을 눌러야 보내며, 교수 PC 가 채점한다.
    // 출제와 답안 기록은 QuizService 가 맡고, 이 화면은 그것을 보여 준다.
    //
    // 결과는 표 하나로 본다 — 학생이 한 줄, 문제가 한 칸이고 맞으면 연한 초록·틀리면 빨강이다.
    // 학생 수십 명에 문제가 여러 개여도 누가 어느 문제에서 막혔는지 한눈에 보이게 하려는 것이다.
    public partial class QuizWindow : UserControl
    {
        private readonly UiContext _ctx = UiContext.Instance;
        private readonly QuizService _session = QuizService.Instance;

        // 결과 표가 지금 보여 주는 퀴즈. 퀴즈가 바뀔 때만 열을 다시 만든다 — 답안이 올 때마다
        // 다시 만들면 가로 스크롤이 처음으로 돌아가 버린다.
        private QuizRound? _shownRound;
        private readonly ObservableCollection<QuizResultRow> _rows = new();

        public QuizWindow()
        {
            InitializeComponent();

            DataContext = _ctx;
            ResultGrid.ItemsSource = _rows;
            // 퀴즈를 내기 전에도 다른 표처럼 머리글이 보이게 칸을 먼저 만든다
            BuildColumns(null);

            _ctx.Quiz.Edited += UpdateAskState;
            _session.Rounds.CollectionChanged += OnRoundsChanged;
            _session.SubmissionReceived += OnSubmissionReceived;
            ServerService.StateChanged += UpdateAskState;

            // 화면은 메뉴를 옮길 때마다 새로 만들어지므로, 떠날 때 구독을 반드시 푼다.
            Unloaded += (_, _) =>
            {
                _ctx.Quiz.Edited -= UpdateAskState;
                _session.Rounds.CollectionChanged -= OnRoundsChanged;
                _session.SubmissionReceived -= OnSubmissionReceived;
                ServerService.StateChanged -= UpdateAskState;

                // 퀴즈 화면을 떠날 때 진행 중이던 퀴즈의 답안까지 파일에 남긴다.
                _session.Save();
            };

            RefreshResults();
            UpdateAskState();
        }

        // ── 문제 작성 ─────────────────────────────────────────

        private void AddOX_Click(object sender, RoutedEventArgs e) => _ctx.Quiz.AddOX();
        private void AddChoice_Click(object sender, RoutedEventArgs e) => _ctx.Quiz.AddChoice();

        private void RemoveQuestion_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is QuizQuestionDraft question)
                _ctx.Quiz.Remove(question);
        }

        private void AddOption_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is QuizQuestionDraft question)
                question.AddOption();
        }

        private void RemoveOption_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not QuizOptionDraft option) return;

            // 보기 줄의 DataContext 는 보기뿐이라, 그 보기를 가진 문제를 찾아 지운다.
            if (FindOwner(option) is { } question) question.RemoveOption(option);
        }

        private QuizQuestionDraft? FindOwner(QuizOptionDraft option)
        {
            foreach (var question in _ctx.Quiz.Questions)
                if (question.Options.Contains(option)) return question;
            return null;
        }

        private void ClearQuestions_Click(object sender, RoutedEventArgs e)
        {
            if (_ctx.Quiz.Questions.Count == 0) return;

            var confirm = MessageBox.Show($"작성 중인 {_ctx.Quiz.Questions.Count}문제를 모두 지웁니다.",
                                          "문제 모두 비우기", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirm == MessageBoxResult.OK) _ctx.Quiz.Clear();
        }

        // ── 출제 ──────────────────────────────────────────────

        private void AskQuiz_Click(object sender, RoutedEventArgs e)
        {
            if (_ctx.Quiz.Problem != null) return;

            if (!_session.Ask(_ctx.Quiz.Snapshot()))
            {
                MessageBox.Show("접속한 학생이 없어 퀴즈를 보내지 못했습니다.", "출제",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 낸 문제는 그대로 남겨 둔다. 같은 퀴즈를 늦게 온 학생에게 다시 내거나 조금 고쳐 낼 때 쓴다.
            RefreshResults();
            UpdateAskState();
        }

        // 출제는 학생이 붙어 있어야 의미가 있다. 시험 중인지 여부는 보지 않는다 —
        // 이 기능의 본래 목적이 수업 중 이해도 확인이기 때문이다.
        private void UpdateAskState()
        {
            bool serverOpen = ServerService.IsRunning;
            int connected = _ctx.Overview.ConnectedCount;
            string? problem = _ctx.Quiz.Problem;

            SendQuizButton.IsEnabled = serverOpen && connected > 0 && problem == null;

            SendHint.Text =
                !serverOpen ? "서버가 닫혀 있습니다. 먼저 서버를 열어 주십시오."
                : connected == 0 ? "접속한 학생이 없습니다."
                : problem ?? $"{_ctx.Quiz.Questions.Count}문제를 접속 중인 학생 {connected}명에게 한 번에 보냅니다.";

            int count = _ctx.Quiz.Questions.Count;
            CountText.Text = $"총 {count}문제";
            EmptyText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── 출제 현황 ─────────────────────────────────────────

        private void OnRoundsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshResults();
        private void OnSubmissionReceived(string studentId) => RefreshResults();

        private void RefreshResults()
        {
            var round = _session.CurrentRound;

            if (round == null)
            {
                RoundLabel.Text = "방금 낸 퀴즈";
                RoundEmpty.Visibility = Visibility.Visible;
                RoundSubmitted.Text = "0";
                RoundMissed.Text = "0";
                RoundAverage.Text = "-";
            }
            else
            {
                string file = round.FilePath.Length > 0 ? $" · 저장: 바탕화면\\퀴즈\\{Path.GetFileName(round.FilePath)}" : "";
                RoundLabel.Text = $"{round.Title} · {round.AskedAt} 출제 · {round.Questions.Count}문제 · 대상 {round.TargetCount}명{file}";
                RoundEmpty.Visibility = Visibility.Collapsed;
                RoundSubmitted.Text = round.SubmittedCount.ToString();
                RoundMissed.Text = round.MissedCount.ToString();
                RoundAverage.Text = round.AverageText;
            }

            ShowRound(round);

            TallyTable.ItemsSource = QuizStudentTally.Build(_session.Rounds);
            TallyHint.Text = _session.Rounds.Count == 0
                ? "이번 세션에서 낸 퀴즈가 없습니다."
                : $"이번 세션에서 낸 퀴즈 {_session.Rounds.Count}번을 합친 것입니다.";
        }

        // 결과 표를 채운다. 퀴즈가 바뀌었으면 열부터 다시 만들고, 같은 퀴즈면 칸과 정답률만 고친다.
        private void ShowRound(QuizRound? round)
        {
            if (!ReferenceEquals(round, _shownRound))
            {
                _shownRound = round;
                _rows.Clear();
                BuildColumns(round);
                if (round != null)
                {
                    var correct = (Brush)FindResource("CorrectCell");
                    var wrong = (Brush)FindResource("WrongCell");
                    foreach (var submission in round.Submissions)
                        _rows.Add(new QuizResultRow(submission, round.Questions, correct, wrong));
                }
            }
            else
            {
                foreach (var row in _rows) row.Update();
            }

            ApplyResultSort();
        }

        // 학번·이름 · 문제마다 한 칸 · 점수·제출 시각. 문제 수가 퀴즈마다 달라 코드에서 만든다.
        private void BuildColumns(QuizRound? round)
        {
            ResultGrid.Columns.Clear();

            ResultGrid.Columns.Add(TextColumn("학번", nameof(QuizResultRow.StudentId), 120));
            ResultGrid.Columns.Add(TextColumn("이름", nameof(QuizResultRow.StudentName), 90));

            if (round != null)
            {
                for (int i = 0; i < round.Questions.Count; i++)
                    ResultGrid.Columns.Add(QuestionColumn(round.Questions[i], i));
            }

            ResultGrid.Columns.Add(TextColumn("점수", nameof(QuizResultRow.ScoreText), 80));

            // 문제가 적으면 표 오른쪽에 빈 머리 칸이 남아 잘린 것처럼 보인다. 마지막 칸이 남는 폭을 채운다.
            var last = TextColumn("제출 시각", nameof(QuizResultRow.SubmittedAt), 90);
            last.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
            last.MinWidth = 90;
            ResultGrid.Columns.Add(last);
        }

        private static DataGridTemplateColumn TextColumn(string header, string path, double width)
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding(path));
            text.SetResourceReference(TextBlock.FontSizeProperty, "FontBody");
            text.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            text.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);

            return new DataGridTemplateColumn
            {
                Header = header,
                Width = width,
                CellTemplate = new DataTemplate { VisualTree = text },
            };
        }

        // 머리에는 문제 번호를 적고, 마우스를 올리면 문제와 정답이 보인다.
        // 칸은 ✓(연한 초록) · ✗(빨강) · −(안 푼 문제) 이고, 마우스를 올리면 학생이 고른 답이 보인다.
        private static DataGridTemplateColumn QuestionColumn(QuizQuestion question, int index)
        {
            var header = new TextBlock
            {
                Text = question.Number.ToString(),
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                ToolTip = $"{question.Number}. {question.Text}\n정답: {question.CorrectText}",
            };

            string cell = $"{nameof(QuizResultRow.Cells)}[{index}]";
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, new Binding($"{cell}.{nameof(QuizResultCell.Fill)}"));
            border.SetBinding(ToolTipProperty, new Binding($"{cell}.{nameof(QuizResultCell.Tip)}"));
            var mark = new FrameworkElementFactory(typeof(TextBlock));
            mark.SetBinding(TextBlock.TextProperty, new Binding($"{cell}.{nameof(QuizResultCell.Mark)}"));
            mark.SetResourceReference(TextBlock.FontSizeProperty, "FontSection");
            mark.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            mark.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            mark.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(mark);

            return new DataGridTemplateColumn
            {
                Header = header,
                Width = 56,
                CellTemplate = new DataTemplate { VisualTree = border },
            };
        }

        // 미제출 → 점수 낮은 순. 교수가 봐야 할 학생이 위로 온다.
        // 답안이 들어오는 동안 줄이 튀는 것이 싫으면 체크를 풀어 학번순으로 둘 수 있다.
        private void ApplyResultSort()
        {
            var view = CollectionViewSource.GetDefaultView(_rows);
            using (view.DeferRefresh())
            {
                view.SortDescriptions.Clear();
                if (SortLowFirst.IsChecked == true)
                    view.SortDescriptions.Add(new SortDescription(nameof(QuizResultRow.SortRank), ListSortDirection.Ascending));
                view.SortDescriptions.Add(new SortDescription(nameof(QuizResultRow.StudentId), ListSortDirection.Ascending));
            }
        }

        // 보기만 하는 표다. 눌러도 줄이 선택되지 않게 바로 푼다.
        private void ResultGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResultGrid.SelectedItems.Count > 0) ResultGrid.UnselectAll();
        }

        private void SortToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (IsLoaded) ApplyResultSort();
        }

        private void ClearSession_Click(object sender, RoutedEventArgs e)
        {
            if (_session.Rounds.Count == 0) return;

            var confirm = MessageBox.Show(
                $"지금까지 낸 퀴즈 {_session.Rounds.Count}번의 답안 기록을 화면에서 비웁니다.\n" +
                "퀴즈마다 저장된 엑셀 파일은 지워지지 않습니다.",
                "퀴즈 기록 비우기", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            if (!_session.ClearSession())
            {
                MessageBox.Show("기록을 파일로 저장하지 못해 비우지 않았습니다.\n" +
                                $"바탕화면의 기록 폴더에 쓸 수 있는지 확인해 주세요.\n\n{QuizService.SessionFolder}",
                                "퀴즈 기록 비우기", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            RefreshResults();
        }
    }

    // 결과 표의 한 줄 (학생 한 명).
    public class QuizResultRow : INotifyPropertyChanged
    {
        private readonly QuizSubmission _submission;
        private readonly IReadOnlyList<QuizQuestion> _questions;
        private readonly Brush _correct;
        private readonly Brush _wrong;

        public QuizResultRow(QuizSubmission submission, IReadOnlyList<QuizQuestion> questions,
                                  Brush correct, Brush wrong)
        {
            _submission = submission;
            _questions = questions;
            _correct = correct;
            _wrong = wrong;
            Update();
        }

        public string StudentId => _submission.StudentId;
        public string StudentName => _submission.StudentName;
        public string ScoreText => _submission.ScoreText;
        public string SubmittedAt => _submission.SubmittedAt;
        public int SortRank => _submission.SortRank;
        public List<QuizResultCell> Cells { get; private set; } = new();

        // 답안이 들어오면 칸을 다시 채운다.
        public void Update()
        {
            Cells = _questions.Select(question => _submission.ResultOf(question) switch
            {
                QuizSubmission.Result.Correct => new QuizResultCell("✓", _correct, $"고른 답: {_submission.AnswerTextOf(question)} (정답)"),
                QuizSubmission.Result.Wrong   => new QuizResultCell("✗", _wrong, $"고른 답: {_submission.AnswerTextOf(question)} (오답)"),
                QuizSubmission.Result.Blank   => new QuizResultCell("−", Brushes.Transparent, "안 푼 문제"),
                _                                  => new QuizResultCell("", Brushes.Transparent, "미제출"),
            }).ToList();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    // 결과 표의 한 칸.
    public record QuizResultCell(string Mark, Brush Fill, string Tip);
}
