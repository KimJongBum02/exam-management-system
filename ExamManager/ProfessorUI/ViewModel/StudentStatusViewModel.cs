using System;
using System.Collections.Generic;
using System.Text;
using System.Collections.ObjectModel;

namespace ProfessorUI.ViewModel
{
    public class StudentBoardViewModel
    {
        // XAML의 ItemsControl과 바인딩될 리스트
        public ObservableCollection<StudentItemViewModel> Students { get; set; }

        public StudentBoardViewModel()
        {
            Students = new ObservableCollection<StudentItemViewModel>();

            // 테스트용 더미 데이터 로드
            LoadDummyData();
        }

        private void LoadDummyData()
        {
            Students.Add(new StudentItemViewModel { StudentId = "20220001", Name = "김민준", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220002", Name = "이서연", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220003", Name = "박지훈", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220006", Name = "강예은", Status = "미접속" });
            Students.Add(new StudentItemViewModel { StudentId = "20220007", Name = "조현우", Status = "미접속" });
            Students.Add(new StudentItemViewModel { StudentId = "20220001", Name = "김민준", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220002", Name = "이서연", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220003", Name = "박지훈", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220006", Name = "강예은", Status = "미접속" });
            Students.Add(new StudentItemViewModel { StudentId = "20220007", Name = "조현우", Status = "미접속" });
            Students.Add(new StudentItemViewModel { StudentId = "20220001", Name = "김민준", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220002", Name = "이서연", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220003", Name = "박지훈", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220006", Name = "강예은", Status = "미접속" });
            Students.Add(new StudentItemViewModel { StudentId = "20220007", Name = "조현우", Status = "미접속" });
            Students.Add(new StudentItemViewModel { StudentId = "20220001", Name = "김민준", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220002", Name = "이서연", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220003", Name = "박지훈", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220006", Name = "강예은", Status = "미접속" });
            Students.Add(new StudentItemViewModel { StudentId = "20220007", Name = "조현우", Status = "미접속" });
            Students.Add(new StudentItemViewModel { StudentId = "20220001", Name = "김민준", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220002", Name = "이서연", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220003", Name = "박지훈", Status = "대기" });
            Students.Add(new StudentItemViewModel { StudentId = "20220006", Name = "강예은", Status = "미접속" });
            Students.Add(new StudentItemViewModel { StudentId = "20220007", Name = "조현우", Status = "미접속" });

        }
    }
}
