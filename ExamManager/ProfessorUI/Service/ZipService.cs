using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ProfessorUI.Service
{
    // 선택한 파일들을 스테이징 폴더 하나로 묶어 7za로 압축+암호화한다.
    //   "무엇을 압축할지(파일 정리·암호)" → 이 클래스 (C#)
    //   "어떻게 압축할지(7za 실행)"      → FileControl.dll (C++)
    internal static class ZipService
    {
        private const string PackageFolderName = "ExamFiles"; // 선택 항목을 모을 스테이징 폴더명

        // 성공 시 사용된 랜덤 암호 반환(배포 시 전송용), 실패 시 null
        public static string? Package(IReadOnlyList<string> sourceItems, string outputArchive)
        {
            if (sourceItems == null || sourceItems.Count == 0)
                return null;

            string sevenZa = Path.Combine(AppContext.BaseDirectory, "7za.exe");
            string stagingRoot = Path.Combine(Path.GetTempPath(), "ExamPkg_" + Guid.NewGuid().ToString("N"));
            string stagingContent = Path.Combine(stagingRoot, PackageFolderName);

            // 새 묶음은 옆에 임시 이름으로 만든 뒤, 다 만들어지면 기존 묶음과 바꾼다.
            // 기존 묶음을 먼저 지우면 압축이 실패했을 때 배포할 파일이 사라진다.
            string pendingArchive = outputArchive + ".tmp";

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

                // 3) 지난번에 실패해 남은 임시 묶음이 있으면 지운다.
                //    7za의 'a'는 기존 아카이브에 "추가"하는데, -mhe=on 이라 이전 암호로 잠긴
                //    헤더를 새 암호로는 열지 못해 그대로 실패한다.
                if (File.Exists(pendingArchive))
                    File.Delete(pendingArchive);

                // 4) DLL 호출: 스테이징 폴더의 "내용물" → 암호 걸린 .7z (-t7z 라 확장자가 달라도 된다)
                //    폴더째 넣으면 학생 쪽에서 ExamFiles\ExamFiles\... 로 한 겹 더 들어간다.
                int code = ZipNative.FC_CompressEncrypt(
                    sevenZa, Path.Combine(stagingContent, "*"), pendingArchive, password);
                if (code != 0) return null;

                // 5) 다 만들어졌으니 기존 묶음과 바꾼다.
                //    배포 중이라 기존 묶음이 열려 있으면 여기서 예외가 난다. 기존 묶음은 그대로 남는다.
                File.Move(pendingArchive, outputArchive, true);
                return password;
            }
            finally
            {
                // 6) 스테이징 폴더와, 바꾸지 못한 임시 묶음 정리 (성공/실패 무관)
                if (Directory.Exists(stagingRoot))
                    Directory.Delete(stagingRoot, true);
                if (File.Exists(pendingArchive))
                    File.Delete(pendingArchive);
            }


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