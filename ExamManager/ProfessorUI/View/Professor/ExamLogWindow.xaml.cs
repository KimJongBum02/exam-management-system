using System.Windows;
using System.Windows.Controls;
using ProfessorUI.Common;

namespace ProfessorUI.View.Professor
{
    // 시험 한 번의 기록을 한 장으로 보는 화면.
    // 학번·이름·출석 여부·부정행위 횟수와, 적발된 학생의 내역을 함께 둔다.
    //
    // 표를 만드는 일은 ExamLogViewModel 이 하고, 여기서는 파일로 내보내는 것만 맡는다.
    public partial class ExamLogWindow : UserControl
    {
        private readonly UiContext _ctx = UiContext.Instance;

        public ExamLogWindow()
        {
            InitializeComponent();
            DataContext = _ctx;
        }

        private void SaveExcel_Click(object sender, RoutedEventArgs e)
        {
            // 저장까지 끝났으면 기억해 둔다 — 종료할 때 "저장하지 않았습니다"로 다시 묻지 않는다.
            if (ExamLogExport.Save(_ctx.ExamLog.Rows)) _ctx.ExamLog.MarkSaved();
        }
    }
}
