using StudentUI.Model;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace StudentUI.Service
{
    // 교수 프로그램이 꺼졌다 다시 켜지면 학생 앱이 스스로 다시 붙는다.
    //
    // 연결이 뜻하지 않게 끊기면 몇 초마다 교수 PC 를 다시 찾는다(ProfessorDiscovery).
    // 찾으면 같은 학번·이름으로 다시 로그인하므로 학생은 아무것도 누르지 않아도 된다.
    // 학생이 로그아웃한 경우는 끊긴 것이 아니라 스스로 나간 것이라 다시 붙지 않는다(Disable).
    //
    // 교수 PC 가 연결 끊김을 알아채기 전에 붙으면 "이미 접속 중인 학번"으로 거절된다.
    // 교수 쪽이 옛 연결을 정리할 때까지(약 20초) 거절돼도 계속 다시 시도한다.
    public static class ReconnectService
    {
        // 다시 찾는 간격. 교수가 프로그램을 다시 켜는 데 걸리는 시간에 비하면 충분히 짧다.
        private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(3);

        // 로그인해 있는 동안만 켜져 있다. 여러 스레드에서 읽으므로 volatile.
        private static volatile bool _enabled;
        private static Student? _student;
        private static string _lastIp = string.Empty;
        private static int _lastPort;

        // 재연결 루프가 하나만 돌게 막는다.
        private static int _running;

        // 교수가 다시 붙은 학생을 알아볼 수 있도록 화면에 알린다(네이티브 연결 이벤트와 별개로 로그인까지 끝난 뒤).
        public static event Action? Reconnected;

        // 앱 시작 시 한 번 호출 — 구독만 해 둔다.
        public static void Start()
        {
            NetworkService.Instance.Disconnected += _ => OnDisconnected();
        }

        // 로그인에 성공하면 부른다. 이 학생·주소로 다시 붙는다.
        public static void Enable(Student student, string ip, int port)
        {
            _student = student;
            _lastIp = ip;
            _lastPort = port;
            _enabled = true;
        }

        // 로그아웃할 때 연결을 끊기 전에 부른다.
        public static void Disable() => _enabled = false;

        // 네이티브 수신 스레드에서 불린다. 여기서 바로 정리하면 그 스레드가 자기 자신을 기다리게 되므로
        // 재연결은 따로 돌린다.
        private static void OnDisconnected()
        {
            if (!_enabled) return;
            if (Interlocked.Exchange(ref _running, 1) == 1) return;

            _ = Task.Run(ReconnectLoopAsync);
        }

        private static async Task ReconnectLoopAsync()
        {
            try
            {
                // 끊긴 연결의 수신·하트비트 스레드를 거둔다. 거두지 않고 다시 연결하면 프로세스가 죽는다.
                NetworkService.Instance.Disconnect();

                while (_enabled && _student != null)
                {
                    await Task.Delay(RetryInterval);
                    if (!_enabled) break;

                    // 그 사이 학생이 로그아웃했다가 직접 다시 로그인했으면 이미 붙어 있다. 할 일이 없다.
                    if (NetworkService.Instance.IsConnected) break;

                    // 교수 PC 가 새 포트로 켜졌을 수 있어 먼저 물어보고, 답이 없으면 지난 주소·포트로 바로 붙어 본다.
                    (string Ip, int Port) target = await ProfessorDiscovery.FindAsync(_lastIp) ?? (_lastIp, _lastPort);
                    if (!NetworkService.Instance.Connect(target.Ip, target.Port)) continue;

                    // 회신 없이 3초가 지나도 승인으로 치므로, 그 사이 또 끊겼는지 연결 상태로 한 번 더 본다.
                    // 그때의 끊김 알림은 이 루프가 도는 중이라 무시됐다.
                    string? rejection = await LoginService.LoginAsync(_student);
                    if (rejection == null && _enabled && NetworkService.Instance.IsConnected)
                    {
                        _lastIp = target.Ip;
                        _lastPort = target.Port;
                        ExamMonitorService.Instance.ReportAfterReconnect();
                        Reconnected?.Invoke();
                        return;
                    }

                    // 거절됐거나 그 사이 로그아웃했다. 연결을 거두고 다음 차례를 기다린다.
                    NetworkService.Instance.Disconnect();
                }
            }
            catch (Exception ex)
            {
                // 배경 작업이라 예외가 새면 조용히 사라진다. 흔적만 남긴다.
                System.Diagnostics.Debug.WriteLine($"자동 재연결 중 오류: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);

                // 다시 붙은 직후, 이 루프가 끝나기 전에 또 끊겼다면 그 끊김 알림은 무시됐다. 놓치지 않게 한 번 더 본다.
                // (성공해서 return 한 경우에도 거쳐야 하므로 finally 안에 둔다)
                if (_enabled && !NetworkService.Instance.IsConnected) OnDisconnected();
            }
        }
    }
}
