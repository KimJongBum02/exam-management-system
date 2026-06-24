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
    /// <summary>
    /// LoginPage.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class LoginPage : Page
    {
        public LoginPage()
        {
            InitializeComponent();

            // DataContext 가 설정된 후에 이벤트 연결
            Loaded += (s, e) =>
            {
                if (DataContext is LoginViewModel vm)
                {
                    vm.LoginSucceeded += () =>
                    {
                        NavigationService.Navigate(new LockScreenPage(vm.Student));
                    };
                }
            };
        }
    }
}
    