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

namespace StudentUI.View.LoginView
{
    public partial class InputIp : Window
    {
        public string IPAddress { get; private set; } = string.Empty;
        public string Port { get; private set; } = string.Empty;

        public InputIp()
        {
            InitializeComponent();
            ConnectButton.Click += (s, e) =>
            {
                string ip = IPTextBox.Text.Trim();
                string port = PortTextBox.Text.Trim();

                if (string.IsNullOrEmpty(ip))
                {
                    MessageBox.Show("IP 주소를 입력해 주세요.");
                    return;
                }

                if (!System.Net.IPAddress.TryParse(ip, out _))
                {
                    MessageBox.Show("올바른 IP 주소 형식이 아닙니다.\n예: 192.168.0.1");
                    return;
                }

                if (string.IsNullOrEmpty(port))
                {
                    MessageBox.Show("포트 번호를 입력해 주세요.");
                    return;
                }

                if (!int.TryParse(port, out int portNumber) || portNumber < 1 || portNumber > 65535)
                {
                    MessageBox.Show("올바른 포트 번호를 입력해 주세요. (1~65535)");
                    return;
                }

                IPAddress = ip;
                Port = port;
                DialogResult = true;
                Close();
            };
        }
    }
}