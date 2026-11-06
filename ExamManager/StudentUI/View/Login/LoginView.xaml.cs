using System.Windows.Controls;
using StudentUI.ViewModel;

namespace StudentUI.View.Login
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
                    // 평소에는 교수 PC 를 자동으로 찾으므로 이 창은 뜨지 않는다. 두 경우에만 뜬다 —
                    // 아무도 답하지 않았을 때, 그리고 붙은 교수 PC 의 명단에 없다고 되돌려 보내졌을 때
                    // (옆 강의실에 붙은 것이라 학생이 제 교수 PC 주소를 직접 적어야 한다).
                    vm.RequestManualAddress += () =>
                    {
                        var dialog = new IpInputWindow();
                        return dialog.ShowDialog() == true
                            ? ((string Ip, int Port)?)(dialog.IPAddress, dialog.Port)
                            : null;
                    };
                }
            };
        }
    }
}
