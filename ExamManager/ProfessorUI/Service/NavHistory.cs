using System;

namespace ProfessorUI.Service
{
    // 뒤로 갈 곳이 있는지 하나로 모아 둔다.
    //
    // 화면 기록(어떤 화면을 거쳐 왔는지)은 셸인 MainWindow 가 들고 있다.
    // 뒤로가기 버튼은 각 화면 안에 흩어져 있어 셸을 직접 붙잡고 있기 어려우므로,
    // 켜질지 꺼질지만 여기서 알려 준다. ExamState 가 시험 단계를 알리는 방식과 같다.
    public static class NavHistory
    {
        private static bool _canGoBack;

        public static bool CanGoBack
        {
            get => _canGoBack;
            set
            {
                if (_canGoBack == value) return;
                _canGoBack = value;
                Changed?.Invoke();
            }
        }

        public static event Action? Changed;
    }
}
