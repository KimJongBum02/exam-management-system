using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ProfessorUI.Service
{
    // 선택한 파일들을 스테이징 폴더 하나로 묶어 7za로 압축+암호화한다.
    //   "무엇을 압축할지(파일 정리·암호)" → 이 클래스 (C#)
    //   "어떻게 압축할지(7za 실행)"      → FileControl.dll (C++)
    internal static class ExamPackager
    {
        private const string PackageFolderName = "ExamFiles"; // 해제 시 생길 루트 폴더명

        // 성공 시 사용된 랜덤 암호 반환(배포 시 전송용), 실패 시 null
        public static string? Package(IReadOnlyList<string> sourceItems, string outputArchive)
        {
            if (sourceItems == null || sourceItems.Count == 0)
                return null;

            string sevenZa = Path.Combine(AppContext.BaseDirectory, "7za.exe");
            string stagingRoot = Path.Combine(Path.GetTempPath(), "ExamPkg_" + Guid.NewGuid().ToString("N"));
            string stagingContent = Path.Combine(stagingRoot, PackageFolderName);

            try
            {
                Directory.CreateDirectory(stagingContent);

                // 1) 선택 항목(파일/폴더)을 스테이징 폴더 하나로 모은다
                foreach (string item in sourceItems)
                {
                    if (Directory.Exists(item))
                        CopyDirectory(item, Path.Combine(stagingContent, Path.GetFileName(item.TrimEnd('\\'))));
                    else if (File.Exists(item))
                        File.Copy(item, Path.Combine(stagingContent, Path.GetFileName(item)), true);
                }

                // 2) 시험별 랜덤 암호 생성
                string password = GeneratePassword();

                // 3) DLL 호출: 스테이징 폴더 → 암호 걸린 .7z
                int code = FileControlService.FC_CompressEncrypt(sevenZa, stagingContent, outputArchive, password);

                return code == 0 ? password : null;
            }
            finally
            {
                // 4) 스테이징 폴더 정리 (성공/실패 무관)
                if (Directory.Exists(stagingRoot))
                    Directory.Delete(stagingRoot, true);
            }


        }

        // [테스트용] .7z를 같은 암호로 해제 → outputFolder 에 복원. 성공 시 true
        public static bool Extract(string archivePath, string outputFolder, string password)
        {
            string sevenZa = Path.Combine(AppContext.BaseDirectory, "7za.exe");
            Directory.CreateDirectory(outputFolder);
            int code = FileControlService.FC_ExtractDecrypt(sevenZa, archivePath, outputFolder, password);
            return code == 0;
        }

        // .NET에는 폴더 깊은 복사가 없어 직접 재귀 복사
        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
            foreach (string dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }

        // 혼동되기 쉬운 0/O/1/l 제외한 문자로 랜덤 암호 생성
        private static string GeneratePassword(int length = 12)
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
            byte[] bytes = RandomNumberGenerator.GetBytes(length);
            var sb = new StringBuilder(length);
            foreach (byte b in bytes)
                sb.Append(chars[b % chars.Length]);
            return sb.ToString();
        }
    }
}