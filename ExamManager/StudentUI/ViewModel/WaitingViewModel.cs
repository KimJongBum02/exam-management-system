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

        // ── 1. 현재 시각 표시 ──
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
                _navigationStore.CurrentViewModel = new StudentExamViewModel(student);
            });

            ExitCommand = new RelayCommand(() =>
            {
                Cleanup();
                Application.Current.Shutdown(); // OnExit에서 연결 해제·정리 수행
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
