using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ProfessorUI.Common;
using ProfessorUI.Service;

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
            if (_ctx.ExamLog.Rows.Count == 0)
            {
                MessageBox.Show("저장할 기록이 없습니다.\n학생이 접속하면 목록이 채워집니다.",
                                "시험 로그", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "시험 로그 저장",
                Filter = "Excel 통합 문서 (*.xlsx)|*.xlsx",
                FileName = $"시험로그_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                ExcelReport.SaveExamLog(_ctx.ExamLog.Rows, dialog.FileName);
                MessageBox.Show($"저장했습니다.\n{dialog.FileName}",
                                "시험 로그", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                // 같은 파일을 엑셀에서 열어 둔 채로 저장하면 여기로 온다.
                MessageBox.Show($"저장하지 못했습니다.\n{ex.Message}\n\n같은 이름의 파일을 엑셀에서 열어 두었다면 닫고 다시 시도해 주세요.",
                                "시험 로그", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
