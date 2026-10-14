using System.IO;
using System.Runtime.InteropServices;

namespace ExamManager.Shared
{
    // 시험 파일·답안을 다루는 일 중 두 앱이 똑같이 하는 것들을 모았다.
    //   · 폴더 통째로 지우기 (시험 파일 정리, 답안 폴더 교체)
    //   · 7za.exe 경로
    //   · FileControl.dll 로 묶고 풀기 (AES-256)
    //
    // 전에는 같은 선언과 같은 경로 계산이 교수·학생 양쪽에 한 벌씩 있었다.
    // 네이티브 시그니처나 7za 위치가 바뀔 때 한쪽만 고치면 빌드는 통과하고 실행에서 깨지므로 한 곳으로 모은다.
    public static class FileSystem
    {
        // 폴더를 통째로 지운다. 읽기 전용 파일이 섞여 있어도 지워진다.
        //
        // Directory.Delete 는 읽기 전용 파일을 만나면 "Access to the path ... is denied" 로 그냥 실패한다.
        // 시험 파일이나 학생 답안에는 읽기 전용이 섞이는 일이 드물지 않아서, 그대로 두면
        // 답안 저장(교수) 과 시험 파일 정리(학생) 가 영문 모를 실패로 끝난다.
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

        // 두 앱 모두 7za.exe 를 실행 파일 옆에 두고 쓴다 (csproj 의 복사 규칙).
        public static string SevenZaPath => Path.Combine(AppContext.BaseDirectory, "7za.exe");

        // 폴더(또는 파일)를 묶고 AES-256 으로 잠근다. 돌려주는 값은 7za 종료 코드(0 = 성공).
        // 코드 1 은 '일부 파일을 읽지 못해 빼고 묶었음' 이라 성공으로 보면 안 된다 — 부르는 쪽에서 가린다.
        public static int CompressEncrypt(string sourcePath, string outputArchive, string password)
            => FC_CompressEncrypt(SevenZaPath, sourcePath, outputArchive, password);

        // 묶음을 풀어 낸다. 돌려주는 값은 7za 종료 코드(0 = 성공).
        // 묶음이 없거나 암호가 틀리면 2 가 오고, 이때 출력 폴더는 만들어지지 않는다.
        public static int ExtractDecrypt(string archivePath, string outputFolder, string password)
            => FC_ExtractDecrypt(SevenZaPath, archivePath, outputFolder, password);

        // ── FileControl.dll (C++) 의 P/Invoke 선언 ──
        [DllImport("FileControl.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private static extern int FC_CompressEncrypt(
            string sevenZaPath, string sourcePath, string outputArchive, string password);

        [DllImport("FileControl.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        private static extern int FC_ExtractDecrypt(
            string sevenZaPath, string archivePath, string outputFolder, string password);
    }
}
