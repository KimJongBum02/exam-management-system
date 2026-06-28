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
    public partial class IPInputDialog : Window
    {
        public string IPAddress { get; private set; } = string.Empty;

        public IPInputDialog()
        {
            InitializeComponent();
            ConnectButton.Click += (s, e) =>
            {
                if (string.IsNullOrEmpty(IPTextBox.Text.Trim()))
                {
                    MessageBox.Show("IP 주소를 입력해 주세요.");
                    return;
                }

                IPAddress = IPTextBox.Text.Trim();
                DialogResult = true;
                Close();
            };
        }
    }
}