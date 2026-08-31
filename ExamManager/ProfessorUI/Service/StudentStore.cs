using System;
using System.Collections.ObjectModel;
using System.Linq;
using ProfessorUI.ViewModel;

namespace ProfessorUI.Service
{
    public class StudentStore
    {
        // ⭐ 싱글톤 인스턴스 (어디서나 접근 가능)
        public static StudentStore Instance { get; } = new StudentStore();

        // ⭐ 단 하나의 학생 리스트 원본 (실제 접속한 학생만 채워짐)
        public ObservableCollection<StudentItemViewModel> Students { get; }

        // 특정 학생이 파일 수신 완료 응답을 보냈을 때 알림 (배포 화면 갱신용) — 인자: 학번
        public event Action<string>? FileReceivedConfirmed;

        private StudentStore()
        {
            Students = new ObservableCollection<StudentItemViewModel>();
        }

        // 학생 접속: 같은 학번이 이미 있으면 정보 갱신, 없으면 새로 추가
        public void AddOrUpdateConnected(string sessionId, string studentId, string name, string ip)
        {
            var existing = Students.FirstOrDefault(s => s.StudentId == studentId);
            if (existing != null)
            {
                existing.SessionId = sessionId;
                existing.Name = name;
                existing.Ip = ip;
                existing.Status = "대기";
                existing.IsConnected = true;
            }
            else
            {
                Students.Add(new StudentItemViewModel
                {
                    SessionId = sessionId,
                    StudentId = studentId,
                    Name = name,
                    Ip = ip,
                    Status = "대기",
                    IsConnected = true
                });
            }
        }

        // 학생 접속 종료: 답안을 냈는지에 따라 다르게 표시한다.
        //
        // 시험 20분 뒤부터 학생이 개별 제출하고 나가므로, 미접속에는 두 경우가 섞인다.
        //   제출하고 정상적으로 나감  → 그대로 두면 된다
        //   못 내고 연결이 끊김        → 교수가 찾아가 봐야 한다
        // 둘을 구분하지 않으면 답안을 못 걷은 학생을 '나갔나 보다' 하고 넘기게 된다.
        public void MarkDisconnected(string sessionId)
        {
            var student = Students.FirstOrDefault(s => s.SessionId == sessionId);
            if (student == null) return;

            student.IsConnected = false;

            // 이미 제출한 학생의 상태는 덮어쓰지 않는다 (제출완료 / 정리실패를 유지).
            if (!student.IsAnswerSubmitted)
                student.Status = "미제출(연결 끊김)";
        }

        // 파일 수신 완료 응답 처리: 해당 학번 학생을 '수신완료'로 표시
        public void MarkFileReceived(string studentId)
        {
            var student = Students.FirstOrDefault(s => s.StudentId == studentId);
            if (student != null)
            {
                student.IsFileReceived = true;
                student.Status = "수신완료";
                FileReceivedConfirmed?.Invoke(studentId);
            }
        }

        // 답안 수신 처리: 해당 학번 학생을 '제출완료'로 표시
        // 이 기록은 학생이 접속을 끊어도 남는다. 그래야 시험 끝에
        // '제출하고 나간 학생'과 '못 내고 끊긴 학생'을 구분할 수 있다.
        public void MarkAnswerSubmitted(string studentId)
        {
            var student = Students.FirstOrDefault(s => s.StudentId == studentId);
            if (student != null)
            {
                student.IsAnswerSubmitted = true;
                student.Status = "제출완료";
            }
        }

        // 정리 실패 처리: 답안은 받았으나 학생 PC에 시험 파일이 남은 상태
        // 답안 자체는 안전하므로 IsAnswerSubmitted는 그대로 둔다.
        public void MarkCleanupFailed(string studentId)
        {
            var student = Students.FirstOrDefault(s => s.StudentId == studentId);
            if (student != null)
                student.Status = "정리실패";
        }

        // 부정행위 알림 처리: 해당 학번 학생을 '부정행위 감지'로 표시
        // (어떤 프로그램이었는지 자세히 보여주는 화면은 아직 없다 — 모니터링 화면 작업에서 붙일 예정)
        public void MarkCheatingDetected(string studentId)
        {
            var student = Students.FirstOrDefault(s => s.StudentId == studentId);
            if (student != null)
                student.Status = "부정행위 감지";
        }
    }
}
