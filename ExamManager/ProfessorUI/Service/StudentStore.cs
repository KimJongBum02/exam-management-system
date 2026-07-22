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
            }
            else
            {
                Students.Add(new StudentItemViewModel
                {
                    SessionId = sessionId,
                    StudentId = studentId,
                    Name = name,
                    Ip = ip,
                    Status = "대기"
                });
            }
        }

        // 학생 접속 종료: 해당 세션의 학생을 '미접속' 상태로 표시
        public void MarkDisconnected(string sessionId)
        {
            var student = Students.FirstOrDefault(s => s.SessionId == sessionId);
            if (student != null)
                student.Status = "미접속";
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
    }
}
