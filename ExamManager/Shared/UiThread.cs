using System.Windows;

// 다른 스레드에서 화면을 건드릴 때 반드시 거치는 한 곳입니다. 두 앱이 함께 씁니다.

namespace ExamManager.Shared
{
    public static class UiThread
    {
        // 화면 스레드로 일을 넘기는 것을 담당합니다. 네이티브 수신 스레드나 배경 작업에서 부릅니다.
        //
        // 지킬 것이 둘입니다 — 앱이 끝나는 중이면 넘기지 않습니다(그때 넘기면 예외가 나고,
        // 그 예외가 네이티브 스레드로 올라가면 앱이 비정상 종료됩니다). 그리고 맡기기만 하고
        // 기다리지 않습니다(BeginInvoke) — 기다리면 화면 스레드와 서로를 기다려 앱이 통째로 굳습니다.
        //
        // 화면이 다 그려진 뒤에 해야 하는 일(포커스 · 맨 아래로 스크롤)은 우선순위가 달라
        // 여기를 쓰지 않고 그 자리에서 DispatcherPriority.Loaded 로 넘깁니다.
        public static void Post(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(action);
        }
    }
}
