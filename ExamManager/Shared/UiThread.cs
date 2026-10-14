using System.Windows;

namespace ExamManager.Shared
{
    // 네이티브 수신 스레드나 배경 작업에서 화면을 건드릴 때 반드시 거치는 한 곳. 두 앱이 함께 쓴다.
    //
    // 화면(WPF)은 만든 스레드 하나만 건드릴 수 있어서, 다른 스레드에서 온 일은 그 스레드로 넘겨야 한다.
    // 넘길 때 지킬 것이 두 가지인데, 넘기는 자리마다 적어 두면 빠뜨리기 쉬워 여기 하나로 모았다.
    //
    //   ① 앱이 끝나는 중이면 넘기지 않는다. 끝나는 중에 넘기면 예외가 나고,
    //      그 예외가 네이티브 스레드로 올라가면 앱이 비정상 종료된다.
    //   ② 맡기기만 하고 기다리지 않는다(BeginInvoke). 기다리면(Invoke) 화면 스레드가 이쪽을
    //      기다리고 있을 때 서로 상대를 기다려 앱이 통째로 굳는다.
    //      (연결 끊김과 로그아웃이 겹칠 때 실제로 그렇게 된다 — 화면 스레드는 네이티브
    //       수신 스레드가 끝나길 기다리고, 수신 스레드는 화면 스레드를 기다린다.)
    //
    // 화면이 다 그려진 뒤에 해야 하는 일(포커스 주기·맨 아래로 스크롤)은 우선순위가 달라
    // 여기를 쓰지 않고 그 자리에서 DispatcherPriority.Loaded 로 따로 넘긴다.
    public static class UiThread
    {
        public static void Post(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(action);
        }
    }
}
