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
        public ICommand StartCommand { get; }
        public event Action? LogoutSucceeded;
        public event Action? StartSuccceeded;
        public LockScreenViewModel(Student student)
        {
            Student = student;

            LogoutCommand = new RelayCommand(() =>
            {
                LogoutSucceeded?.Invoke();
            });

            StartCommand = new RelayCommand(()=>
                {
                StartSuccceeded?.Invoke();
            });
        }
    }
}
