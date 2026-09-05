using System;

namespace ProfessorUI.Service
{
    // 서버를 열고 닫는 지점을 한곳으로 모은다.
    //
    // 지금까지는 App 이 실행되자마자 서버를 켰다. 시험만 생각하면 그래도 됐지만,
    // OX 퀴즈는 수업 중 이해도 확인이 본래 목적이라 시험과 무관하게 서버가 필요하다.
    // 그래서 "언제 켤지"를 App 에 박아 두지 않고 여기로 뺐다.
    //
    // 화면 어디에 버튼을 둘지는 교수 UI 가 정해진 뒤에 붙인다.
    // 지금은 App 이 예전처럼 시작 시 한 번 켜므로 겉보기 동작은 같다.
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
