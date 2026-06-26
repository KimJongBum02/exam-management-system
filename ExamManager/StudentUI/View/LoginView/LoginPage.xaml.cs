using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using StudentUI.ViewModel;
using StudentUI.View.LockScreenView;

namespace StudentUI.View.LoginView
{
    public partial class LoginPage : Page
    {
        public LoginPage()
        {
            InitializeComponent();

            if (DataContext is LoginViewModel vm)
            {
                vm.ShowIPInput += () =>
                {
                    var dialog = new IPInputDialog();
                    if (dialog.ShowDialog() == true)
                    {
                        vm.Student.IPAddress = dialog.IPAddress;
                        NavigationService.Navigate(new LockScreenPage(vm.Student));
                    }
                };

                vm.LoginSucceeded += () =>
                {
                    NavigationService.Navigate(new LockScreenPage(vm.Student));
                };
            }
        }
    }
}
