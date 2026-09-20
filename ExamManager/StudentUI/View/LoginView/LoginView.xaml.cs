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
                // 로그인 창이 뜨면 이름 입력란으로 바로 커서 포커스
                NameInput.StudentName.Focus();

                if (DataContext is LoginViewModel vm)
                {
                    // 평소에는 교수 PC 를 자동으로 찾으므로 이 창은 뜨지 않는다.
                    // 못 찾았을 때만 뜬다.
                    vm.RequestManualAddress += () =>
                    {
                        var dialog = new IPInputDialog();
                        return dialog.ShowDialog() == true
                            ? ((string Ip, int Port)?)(dialog.IPAddress, dialog.Port)
                            : null;
                    };
                }
            };
        }
    }
}
