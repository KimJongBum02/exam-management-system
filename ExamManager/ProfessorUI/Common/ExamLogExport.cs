using System.Windows;
using Microsoft.Win32;
using ProfessorUI.Service;
using ProfessorUI.ViewModel;

namespace ProfessorUI.Common
{
    // 시험 로그를 엑셀로 내보낸다.
    //
    // 두 곳에서 부른다 — 시험 로그 화면의 [엑셀로 저장], 그리고 교수가 프로그램을 끌 때
    // "먼저 저장하시겠습니까"로 붙잡는 자리(MainWindow 의 종료 확인). 저장 흐름이 갈라지면
    // 한쪽만 고쳐져 파일 이름이나 실패 안내가 달라지므로 한 곳에 둔다.
    public static class ExamLogExport
    {
        // 저장했으면 true. 교수가 저장 창에서 취소했거나 실패하면 false 다.
        public static bool Save(IReadOnlyCollection<ExamLogRow> rows)
        {
            if (rows.Count == 0)
            {
                MessageBox.Show("저장할 기록이 없습니다.\n학생이 접속하면 목록이 채워집니다.",
                                "시험 로그", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var dialog = new SaveFileDialog
            {
                Title = "시험 로그 저장",
                Filter = "Excel 통합 문서 (*.xlsx)|*.xlsx",
                FileName = $"시험로그_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
            };
            if (dialog.ShowDialog() != true) return false;

            try
            {
                ExcelReport.SaveExamLog(rows, dialog.FileName);

                // 이번 시험 로그를 남겼다고 기록해 둔다. 시험 종료·답안 수집 화면의
                // [처음 화면으로] 가 이 값으로 열린다 — 정리하면 이 기록이 사라지기 때문이다.
                ExamState.IsLogSaved = true;

                MessageBox.Show($"저장했습니다.\n{dialog.FileName}",
                                "시험 로그", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                // 같은 파일을 엑셀에서 열어 둔 채로 저장하면 여기로 온다.
                MessageBox.Show($"저장하지 못했습니다.\n{ex.Message}\n\n같은 이름의 파일을 엑셀에서 열어 두었다면 닫고 다시 시도해 주세요.",
                                "시험 로그", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }
    }
}
