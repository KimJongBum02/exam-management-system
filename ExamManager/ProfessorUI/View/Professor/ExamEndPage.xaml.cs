using System.Windows;
using System.Windows.Controls;
using ProfessorUI.Service;

namespace ProfessorUI.View.Professor
{
    public partial class ExamEndPage : UserControl
    {
        private readonly UiContext _ctx = UiContext.Instance;

        public ExamEndPage()
        {
            InitializeComponent();

            DataContext = _ctx;
            CollectTable.ItemsSource = _ctx.Overview.Students;

            // 걷은 답안이 어디에 쌓이는지 교수가 바로 찾아갈 수 있게 경로를 적어 둔다.
            CollectFolderText.Text = $"저장 위치: {AnswerCollectService.CollectFolder}";
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e) => SetAll(true);
        private void DeselectAll_Click(object sender, RoutedEventArgs e) => SetAll(false);

        private void SetAll(bool selected)
        {
            foreach (var student in _ctx.Overview.Students)
                student.IsSelected = selected;
        }

        private void ShowSummary_Click(object sender, RoutedEventArgs e)
            => ShellWindow.From(this)?.Navigate(new EndSummaryPage(), 4);
    }
}
