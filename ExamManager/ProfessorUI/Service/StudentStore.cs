using System.Collections.ObjectModel;
using ProfessorUI.ViewModel;

namespace ProfessorUI.Service
{
    public class StudentStore
    {
        // ⭐ 싱글톤 인스턴스 (어디서나 접근 가능)
        public static StudentStore Instance { get; } = new StudentStore();

        // ⭐ 단 하나의 학생 리스트 원본
        public ObservableCollection<StudentItemViewModel> Students { get; }

        private StudentStore()
        {
            Students = new ObservableCollection<StudentItemViewModel>();
            LoadDummyData();
        }

        private void LoadDummyData()
        {
            // 보내주신 더미 데이터 그대로 세팅 (기본 IsFileReceived는 모두 false 상태)
            Students.Add(new StudentItemViewModel { StudentId = "20220001", Name = "김민준", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220002", Name = "이서연", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220003", Name = "박지훈", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220006", Name = "강예은", Status = "미접속" });
            Students.Add(new StudentItemViewModel { StudentId = "20220007", Name = "조현우", Status = "미접속" }); Students.Add(new StudentItemViewModel { StudentId = "20220001", Name = "김민준", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220002", Name = "이서연", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220003", Name = "박지훈", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220006", Name = "강예은", Status = "미접속" });
            Students.Add(new StudentItemViewModel { StudentId = "20220007", Name = "조현우", Status = "미접속" }); Students.Add(new StudentItemViewModel { StudentId = "20220001", Name = "김민준", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220002", Name = "이서연", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220003", Name = "박지훈", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220006", Name = "강예은", Status = "미접속" });
            Students.Add(new StudentItemViewModel { StudentId = "20220007", Name = "조현우", Status = "미접속" }); Students.Add(new StudentItemViewModel { StudentId = "20220001", Name = "김민준", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220002", Name = "이서연", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220003", Name = "박지훈", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220006", Name = "강예은", Status = "미접속" });
            Students.Add(new StudentItemViewModel { StudentId = "20220007", Name = "조현우", Status = "미접속" });
            // ... (원하시는 만큼 반복 추가)
        }
    }
}