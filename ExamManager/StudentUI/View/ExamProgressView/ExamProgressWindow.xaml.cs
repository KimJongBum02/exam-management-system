using System.Windows;
using System.Windows.Input;

namespace StudentUI.View.ExamProgressView
{
    public partial class ExamProgressWindow : Window
    {
        public ExamProgressWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // SizeToContent를 XAML이 아닌 로드 후에 켠다.
            // 투명 창(AllowsTransparency)에서 XAML로 SizeToContent를 주면 첫 렌더가
            // 납작하게(높이 0에 가깝게) 계산되는 WPF 문제가 있어, 렌더 후 켜서 회피한다.
            // 이후 알림/채팅 패널이 펼쳐지면 높이가 자동으로 늘어난다.
            SizeToContent = SizeToContent.Height;

            // 화면 우상단에 배치 (대기 화면과 동일)
            var desktopWorkingArea = SystemParameters.WorkArea;
            this.Left = desktopWorkingArea.Right - this.Width - 10;
            this.Top = desktopWorkingArea.Top + 10;
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 배경 영역에서만 드래그 - 버튼/입력창 클릭 시에는 발동하지 않음
            if (e.OriginalSource is System.Windows.Controls.Border ||
                e.OriginalSource is System.Windows.Controls.TextBlock ||
                e.OriginalSource is System.Windows.Controls.Grid)
            {
                this.DragMove();
            }
        }
    }
}
