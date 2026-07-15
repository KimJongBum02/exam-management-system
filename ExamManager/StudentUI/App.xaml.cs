using NetworkLib;
using StudentUI.Service;
using StudentUI.ViewModel;
using StudentUI.View.WaitingView;
using System.Configuration;
using System.Data;
using System.Windows;

namespace StudentUI
{
    public partial class App : Application
    {
        private NavigationStore _navigationStore;
        private Window _currentWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 윈도우 전환 시 앱이 종료되지 않도록 명시적 종료 모드 설정
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _navigationStore = new NavigationStore();
            _navigationStore.CurrentViewModelChanged += OnCurrentViewModelChanged;

            var loginViewModel = new LoginViewModel(_navigationStore);
            _navigationStore.CurrentViewModel = loginViewModel;

            // ── 클라이언트 테스트 (임시) ────────────────────
            NetworkLibrary.Initialize();
            var client = new StudentClient();
            client.Connected += (ip, port) => MessageBox.Show($"서버 연결 성공: {ip}:{port}");
            client.Disconnected += reason => MessageBox.Show($"연결 끊김: {reason}");
            client.PacketReceived += (type, payload, len) =>
            {
                if (type == PacketType.LoginResponse)
                    MessageBox.Show("로그인 승인됨!");
            };
            /* 
            bool connected = client.Connect("127.0.0.1", 9000);
            if (connected)
            {
                var loginPayload = new byte[80];
                System.Text.Encoding.UTF8.GetBytes("20220001").CopyTo(loginPayload, 0);
                System.Text.Encoding.UTF8.GetBytes("홍길동").CopyTo(loginPayload, 16);
                client.SendPacket(PacketType.StudentLogin, loginPayload);
            }
            */
        }

        private void OnCurrentViewModelChanged()
        {
            Window nextWindow = null;

            if (_navigationStore.CurrentViewModel is LoginViewModel loginVM)
            {
                nextWindow = new MainWindow()
                {
                    DataContext = loginVM
                };
            }
            else if (_navigationStore.CurrentViewModel is WaitingViewModel waitingVM)
            {
                nextWindow = new WaitingWindow()
                {
                    DataContext = waitingVM
                };
            }
            else if (_navigationStore.CurrentViewModel is StudentExamViewModel examVM)
            {
                nextWindow = new StudentUI.View.StudentExamView.StudentExamWindow()
                {
                    DataContext = examVM
                };
            }

            if (nextWindow != null)
            {
                // 이전 창을 먼저 닫고 새 창을 표시 (AllowsTransparency 창 전환 렌더링 충돌 방지)
                var oldWindow = _currentWindow;
                _currentWindow = nextWindow;

                if (oldWindow != null)
                {
                    // 네비게이션에 의한 닫힘이므로 종료 핸들러를 먼저 떼어낸다
                    oldWindow.Closed -= OnCurrentWindowClosed;
                    oldWindow.Close();
                }

                nextWindow.Closed += OnCurrentWindowClosed;
                nextWindow.Show();
            }
        }

        // 사용자가 현재 창을 직접 닫는 경우(좀비 프로세스를 막기 위함)
        private void OnCurrentWindowClosed(object? sender, EventArgs e)
        {
            Shutdown();
        }
    }
}
