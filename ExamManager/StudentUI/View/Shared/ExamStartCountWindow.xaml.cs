using System.Windows;

namespace StudentUI.View.Shared
{
    // 시험 진입 시 '곧 프로그램이 정리됩니다, 저장하세요' 카운트다운 배너.
    // 바탕화면 위에 떠 있고 클릭을 가로채지 않아, 학생이 뒤의 자기 앱으로 가 저장할 수 있다.
    public partial class ExamStartCountWindow : Window
    {
        public ExamStartCountWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
                Top = 40;
            };
        }

        // 남은 초를 갱신한다(ClassControlService 가 1초마다 호출).
        public void SetSeconds(int seconds)
        {
            MsgText.Text = $"실행 중인 프로그램이 {seconds}초 후 정리됩니다. 지금 저장해 주세요.";
        }
    }
}
