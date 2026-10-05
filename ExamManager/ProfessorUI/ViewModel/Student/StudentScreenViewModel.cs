// StudentScreenViewModel.cs
// 화면 모니터링 그리드에서 학생 한 명의 썸네일 타일을 나타내는 뷰모델 아이템.
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace ProfessorUI.ViewModel
{
    public class StudentScreenViewModel : INotifyPropertyChanged
    {
        // 접속해 있지 않으면 비어 있다(명단 학생이 아직 들어오지 않았거나 끊긴 경우).
        public string SessionId  { get; private set; }
        public string StudentId  { get; }
        public string StudentName { get; }
        public string DisplayName => $"{StudentName}  ({StudentId})";

        public bool IsConnected => SessionId.Length > 0;

        // 화면이 오기 전 검은 칸 가운데 문구
        public string PlaceholderText => IsConnected ? "대기 중…" : "미접속";

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

        private bool _isKeyboardLocked;
        /// <summary>현재 교수에 의해 키보드가 잠긴 상태인지 여부</summary>
        public bool IsKeyboardLocked
        {
            get => _isKeyboardLocked;
            set { _isKeyboardLocked = value; OnPropertyChanged(); }
        }

        public StudentScreenViewModel(string sessionId, string studentId, string studentName)
        {
            SessionId   = sessionId;
            StudentId   = studentId;
            StudentName = studentName;
        }

        // 수강생 명단의 한 명. 접속 전이라 화면 없이 검은 칸으로 자리만 잡아 둔다.
        public StudentScreenViewModel(string studentId, string studentName)
            : this(string.Empty, studentId, studentName)
        {
            _lastUpdated = string.Empty;
        }

        // 명단 학생이 접속했다. 화면이 오면 이 칸에 뜬다.
        public void Connect(string sessionId)
        {
            SessionId = sessionId;
            LastUpdated = "대기 중…";
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(PlaceholderText));
        }

        // 명단 학생의 접속이 끊겼다. 자리는 남기고 지난 화면은 지워 다시 검은 칸으로 둔다.
        // 학생 앱은 끊기면 키보드 잠금을 스스로 푼다(KeyboardLockService). 같은 칸에 다시 붙으므로 잠금 표시도 되돌린다.
        public void Disconnect()
        {
            SessionId = string.Empty;
            Screen = null;
            LastUpdated = string.Empty;
            IsKeyboardLocked = false;
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(PlaceholderText));
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
