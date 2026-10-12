using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ExamManager.Shared;
using System.Text;

namespace ProfessorUI.Service
{
    // 선택한 파일들을 스테이징 폴더 하나로 묶어 7za로 압축·암호화한다.
    //   "무엇을 압축할지(파일 정리·암호)" → 이 클래스 (C#)
    //   "어떻게 압축할지(7za 실행)"      → FileControl.dll (C++)
    internal static class ZipService
    {
        private const string PackageFolderName = "ExamFiles"; // 선택 항목을 모을 스테이징 폴더명

        // 진행률을 나누는 기준.
        // 파일을 모으는 일(복사)과 묶는 일(7za)이 둘 다 디스크를 크게 쓰므로 비슷하게 나눠 준다.
        private const int GatherEnd = 55;   // 여기까지가 '모으는 중'
        private const int ArchiveEnd = 95;  // 여기까지가 '압축·암호화 중', 나머지는 마무리

        // 성공 시 사용된 랜덤 암호 반환(배포 시 전송용), 실패 시 null
        //
        // progress 는 (퍼센트, 지금 하는 일) 을 받는다. 없으면 진행률을 알리지 않는다.
        // 7za 는 진행률을 돌려주지 않으므로, 압축 중에는 만들어지고 있는 묶음 파일의 크기를
        // 원본 크기와 견주어 어림한다. 정확한 값은 아니지만 멈춰 있지 않다는 것은 보여 준다.
        public static string? Package(IReadOnlyList<string> sourceItems, string outputArchive,
                                      IProgress<(int Percent, string Phase)>? progress = null)
        {
            if (sourceItems == null || sourceItems.Count == 0)
                return null;

            long totalBytes = TotalSize(sourceItems);

            string stagingRoot = Path.Combine(Path.GetTempPath(), "ExamPkg_" + Guid.NewGuid().ToString("N"));
            string stagingContent = Path.Combine(stagingRoot, PackageFolderName);

            // 새 묶음은 옆에 임시 이름으로 만든 뒤, 다 만들어지면 기존 묶음과 바꾼다.
            // 기존 묶음을 먼저 지우면 압축이 실패했을 때 배포할 파일이 사라진다.
            string pendingArchive = outputArchive + ".tmp";

            try
            {
                Directory.CreateDirectory(stagingContent);

                // 1) 선택 항목(파일/폴더)을 스테이징 폴더 하나로 모은다.
                //    복사한 바이트만큼 진행률을 올린다 — 이 구간은 우리가 세고 있어 정확하다.
                long copied = 0;
                progress?.Report((0, "시험 파일을 모으는 중..."));

                void CopyOne(string from, string to)
                {
                    File.Copy(from, to, true);
                    copied += FileLength(from);
                    progress?.Report((Share(copied, totalBytes, 0, GatherEnd), "시험 파일을 모으는 중..."));
                }

                foreach (string item in sourceItems)
                {
                    if (Directory.Exists(item))
                        CopyDirectory(item, Path.Combine(stagingContent, Path.GetFileName(item.TrimEnd('\\'))), CopyOne);
                    else if (File.Exists(item))
                        CopyOne(item, Path.Combine(stagingContent, Path.GetFileName(item)));
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
                //
                //    7za 는 한 번 부르면 끝날 때까지 아무것도 돌려주지 않는다. 그동안 화면이 멈춘 것처럼
                //    보이지 않도록, 만들어지는 중인 묶음 파일이 얼마나 커졌는지를 따로 지켜보며 올린다.
                int code;
                using (var archiving = new CancellationTokenSource())
                {
                    Task watcher = Task.Run(() => WatchArchiveGrowing(pendingArchive, totalBytes, progress, archiving.Token));
                    try
                    {
                        code = FileSystem.CompressEncrypt(Path.Combine(stagingContent, "*"), pendingArchive, password);
                    }
                    finally
                    {
                        archiving.Cancel();
                        watcher.Wait();
                    }
                }

                if (code != 0) return null;

                // 5) 다 만들어졌으니 기존 묶음과 바꾼다.
                //    배포 중이라 기존 묶음이 열려 있으면 여기서 예외가 난다. 기존 묶음은 그대로 남는다.
                progress?.Report((ArchiveEnd, "묶음 파일을 저장하는 중..."));
                File.Move(pendingArchive, outputArchive, true);
                progress?.Report((100, "압축·암호화 완료!"));
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

        // .NET에는 폴더 깊은 복사가 없어 직접 재귀 복사.
        // 파일 하나하나는 넘겨받은 copyOne 이 옮긴다 — 부르는 쪽이 복사한 양을 셀 수 있게 한다.
        private static void CopyDirectory(string source, string dest, Action<string, string> copyOne)
        {
            Directory.CreateDirectory(dest);
            foreach (string file in Directory.GetFiles(source))
                copyOne(file, Path.Combine(dest, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)), copyOne);
        }

        // 압축이 도는 동안 묶음 파일이 커지는 것을 보고 진행률을 올린다.
        // 되돌아가지 않도록 올라간 값만 알린다. 압축률에 따라 ArchiveEnd 에 못 미친 채 끝날 수 있는데,
        // 그때는 마지막에 저장 단계가 ArchiveEnd 로 올려 준다.
        private static void WatchArchiveGrowing(string archivePath, long totalBytes,
                                                IProgress<(int, string)>? progress, CancellationToken token)
        {
            if (progress == null || totalBytes <= 0) return;

            int last = GatherEnd;
            while (!token.IsCancellationRequested)
            {
                Thread.Sleep(200);

                int percent = Share(FileLength(archivePath), totalBytes, GatherEnd, ArchiveEnd);
                if (percent <= last) continue;

                last = percent;
                progress.Report((last, "압축·암호화하는 중..."));
            }
        }

        // done/total 을 from~to 구간 안의 퍼센트로 바꾼다.
        private static int Share(long done, long total, int from, int to)
        {
            if (total <= 0) return from;
            double ratio = Math.Min(1.0, (double)done / total);
            return from + (int)((to - from) * ratio);
        }

        // 고른 항목 전체의 바이트 수. 읽지 못하는 것은 0 으로 친다 — 진행률 계산에만 쓴다.
        private static long TotalSize(IReadOnlyList<string> items)
        {
            long total = 0;
            foreach (string item in items)
            {
                if (Directory.Exists(item))
                    foreach (string file in Directory.GetFiles(item, "*", SearchOption.AllDirectories))
                        total += FileLength(file);
                else
                    total += FileLength(item);
            }
            return total;
        }

        private static long FileLength(string path)
        {
            try { return new FileInfo(path).Length; } catch (Exception) { return 0; }
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