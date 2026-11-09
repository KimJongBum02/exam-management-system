using System.Diagnostics;
using System.IO;
using System.Text.Json;

// 프로그램이 꺼져도 남아야 하는 작은 기록을 담당하는 파일입니다
// (시험 이어받기 · 강의실 프로그램 목록 · 방화벽 흔적). 두 앱이 함께 씁니다.

namespace ExamManager.Shared
{
    // 두는 곳은 %LocalAppData%\ExamManager\ 입니다. 앱 폴더에 두면 프로그램을 다시 깔 때 같이 사라집니다.
    // '언제까지 유효한가 · 깨졌으면 어떻게 하는가' 는 기록마다 달라서 부르는 쪽에 남겨 둡니다.
    public static class AppRecord
    {
        // 기록 파일의 전체 경로를 담당합니다. 폴더는 쓸 때 만듭니다.
        public static string PathOf(string fileName) => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExamManager", fileName);

        // 기록을 읽는 일을 담당합니다. 파일이 없거나 깨졌으면 null 이고, 부르는 쪽은 빈 상태에서 다시 시작합니다.
        // what 은 실패를 알릴 때 쓰는 이름입니다("시험 기록" → "시험 기록을 읽지 못했습니다").
        public static T? ReadJson<T>(string path, string what) where T : class
        {
            try
            {
                return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{what}을 읽지 못했습니다: {ex.Message}");
                return null;
            }
        }

        // 기록을 남기는 일을 담당합니다. 폴더가 없으면 만듭니다.
        // 실패해도 이번 실행은 그대로 돌아갑니다 — 다시 켰을 때 이어받지 못할 뿐입니다.
        public static void WriteJson<T>(string path, T value, string what, bool indented = false)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path,
                    JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = indented }));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{what}을 남기지 못했습니다: {ex.Message}");
            }
        }

        // 기록을 지우는 일을 담당합니다. 파일이 없어도, 못 지워도 그냥 넘어갑니다.
        public static void Delete(string path)
        {
            try { File.Delete(path); } catch { }
        }
    }
}
