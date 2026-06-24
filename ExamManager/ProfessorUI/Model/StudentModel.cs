using System;
using NetworkLib; // StudentStatus 사용을 위해 포함

namespace ProfessorUI.Model
{
    public class StudentModel
    {
        public string StudentName { get; set; } = string.Empty;
        public string StudentNumber { get; set; } = string.Empty;
        public StudentStatus CurrentStatus { get; set; } = StudentStatus.NotConnected;
    }
}
