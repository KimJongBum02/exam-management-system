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
    public class WaitingViewModel : INotifyPropertyChanged
    {
        private readonly NavigationStore _navigationStore;
        private readonly DispatcherTimer _clockTimer;

        public Student Student { get; }

        public string StudentInfo => $"{Student.StudentNumber} {Student.StudentName}";

        // 시험 파일 수신 상태 (파일은 대기 중에 도착하므로 이 화면에서도 보여준다)
        public ExamFileStore ExamFile => ExamFileStore.Instance;

        // 채팅 (싱글턴 뷰모델 공유)
        public SharedChatViewModel ChatVM => SharedChatViewModel.Instance;

        // ── 1. 현재 시각 표시 ──
        private string _currentDate = string.Empty;
        public string CurrentDate
        {
            get => _currentDate;
            set { _currentDate = value; OnPropertyChanged(); }
        }

        private string _currentTime = string.Empty;
        public string CurrentTime
        {
            get => _currentTime;
            set { _currentTime = value; OnPropertyChanged(); }
        }

        // ── 2. 접속 상태 표시 ──
        private bool _isConnected = false;
        public bool IsConnected
        {
            get => _isConnected;
            set { _isConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConnectionStatusText)); }
        }

        public string ConnectionStatusText => IsConnected ? "서버 연결됨" : "서버 미연결";

        // ── 알림/채팅 펼침 패널 상태 (하나만 열림) ──
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
        public ICommand TestExamCommand { get; }
        public ICommand ExitCommand { get; }

        public WaitingViewModel(NavigationStore navigationStore, Student student)
        {
            _navigationStore = navigationStore;
            Student = student;

            // 로그인 단계에서 실제 연결을 맺고 넘어오므로 현재 연결 상태를 반영하고,
            // 이후 서버가 끊기면(교수 PC 종료 등) 실시간으로 '미연결'로 갱신한다.
            IsConnected = NetworkService.Instance.IsConnected;
            NetworkService.Instance.Disconnected += OnServerDisconnected;

            // 교수가 '시험 준비 상태로 전환'을 누르면 준비 화면으로 넘어간다.
            // 학생이 직접 화면을 넘기던 임시 버튼(TestExamCommand)을 대체하는 경로다.
            NetworkService.Instance.PacketReceived += OnPacketReceived;

            // 시계 타이머 (1초 간격)
            UpdateTime();
            _clockTimer = new DispatcherTimer();
            _clockTimer.Interval = TimeSpan.FromSeconds(1);
            _clockTimer.Tick += (s, e) => UpdateTime();
            _clockTimer.Start();

            LogoutCommand = new RelayCommand(() =>
            {
                Cleanup();
                NetworkService.Instance.Disconnect(); // 로그아웃 시 연결도 정리
                _navigationStore.CurrentViewModel = new LoginViewModel(_navigationStore);
            });

            TestExamCommand = new RelayCommand(() =>
            {
                Cleanup();
                _navigationStore.CurrentViewModel = new StudentExamViewModel(_navigationStore, student);
            });


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

            ExitCommand = new RelayCommand(() =>
            {
                Cleanup();
                Application.Current.Shutdown(); // OnExit에서 연결 해제·정리 수행
            });
        }

        // 교수 PC의 시험 단계 알림 (네이티브 스레드에서 호출됨)
        private void OnPacketReceived(PacketType type, IntPtr payload, uint payloadLen)
        {
            if (type != PacketType.ExamPhaseChange) return;

            // 형식이 깨졌거나 모르는 단계면 무시한다.
            if (!ExamPhasePayload.TryDecode(payload, payloadLen, out ExamPhase phase)) return;
            if (phase != ExamPhase.Ready) return;

            // 화면 전환은 UI 스레드에서만 할 수 있다.
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            dispatcher.BeginInvoke(() =>
            {
                // 신호가 두 번 와도 화면을 두 번 만들지 않는다.
                if (_navigationStore.CurrentViewModel != this) return;

                Cleanup();
                _navigationStore.CurrentViewModel = new StudentExamViewModel(_navigationStore, Student);
            });
        }

        // 서버 연결이 끊겼을 때 UI 상태를 갱신 (네이티브 스레드에서 호출됨)
        private void OnServerDisconnected(DisconnectReason reason)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(() => IsConnected = false);
        }

        // 화면을 떠날 때 타이머·이벤트 구독을 정리
        private void Cleanup()
        {
            _clockTimer.Stop();
            NetworkService.Instance.Disconnected -= OnServerDisconnected;
            NetworkService.Instance.PacketReceived -= OnPacketReceived;
        }

        private void UpdateTime()
        {
            CurrentDate = DateTime.Now.ToString("yyyy. MM. dd (ddd)");
            CurrentTime = DateTime.Now.ToString("HH:mm:ss");
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
