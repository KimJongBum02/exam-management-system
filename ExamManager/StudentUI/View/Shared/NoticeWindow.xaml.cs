using System.Windows;
using System.Windows.Input;

namespace StudentUI.View.Shared
{
    // 교수가 보낸 전체 공지를 띄우는 작은 창. OX 퀴즈 창과 같은 모양이다.
    //
    // 시험 화면·대기 화면 어디에 있든 떠야 하므로 별도 창으로 둔다.
    // 퀴즈와 달리 답을 받을 것이 없어 [확인] 하나만 둔다. 누르면 닫힌다.
    public partial class NoticeWindow : Window
    {
        public NoticeWindow(string notice)
        {
            InitializeComponent();
            NoticeText.Text = notice;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e) => Close();

        // 테두리를 잡고 창을 옮길 수 있게 한다. 공지가 화면을 가릴 때를 위한 것이다.
        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}
