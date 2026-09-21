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
        // ── OX 퀴즈 세션 ──
        // 문제별 집계 · 학생 응답 하나하나 · 학생별 누적을 각각 다른 시트에 담는다.
        // 낸 순서대로 번호를 매긴다 (화면 목록은 최근 것이 위라 뒤집어서 쓴다).
        public static void SaveQuizSession(IEnumerable<QuizRound> rounds, string path)
        {
            var ordered = rounds.Reverse().ToList();

            using var book = new XLWorkbook();

            var bySheet = book.AddWorksheet("문제별");
            WriteHeader(bySheet, "번호", "출제 시각", "문제", "정답", "대상", "응답", "정답", "오답", "미응답");
            for (int i = 0; i < ordered.Count; i++)
            {
                var round = ordered[i];
                int row = i + 2;
                bySheet.Cell(row, 1).Value = i + 1;
                bySheet.Cell(row, 2).Value = round.AskedAt;
                bySheet.Cell(row, 3).Value = round.Question;
                bySheet.Cell(row, 4).Value = round.CorrectAnswerText;
                bySheet.Cell(row, 5).Value = round.TargetCount;
                bySheet.Cell(row, 6).Value = round.AnsweredCount;
                bySheet.Cell(row, 7).Value = round.CorrectCount;
                bySheet.Cell(row, 8).Value = round.WrongCount;
                bySheet.Cell(row, 9).Value = round.MissedCount;
            }
            Finish(bySheet);

            var detailSheet = book.AddWorksheet("응답 상세");
            WriteHeader(detailSheet, "번호", "문제", "학번", "이름", "응답", "결과", "응답 시각");
            int line = 2;
            for (int i = 0; i < ordered.Count; i++)
            {
                foreach (var response in ordered[i].Responses)
                {
                    detailSheet.Cell(line, 1).Value = i + 1;
                    detailSheet.Cell(line, 2).Value = ordered[i].Question;
                    detailSheet.Cell(line, 3).Value = response.StudentId;
                    detailSheet.Cell(line, 4).Value = response.StudentName;
                    detailSheet.Cell(line, 5).Value = response.AnswerText;
                    detailSheet.Cell(line, 6).Value = response.ResultText;
                    detailSheet.Cell(line, 7).Value = response.RespondedAt;
                    line++;
                }
            }
            Finish(detailSheet);

            var tallySheet = book.AddWorksheet("학생별 누적");
            WriteHeader(tallySheet, "학번", "이름", "출제", "응답", "정답", "미응답");
            var tallies = BuildTally(ordered);
            for (int i = 0; i < tallies.Count; i++)
            {
                var tally = tallies[i];
                int row = i + 2;
                tallySheet.Cell(row, 1).Value = tally.StudentId;
                tallySheet.Cell(row, 2).Value = tally.StudentName;
                tallySheet.Cell(row, 3).Value = tally.AskedCount;
                tallySheet.Cell(row, 4).Value = tally.AnsweredCount;
                tallySheet.Cell(row, 5).Value = tally.CorrectCount;
                tallySheet.Cell(row, 6).Value = tally.MissedCount;
            }
            Finish(tallySheet);

            SaveTo(book, path);
        }

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

        // 학생별 누적. QuizService 의 것과 같은 계산이지만,
        // 저장 시점의 목록을 그대로 쓰기 위해 넘겨받은 것으로 다시 센다.
        private static List<QuizStudentTally> BuildTally(IEnumerable<QuizRound> rounds)
        {
            var byStudent = new Dictionary<string, QuizStudentTally>();

            foreach (var round in rounds)
            {
                foreach (var response in round.Responses)
                {
                    if (!byStudent.TryGetValue(response.StudentId, out var tally))
                    {
                        tally = new QuizStudentTally
                        {
                            StudentId = response.StudentId,
                            StudentName = response.StudentName,
                        };
                        byStudent[response.StudentId] = tally;
                    }

                    tally.AskedCount++;
                    if (response.HasAnswered) tally.AnsweredCount++;
                    if (response.IsCorrect) tally.CorrectCount++;
                }
            }

            return byStudent.Values.OrderBy(t => t.StudentId).ToList();
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
