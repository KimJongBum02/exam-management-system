using System.Windows;
using ProfessorUI.Service;

namespace ProfessorUI.View.Professor
{
    // 학생을 받을 포트를 바꾸는 창.
    //
    // 평소에는 9000 을 그대로 쓴다. 강의실 PC 에서 그 포트를 다른 프로그램이 이미 쓰고 있을 때만
    // 여기서 바꾼다. 바꾸면 서버를 닫았다 다시 열기 때문에 붙어 있던 학생은 모두 끊어진다.
    public partial class PortWindow : Window
    {
        public PortWindow()
        {
            InitializeComponent();

            PortBox.Text = ServerService.Port.ToString();
            Loaded += (_, _) => { PortBox.Focus(); PortBox.SelectAll(); };

            ApplyButton.Click += (_, _) => Apply();
        }

        private void Apply()
        {
            // 1024 아래는 윈도우가 쓰는 자리라 열리지 않는 경우가 많아 아예 받지 않는다.
            if (!int.TryParse(PortBox.Text.Trim(), out int port) || port < 1024 || port > 65535)
            {
                ErrorText.Text = "1024 ~ 65535 사이의 번호를 입력해 주세요.";
                return;
            }

            string? failReason = ServerService.ChangePort(port);
            if (failReason != null)
            {
                ErrorText.Text = failReason;
                return;
            }

            DialogResult = true;
            Close();
        }
    }
}
