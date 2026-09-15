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
        private Window _quizWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 방화벽 지킴이로 켜진 경우(FirewallGuard 참고). 창을 띄우지 않고, 학생 앱이 끝나면 차단을 풀고 끝난다.
            // 아래의 시작 청소보다 먼저 봐야 한다 — 지킴이가 켜지자마자 방금 건 차단을 풀면 안 된다.
            if (Service.FirewallGuard.RunIfRequested(e.Args))
            {
                Environment.Exit(0);
                return;
            }

            // 윈도우 전환 시 앱이 종료되지 않도록 명시적 종료 모드 설정
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // 지난 시험이 DNS 를 되돌리지 못하고 끝났으면 지금 되돌린다.
            // 이 안전장치가 없으면 그 PC 는 인터넷이 되지 않는 채로 남는다.
            // (예전 DNS 방식 배포판이 남긴 흔적용. 강의실 PC 가 모두 새 판으로 바뀌면 지운다)
            Service.DnsRedirectService.RestoreIfLeftOver();

            // 방화벽 차단도 마찬가지다. 지난번에 풀지 못하고 끝났으면 켜자마자 푼다.
            // 진행 중이던 시험이 있어도 예외 없이 푼다 — 차단은 같은 학번으로 다시 로그인한 뒤 새로 건다.
            Service.FirewallPolicyService.Restore();
            var examSession = Service.ExamSessionStore.Load();

            RegisterShutdownGuards();

            _navigationStore = new NavigationStore();
            _navigationStore.CurrentViewModelChanged += OnCurrentViewModelChanged;

            var loginViewModel = new LoginViewModel(_navigationStore);
            _navigationStore.CurrentViewModel = loginViewModel;

            // 파일 수신 상태 저장소 구독 시작 (대기 화면에서 도착한 파일도 놓치지 않도록 앱 시작 시점에)
            Service.ExamFileStore.Instance.Start();

            // 프로세스 감시 구독 시작 — 여기서는 구독만 하고,
            // 실제 감시는 교수가 시험 시작을 눌러 압축 해제가 끝난 뒤에 켜진다.
            Service.ExamMonitorService.Instance.Start();

            // 시험 남은 시간 구독 시작 — 교수가 시험 시작을 누르면 세기 시작한다.
            Service.ExamTimeStore.Instance.Start();
            // 답안 제출 구독 시작 — 교수의 수집 요청을 기다린다.
            Service.AnswerSubmitService.Instance.Start();

            // 진행 중이던 시험이 있으면 되살린다. 시험 폴더·암호·남은 시간은 바로,
            // 감시와 차단은 같은 학번으로 다시 로그인한 뒤에 건다(LoginViewModel).
            if (examSession != null)
            {
                Service.ExamFileStore.Instance.Resume(examSession.ArchiveName, examSession.Password, examSession.DeliveredFiles);
                Service.ExamTimeStore.Instance.Resume(examSession.StartedAtUtc, examSession.ExamEnded);
                Service.ExamMonitorService.Instance.ResumeFromSession(examSession);
            }

            // OX 퀴즈 구독 시작 — 교수가 낸 문제를 기다린다.
            // 수업 중 이해도 확인에도 쓰는 기능이라 시험 화면에 묶지 않고 여기서 받는다.
            Service.QuizService.Instance.Start();
            Service.QuizService.Instance.QuestionReceived += ShowQuizWindow;

            // ── 시험 파일을 수신하면 교수 PC로 '수신 완료' 응답을 보낸다 ──
            // 실제 접속/로그인 패킷 전송은 IP 입력 후 LoginViewModel.CompleteLogin 에서 수행한다.
            // 수신 사실은 대기 화면·시험 준비 화면이 ExamFileStore를 통해 표시하므로 별도 알림창은 띄우지 않는다.
            Service.NetworkService.Instance.FileReceived += (tid, senderId, fileName, tempPath, size, pw) =>
            {
                // ExamStatusUpdate 는 276바이트 고정 형식이다([학번 16][status 4][detail 256]).
                // status 를 4바이트만 보내면 교수 쪽 TryDecode 가 길이 검사에서 걸러 버려,
                // 파일을 받았는데도 배포 단계의 '수신' 숫자가 움직이지 않는다.
                byte[] payload = ExamStatusUpdatePayload.Encode(StudentStatus.FileReceived);
                Service.NetworkService.Instance.SendPacket(PacketType.ExamStatusUpdate, payload);
            };

            // ── 서버 연결 시 화면 캡처 시작, 연결 해제 시 정지 ──
            Service.NetworkService.Instance.Connected += (ip, port) =>
                Dispatcher.Invoke(() => Service.ScreenCaptureService.Instance.Start());

            Service.NetworkService.Instance.Disconnected += reason =>
                Service.ScreenCaptureService.Instance.Stop();
        }

        // 새 문제가 오면 앞 문제 창은 닫고 새로 띄운다.
        // 교수가 연달아 낼 수 있어, 창이 쌓이면 학생이 어느 문제에 답하는지 헷갈린다.
        private void ShowQuizWindow(string question)
        {
            _quizWindow?.Close();

            _quizWindow = new View.QuizView.QuizWindow(question);
            _quizWindow.Closed += (_, _) => _quizWindow = null;
            _quizWindow.Show();
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
                    // 화면 전환에 의한 닫힘은 종료 확인창을 띄우지 않도록 표시한다
                    if (oldWindow is StudentUI.View.StudentExamView.StudentExamWindow examWindow)
                        examWindow.IsNavigating = true;
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

        // 어떤 경로로 끝나든 프로세스 감시를 끄고 네트워크 차단을 푼다. 시험 중이어도 예외 없다.
        //
        // 차단이 남으면 그 PC 는 인터넷이 되지 않는다. 강제 종료처럼 이 코드가 아예 돌지 못하는
        // 경우는 지킴이(FirewallGuard)가 푼다. 프로세스 감시는 앱과 함께 사라지므로 따로 챙길 것이 없다.
        // 시험이 끝난 강의실 PC 가 먹통으로 남는 것이 가장 나쁜 결과라 경로마다 막아 둔다.
        //
        // OnExit 하나로는 부족하다. 아래 넷은 OnExit 를 거치지 않거나,
        // 거치더라도 그 전에 프로세스가 사라질 수 있는 경로다.
        private void RegisterShutdownGuards()
        {
            // 윈도우 종료·재시작·로그오프. 창을 닫는 절차를 밟지 않고 앱이 끝난다.
            SessionEnding += (_, _) => RestoreSafely();

            // UI 스레드에서 처리되지 않은 예외. 실제로 가장 자주 밟는 경로다.
            // 일부러 Handled 로 덮지 않는다 — 상태가 깨진 채로 시험을 이어 가는 것보다,
            // 감시와 차단을 정리하고 끝낸 뒤 다시 실행하는 편이 낫다.
            DispatcherUnhandledException += (_, _) => RestoreSafely();

            // 감시 콜백처럼 UI 가 아닌 스레드에서 터진 예외.
            AppDomain.CurrentDomain.UnhandledException += (_, _) => RestoreSafely();

            // Shutdown() 을 거치지 않고 프로세스가 내려가는 경우의 마지막 그물.
            AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreSafely();
        }

        // 여러 번 불려도 안전하다.
        // Dispose 는 이미 정리된 것을 건너뛰고, Restore 는 지울 차단 규칙이 없으면 아무 일도 하지 않는다.
        private static void RestoreSafely()
        {
            try { Service.ExamMonitorService.Instance.Dispose(); } catch { }
        }

        // 프로그램 종료 시 서버 연결을 끊고 네이티브 리소스를 정리한다.
        // (정리하지 않으면 종료 중 네이티브 콜백이 CLR로 들어와 오류가 발생한다)
        protected override void OnExit(ExitEventArgs e)
        {
            // 감시를 먼저 멈춘다. 네트워크를 먼저 닫으면 적발 보고가 갈 곳을 잃는다.
            try { Service.ExamMonitorService.Instance.Dispose(); } catch { }
            try { Service.NetworkService.Instance.Dispose(); } catch { }
            base.OnExit(e);
        }
    }
}
