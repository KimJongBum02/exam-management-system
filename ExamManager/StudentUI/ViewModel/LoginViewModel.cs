using StudentUI.Model;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;

namespace StudentUI.ViewModel
{
    public class LoginViewModel
    {
        public Student Student { get; set; } = new Student();
        public ICommand LoginCommand { get; }
        public event Action? LoginSucceeded;

        public LoginViewModel()
        {
            LoginCommand = new RelayCommand(() =>
            {
                if (TryLogin())
                {
                    LoginSucceeded?.Invoke();
                }
                else
                {
                    System.Windows.MessageBox.Show("이름과 9자리 학번을 입력해 주세요.");
                }
            });
        }
        public bool TryLogin()
        {
            if (string.IsNullOrEmpty(Student.StudentName))
                return false;

            if (string.IsNullOrEmpty(Student.StudentNumber) || Student.StudentNumber.Length != 9)
                return false;

            return true;
        }
    }
}
