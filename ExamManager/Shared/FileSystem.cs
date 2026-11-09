using System.IO;
using System.Runtime.InteropServices;

// 시험 파일 · 답안을 다루는 일 중 두 앱이 똑같이 하는 것을 담당하는 파일입니다
// (폴더 지우기 · 폴더 복사 · 7za.exe 경로 · FileControl.dll 로 묶고 풀기).

namespace ExamManager.Shared
{
    // 네이티브 시그니처나 7za 위치가 바뀔 때 한쪽만 고치면 빌드는 통과하고 실행에서 깨지므로
    // 두 앱이 이 한 곳을 함께 씁니다.
    public static class FileSystem
    {
        // 폴더를 통째로 지우는 일을 담당합니다. 읽기 전용 파일이 섞여 있어도 지워집니다.
        //
        // Directory.Delete 는 읽기 전용 파일을 만나면 그냥 실패합니다. 시험 파일이나 학생 답안에는
        // 읽기 전용이 드물지 않아, 그대로 두면 답안 저장과 시험 파일 정리가 영문 모를 실패로 끝납니다.
        public static void DeleteFolder(string folder)
        {
            foreach (string path in Directory.GetFileSystemEntries(folder, "*", SearchOption.AllDirectories))
            {
                FileAttributes attributes = File.GetAttributes(path);
                if (attributes.HasFlag(FileAttributes.ReadOnly))
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }

            Directory.Delete(folder, true);
        }

        // 폴더를 통째로 복사하는 일을 담당합니다. .NET 에는 폴더 깊은 복사가 없어 직접 재귀합니다.
        // copyOne 을 주면 파일 하나하나를 그쪽이 옮깁니다(교수 쪽이 진행률을 세는 데 씁니다).
        // 주지 않으면 덮어쓰기로 그냥 복사합니다.
        public static void CopyFolder(string source, string dest, Action<string, string>? copyOne = null)
        {
            Directory.CreateDirectory(dest);

            foreach (string file in Directory.GetFiles(source))
            {
                string target = Path.Combine(dest, Path.GetFileName(file));
                if (copyOne == null) File.Copy(file, target, true);
                else copyOne(file, target);
            }

            foreach (string dir in Directory.GetDirectories(source))
                CopyFolder(dir, Path.Combine(dest, Path.GetFileName(dir)), copyOne);
        }

        // 7za.exe 의 자리를 담당합니다. 두 앱 모두 실행 파일 옆에 두고 씁니다(csproj 의 복사 규칙).
        public static string SevenZaPath => Path.Combine(AppContext.BaseDirectory, "7za.exe");

        // 폴더(또는 파일)를 묶고 AES-256 으로 잠그는 일을 담당합니다. 돌려주는 값은 7za 종료 코드(0 = 성공)입니다.
        // 코드 1 은 '일부 파일을 읽지 못해 빼고 묶었음' 이라 성공으로 보면 안 됩니다.
        public static int CompressEncrypt(string sourcePath, string outputArchive, string password)
            => FC_CompressEncrypt(SevenZaPath, sourcePath, outputArchive, password);

        // 묶음을 풀어 내는 일을 담당합니다. 돌려주는 값은 7za 종료 코드(0 = 성공)입니다.
        // 묶음이 없거나 암호가 틀리면 2 가 오고, 이때 출력 폴더는 만들어지지 않습니다.
        public static int ExtractDecrypt(string archivePath, string outputFolder, string password)
            => FC_ExtractDecrypt(SevenZaPath, archivePath, outputFolder, password);

        // ── FileControl.dll (C++) 의 P/Invoke 선언입니다 ──
        [DllImport("FileControl.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private static extern int FC_CompressEncrypt(
            string sevenZaPath, string sourcePath, string outputArchive, string password);

        [DllImport("FileControl.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private static extern int FC_ExtractDecrypt(
            string sevenZaPath, string archivePath, string outputFolder, string password);
    }
}
