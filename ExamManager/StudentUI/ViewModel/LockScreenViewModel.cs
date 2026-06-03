using StudentUI.Model;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;


namespace StudentUI.ViewModel
{
    class LockScreenViewModel
    {
        public Student Student { get; set; }
        public ICommand LogoutCommand { get; }
        public event Action? LogoutSucceeded;

        public LockScreenViewModel(Student student)
        {
            Student = student;

            LogoutCommand = new RelayCommand(() =>
            {
                LogoutSucceeded?.Invoke();
            });
        }
    }
}
