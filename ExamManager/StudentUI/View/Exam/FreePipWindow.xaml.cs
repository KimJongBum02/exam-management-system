using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ExamManager.Shared;
using NetworkLib;
using StudentUI.Model;
using StudentUI.Service;

namespace StudentUI.View.Exam
{
    // 자유 상태에서 바탕화면 위에 떠 있는 작은 창. 학번·이름·현재 시각·연결 상태와
    // 교수의 알림(공지)·채팅을 보여 준다.
    // 시험(덮개)·통제로 넘어가면 숨겨지고, 자유로 돌아오면 다시 뜬다(ExamWindow 가 켜고 끈다).
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
            UiWindow.PinTopRight(this);
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

        // 제목줄이 없는 창이라 몸통을 끌어서 옮긴다 (떼어진 뒤 들어오는 경우는 UiWindow.Drag 가 가린다).
        // 입력칸·단추·스크롤바를 누른 것은 그쪽이 가져가므로 여기까지 올라오지 않는다.
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            UiWindow.Drag(this);
        }

        // 전원 단추 — 이 PC 를 끈다. 수업이 끝난 학생이 자리에서 바로 끄는 길이다.
        //
        // 프로그램만 끄는 길은 일부러 두지 않았다(사용자 결정). 강의실 PC 라 학생이 감시 프로그램만
        // 내리고 PC 를 쓰는 것을 막는다. 조교가 앱만 내려야 하면 작업 관리자를 쓴다.
        private void OnPowerOff(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("컴퓨터를 종료합니다.\n저장하지 않은 작업은 사라집니다.\n\n종료하시겠습니까?",
                                "컴퓨터 종료", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            // 학생 앱은 관리자 권한으로 돌아(app.manifest) shutdown.exe 를 바로 쓸 수 있다.
            // /f 는 주지 않는다 — 저장하지 않은 문서를 묻지도 않고 닫아 버린다. 막는 앱이 있으면
            // 윈도우가 '그래도 종료' 화면을 띄워 학생이 고르게 둔다.
            // 감시·차단 정리는 윈도우가 보내는 종료 신호를 받아 App.SessionEnding 이 한다.
            try
            {
                using var shutdown = Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 0")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                })!;

                string error = shutdown.StandardError.ReadToEnd();
                shutdown.WaitForExit();
                if (shutdown.ExitCode == 0) return;

                ReportPowerOffFailed(error.Trim().Length > 0 ? error.Trim() : $"종료 명령이 코드 {shutdown.ExitCode} 로 끝났습니다.");
            }
            catch (Exception ex)
            {
                ReportPowerOffFailed(ex.Message);
            }
        }

        // 못 껐으면 반드시 말한다. 아무 말 없이 돌아오면 학생은 눌렀는지도 모른 채 자리를 뜬다.
        private static void ReportPowerOffFailed(string reason)
            => MessageBox.Show($"컴퓨터를 종료하지 못했습니다.\n\n{reason}\n\n시작 메뉴에서 직접 종료해 주세요.",
                               "컴퓨터 종료", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
