// StudentScreenItem.cs
// 화면 모니터링 그리드에서 학생 한 명의 썸네일 타일을 나타내는 뷰모델 아이템.
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace ProfessorUI.ViewModel
{
    public class StudentScreenItem : INotifyPropertyChanged
    {
        public string SessionId  { get; }
        public string StudentId  { get; }
        public string StudentName { get; }
        public string DisplayName => $"{StudentName}  ({StudentId})";

        private BitmapSource? _screen;
        /// <summary>가장 최근에 수신한 화면 JPEG를 BitmapSource로 변환한 값. null이면 아직 수신 전.</summary>
        public BitmapSource? Screen
        {
            get => _screen;
            set { _screen = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasScreen)); }
        }

        public bool HasScreen => _screen != null;

        private string _lastUpdated = "대기 중…";
        public string LastUpdated
        {
            get => _lastUpdated;
            set { _lastUpdated = value; OnPropertyChanged(); }
        }

        public StudentScreenItem(string sessionId, string studentId, string studentName)
        {
            SessionId   = sessionId;
            StudentId   = studentId;
            StudentName = studentName;
        }

        /// <summary>JPEG 바이트 배열을 받아 Screen 프로퍼티를 갱신합니다. UI 스레드에서 호출하세요.</summary>
        public void ApplyJpeg(byte[] jpeg)
        {
            try
            {
                using var ms = new System.IO.MemoryStream(jpeg);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption  = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze(); // 크로스 스레드 안전
                Screen      = bi;
                LastUpdated = DateTime.Now.ToString("HH:mm:ss");
            }
            catch { /* 손상된 JPEG는 무시 */ }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
