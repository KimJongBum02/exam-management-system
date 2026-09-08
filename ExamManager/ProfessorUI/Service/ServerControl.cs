using System;

namespace ProfessorUI.Service
{
    // 서버를 열고 닫는 지점을 한곳으로 모은다.
    //
    // 앱이 뜨는 것만으로는 서버가 열리지 않는다. 마법사 2단계의 [서버 열기] 를
    // 눌러야 열린다 — 파일 준비와 감시 목록이 끝나기 전에 학생이 붙으면
    // 아무것도 받지 못한 채 대기하게 되기 때문이다.
    //
    // OX 퀴즈처럼 시험과 무관하게 서버가 필요한 경우도 지금은 2단계를 거쳐야 한다.
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
