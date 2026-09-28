using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using ProfessorUI.Model;
using ProfessorUI.ViewModel;

namespace ProfessorUI.Service
{
    // 기록을 엑셀(.xlsx) 파일로 내보낸다.
    //
    // 교수가 성적·출결 처리에 그대로 쓰는 파일이라, 화면에 보이는 문구를 그대로 적는다.
    // 어떤 값을 쓸지는 화면·서비스가 정하고, 여기서는 표로 옮기는 일만 한다.
    public static class ExcelReport
    {
        // ── 수업 확인 퀴즈 한 번 ──
        // 화면의 결과 표와 같은 모양이다: 학생이 한 줄, 문제가 한 칸, 맞으면 연한 초록·틀리면 빨강.
        // 위의 두 줄에 문제별 정답과 정답률을 적고, 문제 내용은 "문제" 시트에 따로 둔다.
        public static void SaveQuizRound(QuizRound round, string path)
        {
            using var book = new XLWorkbook();
            var stats = round.BuildQuestionStats();
            int questionCount = round.Questions.Count;
            int scoreColumn = 3 + questionCount;

            var resultSheet = book.AddWorksheet("결과");
            WriteHeader(resultSheet, new[] { "학번", "이름" }
                .Concat(round.Questions.Select(q => q.Number.ToString()))
                .Concat(new[] { "점수", "제출 시각" }).ToArray());

            resultSheet.Cell(2, 1).Value = "정답";
            resultSheet.Cell(3, 1).Value = "정답률";
            for (int q = 0; q < questionCount; q++)
            {
                resultSheet.Cell(2, 3 + q).Value = stats[q].CorrectText;
                resultSheet.Cell(3, 3 + q).Value = stats[q].RateText;
            }
            resultSheet.Range(2, 1, 3, scoreColumn + 1).Style.Font.Bold = true;
            resultSheet.Range(2, 1, 3, scoreColumn + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F3F4F6");

            int row = 4;
            foreach (var submission in round.Submissions.OrderBy(s => s.StudentId, StringComparer.Ordinal))
            {
                resultSheet.Cell(row, 1).Value = submission.StudentId;
                resultSheet.Cell(row, 2).Value = submission.StudentName;
                for (int q = 0; q < questionCount; q++)
                {
                    var question = round.Questions[q];
                    var cell = resultSheet.Cell(row, 3 + q);
                    cell.Value = submission.AnswerTextOf(question);

                    var result = submission.ResultOf(question);
                    if (result == QuizSubmission.Result.Correct)
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml(CorrectFill);
                    else if (result == QuizSubmission.Result.Wrong)
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml(WrongFill);
                }
                resultSheet.Cell(row, scoreColumn).Value = submission.ScoreText;
                resultSheet.Cell(row, scoreColumn + 1).Value = submission.SubmittedAt;
                row++;
            }
            resultSheet.Range(1, 3, row - 1, scoreColumn + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            Finish(resultSheet);
            resultSheet.SheetView.Freeze(3, 2);   // 정답·정답률 줄과 학번·이름 칸이 따라온다

            var questionSheet = book.AddWorksheet("문제");
            WriteHeader(questionSheet, "번호", "문제", "보기", "정답", "맞음", "틀림", "미응답", "미제출", "정답률");
            for (int q = 0; q < questionCount; q++)
            {
                var stat = stats[q];
                var question = stat.Question;
                int r = q + 2;
                questionSheet.Cell(r, 1).Value = question.Number;
                questionSheet.Cell(r, 2).Value = question.Text;
                questionSheet.Cell(r, 3).Value = question.IsOX
                    ? "O / X"
                    : string.Join("  ", question.Options.Select((option, i) => $"{i + 1}) {option}"));
                questionSheet.Cell(r, 4).Value = question.CorrectText;
                questionSheet.Cell(r, 5).Value = stat.CorrectCount;
                questionSheet.Cell(r, 6).Value = stat.WrongCount;
                questionSheet.Cell(r, 7).Value = stat.BlankCount;
                questionSheet.Cell(r, 8).Value = round.MissedCount;
                questionSheet.Cell(r, 9).Value = stat.RateText;
            }
            Finish(questionSheet);

            SaveTo(book, path);
        }

        // 화면의 결과 표와 같은 색이다 (QuizWindow.xaml 의 CorrectCell·WrongCell).
        private const string CorrectFill = "#D9F2DF";
        private const string WrongFill = "#F5B5B5";

        // ── 시험 로그 ──
        // 학생 한 명이 한 줄이다. 부정행위를 한 학생만 마지막 칸에 내역이 적힌다.
        public static void SaveExamLog(IEnumerable<ExamLogRow> rows, string path)
        {
            using var book = new XLWorkbook();
            var sheet = book.AddWorksheet("시험 로그");
            WriteHeader(sheet, "학번", "이름", "출석 여부", "부정행위 횟수", "부정행위 내용");

            int row = 2;
            foreach (var entry in rows)
            {
                sheet.Cell(row, 1).Value = entry.StudentId;
                sheet.Cell(row, 2).Value = entry.Name;
                sheet.Cell(row, 3).Value = entry.AttendanceText;
                sheet.Cell(row, 4).Value = entry.CheatCount;
                sheet.Cell(row, 5).Value = entry.CheatDetail;
                row++;
            }

            // 부정행위가 여러 건이면 줄바꿈으로 이어 붙였다. 셀 안에서 줄이 접히게 해 둔다.
            sheet.Column(5).Style.Alignment.WrapText = true;
            sheet.Column(5).Width = 60;

            Finish(sheet, autoFitLastColumn: false);
            SaveTo(book, path);
        }

        private static void WriteHeader(IXLWorksheet sheet, params string[] titles)
        {
            for (int i = 0; i < titles.Length; i++)
                sheet.Cell(1, i + 1).Value = titles[i];

            var header = sheet.Row(1);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#EDEDEE");
            sheet.SheetView.FreezeRows(1);   // 학생이 많아도 머리글이 따라온다
        }

        private static void Finish(IXLWorksheet sheet, bool autoFitLastColumn = true)
        {
            if (autoFitLastColumn) sheet.Columns().AdjustToContents();
            else sheet.Columns(1, Math.Max(1, sheet.LastColumnUsed()?.ColumnNumber() - 1 ?? 1)).AdjustToContents();
        }

        // 저장할 폴더가 아직 없을 수 있다 (첫 저장).
        private static void SaveTo(XLWorkbook book, string path)
        {
            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            book.SaveAs(path);
        }
    }
}
