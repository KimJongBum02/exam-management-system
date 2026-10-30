// ScreenBoardViewModel.cs
// 화면 모니터링 페이지의 ViewModel.
// 접속 중인 학생 목록과 각 학생의 최신 화면 이미지를 관리합니다.
// 수강생 명단을 불러오면 검은 칸을 미리 깔고, 학생이 접속하면 그 칸에 화면이 뜹니다. 타일은 늘 학번 순입니다.
using System.Collections.ObjectModel;
using NetworkLib;
using ProfessorUI.Service;
using ExamManager.Shared;

namespace ProfessorUI.ViewModel
{
    public class ScreenBoardViewModel : UiBinding
    {
        // ── 싱글톤 ──────────────────────────────────────────────────────────
        public static ScreenBoardViewModel Instance { get; } = new();

        // 이 목록은 처음 쓰일 때 만들어진다. 그보다 먼저 명단을 불러왔을 수 있어 만들 때도 한 번 깐다.
        private ScreenBoardViewModel()
        {
            StudentExcelStore.Changed += ApplyRoster;
            ApplyRoster();
        }

        // ── 학생 목록 (UI 스레드에서만 조작) ──────────────────────────────
        public ObservableCollection<StudentScreenViewModel> Students { get; } = new();

        public string StudentCountText => $"접속 중인 학생: {Students.Count(s => s.IsConnected)}명" +
                                          (StudentExcelStore.HasRoster ? $" / 수강생 명단 {StudentExcelStore.Entries.Count}명" : "");
        public bool   HasNoStudents     => Students.Count == 0;

        private bool _hasLockedStudents;
        /// <summary>현재 1명 이상의 학생 키보드가 잠겨 있는지 여부</summary>
        public bool HasLockedStudents
        {
            get => _hasLockedStudents;
            set
            {
                if (_hasLockedStudents == value) return;
                _hasLockedStudents = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 키보드 일괄 잠금 또는 전체 해제 토글합니다.
        /// 잠긴 학생이 1명이라도 있으면 모두 해제하고, 아무도 잠겨 있지 않으면 모두 잠급니다.
        /// </summary>
        public void ToggleAllKeyboardLock()
        {
            bool lockToApply = !HasLockedStudents;

            byte payload = (byte)(lockToApply ? 1 : 0);
            NetworkService.Instance.Broadcast(PacketType.LockKeyboard, new byte[] { payload });

            foreach (var student in Students)
            {
                student.IsKeyboardLocked = lockToApply;
            }

            CheckAllLockState();
        }

        /// <summary>개별 학생 잠금 상태 변경 시 전체 잠금 상태를 동기화합니다.</summary>
        public void CheckAllLockState()
        {
            HasLockedStudents = Students.Any(s => s.IsConnected && s.IsKeyboardLocked);
        }

        // ── 학생 추가 (StudentConnected 콜백에서 호출) ──────────────────────
        /// <summary>학생이 새로 접속했을 때 타일을 추가합니다. UI 스레드에서 호출하세요.</summary>
        public void AddStudent(string sessionId, string studentId, string studentName, string _ip)
        {
            if (Students.Any(s => s.SessionId == sessionId)) return;

            // 이미 접속 중인 학생들이 모두 잠겨 있는 상태라면 새로 접속한 학생도 잠금 적용
            var connected = Students.Where(s => s.IsConnected).ToList();
            bool shouldLock = connected.Count > 0 && connected.All(s => s.IsKeyboardLocked);

            // 명단 학생이면 미리 깔아 둔 자리에 화면을 붙인다
            var seat = Students.FirstOrDefault(s => s.StudentId == studentId && !s.IsConnected);
            if (seat != null)
            {
                seat.Connect(sessionId);
                if (shouldLock)
                {
                    seat.IsKeyboardLocked = true;
                    NetworkService.Instance.SendToSession(sessionId, PacketType.LockKeyboard, new byte[] { 1 });
                }
            }
            else
            {
                var newStudent = new StudentScreenViewModel(sessionId, studentId, studentName);
                if (shouldLock)
                {
                    newStudent.IsKeyboardLocked = true;
                    NetworkService.Instance.SendToSession(sessionId, PacketType.LockKeyboard, new byte[] { 1 });
                }
                // 명단에 없는 학생도 학번 순 자리에 넣는다. 접속한 순서대로 뒤에 붙이면
                // 같은 학생이 대시보드와 이 화면에서 다른 자리에 있게 된다.
                int at = 0;
                while (at < Students.Count && string.CompareOrdinal(Students[at].StudentId, studentId) < 0)
                    at++;

                Students.Insert(at, newStudent);
            }
            NotifyCount();
        }

        // ── 학생 제거 (StudentDisconnected 콜백에서 호출) ───────────────────
        /// <summary>학생이 접속을 끊었을 때 타일을 제거합니다. UI 스레드에서 호출하세요.</summary>
        public void RemoveStudent(string sessionId)
        {
            var item = Students.FirstOrDefault(s => s.SessionId == sessionId);
            if (item == null) return;

            // 끊겼다는 것을 먼저 알린다. 이 알림을 받아 상세 보기·원격 제어 창이 스스로 닫힌다.
            // 예전에는 명단에 없는 학생의 타일을 지우기만 해서 SessionId 가 그대로 남았고,
            // 두 창은 학생이 끊긴 줄 모른 채 멈춘 화면을 계속 띄웠다(명단을 쓰지 않으면 모든 학생이 이 경로다).
            item.Disconnect();

            // 명단 학생은 자리를 남기고 검은 칸으로 되돌린다. 빠진 자리가 그대로 보여야 한다.
            if (StudentExcelStore.Find(item.StudentId) == null) Students.Remove(item);
            NotifyCount();
        }

        // ── 화면 갱신 (ScreenCapture 패킷 수신 시 호출) ─────────────────────
        /// <summary>수신한 JPEG 데이터를 해당 학생의 타일에 적용합니다. UI 스레드에서 호출하세요.</summary>
        public void UpdateScreen(string studentId, byte[] jpeg)
        {
            var item = Students.FirstOrDefault(s => s.StudentId == studentId && s.IsConnected);
            item?.ApplyJpeg(jpeg);
        }

        // ── 명단 적용 ───────────────────────────────────────────────────────
        // 명단 자리와 접속 학생을 합쳐 학번 순으로 칸을 다시 깐다. 이미 접속해 있는 학생의 칸은
        // 받아 둔 화면째 제 학번 자리로 옮긴다. 열려 있는 상세 보기 창도 같은 칸을 보므로 그대로 이어진다.
        private void ApplyRoster()
        {
            var connected = Students.Where(s => s.IsConnected).ToList();
            Students.Clear();

            var seats = StudentExcelStore.Entries
                .Select(entry => connected.FirstOrDefault(s => s.StudentId == entry.StudentId)
                                 ?? new StudentScreenViewModel(entry.StudentId, entry.Name))
                .Concat(connected.Where(s => StudentExcelStore.Find(s.StudentId) == null));

            foreach (var tile in seats.OrderBy(s => s.StudentId, StringComparer.Ordinal))
                Students.Add(tile);

            NotifyCount();
        }

        private void NotifyCount()
        {
            OnPropertyChanged(nameof(StudentCountText));
            OnPropertyChanged(nameof(HasNoStudents));
        }
    }
}
