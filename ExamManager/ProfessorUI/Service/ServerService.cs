using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ProfessorUI.Service
{
    // 서버를 열고 닫는 지점을 한곳으로 모은다.
    //
    // 앱을 켜면 App.OnStartup 이 바로 연다. 교수가 따로 열어 줄 것이 없어야
    // 서버가 닫힌 줄 모르고 학생을 기다리게 하는 사고가 없다.
    // 닫는 것은 앱을 끌 때와 포트를 바꿀 때뿐이다.
    public static class ServerService
    {
        // 학생 프로그램도 이 포트부터 찾는다. 바꿀 일이 없으면 이대로 쓴다.
        public const int DefaultPort = 9000;

        private static bool _isRunning;
        private static int _port = DefaultPort;

        public static bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (_isRunning == value) return;
                _isRunning = value;
                StateChanged?.Invoke();
            }
        }

        // 학생에게 불러 줄 이 PC 의 주소. 서버가 닫혀 있어도 확인할 수 있다.
        public static string Address => NetworkService.GetLocalIPv4();

        // 지금 학생을 받고 있는 포트. 바꾼 값은 앱을 끄면 사라지고 다음 실행 때 9000 으로 돌아온다.
        public static int Port => _port;

        public static event Action? StateChanged;

        // 이미 열려 있으면 아무 것도 하지 않고 true 를 돌려준다.
        // 화면에서 여러 번 눌러도 서버가 두 번 열리지 않게 하기 위함이다.
        public static bool Start()
        {
            if (_isRunning) return true;

            if (!NetworkService.Instance.StartServer()) return false;

            // 학생이 교수 PC 의 IP 를 몰라도 찾아올 수 있게 함께 연다.
            AutoConnectService.Start();

            IsRunning = true;
            return true;
        }

        public static void Stop()
        {
            if (!_isRunning) return;

            AutoConnectService.Stop();
            NetworkService.Instance.StopServer();
            IsRunning = false;
        }

        // ── 서버가 실제로 학생을 받을 수 있는지 ──
        // IsRunning 은 연 적이 있는지만 안다. 그 뒤에 네트워크가 끊기거나 접속 받기가 멈춰도 true 로 남으므로
        // 상단바는 이 값을 몇 초마다 다시 본다(ServerStatusViewModel).
        public enum Health
        {
            Active,       // 학생을 받고 있음
            NotStarted,   // 서버를 열지 못했음 (앱 시작 때 포트를 다른 프로그램이 쓰는 등)
            NoNetwork,    // 이 PC 가 강의실 네트워크에 없음 (랜선·와이파이 끊김)
            Stopped,      // 열었지만 접속 받기가 멈춤 (네이티브 오류)
        }

        public static Health CheckHealth()
        {
            if (!_isRunning) return Health.NotStarted;
            if (!NetworkService.Instance.IsServerListening) return Health.Stopped;
            if (!HasClassroomNetwork()) return Health.NoNetwork;
            return Health.Active;
        }

        // 켜져 있고 IPv4 주소와 게이트웨이가 있는 랜 카드가 하나라도 있으면 네트워크에 붙어 있는 것으로 본다.
        // 게이트웨이를 보는 이유: VMware 같은 가상 랜 카드는 랜선을 뽑아도 켜져 있지만 게이트웨이가 없다.
        private static bool HasClassroomNetwork()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces().Any(nic =>
                    nic.OperationalStatus == OperationalStatus.Up &&
                    nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                    nic.GetIPProperties() is var ip &&
                    ip.UnicastAddresses.Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork) &&
                    ip.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork));
            }
            catch (NetworkInformationException)
            {
                return true; // 확인할 수 없으면 끊겼다고 단정하지 않는다
            }
        }

        // 같은 포트로 서버를 다시 연다. 성공하면 null, 실패하면 화면에 보일 이유.
        // 앱을 다시 켜는 것과 달리 학생 목록·경고·시험 단계는 그대로 남는다.
        // 접속 중이던 학생은 끊겼다가 학생 앱의 자동 재연결로 몇 초 안에 돌아온다.
        public static string? Restart()
        {
            Stop();
            NetworkService.Instance.RecreateServer(_port);

            if (!Start())
                return $"포트 {_port}번을 열지 못했습니다. 다른 프로그램이 쓰고 있는지 확인해 주세요.";

            StateChanged?.Invoke();
            return null;
        }

        // 포트를 바꿔 서버를 다시 연다. 성공하면 null, 실패하면 화면에 보일 이유를 돌려준다.
        // 새 포트가 열리지 않으면 쓰던 포트로 되돌린다 — 바꾸려다 서버가 닫힌 채 남지 않게 한다.
        public static string? ChangePort(int port)
        {
            if (port == _port) return null;

            int previous = _port;
            Stop();

            _port = port;
            NetworkService.Instance.RecreateServer(port);

            if (!Start())
            {
                _port = previous;
                NetworkService.Instance.RecreateServer(previous);
                Start();
                return $"포트 {port}번을 열지 못했습니다. 다른 프로그램이 쓰고 있는지 확인해 주세요.";
            }

            StateChanged?.Invoke();
            return null;
        }
    }
}
