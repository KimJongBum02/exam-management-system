using ProfessorUI.Service;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProfessorUI.ViewModel
{
    public class MainViewModel : INotifyPropertyChanged
    {
        // 🎯 View가 바인딩할 수 있도록 LayoutStore를 프로퍼티로 엽니다.
        public LayoutStore LayoutStore { get; }

        // 🎯 학생에게 알려줄 이 PC의 접속 IP (상단 헤더에 표시)
        public string ServerAddress { get; } = NetworkService.GetLocalIPv4();

        // 🎯 우측에 무엇을 보여줄지 결정하는 프로퍼티
        public object? RightPaneViewModel { get; private set; }


        // (기존 네비게이션 관련 코드들...)
        public SidebarViewModel SidebarViewModel { get; }
        public DashBoardCardViewModel DashBoardCardVM { get; } = new DashBoardCardViewModel();
        public CurrentTimeViewModel CurrentTimeVM { get; } = new CurrentTimeViewModel();

        // 2. 모니터링용 전용 뷰모델 (보조창 연결용)
        public MonitorViewModel MonitorVM { get; }
        public object? CurrentViewModel => _navigationStore.CurrentViewModel;
        private readonly NavigationStore _navigationStore;

        // 생성자에서 LayoutStore를 주입받습니다.
        public MainViewModel(NavigationStore navigationStore, LayoutStore layoutStore)
        {
            _navigationStore = navigationStore;
            _navigationStore.CurrentViewModelChanged += OnCurrentViewModelChanged;

            // 🎯 주입받은 Store를 프로퍼티에 연결
            LayoutStore = layoutStore;
            LayoutStore.PropertyChanged += OnLayoutStorePropertyChanged;

            SidebarViewModel = new SidebarViewModel(navigationStore);
            // 2. 여기서 초기화합니다.
            MonitorVM = new MonitorViewModel(DashBoardCardVM, CurrentTimeVM);
        }

        private void OnLayoutStorePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LayoutStore.IsChatOpen) ||
                e.PropertyName == nameof(LayoutStore.IsNotificationOpen))
            {
                // 열려있는 상태에 따라 뷰모델 생성
                if (LayoutStore.IsChatOpen) RightPaneViewModel = new ChatViewModel();
                else if (LayoutStore.IsNotificationOpen) RightPaneViewModel = new NotificationViewModel();
                else RightPaneViewModel = null;

                OnPropertyChanged(nameof(RightPaneViewModel));
            }
        }

        private void OnCurrentViewModelChanged()
        {
            OnPropertyChanged(nameof(CurrentViewModel));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}