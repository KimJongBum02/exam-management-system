using NetworkLib;
using System.Windows;
using System.Windows.Input;

namespace ProfessorUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private bool _shuttingDown;

        // 화면 예외를 알리는 창은 한 번만 띄운다. 배치·렌더링에서 나는 예외는 화면을 그릴 때마다
        // 되풀이되므로, 그대로 두면 확인창이 끝없이 쌓여 교수가 아무것도 누르지 못한다.
        private bool _crashNoticeShown;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 예외가 나면 이유를 반드시 남긴다. 교수 앱이 말없이 사라지면 서버가 닫혀
            // 학생 전원이 한꺼번에 끊기는데, 무엇 때문이었는지 알 길이 없는 것이 가장 나쁘다.
            // (학생 앱은 전부터 바탕화면 crash.log 를 남겼고, 교수 앱에는 그물이 아예 없었다)
            RegisterCrashLog();

            // 교수 앱은 한 번만 뜬다. 두 번째는 서버를 열기 전에 돌려보낸다.
            // 네이티브가 포트를 독점으로 열게 바꿨으므로(ProfessorServer::Start) 두 번째는
            // 어차피 서버를 열지 못하는데, 그 상태로 화면이 떠 있으면 교수가 어느 창이 진짜인지
            // 알 수 없다. 여기서 아예 막고 이유를 알려 준다.
            if (AlreadyRunning())
            {
                MessageBox.Show("시험 운영 프로그램이 이미 실행 중입니다.\n\n" +
                                "작업 표시줄에서 먼저 켜 둔 창을 찾아 주세요.\n" +
                                "두 번 띄우면 학생 접속을 받는 쪽과 교수가 보는 쪽이 어긋날 수 있습니다.",
                                "이미 실행 중", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown();
                return;
            }
            // 모든 창의 글꼴을 App.xaml 의 AppFont 로 정한다. 창을 만들기 전에 불러야 한다.
            ExamManager.Shared.UiFont.Apply((System.Windows.Media.FontFamily)Resources["AppFont"]);
            // 키보드 포커스 점선 사각형을 앱 전체에서 끈다.
            // 알트탭처럼 키보드를 쓴 뒤 창으로 돌아오면 마지막에 누른 버튼·메뉴·스크롤 영역에 점선이 생긴다.
            // 스타일마다 막으면 빠지는 곳이 생겨, 포커스를 받기 직전에 모든 요소에서 한 번에 끈다(점선은 그 뒤에 그려진다).
            // Loaded 로 걸면 Loaded 처리기가 따로 붙은 요소에만 신호가 와서 스텝 버튼 같은 곳에 점선이 남는다.
            EventManager.RegisterClassHandler(typeof(FrameworkElement), Keyboard.PreviewGotKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler((_, e) =>
                {
                    if (e.NewFocus is FrameworkElement element) element.FocusVisualStyle = null;
                }));
            // 소프트웨어 렌더링(CPU)으로 강제 전환하여 그래픽 깨짐 방지
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            // ── 서버 시작 및 학생 접속/응답 이벤트를 현황판(StudentStore)에 연동 ──
            // 콜백은 네이티브 스레드에서 올라오므로 UI 스레드로 넘겨 처리한다.
            var network = Service.NetworkService.Instance;
            var report = Service.StudentReportService.Instance;

            network.StudentConnected += (sid, studentId, name, ip) =>
                PostToUi(() =>
                {
                    report.StudentConnected(sid, studentId, name, ip);
                    ViewModel.ScreenBoardViewModel.Instance.AddStudent(sid, studentId, name, ip);
                    // 늦게 들어오거나 다시 접속한 학생도 상단에 같은 중요 공지를 보게 한다.
                    Service.ChatService.Instance.SendImportantNoticeTo(sid);

                    // 학생 PC 상태(자유 · 통제 · 시험)도 다시 걸어 준다.
                    // 학생 앱은 연결이 끊기는 순간 잠금과 덮개를 스스로 푼다. 다시 알려 주지 않으면
                    // 랜선이 한 번 흔들린 학생만 자유 상태로 남아 감시 공백이 생긴다(ClassModeState).
                    Service.ClassModeState.SendOnConnect(sid, studentId);
                });

            network.StudentDisconnected += (sid, studentId, name, reason) =>
                PostToUi(() =>
                {
                    // 시험 중에 끊긴 학생은 교수가 찾아가 봐야 한다. 창이 뒤에 있어도 알 수 있게 한다.
                    if (report.StudentDisconnected(sid, studentId, name))
                        ExamManager.Shared.UiSignal.FlashTaskbar();

                    ViewModel.ScreenBoardViewModel.Instance.RemoveStudent(sid);
                });

            network.PacketReceived += (sid, studentId, name, type, payload, len) =>
            {
                if (type == PacketType.ExamStatusUpdate &&
                    ExamStatusUpdatePayload.TryDecode(payload, len, out StudentStatus status, out _))
                {
                    PostToUi(() => report.ExamStatusUpdate(studentId, status));
                }
                else if (type == PacketType.MonitorStatusReport &&
                         MonitorStatusPayload.TryDecode(payload, len, out MonitorFlags flags, out string monitorDetail))
                {
                    PostToUi(() => report.MonitorStatus(studentId, name, flags, monitorDetail));
                }
                else if (type == PacketType.InstalledProgramsReport &&
                         InstalledProgramsPayload.TryDecode(payload, len, out var programs))
                {
                    // 강의실 PC 에 설치된 프로그램 목록. 프로그램 선택창의 후보로 쓰도록 모아 둔다.
                    // 화면에 묶인 데이터가 아니고 파일 저장이 끼어 있어, 화면 스레드로 넘기지 않고 여기서 처리한다.
                    Service.ClassroomProgramStore.Merge(programs);
                }
                else if (type == PacketType.CheatingAlert)
                {
                    // 알림 문구를 만드는 데 페이로드가 필요하므로, 화면 스레드로 넘기기 전에 읽는다.
                    // (수신 버퍼는 콜백이 끝나면 사라진다)
                    PostToUiWithPayload(payload, len,
                        (copied, copiedLen) => report.CheatingAlert(studentId, name, copied, copiedLen));
                }
                else if (type == PacketType.ScreenCapture && len > 0)
                {
                    // 학생이 보낸 화면 캡처 JPEG → 모니터링 뷰모델에 전달
                    byte[] jpeg = new byte[len];
                    System.Runtime.InteropServices.Marshal.Copy(payload, jpeg, 0, (int)len);
                    PostToUi(() => ViewModel.ScreenBoardViewModel.Instance.UpdateScreen(studentId, jpeg));
                }
            };

            // 퀴즈 응답 구독 시작 — 설문의 O/X 와 수업 확인 퀴즈의 답안을 받아 기록한다.
            // 수업 중에도 쓰는 기능이라 시험 단계와 무관하게 앱 시작 때 켜 둔다.
            Service.SurveyService.Instance.Start();
            Service.QuizService.Instance.Start();

            // 원격 제어 구독 시작 — 학생이 보내는 접속 문자열을 받는다.
            Service.RemoteControlService.Instance.Start();

            // 답안 수집 구독 시작 — 학생이 보낸 답안을 저장하고 확인 회신을 보낸다.
            Service.AnswerCollectService.Instance.Start();
            // 먼저 제출한 학생이 있으면 교수가 바로 알 수 있게 작업표시줄도 깜빡인다.
            Service.AnswerCollectService.Instance.AnswerCollected += (studentId, savedPath) =>
                PostToUi(() =>
                {
                    Service.StudentStore.Instance.MarkAnswerSubmitted(studentId);
                    ExamManager.Shared.UiSignal.FlashTaskbar();
                });

            // 진행 중이던 시험을 되살린다(ExamSessionStore).
            // 이게 없으면 시험 도중 교수 프로그램이 꺼졌을 때 단계가 '시험 전'으로 돌아가,
            // 학생들은 시험을 계속 치고 있는데 시험 종료·답안 수집 메뉴가 열리지 않는다.
            // 서버를 열기 전에 되살려, 학생이 다시 붙는 순간 교수 화면이 이미 시험 중이게 한다.
            Service.ExamSessionStore.Start();

            // 서버는 앱을 켜는 순간 열어 둔다.
            // 교수가 따로 열어 줄 것이 없어야 학생이 접속하지 못하는 사고가 생기지 않는다.
            // 여닫는 일은 Service.ServerService 이 맡는다.
            if (!Service.ServerService.Start())
                MessageBox.Show($"서버를 열지 못했습니다. 포트 {Service.ServerService.Port}번을 다른 프로그램이 쓰고 있는지 확인해 주세요.",
                                "서버 열기 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            // ───────────────────────────────────


            // 교수 화면의 주 창. 왼쪽 메뉴로 안쪽 화면만 갈아 끼운다.
            new View.Professor.MainWindow().Show();
        }

        // 프로그램이 도는 동안 들고 있는 표식. 프로세스가 끝나면 윈도우가 거둬 간다.
        // 필드로 둬야 가비지 수집이 중간에 가져가지 않는다.
        private static Mutex? _onlyOne;

        // 이미 떠 있으면 true.
        // Local\ 로 둔다 — 로그인한 사용자 안에서만 본다. 강의실 PC 는 교수 한 사람이 쓰므로
        // 그걸로 충분하고, Global\ 로 두면 다른 사용자로 로그인한 교수가 띄울 수 없게 된다.
        private static bool AlreadyRunning()
        {
            _onlyOne = new Mutex(initiallyOwned: true, @"Local\ExamManager.ProfessorUI", out bool mine);
            return !mine;
        }

        // 처리되지 않은 예외를 바탕화면 crash.log 에 남긴다.
        //
        // 학생 앱과 정책이 일부러 다르다. 학생 앱은 상태가 깨진 채 시험을 이어 가지 않도록 그냥 끝내지만,
        // 교수 앱은 강의실 전체의 서버다. 화면 쪽 예외 하나로 끝내면 치르고 있는 학생 전원이 끊긴다.
        // 서버와 네이티브 수신 스레드는 화면과 따로 도므로, 화면에서 난 예외는 적어 두고 계속 연다.
        private void RegisterCrashLog()
        {
            DispatcherUnhandledException += (_, e) =>
            {
                ExamManager.Shared.CrashLog.Write("화면 스레드", e.Exception);

                // 서버를 닫지 않는다. 교수 화면 일부가 어긋나는 것이 학생 전원 끊김보다 낫다.
                e.Handled = true;

                // 조용히 넘기지는 않는다 — 교수가 모르면 어긋난 화면을 그대로 믿고 시험을 진행한다.
                if (_crashNoticeShown) return;
                _crashNoticeShown = true;
                MessageBox.Show(
                    "화면 처리 중 오류가 발생했습니다.\n학생 접속과 서버는 그대로 열려 있습니다.\n\n" +
                    "화면이 이상해 보이면 시험을 마친 뒤 프로그램을 다시 켜 주시고,\n" +
                    "바탕화면의 crash.log 를 함께 보내 주세요.",
                    "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            };

            // 네이티브 수신 스레드나 배경 작업에서 터진 예외. 여기서는 프로세스가 내려가는 것을 막을 수 없다.
            // 적어 두는 것만으로도 "교수 앱이 그냥 사라졌다"에서 벗어난다.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                ExamManager.Shared.CrashLog.Write("배경 스레드", e.ExceptionObject as Exception);
        }

        // 네이티브 스레드에서 올라온 콜백을 UI 스레드로 안전하게 넘긴다.
        // 종료가 시작되었으면 무시하여 종료 중 Dispatcher 예외를 막는다.
        private void PostToUi(Action action)
        {
            if (_shuttingDown) return;
            ExamManager.Shared.UiThread.Post(action);
        }


        // 수신 버퍼는 콜백이 끝나면 사라진다. 화면 스레드에서 읽어야 하는 패킷은
        // 바이트를 복사해 두고, 그 복사본을 가리키는 포인터로 넘긴다.
        private void PostToUiWithPayload(IntPtr payload, uint len, Action<IntPtr, uint> action)
        {
            if (payload == IntPtr.Zero || len == 0) return;

            byte[] copy = new byte[len];
            System.Runtime.InteropServices.Marshal.Copy(payload, copy, 0, (int)len);

            PostToUi(() =>
            {
                var handle = System.Runtime.InteropServices.GCHandle.Alloc(copy, System.Runtime.InteropServices.GCHandleType.Pinned);
                try { action(handle.AddrOfPinnedObject(), len); }
                finally { handle.Free(); }
            });
        }

        // 프로그램 종료 시 서버를 멈추고 네이티브 리소스를 정리한다.
        // (이 정리를 하지 않으면 종료 중 네이티브 콜백이 CLR로 들어와 오류가 발생한다)
        protected override void OnExit(ExitEventArgs e)
        {
            _shuttingDown = true;
            try { Common.UiContext.Instance.Server.StopChecking(); } catch { }
            // 답안은 마지막 답안이 들어오고 2초 뒤에 저장된다. 그 사이에 꺼져도 남도록 여기서 한 번 더 저장한다.
            try { Service.QuizService.Instance.Save(); } catch { }
            try { Service.ServerService.Stop(); } catch { }
            try { Service.NetworkService.Instance.Dispose(); } catch { }
            base.OnExit(e);
        }
    }

}
