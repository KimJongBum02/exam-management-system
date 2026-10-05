using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using ProfessorUI.Model;

namespace ProfessorUI.Service
{
    // 수강생 명단. 학교 포털에서 받은 엑셀을 불러와 들고 있는다.
    //
    // 명단이 있으면 대시보드와 화면 모니터링이 엑셀 순서대로 학생 칸을 미리 깔아 둔다.
    // 학생이 접속하면 그 칸이 채워지고, 명단에 없는 학번이나 이름이 다른 학생은 눈에 띄게 표시된다.
    // 학번·이름을 잘못 쳐서 들어온 학생을 시험 전에 바로 찾기 위함이다.
    //
    // 학생 목록(StudentStore)과는 따로 둔다. 섞으면 배포 표·재배포 대상·채팅 목록에도
    // 접속하지 않은 학생 줄이 생긴다. 파일로 남기지 않으므로 앱을 다시 켜면 다시 불러온다.
    public static class StudentExcelStore
    {
        // 머리글을 찾아볼 위쪽 줄 수. 포털 파일은 맨 위에 제목·날짜 줄이 붙기도 해서 첫 줄로 단정하지 않는다.
        private const int HeaderSearchRows = 20;

        // 엑셀 순서 그대로다. 화면도 이 순서로 칸을 놓는다.
        public static IReadOnlyList<RosterEntry> Entries { get; private set; } = Array.Empty<RosterEntry>();

        public static string FileName { get; private set; } = string.Empty;

        public static bool HasRoster => Entries.Count > 0;

        // 명단을 새로 불러왔을 때. 불러오기는 화면에서 하므로 화면 스레드에서 알린다.
        public static event Action? Changed;

        public static RosterEntry? Find(string studentId)
            => Entries.FirstOrDefault(e => e.StudentId == studentId);

        // 학생이 로그인 때 친 이름이 명단과 같은지. 띄어쓰기는 보지 않는다.
        public static bool SameName(string a, string b)
            => Regex.Replace(a, @"\s", "") == Regex.Replace(b, @"\s", "");

        // 엑셀 파일을 읽어 명단을 바꾼다. 성공하면 null, 못 읽었으면 화면에 보일 이유.
        // 못 읽었으면 쓰던 명단을 그대로 둔다.
        public static string? Load(string path)
        {
            // ClosedXML 은 2007 이후 형식(.xlsx)만 읽는다
            if (Path.GetExtension(path).Equals(".xls", StringComparison.OrdinalIgnoreCase))
                return "예전 엑셀 형식(.xls)은 읽지 못합니다.\n엑셀에서 파일을 연 뒤 [다른 이름으로 저장] → 'Excel 통합 문서(*.xlsx)'로 저장해 다시 불러와 주세요.";

            List<RosterEntry> entries;
            try
            {
                // 엑셀에서 열어 둔 채로 불러와도 읽히도록 공유 모드로 연다.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var book = new XLWorkbook(stream);
                entries = Read(book);
            }
            catch (Exception)
            {
                return "엑셀 파일을 읽지 못했습니다.\n엑셀에서 파일을 연 뒤 'Excel 통합 문서(*.xlsx)'로 다시 저장해 불러와 주세요.";
            }

            if (entries.Count == 0)
                return "학번·이름 열을 찾지 못했습니다.\n파일 위쪽에 '학번'과 '이름'(또는 '성명') 머리글이 있는지 확인해 주세요.";

            Entries = entries;
            FileName = Path.GetFileName(path);
            Changed?.Invoke();
            return null;
        }

        // 시트마다 위쪽 줄에서 '학번'·'이름' 머리글이 함께 있는 줄을 찾고, 그 아래 두 열만 읽는다.
        // 다른 열(학과·학년·연락처 등)은 보지 않는다. 학번이나 이름이 빈 줄(빈 줄·합계 줄)은 건너뛰고,
        // 같은 학번이 또 나오면 처음 것만 둔다.
        private static List<RosterEntry> Read(XLWorkbook book)
        {
            foreach (var sheet in book.Worksheets)
            {
                var used = sheet.RangeUsed();
                if (used == null) continue;

                int lastRow = used.LastRow().RowNumber();
                int lastColumn = used.LastColumn().ColumnNumber();

                for (int header = 1; header <= Math.Min(lastRow, HeaderSearchRows); header++)
                {
                    var titles = Enumerable.Range(1, lastColumn)
                        .Select(column => Regex.Replace(Text(sheet.Cell(header, column)), @"\s", ""))
                        .ToList();

                    int idColumn = titles.FindIndex(t => t.Contains("학번")) + 1;
                    int nameColumn = NameColumn(titles);
                    if (idColumn == 0 || nameColumn == 0) continue;

                    var entries = new List<RosterEntry>();
                    for (int row = header + 1; row <= lastRow; row++)
                    {
                        string id = Text(sheet.Cell(row, idColumn));
                        string name = Text(sheet.Cell(row, nameColumn));
                        if (id.Length == 0 || name.Length == 0 || entries.Any(e => e.StudentId == id)) continue;

                        entries.Add(new RosterEntry(id, name));
                    }
                    if (entries.Count > 0) return entries;
                }
            }
            return new List<RosterEntry>();
        }

        // 이름 열. '이름'·'성명' 그대로인 칸을 먼저 찾고, 없으면 '학생이름'처럼 들어 있는 칸을 찾는다.
        // '영문이름'은 로그인 때 치는 한글 이름이 아니라서 뺀다. 못 찾으면 0.
        private static int NameColumn(List<string> titles)
        {
            int exact = titles.FindIndex(t => t is "이름" or "성명");
            if (exact >= 0) return exact + 1;

            return titles.FindIndex(t => (t.Contains("이름") || t.Contains("성명")) && !t.Contains("영문")) + 1;
        }

        // 칸에 보이는 그대로 읽는다. 학번이 숫자로 들어 있어도 202407021 처럼 나온다.
        private static string Text(IXLCell cell) => cell.GetFormattedString().Trim();
    }
}
