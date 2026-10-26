using System.Windows;
using System.Windows.Input;
using StudentUI.Service;

namespace StudentUI.View.StudentExamView
{
    // 시험 화면을 내려놓고 편집기에서 실습을 해도 남은 시간이 보이도록, 항상 위에 뜨는 작은 창.
    // 시험 화면 시간 카드의 작은 창 버튼으로 켜고 끈다.
    public partial class ExamTimerPipWindow : Window
    {
        // submitCommand: 바탕화면에서 바로 답안을 낼 수 있게 메인 시험 창과 같은 제출 명령을 받는다.
        // 없으면(타이머만 쓰는 경우) 제출 버튼을 숨긴다.
        public ExamTimerPipWindow(ICommand? submitCommand = null)
        {
            InitializeComponent();

            // 남은 시간은 화면이 바뀌어도 이어지는 저장소 하나에서 온다.
            DataContext = ExamTimeStore.Instance;

            if (submitCommand != null) SubmitButton.Command = submitCommand;
            else SubmitButton.Visibility = Visibility.Collapsed;

            // 처음에는 화면 오른쪽 위에 둔다. 코딩 화면과 겹치면 끌어서 옮기면 된다.
            Loaded += (_, _) =>
            {
                Left = SystemParameters.WorkArea.Right - ActualWidth - 16;
                Top = SystemParameters.WorkArea.Top + 16;
            };
        }

        // 제목줄이 없는 창이라 몸통을 끌어서 옮긴다.
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            DragMove();
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();
    }
}
