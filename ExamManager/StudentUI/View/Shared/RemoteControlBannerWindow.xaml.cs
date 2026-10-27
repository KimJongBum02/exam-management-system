using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace StudentUI.View.Shared
{
    // 원격 제어가 켜져 있는 동안 화면 위쪽에 떠 있는 알림.
    // 클릭을 가로채지 않아(IsHitTestVisible=False) 교수가 그 자리를 조작해도 막히지 않는다.
    public partial class RemoteControlBannerWindow : Window
    {
        private bool _ended;

        public RemoteControlBannerWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
                Top = 30;
            };
        }

        /// <summary>원격 제어가 끝났음을 알리고 잠시 뒤 스스로 닫힌다.</summary>
        public void ShowEnded()
        {
            // 배너가 말없이 사라지면 학생은 원격이 끝난 것인지 알림이 깨진 것인지 알 수 없다.
            // 끝났다는 말을 분명히 남기고 사라진다.
            if (_ended) return;
            _ended = true;

            IconText.Text = "✔";
            TitleText.Text = "원격 제어가 끝났습니다.";
            DetailText.Text = "이제 마우스·키보드를 다시 쓸 수 있습니다.";

            // 파란 '진행 중' 과 한눈에 구별되게 색까지 바꾼다.
            Frame.Background = new SolidColorBrush(Color.FromRgb(0xEC, 0xFD, 0xF5));
            Frame.BorderBrush = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69));
            TitleText.Foreground = new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46));
            DetailText.Foreground = new SolidColorBrush(Color.FromRgb(0x04, 0x78, 0x57));

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Close();
            };
            timer.Start();
        }
    }
}
