using StudentUI.Model;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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

        public WaitingViewModel(NavigationStore navigationStore, Student student)
        {
            _navigationStore = navigationStore;
            Student = student;

            // 시계 타이머 (1초 간격)
            UpdateTime();
            _clockTimer = new DispatcherTimer();
            _clockTimer.Interval = TimeSpan.FromSeconds(1);
            _clockTimer.Tick += (s, e) => UpdateTime();
            _clockTimer.Start();

            LogoutCommand = new RelayCommand(() =>
            {
                _clockTimer.Stop();
                _navigationStore.CurrentViewModel = new LoginViewModel(_navigationStore);
            });

            TestExamCommand = new RelayCommand(() =>
            {
                _clockTimer.Stop();
                _navigationStore.CurrentViewModel = new StudentExamViewModel(student);
            });
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
