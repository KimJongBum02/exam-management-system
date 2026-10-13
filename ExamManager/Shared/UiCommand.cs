using System;
using System.Windows.Input;

namespace ExamManager.Shared
{
    // 화면의 버튼·단축키를 ViewModel 의 코드에 잇는 다리. 두 앱이 함께 쓴다.
    //
    // XAML 의 Command="{Binding 무엇무엇Command}" 가 요구하는 규격(ICommand)을 채운다.
    //   execute    — 눌렀을 때 할 일
    //   canExecute — 지금 눌러도 되는지. false 면 WPF 가 버튼을 알아서 잠근다(회색).
    //
    // canExecute 를 다시 묻는 시점은 WPF 가 정한다(CommandManager.RequerySuggested).
    // 화면 조작 없이 상태만 바뀌어 버튼이 안 풀릴 때는 CommandManager.InvalidateRequerySuggested() 로 떠민다.
    //
    // 인자(parameter)는 XAML 의 CommandParameter 로 들어온다 — 표의 어느 줄을 눌렀는지 같은 것.
    // 쓰지 않는 자리는 `_ =>` 로 받고 무시하면 된다.
    public class UiCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public UiCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add    => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _execute(parameter);
    }
}
