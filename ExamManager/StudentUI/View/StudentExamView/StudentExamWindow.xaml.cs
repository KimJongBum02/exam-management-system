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

        // 화면 전환에 의한 닫힘이면 true — 이때는 종료 확인창을 띄우지 않는다.
        public bool IsNavigating { get; set; }

        // 창을 닫으면 앱이 그대로 종료되고 수신한 시험 파일 상태도 사라지므로,
        // 사용자가 직접 닫을 때만(전환이 아닐 때) 실수 방지 확인을 한 번 한다.
        protected override void OnClosing(CancelEventArgs e)
        {
            if (IsNavigating)
            {
                base.OnClosing(e);
                return;
            }

            var answer = MessageBox.Show(
                "시험 화면을 닫으면 프로그램이 종료됩니다.\n정말 종료하시겠습니까?",
                "종료 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
                e.Cancel = true;

            base.OnClosing(e);
        }
    }
}
