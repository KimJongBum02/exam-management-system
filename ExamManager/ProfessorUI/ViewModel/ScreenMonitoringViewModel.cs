// ScreenMonitoringViewModel.cs
// 화면 모니터링 페이지의 ViewModel.
// 접속 중인 학생 목록과 각 학생의 최신 화면 이미지를 관리합니다.
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ProfessorUI.ViewModel
{
    public class ScreenMonitoringViewModel : INotifyPropertyChanged
    {
        // ── 싱글톤 ──────────────────────────────────────────────────────────
        public static ScreenMonitoringViewModel Instance { get; } = new();

        // ── 학생 목록 (UI 스레드에서만 조작) ──────────────────────────────
        public ObservableCollection<StudentScreenItem> Students { get; } = new();

        public string StudentCountText  => $"접속 중인 학생: {Students.Count}명";
        public bool   HasNoStudents     => Students.Count == 0;

        // ── 학생 추가 (StudentConnected 콜백에서 호출) ──────────────────────
        /// <summary>학생이 새로 접속했을 때 타일을 추가합니다. UI 스레드에서 호출하세요.</summary>
        public void AddStudent(string sessionId, string studentId, string studentName, string _ip)
        {
            if (Students.Any(s => s.SessionId == sessionId)) return;
            Students.Add(new StudentScreenItem(sessionId, studentId, studentName));
            OnPropertyChanged(nameof(StudentCountText));
            OnPropertyChanged(nameof(HasNoStudents));
        }

        // ── 학생 제거 (StudentDisconnected 콜백에서 호출) ───────────────────
        /// <summary>학생이 접속을 끊었을 때 타일을 제거합니다. UI 스레드에서 호출하세요.</summary>
        public void RemoveStudent(string sessionId)
        {
            var item = Students.FirstOrDefault(s => s.SessionId == sessionId);
            if (item != null)
            {
                Students.Remove(item);
                OnPropertyChanged(nameof(StudentCountText));
                OnPropertyChanged(nameof(HasNoStudents));
            }
        }

        // ── 화면 갱신 (ScreenCapture 패킷 수신 시 호출) ─────────────────────
        /// <summary>수신한 JPEG 데이터를 해당 학생의 타일에 적용합니다. UI 스레드에서 호출하세요.</summary>
        public void UpdateScreen(string studentId, byte[] jpeg)
        {
            var item = Students.FirstOrDefault(s => s.StudentId == studentId);
            item?.ApplyJpeg(jpeg);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
