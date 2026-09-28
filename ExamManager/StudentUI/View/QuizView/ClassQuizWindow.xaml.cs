using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using StudentUI.Model;
using StudentUI.Service;

namespace StudentUI.View.QuizView
{
    // 교수가 낸 수업 확인 퀴즈를 푸는 창. CBT 문제풀이 화면처럼 왼쪽에 문제와 보기, 오른쪽에 답안 표기란을 둔다.
    //
    // 답은 고를 때마다 바꿀 수 있고, [답안 제출]을 눌러야 교수에게 간다. 제출은 한 번뿐이다.
    // 수업 중에 쓰는 창이라 최소화·최대화·닫기를 모두 둔다. 내지 않고 닫으면 미제출로 남는다.
    public partial class ClassQuizWindow : Window
    {
        private readonly string _quizId;
        private readonly List<ClassQuizItem> _items;
        private bool _submitted;
        private bool _closeWithoutAsking;

        public ClassQuizWindow(string quizId, List<(string Question, string[] Options)> questions)
        {
            InitializeComponent();

            _quizId = quizId;
            _items = questions.Select((q, i) => new ClassQuizItem(i + 1, q.Question, q.Options)).ToList();
            foreach (var item in _items) item.PropertyChanged += (_, _) => UpdateCounts();

            QuestionList.ItemsSource = _items;
            AnswerSheet.ItemsSource = _items;
            UpdateCounts();
        }

        // 새 퀴즈가 와서 이 창을 치울 때 쓴다. 묻지 않고 닫는다.
        public void CloseWithoutAsking()
        {
            _closeWithoutAsking = true;
            Close();
        }

        private void Choice_Click(object sender, RoutedEventArgs e)
        {
            if (!_submitted && (sender as FrameworkElement)?.DataContext is ClassQuizChoice choice)
                choice.Owner.Choose(choice);
        }

        // 답안 표기란에서 고르면 왼쪽도 그 문제로 옮겨 보여 준다.
        private void SheetChoice_Click(object sender, RoutedEventArgs e)
        {
            if (_submitted || (sender as FrameworkElement)?.DataContext is not ClassQuizChoice choice) return;
            choice.Owner.Choose(choice);
            ShowQuestion(choice.Owner);
        }

        private void Unanswered_Click(object sender, RoutedEventArgs e)
        {
            var first = _items.FirstOrDefault(i => !i.IsAnswered);
            if (first != null) ShowQuestion(first);
        }

        private void ShowQuestion(ClassQuizItem item)
        {
            if (QuestionList.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement container)
                container.BringIntoView();
        }

        private void UpdateCounts()
        {
            int remain = _items.Count(i => !i.IsAnswered);
            TotalText.Text = $"전체 문제 수 : {_items.Count}";
            RemainText.Text = $"안 푼 문제 수 : {remain}";
            UnansweredButton.IsEnabled = !_submitted && remain > 0;
        }

        private void Submit_Click(object sender, RoutedEventArgs e)
        {
            if (_submitted) return;

            int remain = _items.Count(i => !i.IsAnswered);
            string message = remain > 0
                ? $"안 푼 문제가 {remain}개 있습니다.\n제출하면 답을 바꿀 수 없습니다. 이대로 제출하시겠습니까?"
                : "제출하면 답을 바꿀 수 없습니다. 제출하시겠습니까?";
            if (MessageBox.Show(this, message, "답안 제출", MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes)
                return;

            if (!QuizService.Instance.SubmitClassQuiz(_quizId, _items.Select(i => i.Answer)))
            {
                // 답은 그대로 두어 연결이 돌아오면 다시 누를 수 있게 한다.
                MessageBox.Show(this, "교수 PC 와 연결이 끊겨 제출하지 못했습니다. 잠시 뒤 다시 눌러 주십시오.",
                                "답안 제출", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _submitted = true;
            QuestionList.IsEnabled = false;
            AnswerSheet.IsEnabled = false;
            SubmitButton.IsEnabled = false;
            SubmitButton.Content = "제출 완료";
            StatusText.Text = "제출했습니다. 창을 닫아도 됩니다.";
            UpdateCounts();
        }

        // 내지 않고 닫으려 하면 한 번 묻는다. 수업 중이라 막지는 않는다.
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_submitted && !_closeWithoutAsking)
            {
                var answer = MessageBox.Show(this,
                    "아직 답안을 제출하지 않았습니다.\n창을 닫으면 이 퀴즈는 미제출로 처리됩니다. 닫으시겠습니까?",
                    "수업 확인 퀴즈", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes) e.Cancel = true;
            }
            base.OnClosing(e);
        }
    }
}
