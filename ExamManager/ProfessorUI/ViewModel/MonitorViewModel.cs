//MonitorViewModel.cs
using ProfessorUI.ViewModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;


namespace ProfessorUI.ViewModel
{
    public class MonitorViewModel : INotifyPropertyChanged
    {
        // 각각의 뷰모델을 독립적인 속성으로 선언
        public DashBoardCardViewModel DashBoardCardVM { get; }
        public CurrentTimeViewModel CurrentTimeVM { get; }

        // 메인에서 만든 걸 전달받습니다.
        // 1. 기존 데이터를 주입받는 생성자
        public MonitorViewModel(DashBoardCardViewModel cardVM, CurrentTimeViewModel timeVM)
        {
            DashBoardCardVM = cardVM;
            CurrentTimeVM = timeVM;
        }

        // 2. [추가] 기본 생성자 (필요시 새로 생성될 때를 대비)
        public MonitorViewModel()
        {
            DashBoardCardVM = new DashBoardCardViewModel();
            CurrentTimeVM = new CurrentTimeViewModel();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}