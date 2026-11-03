using System.Windows.Input;

// 화면의 버튼 · 단축키를 ViewModel 의 코드에 잇는 다리입니다. 두 앱이 함께 씁니다.

namespace ExamManager.Shared
{
    // XAML 의 Command="{Binding 무엇무엇Command}" 가 요구하는 규격(ICommand)을 채웁니다.
    //   execute    눌렀을 때 할 일입니다
    //   canExecute 지금 눌러도 되는지입니다. false 면 WPF 가 버튼을 알아서 잠급니다(회색).
    //
    // 인자는 XAML 의 CommandParameter 로 들어옵니다(표의 어느 줄을 눌렀는지 같은 것).
    // 쓰지 않는 자리는 `_ =>` 로 받아 무시합니다.
    public class UiCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public UiCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        // 다시 물어볼 시점은 WPF 가 정합니다. 화면 조작 없이 상태만 바뀌어 버튼이 안 풀리면
        // CommandManager.InvalidateRequerySuggested() 로 떠밉니다.
        public event EventHandler? CanExecuteChanged
        {
            add    => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _execute(parameter);
    }
}
