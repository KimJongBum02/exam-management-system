using System;
using System.Collections.Generic;
using System.Text;
using System.Collections.ObjectModel;

namespace ProfessorUI.ViewModel
{
    public class StudentBoardViewModel
    {
        // XAML의 ItemsControl과 바인딩될 리스트
        public ObservableCollection<StudentItemViewModel> Students => Service.StudentStore.Instance.Students;

        public StudentBoardViewModel()
        {

        }
    }
}
