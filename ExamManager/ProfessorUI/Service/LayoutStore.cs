using ProfessorUI.ViewModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input; // 💡 커맨드 사용을 위해 필요

namespace ProfessorUI.Service
{
    public class LayoutStore : INotifyPropertyChanged
    {
        // 💡 처음 실행 시 무조건 닫혀있도록 false 확실히 초기화
        private bool _isRightPaneVisible = false;
        private bool _isChatOpen = false;
        private bool _isNotificationOpen = false;

        public bool IsRightPaneVisible
        {
            get => _isRightPaneVisible;
            private set { _isRightPaneVisible = value; OnPropertyChanged(); }
        }

        public bool IsChatOpen
        {
            get => _isChatOpen;
            private set { _isChatOpen = value; OnPropertyChanged(); }
        }

        public bool IsNotificationOpen
        {
            get => _isNotificationOpen;
            private set { _isNotificationOpen = value; OnPropertyChanged(); }
        }

        // 🎯 XAML에서 직접 바인딩하여 작동시킬 커맨드 프로퍼티들
        public ICommand ToggleChatCommand { get; }
        public ICommand ToggleNotificationCommand { get; }
        public ICommand CloseRightPaneCommand { get; }

        public LayoutStore()
        {
            // 💡 생성자 시점에 메서드들을 커맨드로 랩핑합니다. (프로젝트 내 RelayCommand 사용)
            ToggleChatCommand = new RelayCommand(o => ToggleChat());
            ToggleNotificationCommand = new RelayCommand(o => ToggleNotification());
            CloseRightPaneCommand = new RelayCommand(o => CloseRightPane());
        }

        public void ToggleChat()
        {
            if (IsChatOpen)
            {
                CloseRightPane();
            }
            else
            {
                IsNotificationOpen = false;
                IsChatOpen = true;
                IsRightPaneVisible = true;
            }
        }

        public void ToggleNotification()
        {
            if (IsNotificationOpen)
            {
                CloseRightPane();
            }
            else
            {
                IsChatOpen = false;
                IsNotificationOpen = true;
                IsRightPaneVisible = true;
            }
        }

        public void CloseRightPane()
        {
            IsChatOpen = false;
            IsNotificationOpen = false;
            IsRightPaneVisible = false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}