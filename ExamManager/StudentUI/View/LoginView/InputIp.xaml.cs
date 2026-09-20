using System;
using System.Windows;

namespace StudentUI.View.LoginView
{
    // 교수 PC 를 자동으로 찾지 못했을 때만 뜨는 창.
    // 교수가 포트를 바꿨다면 "192.168.0.5:9100" 처럼 함께 적을 수 있다.
    public partial class IPInputDialog : Window
    {
        public string IPAddress { get; private set; } = string.Empty;
        public int Port { get; private set; } = DefaultPort;

        private const int DefaultPort = 9000;

        public IPInputDialog()
        {
            InitializeComponent();

            // 창이 열리면 IP 입력란으로 바로 커서 포커스
            Loaded += (s, e) => IPTextBox.Focus();

            ConnectButton.Click += (s, e) =>
            {
                string input = IPTextBox.Text.Trim();
                if (string.IsNullOrEmpty(input))
                {
                    MessageBox.Show("IP 주소를 입력해 주세요.");
                    return;
                }

                // 포트를 함께 적었으면 떼어 낸다. 적지 않았으면 기본 포트로 붙는다.
                string[] parts = input.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1], out int port))
                {
                    input = parts[0];
                    Port = port;
                }

                IPAddress = input;
                DialogResult = true;
                Close();
            };
        }
    }
}
