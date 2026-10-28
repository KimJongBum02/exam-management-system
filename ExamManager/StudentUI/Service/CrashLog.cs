using System.IO;
using System.Text;

namespace StudentUI.Service
{
    // 학생 앱이 왜 죽었는지 파일로 남긴다.
    //
    // 학생 PC 는 교수가 들여다볼 수 없고, 앱이 죽으면 화면도 함께 사라진다. 남기지 않으면
    // "시험 시작을 눌렀더니 학생 앱이 사라졌다"는 말만 남고 원인을 좁힐 길이 없다.
    //
    // 두는 곳은 바탕화면이다. 숨은 폴더에 있으면 강의실에서 학생에게 경로를 불러 주며 찾게 해야 하는데,
    // 바탕화면에 있으면 "바탕화면의 crash.log 보내 주세요" 한마디로 끝난다.
    // 시험 파일을 바탕화면에 두는 것과 같은 방식이다(ExamFileStore.ExtractFolder).
    // 설치 폴더에는 두지 않는다 — 읽기 전용인 강의실 PC 가 있다.
    // 다시 켜도 지우지 않고 뒤에 덧붙인다(같은 PC 에서 여러 번 죽는 것을 보려면 필요하다).
    internal static class CrashLog
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "crash.log");

        // 어느 스레드에서 불러도 되고, 여러 번 불려도 된다.
        // 남기지 못해도 그냥 넘어간다 — 여기서 또 예외를 내면 정작 정리(RestoreSafely)가 돌지 못한다.
        public static void Write(string where, Exception? ex)
        {
            try
            {
                var text = new StringBuilder();
                text.AppendLine($"───── {DateTime.Now:yyyy-MM-dd HH:mm:ss} · {where} ─────");
                // ToString 하나에 예외 종류 · 메시지 · 스택 · 안쪽 예외까지 다 들어 있다.
                text.AppendLine(ex?.ToString() ?? "예외 객체가 없다 (네이티브에서 끝났을 수 있다)");
                text.AppendLine();

                File.AppendAllText(LogPath, text.ToString());
            }
            catch { }
        }
    }
}
