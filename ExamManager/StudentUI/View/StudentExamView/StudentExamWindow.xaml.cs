using System;
//using System.Collections.Generic;
//using System.Text;
using System.ComponentModel;
using System.Windows;
//using System.Windows.Controls;
//using System.Windows.Data;
//using System.Windows.Documents;
//using System.Windows.Input;
//using System.Windows.Media;
//using System.Windows.Media.Imaging;
//using System.Windows.Navigation;
//using System.Windows.Shapes;

namespace StudentUI.View.StudentExamView
{
    public partial class StudentExamWindow : Window
    {
        public StudentExamWindow()
        {
            InitializeComponent();
        }

        // 창을 닫으면 앱이 그대로 종료되고 수신한 시험 파일 상태도 사라지므로,
        // 시험 중 실수로 닫는 것을 막기 위해 한 번 확인한다.
        protected override void OnClosing(CancelEventArgs e)
        {
            var answer = MessageBox.Show(
                "시험 화면을 닫으면 프로그램이 종료됩니다.\n정말 종료하시겠습니까?",
                "종료 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
                e.Cancel = true;

            base.OnClosing(e);
        }
    }
}
