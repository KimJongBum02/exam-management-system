using System;
using System.Windows;
using System.Windows.Controls;
using StudentUI.ViewModel;

namespace StudentUI.View.LoginView
{
    public partial class LoginView : UserControl
    {
        public LoginView()
        {
            InitializeComponent();
            
            this.Loaded += (s, e) =>
            {
                if (DataContext is LoginViewModel vm)
                {
                    vm.ShowIPInput += () =>
                    {
                        var dialog = new IPInputDialog();
                        if (dialog.ShowDialog() == true)
                        {
                            vm.Student.IPAddress = dialog.IPAddress;
                            vm.CompleteLogin();
                        }
                    };
                }
            };
        }
    }
}
