using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shell;

namespace ProfessorUI.Common
{
    // 창 제목줄을 우리가 그린 것으로 바꾼다.
    //
    // WindowStyle="None" 으로 제목줄을 떼어 내면 크기 조절·화면 가장자리에 붙이기(스냅)·
    // 더블클릭 최대화가 모두 사라져 하나하나 직접 만들어야 한다.
    // WindowChrome 은 제목줄의 '그림'만 우리에게 넘기고 창을 옮기고 키우는 일은 윈도가 그대로 맡으므로
    // 이쪽을 쓴다. (학생 UI 의 작은 창들은 크기를 바꾸지 않아 WindowStyle="None" 으로 되어 있다)
    //
    // 제목줄 안에 둔 버튼에는 WindowChrome.IsHitTestVisibleInChrome="True" 를 줘야 눌린다 —
    // 그러지 않으면 그 자리가 '끌어서 창 옮기기' 영역이라 눌림이 먹히지 않는다.
    //
    // 창을 최대화하면 윈도는 창을 테두리 두께만큼 화면 밖으로 넘겨 잡는다(제목줄이 있을 때도 마찬가지였다).
    // 그래서 제목줄의 단추는 창 가장자리에 딱 붙이지 않고 조금 안쪽에 둔다 — Styles.xaml 참고.
    public static class AppChrome
    {
        // 창에 붙이면 최소화·최대화·닫기 버튼이 쓰는 명령을 이어 준다.
        // 제목줄 모양은 Styles.xaml 의 AppWindow 스타일이 맡는다.
        public static readonly DependencyProperty ManagedProperty =
            DependencyProperty.RegisterAttached(
                "Managed", typeof(bool), typeof(AppChrome), new PropertyMetadata(false, OnManagedChanged));

        public static void SetManaged(DependencyObject target, bool value) => target.SetValue(ManagedProperty, value);
        public static bool GetManaged(DependencyObject target) => (bool)target.GetValue(ManagedProperty);

        private static void OnManagedChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
        {
            if (target is not Window window || e.NewValue is not true) return;

            Bind(window, SystemCommands.MinimizeWindowCommand, () => SystemCommands.MinimizeWindow(window));
            Bind(window, SystemCommands.MaximizeWindowCommand, () => SystemCommands.MaximizeWindow(window));
            Bind(window, SystemCommands.RestoreWindowCommand, () => SystemCommands.RestoreWindow(window));
            Bind(window, SystemCommands.CloseWindowCommand, () => SystemCommands.CloseWindow(window));
        }

        private static void Bind(Window window, RoutedCommand command, Action run)
            => window.CommandBindings.Add(new CommandBinding(command, (_, _) => run()));
    }
}
