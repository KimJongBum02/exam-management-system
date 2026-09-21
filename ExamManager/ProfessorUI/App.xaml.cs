using System;
using NetworkLib;
using System.Configuration;
using System.Data;
using System.Windows;

namespace ProfessorUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private bool _shuttingDown;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // 키보드 포커스 점선 사각형을 앱 전체에서 끈다.
            // 알트탭처럼 키보드를 쓴 뒤 창으로 돌아오면 마지막에 누른 버튼·메뉴·스크롤 영역에 점선이 생긴다.
            // 스타일마다 막으면 빠지는 곳이 생겨, 화면에 올라오는 모든 요소에서 한 번에 끈다.
            EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((s, _) => ((FrameworkElement)s).FocusVisualStyle = null));
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

            // OX 퀴즈 응답 구독 시작 — 학생이 보낸 O/X 를 받아 기록한다.
            // 수업 중에도 쓰는 기능이라 시험 단계와 무관하게 앱 시작 때 켜 둔다.
            Service.QuizService.Instance.Start();

            // 답안 수집 구독 시작 — 학생이 보낸 답안을 저장하고 확인 회신을 보낸다.
            Service.AnswerCollectService.Instance.Start();
            // 먼저 제출한 학생이 있으면 교수가 바로 알 수 있게 작업표시줄도 깜빡인다.
            Service.AnswerCollectService.Instance.AnswerCollected += (studentId, savedPath) =>
                PostToUi(() =>
                {
                    Service.StudentStore.Instance.MarkAnswerSubmitted(studentId);
                    ExamManager.Shared.UiSignal.FlashTaskbar();
                });

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

        // 네이티브 스레드에서 올라온 콜백을 UI 스레드로 안전하게 넘긴다.
        // 종료가 시작되었으면 무시하여 종료 중 Dispatcher 예외를 막는다.
        private void PostToUi(Action action)
        {
            if (_shuttingDown) return;
            var dispatcher = Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(action);
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
            try { Service.ServerService.Stop(); } catch { }
            try { Service.NetworkService.Instance.Dispose(); } catch { }
            base.OnExit(e);
        }
    }

}
