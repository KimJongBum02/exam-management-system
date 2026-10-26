using System.Windows;

namespace StudentUI.View.Shared
{
    // 통제 상태에서 화면 전체를 덮는 창. 뒤의 프로그램은 그대로 돌아가고 화면만 가린다(피카 방식).
    // 작업표시줄까지 덮도록 주 화면 전체 크기로 띄운다. (다중 모니터 대응은 테스트 범위 밖)
    // 시험 상태는 시험 창(StudentExamWindow)이 스스로 덮개가 되므로 이 창을 쓰지 않는다.
    public partial class ClassControlOverlayWindow : Window
    {
        public ClassControlOverlayWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                Left = 0;
                Top = 0;
                Width = SystemParameters.PrimaryScreenWidth;
                Height = SystemParameters.PrimaryScreenHeight;
                Topmost = true;
            };
        }
    }
}
