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

namespace StudentUI.View.StudentExamView
{
    /// <summary>
    /// StudentFile.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class StudentFile : UserControl
    {
        public StudentFile()
        {
            InitializeComponent();
            UnzipButton.Click += (s, e) =>
            {
                MessageBox.Show("압축 해제 버튼 클릭!");
            };
        }
    }
}
