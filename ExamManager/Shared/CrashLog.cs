using System.IO;
using System.Text;

// 앱이 왜 죽었는지를 파일로 남기는 일을 담당하는 파일입니다. 두 앱이 함께 씁니다.

namespace ExamManager.Shared
{
    // 두는 곳은 바탕화면입니다 — 숨은 폴더에 두면 강의실에서 경로를 불러 주며 찾게 해야 하지만,
    // 바탕화면이면 "바탕화면의 crash.log 보내 주세요" 한마디로 끝납니다.
    // 읽기 전용인 강의실 PC 가 있어 설치 폴더에는 두지 않고, 다시 켜도 지우지 않고 뒤에 덧붙입니다.
    public static class CrashLog
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "crash.log");

        // 기록 한 건을 남기는 일을 담당합니다. 어느 스레드에서 불러도 되고, 여러 번 불려도 됩니다.
        // 남기지 못해도 그냥 넘어갑니다 — 여기서 또 예외를 내면 정작 정리가 돌지 못합니다.
        public static void Write(string where, Exception? ex)
        {
            try
            {
                // 한 PC 에서 두 앱을 함께 띄우면 같은 파일에 쓰므로 줄머리에 앱 이름을 적습니다.
                string app = Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "알 수 없는 앱";

                var text = new StringBuilder();
                text.AppendLine($"───── {DateTime.Now:yyyy-MM-dd HH:mm:ss} · {app} · {where} ─────");
                // ToString 하나에 예외 종류 · 메시지 · 스택 · 안쪽 예외까지 다 들어 있습니다.
                text.AppendLine(ex?.ToString() ?? "예외 객체가 없다 (네이티브에서 끝났을 수 있다)");
                text.AppendLine();

                File.AppendAllText(LogPath, text.ToString());
            }
            catch { }
        }
    }
}
