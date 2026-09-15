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

            network.StudentConnected += (sid, studentId, name, ip) =>
                PostToUi(() =>
                {
                    Service.StudentStore.Instance.AddOrUpdateConnected(sid, studentId, name, ip);
                    ViewModel.ScreenMonitoringViewModel.Instance.AddStudent(sid, studentId, name, ip);
                });

            network.StudentDisconnected += (sid, studentId, name, reason) =>
                PostToUi(() =>
                {
                    Service.StudentStore.Instance.MarkDisconnected(sid);
                    ViewModel.ScreenMonitoringViewModel.Instance.RemoveStudent(sid);

                    // 시험 중에 끊긴 학생은 교수가 찾아가 봐야 한다. 창이 뒤에 있어도 알 수 있게 한다.
                    // (로그인을 거절한 접속은 학번이 비어 있다 — 학생이 아니므로 알리지 않는다)
                    if (Service.ExamState.IsExamStarted && studentId.Length > 0)
                    {
                        ExamManager.Shared.UiSignal.FlashTaskbar();

                        // 답안을 내지 않고 끊겼으면 보안 경고로도 남긴다.
                        // 학생 프로그램이 꺼진 것이라면 인터넷 차단도 함께 풀려 있다(FirewallGuard).
                        bool submitted = Service.StudentStore.Instance.Students
                            .Any(s => s.StudentId == studentId && s.IsAnswerSubmitted);
                        if (!submitted)
                            Service.AlertStore.Instance.Add(studentId, name,
                                "시험 중 연결 끊김 (답안 미제출)", Service.AlertKind.Security);
                    }
                });

            network.PacketReceived += (sid, studentId, name, type, payload, len) =>
            {
                if (type == PacketType.ExamStatusUpdate &&
                    ExamStatusUpdatePayload.TryDecode(payload, len, out StudentStatus status, out _))
                {
                    if (status == StudentStatus.FileReceived)
                        PostToUi(() => Service.StudentStore.Instance.MarkFileReceived(studentId));

                    // 답안은 받았지만 학생 PC에 시험 파일이 남아 있다는 보고.
                    // 그 자리에 앉는 다음 학생이 앞사람 답안을 보게 되므로 교수가 직접 확인해야 한다.
                    else if (status == StudentStatus.CleanupFailed)
                        PostToUi(() => Service.StudentStore.Instance.MarkCleanupFailed(studentId));

                    // 시험 파일까지 지웠다는 보고. 이게 와야 '완료'로 본다.
                    else if (status == StudentStatus.CleanupSucceeded)
                        PostToUi(() => Service.StudentStore.Instance.MarkCleanupSucceeded(studentId));
                }
                else if (type == PacketType.MonitorStatusReport &&
                         MonitorStatusPayload.TryDecode(payload, len, out MonitorFlags flags, out string monitorDetail))
                {
                    // 답안 제출 뒤 네트워크 차단을 풀었다는 보고. 정상 절차라 경고로 올리지 않는다.
                    // (아래 감시 상태 보고로 읽으면 두 감시가 모두 꺼진 것으로 보여 잘못된 경고가 뜬다)
                    if (flags.HasFlag(MonitorFlags.NetworkReleased)) return;

                    // 감시가 실제로 켜졌는지 학생이 알려 온다.
                    // 이게 없으면 교수는 감시가 도는 줄 알고 시험을 진행하게 된다.
                    bool processOn = flags.HasFlag(MonitorFlags.ProcessMonitor);
                    bool networkOn = flags.HasFlag(MonitorFlags.NetworkMonitor);

                    PostToUi(() =>
                    {
                        Service.StudentStore.Instance.MarkMonitorStatus(studentId, processOn, networkOn, monitorDetail);

                        // 감시가 걸리지 않은 학생은 보안 경고로 올린다. 학생 표에는 감시 칸이 없다.
                        // 실패 이유는 학생이 차단 실패 때만 적어 보낸다.
                        if (!networkOn)
                            Service.AlertStore.Instance.Add(studentId, name,
                                monitorDetail.Length > 0 ? $"인터넷 차단 실패: {monitorDetail}" : "인터넷 차단 실패",
                                Service.AlertKind.Security);
                        if (!processOn)
                            Service.AlertStore.Instance.Add(studentId, name,
                                "프로그램 감시를 켜지 못함", Service.AlertKind.Security);
                    });
                }
                else if (type == PacketType.InstalledProgramsReport &&
                         InstalledProgramsPayload.TryDecode(payload, len, out var programs))
                {
                    // 강의실 PC 에 설치된 프로그램 목록. 프로그램 선택창의 후보로 쓰도록 모아 둔다.
                    // 화면에 묶인 데이터가 아니고 파일 저장이 끼어 있어, 화면 스레드로 넘기지 않고 여기서 처리한다.
                    Service.ClassroomPrograms.Merge(programs);
                }
                else if (type == PacketType.CheatingAlert)
                {
                    // 누가 보냈는지는 로그인 때 등록된 세션 정보로 알 수 있으므로 페이로드에서 읽지 않는다.
                    // 학생이 보낸 문구는 실행 파일 이름 기준이라, 아는 프로그램은 사람이 부르는 이름으로 바꿔 보여 준다.
                    string description = FriendlyAlert(ReadAlertDescription(payload, len));

                    // 허용 프로그램 종료는 참고용이다. 빌드를 마쳤거나 할 일을 끝내 닫았을 수 있어
                    // 알림 목록에만 남기고 학생을 '부정행위 감지'로 바꾸지 않는다.
                    // 금지 프로그램 실행 등 나머지는 그대로 부정행위로 표시한다.
                    bool isReference = ReadAlertType(payload, len) == CheatingAlertType.RequiredProcessTerminated;

                    PostToUi(() =>
                    {
                        if (!isReference)
                            Service.StudentStore.Instance.MarkCheatingDetected(studentId);
                        Service.AlertStore.Instance.Add(studentId, name, description,
                            isReference ? Service.AlertKind.Reference : Service.AlertKind.Cheating);
                    });
                }
                else if (type == PacketType.ScreenCapture && len > 0)
                {
                    // 학생이 보낸 화면 캡처 JPEG → 모니터링 뷰모델에 전달
                    byte[] jpeg = new byte[len];
                    System.Runtime.InteropServices.Marshal.Copy(payload, jpeg, 0, (int)len);
                    PostToUi(() => ViewModel.ScreenMonitoringViewModel.Instance.UpdateScreen(studentId, jpeg));
                }
            };

            // OX 퀴즈 응답 구독 시작 — 학생이 보낸 O/X 를 받아 기록한다.
            // 수업 중에도 쓰는 기능이라 시험 단계와 무관하게 앱 시작 때 켜 둔다.
            Service.QuizSessionService.Instance.Start();

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
            // 여닫는 일은 Service.ServerControl 이 맡는다.
            if (!Service.ServerControl.Start())
                MessageBox.Show("서버를 열지 못했습니다. 포트 9000을 다른 프로그램이 쓰고 있는지 확인해 주세요.",
                                "서버 열기 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            // ───────────────────────────────────


            // 교수 화면은 새 UI(ShellWindow)를 쓴다.
            // 옛 화면은 View/_Legacy 로 옮겨 두었고 더 이상 띄우지 않는다.
            new View.Professor.ShellWindow().Show();
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


        // 부정행위 알림 패킷에서 설명 문구만 꺼낸다.
        // CheatingAlertPayload = [studentId 16][studentName 64][alertType 4][description 256]
        private static string ReadAlertDescription(IntPtr payload, uint len)
        {
            const int DescriptionOffset = 84;
            if (payload == IntPtr.Zero || len <= DescriptionOffset) return "(내용 없음)";

            // 남은 길이만큼만 읽어 버퍼 밖으로 나가지 않도록 하고, 빈 칸(널)은 잘라낸다.
            int available = (int)len - DescriptionOffset;
            string text = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(
                              payload + DescriptionOffset, available) ?? "";
            return text.Split('\0')[0];
        }

        // 알림 문구 속 실행 파일 이름을 사람이 부르는 이름으로 바꾼다.
        // 예: "금지된 프로그램 실행: Windows Command Processor (cmd.exe)" → "금지된 프로그램 실행: 명령 프롬프트".
        // 아는 프로그램(KnownPrograms)일 때만 바꾸고, 없으면 학생이 보낸 문구를 그대로 둔다.
        // 사이트 접속 시도(도메인)처럼 실행 파일 이름이 없는 알림은 그대로 지나간다.
        private static string FriendlyAlert(string description)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                description, @"[^\s():]+\.exe", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success) return description;

            string? friendly = ExamManager.Shared.KnownPrograms.DisplayNameFor(match.Value);
            if (friendly == null) return description;

            // "…: <프로그램명>" 에서 이름 부분만 친숙한 이름으로 갈아 끼운다.
            int separator = description.LastIndexOf(": ", System.StringComparison.Ordinal);
            return separator < 0 ? friendly : description.Substring(0, separator + 2) + friendly;
        }

        // 부정행위 알림 패킷에서 종류를 꺼낸다. 길이가 모자라면 null.
        // CheatingAlertPayload = [studentId 16][studentName 64][alertType 4][description 256]
        private static CheatingAlertType? ReadAlertType(IntPtr payload, uint len)
        {
            const int AlertTypeOffset = 80;
            if (payload == IntPtr.Zero || len < AlertTypeOffset + 4) return null;

            return (CheatingAlertType)(uint)System.Runtime.InteropServices.Marshal.ReadInt32(payload + AlertTypeOffset);
        }

        // 프로그램 종료 시 서버를 멈추고 네이티브 리소스를 정리한다.
        // (이 정리를 하지 않으면 종료 중 네이티브 콜백이 CLR로 들어와 오류가 발생한다)
        protected override void OnExit(ExitEventArgs e)
        {
            _shuttingDown = true;
            try { Service.ServerControl.Stop(); } catch { }
            try { Service.NetworkService.Instance.Dispose(); } catch { }
            base.OnExit(e);
        }
    }

}
