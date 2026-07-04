using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace ProfessorUI.ViewModel
{
    public class CurrentTimeViewModel : INotifyPropertyChanged
    {
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

        public CurrentTimeViewModel()
        {
            // 타이머 시작 전 초기값 주입
            UpdateTime();

            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += (s, e) => UpdateTime();
            timer.Start();
        }

        private void UpdateTime()
        {
            // 한국인들이 가장 좋아하는 "2026. 06. 30 (화)" 포맷
            CurrentDate = DateTime.Now.ToString("yyyy. MM. dd (ddd)");
            // "19:33:59" 포맷
            CurrentTime = DateTime.Now.ToString("HH:mm:ss");
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}