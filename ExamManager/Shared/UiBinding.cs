using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ExamManager.Shared
{
    // 값이 바뀌었음을 화면에 알리는 바탕. 교수·학생 프로그램이 함께 쓴다.
    //
    // C# 속성은 값이 바뀌어도 아무에게도 알리지 않아, 화면은 다시 그릴 이유를 모른다.
    // WPF 는 그 신호 규격(INotifyPropertyChanged)을 정해 두었고 이 클래스가 그것을 채운다.
    // 화면이 바인딩하는 클래스는 이것을 물려받고, 값을 바꾼 자리에서 OnPropertyChanged 를 부른다.
    //
    //   set { _text = value; OnPropertyChanged(); }   — 괄호를 비우면 속성 이름이 자동으로 들어간다
    //   OnPropertyChanged(nameof(IsEnabled));         — 계산 속성은 근거가 바뀔 때 이름을 지목한다
    //   OnPropertyChanged(null);                      — 이 클래스에 묶인 바인딩을 전부 다시 읽는다
    //
    // 부르지 않으면 값만 바뀌고 화면은 옛 값으로 남는다. 예외도 빌드 오류도 나지 않는다.
    public class UiBinding : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
