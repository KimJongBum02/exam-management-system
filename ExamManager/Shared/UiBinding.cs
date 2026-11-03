using System.ComponentModel;
using System.Runtime.CompilerServices;

// 값이 바뀌었음을 화면에 알리는 바탕을 담당하는 파일입니다. 두 앱이 함께 씁니다.

namespace ExamManager.Shared
{
    // 화면이 바인딩하는 클래스는 이것을 물려받고, 값을 바꾼 자리에서 OnPropertyChanged 를 부릅니다.
    //   set { _text = value; OnPropertyChanged(); }   괄호를 비우면 속성 이름이 자동으로 들어갑니다
    //   OnPropertyChanged(nameof(IsEnabled));         계산 속성은 근거가 바뀔 때 이름을 지목합니다
    //   OnPropertyChanged(null);                      이 클래스에 묶인 바인딩을 전부 다시 읽습니다
    //
    // 부르지 않으면 값만 바뀌고 화면은 옛 값으로 남습니다. 예외도 빌드 오류도 나지 않습니다.
    public class UiBinding : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
