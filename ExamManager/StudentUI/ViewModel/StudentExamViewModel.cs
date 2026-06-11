using System;
using System.Collections.Generic;
using System.Text;
using StudentUI.Model;

namespace StudentUI.ViewModel
{
    public class StudentExamViewModel
    {

        public Student Student { get; set; } = new Student();

        public StudentExamViewModel(Student student)
        {
            Student = student;
        }
    }
}
