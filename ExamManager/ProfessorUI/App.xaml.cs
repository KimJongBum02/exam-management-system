using System;
using NetworkLib;
using ProfessorUI.View;
using System.Configuration;
using System.Data;
using System.Windows;
using ProfessorUI.View.MonitoringView;

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
            // 소프트웨어 렌더링(CPU)으로 강제 전환하여 그래픽 깨짐 방지
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            // 1. 상태 저장소 생성 (프로그램 전체에서 딱 1개만 존재)
            Service.NavigationStore navigationStore = new Service.NavigationStore();

            Service.LayoutStore layoutStore = new ProfessorUI.Service.LayoutStore();

            // 처음 프로그램이 켜졌을 때 보여줄 기본 화면 설정 (예: 현황판)
            //navigationStore.CurrentViewModel = new StudentBoardViewModel();

            // 2. 메인 뷰모델 생성 (저장소와 사이드바 뷰모델을 연결)
            ViewModel.MainViewModel mainViewModel = new ViewModel.MainViewModel(navigationStore, layoutStore);

            // 3. 메인 창(MainWindow) 생성 후 데이터(ViewModel) 연결
            MainWindow mainWindow = new MainWindow()
            {
                DataContext = mainViewModel // MainWindow의 데이터는 MainViewModel이 담당한다!
            };

            // ── 서버 시작 및 학생 접속/응답 이벤트를 현황판(StudentStore)에 연동 ──
            // 콜백은 네이티브 스레드에서 올라오므로 UI 스레드로 넘겨 처리한다.
            var network = Service.NetworkService.Instance;

            network.StudentConnected += (sid, studentId, name, ip) =>
                PostToUi(() => Service.StudentStore.Instance.AddOrUpdateConnected(sid, studentId, name, ip));

            network.StudentDisconnected += (sid, studentId, name, reason) =>
                PostToUi(() => Service.StudentStore.Instance.MarkDisconnected(sid));

            network.PacketReceived += (sid, studentId, name, type, payload, len) =>
            {
                if (type == PacketType.ExamStatusUpdate &&
                    ReadStatus(payload, len) == StudentStatus.FileReceived)
                {
                    PostToUi(() => Service.StudentStore.Instance.MarkFileReceived(studentId));
                }
                else if (type == PacketType.CheatingAlert)
                {
                    // 누가 보냈는지는 로그인 때 등록된 세션 정보로 알 수 있으므로 페이로드에서 읽지 않는다.
                    System.Diagnostics.Debug.WriteLine($"[부정행위] {studentId} {name} — {ReadAlertDescription(payload, len)}");
                    PostToUi(() => Service.StudentStore.Instance.MarkCheatingDetected(studentId));
                }
            };

            if (!network.StartServer())
                MessageBox.Show("서버 시작에 실패했습니다. 포트 9000을 확인해 주세요.");
            // ───────────────────────────────────


            // 기존 MainWindow 띄우는 코드 아래에 추가...
            mainWindow.Show();

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

        // 학생이 보낸 상태 갱신 패킷의 payload(4바이트)를 StudentStatus로 해석
        private static StudentStatus ReadStatus(IntPtr payload, uint len)
        {
            if (payload == IntPtr.Zero || len < 4) return StudentStatus.NotConnected;
            return (StudentStatus)(uint)System.Runtime.InteropServices.Marshal.ReadInt32(payload);
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

        // 프로그램 종료 시 서버를 멈추고 네이티브 리소스를 정리한다.
        // (이 정리를 하지 않으면 종료 중 네이티브 콜백이 CLR로 들어와 오류가 발생한다)
        protected override void OnExit(ExitEventArgs e)
        {
            _shuttingDown = true;
            try { Service.NetworkService.Instance.Dispose(); } catch { }
            base.OnExit(e);
        }
    }

}
