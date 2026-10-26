using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ExamManager.Shared;
using NetworkLib;
using StudentUI.Model;
using StudentUI.Service;

namespace StudentUI.View.StudentExamView
{
    // 자유 상태에서 바탕화면 위에 떠 있는 작은 창. 학번·이름·현재 시각·연결 상태만 보여 준다.
    // 시험(덮개)·통제로 넘어가면 숨겨지고, 자유로 돌아오면 다시 뜬다(StudentExamWindow 가 켜고 끈다).
    public partial class FreePipWindow : Window
    {
        private readonly DispatcherTimer _clock;

        public FreePipWindow(Student student)
        {
            InitializeComponent();

            InfoText.Text = $"{student.StudentName} · {student.StudentNumber}".Trim(' ', '·');

            // 현재 시각은 1초마다 갱신한다.
            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clock.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("HH:mm:ss");
            _clock.Start();
            ClockText.Text = DateTime.Now.ToString("HH:mm:ss");

            UpdateConnection(NetworkService.Instance.IsConnected);
            NetworkService.Instance.Connected += OnConnected;
            NetworkService.Instance.Disconnected += OnDisconnected;

            Closed += (_, _) =>
            {
                _clock.Stop();
                NetworkService.Instance.Connected -= OnConnected;
                NetworkService.Instance.Disconnected -= OnDisconnected;
            };

            // 처음에는 화면 오른쪽 위에 둔다. 겹치면 끌어서 옮기면 된다.
            Loaded += (_, _) =>
            {
                Left = SystemParameters.WorkArea.Right - ActualWidth - 16;
                Top = SystemParameters.WorkArea.Top + 16;
            };
        }

        // 연결 콜백은 네이티브 스레드에서 올라오므로 화면 스레드로 넘긴다.
        private void OnConnected(string ip, int port) => UiThread.Post(() => UpdateConnection(true));
        private void OnDisconnected(DisconnectReason reason) => UiThread.Post(() => UpdateConnection(false));

        private void UpdateConnection(bool connected)
        {
            ConnDot.Fill = new SolidColorBrush(connected ? Color.FromRgb(0x22, 0xC5, 0x5E)   // 초록
                                                         : Color.FromRgb(0xEF, 0x44, 0x44)); // 빨강
            ConnText.Text = connected ? "실시간 연결 중" : "서버 미연결";
        }

        // 제목줄이 없는 창이라 몸통을 끌어서 옮긴다.
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            DragMove();
        }

        // 닫기는 곧 프로그램 종료다(큰 창은 숨어 있어 여기서 끝낸다).
        private void OnClose(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("프로그램을 종료하시겠습니까?", "종료 확인",
                                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                Application.Current.Shutdown();
        }
    }
}
