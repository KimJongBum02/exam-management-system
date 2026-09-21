using System.Windows;
using System.Windows.Controls;
using ProfessorUI.Service;
using ProfessorUI.Common;

namespace ProfessorUI.View.Professor
{
    public partial class ExamEndWindow : UserControl
    {
        private readonly UiContext _ctx = UiContext.Instance;

        public ExamEndWindow()
        {
            InitializeComponent();

            DataContext = _ctx;
            CollectTable.ItemsSource = _ctx.Overview.Students;

            // 걷은 답안이 어디에 쌓이는지 교수가 바로 찾아갈 수 있게 경로를 적어 둔다.
            CollectFolderText.Text = $"저장 위치: {AnswerCollectService.CollectFolder}";
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e) => SetAll(true);
        private void DeselectAll_Click(object sender, RoutedEventArgs e) => SetAll(false);

        // 승인할 수 없는 학생(미수집·이미 승인)은 전체 선택에서도 빠진다.
        private void SetAll(bool selected)
        {
            foreach (var student in _ctx.Overview.Students)
                student.IsSelected = selected && student.CanApprove;
        }

        private void ShowSummary_Click(object sender, RoutedEventArgs e)
            => MainWindow.From(this)?.Navigate(new ExamResultWindow(), 3);
    }
}
