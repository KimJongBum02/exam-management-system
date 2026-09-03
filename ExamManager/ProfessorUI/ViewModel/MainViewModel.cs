using ProfessorUI.Service;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;

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

        // 🎯 채팅 뷰모델 싱글턴 (상태 유지용)
        private readonly ChatViewModel _sharedChatViewModel;

        // 🎯 알림 뷰모델도 하나만 둔다. 매번 새로 만들면 스크롤 위치 같은 상태가 초기화된다.
        private readonly NotificationViewModel _sharedNotificationViewModel;

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
            
            // 🎯 채팅 뷰모델 미리 생성 (싱글턴)
            _sharedChatViewModel = new ChatViewModel();
            _sharedNotificationViewModel = new NotificationViewModel();

            // LayoutStore의 이벤트에 반응하여 특정 탭 열기 연동
            LayoutStore.ChatOpenedForStudent += OnChatOpenedForStudent;

            // 학생 스토어 변경 시 채팅 열기 커맨드 연동
            StudentStore.Instance.Students.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (StudentItemViewModel item in e.NewItems)
                    {
                        item.RequestOpenChat = (sessionId, name) => LayoutStore.OpenChatForStudent(sessionId, name);
                    }
                }
            };
            // 이미 등록된 학생들에 대해서도 초기화
            foreach (var item in StudentStore.Instance.Students)
            {
                item.RequestOpenChat = (sessionId, name) => LayoutStore.OpenChatForStudent(sessionId, name);
            }

            SidebarViewModel = new SidebarViewModel(navigationStore);
            // 2. 여기서 초기화합니다.
            MonitorVM = new MonitorViewModel(DashBoardCardVM, CurrentTimeVM);
        }

        private void OnChatOpenedForStudent(string sessionId, string studentName)
        {
            // 채팅 탭 활성화 로직
            var tab = _sharedChatViewModel.Tabs.FirstOrDefault(t => t.SessionId == sessionId);
            if (tab == null)
            {
                tab = new ChatTabItem { TabName = studentName, SessionId = sessionId };
                _sharedChatViewModel.Tabs.Add(tab);
            }
            _sharedChatViewModel.SelectedTab = tab;
            
            if (!LayoutStore.IsChatOpen)
            {
                LayoutStore.ToggleChat();
            }
        }

        private void OnLayoutStorePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LayoutStore.IsChatOpen) ||
                e.PropertyName == nameof(LayoutStore.IsNotificationOpen))
            {
                // 열려있는 상태에 따라 뷰모델 생성/매핑
                if (LayoutStore.IsChatOpen) RightPaneViewModel = _sharedChatViewModel;
                else if (LayoutStore.IsNotificationOpen) RightPaneViewModel = _sharedNotificationViewModel;
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