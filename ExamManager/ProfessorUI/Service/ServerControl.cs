using System;

namespace ProfessorUI.Service
{
    // 서버를 열고 닫는 지점을 한곳으로 모은다.
    //
    // 앱을 켜면 App.OnStartup 이 바로 연다. 교수가 따로 열어 줄 것이 없어야
    // 서버가 닫힌 줄 모르고 학생을 기다리게 하는 사고가 없다.
    // 닫는 것은 앱을 끌 때뿐이다.
    public static class ServerControl
    {
        private static bool _isRunning;

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

        public static int Port => 9000;

        public static event Action? StateChanged;

        // 이미 열려 있으면 아무 것도 하지 않고 true 를 돌려준다.
        // 화면에서 여러 번 눌러도 서버가 두 번 열리지 않게 하기 위함이다.
        public static bool Start()
        {
            if (_isRunning) return true;

            if (!NetworkService.Instance.StartServer()) return false;

            IsRunning = true;
            return true;
        }

        public static void Stop()
        {
            if (!_isRunning) return;

            NetworkService.Instance.StopServer();
            IsRunning = false;
        }
    }
}
