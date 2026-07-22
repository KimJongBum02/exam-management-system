using System;
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

            // ── 시험 파일을 수신하면 교수 PC로 '수신 완료' 응답을 보낸다 ──
            // 실제 접속/로그인 패킷 전송은 IP 입력 후 LoginViewModel.CompleteLogin 에서 수행한다.
            Service.NetworkService.Instance.FileReceived += (tid, senderId, fileName, tempPath, size, pw) =>
            {
                byte[] payload = BitConverter.GetBytes((uint)StudentStatus.FileReceived);
                Service.NetworkService.Instance.SendPacket(PacketType.ExamStatusUpdate, payload);

                if (!Dispatcher.HasShutdownStarted)
                    Dispatcher.BeginInvoke(() =>
                        MessageBox.Show($"시험 파일을 수신했습니다: {fileName}", "파일 수신 완료"));
            };
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

        // 프로그램 종료 시 서버 연결을 끊고 네이티브 리소스를 정리한다.
        // (정리하지 않으면 종료 중 네이티브 콜백이 CLR로 들어와 오류가 발생한다)
        protected override void OnExit(ExitEventArgs e)
        {
            try { Service.NetworkService.Instance.Dispose(); } catch { }
            base.OnExit(e);
        }
    }
}
