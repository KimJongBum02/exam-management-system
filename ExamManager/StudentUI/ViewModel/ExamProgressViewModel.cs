using NetworkLib;
using StudentUI.Model;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using StudentUI.Service;

namespace StudentUI.ViewModel
{
    // 시험 '시작' 상태의 학생 화면. 대기 화면과 같은 플로팅 위젯 형태이며,
    // 아래에 알림/채팅 버튼을 두고 누르면 위젯이 아래로 펼쳐지며 해당 패널이 나타난다.
    // (채팅·알림 내용의 실제 송수신은 이후 네트워크 연결 단계에서 구현)
    public class ExamProgressViewModel : INotifyPropertyChanged
    {
        private readonly NavigationStore _navigationStore;
        private readonly DispatcherTimer _clockTimer;

        public Student Student { get; }

        public string StudentInfo => $"{Student.StudentNumber} {Student.StudentName}";

        // 채팅 (싱글턴 뷰모델 공유)
        public SharedChatViewModel ChatVM => SharedChatViewModel.Instance;

        // ── 현재 시각 (교수 PC가 시험 시간을 보내기 전까지는 시계로 표시) ──
        private string _currentTime = string.Empty;
        public string CurrentTime
        {
            get => _currentTime;
            set { _currentTime = value; OnPropertyChanged(); }
        }

        // ── 서버 접속 상태 ──
        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set { _isConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConnectionStatusText)); }
        }

        public string ConnectionStatusText => IsConnected ? "서버 연결됨" : "서버 미연결";

        // ── 펼침 패널 상태 (알림/채팅 중 하나만 열림) ──
        private bool _isNotificationOpen;
        public bool IsNotificationOpen
        {
            get => _isNotificationOpen;
            set { _isNotificationOpen = value; OnPropertyChanged(); }
        }

        private bool _isChatOpen;
        public bool IsChatOpen
        {
            get => _isChatOpen;
            set { _isChatOpen = value; OnPropertyChanged(); }
        }

        public ICommand ToggleNotificationCommand { get; }
        public ICommand ToggleChatCommand { get; }
        public ICommand LogoutCommand { get; }
        public ICommand ExitCommand { get; }

        // 교수 PC 시험 흐름 연결 전까지, 대기/준비 화면으로 되돌아가 테스트하기 위한 임시 전환
        public ICommand GoToWaitingCommand { get; }
        public ICommand GoToPrepCommand { get; }

        public ExamProgressViewModel(NavigationStore navigationStore, Student student)
        {
            _navigationStore = navigationStore;
            Student = student;

            IsConnected = NetworkService.Instance.IsConnected;
            NetworkService.Instance.Disconnected += OnServerDisconnected;

            UpdateTime();
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (s, e) => UpdateTime();
            _clockTimer.Start();

            ToggleNotificationCommand = new RelayCommand(() =>
            {
                IsNotificationOpen = !IsNotificationOpen;
                if (IsNotificationOpen) IsChatOpen = false; // 한 번에 하나만
            });

            ToggleChatCommand = new RelayCommand(() =>
            {
                IsChatOpen = !IsChatOpen;
                if (IsChatOpen) IsNotificationOpen = false;
            });

            LogoutCommand = new RelayCommand(() =>
            {
                Cleanup();
                NetworkService.Instance.Disconnect();
                _navigationStore.CurrentViewModel = new LoginViewModel(_navigationStore);
            });

            ExitCommand = new RelayCommand(() =>
            {
                Cleanup();
                Application.Current.Shutdown();
            });

            GoToWaitingCommand = new RelayCommand(() =>
            {
                Cleanup();
                _navigationStore.CurrentViewModel = new WaitingViewModel(_navigationStore, Student);
            });

            GoToPrepCommand = new RelayCommand(() =>
            {
                Cleanup();
                _navigationStore.CurrentViewModel = new StudentExamViewModel(_navigationStore, Student);
            });
        }

        // 서버 연결이 끊겼을 때 UI 상태 갱신 (네이티브 스레드에서 호출됨)
        private void OnServerDisconnected(DisconnectReason reason)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(() => IsConnected = false);
        }

        private void Cleanup()
        {
            _clockTimer.Stop();
            NetworkService.Instance.Disconnected -= OnServerDisconnected;
        }

        private void UpdateTime()
        {
            CurrentTime = DateTime.Now.ToString("HH:mm:ss");
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
