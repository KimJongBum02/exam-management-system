using System.Windows.Controls;
using ProfessorUI.Service;

namespace ProfessorUI.View.Professor
{
    // 화면 제목 왼쪽의 뒤로가기 버튼. 모든 화면이 같은 것을 쓴다.
    //
    // 화면은 메뉴를 옮길 때마다 새로 만들어지고 버려지므로,
    // 화면에 올라와 있는 동안만 알림을 받는다. 그대로 두면 버려진 화면의 버튼까지 계속 붙잡혀 있다.
    public partial class BackButton : UserControl
    {
        public BackButton()
        {
            InitializeComponent();

            Loaded += (_, _) =>
            {
                NavHistory.Changed += ApplyState;
                ApplyState();
            };
            Unloaded += (_, _) => NavHistory.Changed -= ApplyState;
        }

        private void ApplyState() => Button.IsEnabled = NavHistory.CanGoBack;

        private void Back_Click(object sender, System.Windows.RoutedEventArgs e)
            => MainWindow.From(this)?.GoBack();
    }
}
