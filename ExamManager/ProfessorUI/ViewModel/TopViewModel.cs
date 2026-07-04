using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProfessorUI.ViewModel
{
    public class TopViewModel : INotifyPropertyChanged
    {
        // 묶어서 메인으로 올려줄 자식 뷰모델 2개
        public DashBoardCardViewModel DashBoardCardVM { get; }
        public CurrentTimeViewModel CurrentTimeVM { get; }

        public TopViewModel()
        {
            // 각각의 부품을 생성합니다.
            DashBoardCardVM = new DashBoardCardViewModel();
            CurrentTimeVM = new CurrentTimeViewModel();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}