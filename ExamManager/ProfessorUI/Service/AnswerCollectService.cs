using NetworkLib;
using System;
using System.IO;

namespace ProfessorUI.Service
{
    // 학생이 보낸 답안을 받아 저장하고, 잘 받았다고 회신한다.
    //
    // 학생은 이 회신을 받아야만 자기 PC의 시험 파일을 지운다.
    // 그래서 여기서 회신을 빠뜨리면 학생 PC가 정리되지 않고 멈춰 있게 된다.
    //
    // 파일이 온전한지는 네이티브가 이미 SHA-256으로 검사한다(FileTransfer.cpp).
    // 손상된 파일은 FileReceived가 아예 발생하지 않으므로, 여기까지 왔다면 온전한 파일이다.
    public class AnswerCollectService
    {
        public static AnswerCollectService Instance { get; } = new AnswerCollectService();

        // 걷은 답안을 모아 둘 폴더. 교수가 바로 찾아갈 수 있도록 바탕화면에 둔다.
        // 이 아래에 학생별 폴더가 하나씩 쌓인다.
        public static string CollectFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "답안 파일");

        // 답안을 하나 받을 때마다 알린다 (학번, 저장 경로).
        public event Action<string, string>? AnswerCollected;

        private AnswerCollectService() { }

        // 앱 시작 시 한 번 호출 — 구독만 해둔다.
        public void Start()
        {
            NetworkService.Instance.FileReceived += OnFileReceived;
        }

        // 네이티브 수신 스레드에서 불린다. 화면은 건드리지 않는다.
        // senderId는 세션 ID다 (ClientSession.cpp에서 그렇게 넘긴다).
        private void OnFileReceived(string transferId, string sessionId, string fileName,
                                    string tempPath, long size, string password)
        {
            // 학생을 로그인 때 등록된 세션으로 찾는다. 파일 이름에 기대지 않는다.
            (string studentId, string studentName) = ResolveStudent(sessionId);

            string? savedPath = SaveAnswer(studentId, studentName, tempPath, password);
            bool success = savedPath != null;

            // 저장까지 끝난 뒤에 회신한다.
            // 먼저 회신하면 학생이 파일을 지운 다음에 교수 쪽 저장이 실패할 수 있다.
            NetworkService.Instance.SendToSession(
                sessionId,
                PacketType.CommandAck,
                CommandAckPayload.Encode(
                    PacketType.ExamSubmitRequest,
                    success,
                    success ? "답안을 정상적으로 받았습니다." : "답안을 저장하지 못했습니다."));

            if (success)
                AnswerCollected?.Invoke(studentId, savedPath!);
        }

        // 세션 ID로 학번과 이름을 찾는다. 못 찾으면 세션 ID를 학번 자리에 쓴다 —
        // 누구인지 몰라도 파일은 반드시 남겨야 하기 때문이다.
        private static (string StudentId, string Name) ResolveStudent(string sessionId)
        {
            foreach (var student in StudentStore.Instance.Students)
            {
                if (student.SessionId == sessionId && student.StudentId.Length > 0)
                    return (student.StudentId, student.Name);
            }
            return (sessionId, "");
        }

        // 네이티브가 임시 폴더에 받아 둔 답안을 학생별 폴더로 풀어 둔다.
        // 임시 파일은 언제 정리될지 모르므로 반드시 옮겨 두어야 한다.
        //
        // 학생이 보내는 답안은 암호가 걸린 .7z 다. 그대로 두면 교수가 열어 볼 수 없어
        // 받은 자리에서 바로 푼다. 암호는 학생에게 보낸 것과 같은 값이 함께 올라온다.
        private static string? SaveAnswer(string studentId, string studentName, string tempPath, string password)
        {
            try
            {
                Directory.CreateDirectory(CollectFolder);

                // 같은 학생이 다시 내면 덮어쓴다. 마지막 제출이 최종본이다.
                string studentFolder = Path.Combine(CollectFolder, BuildFolderName(studentId, studentName));
                string archivePath = studentFolder + ".7z";

                File.Copy(tempPath, archivePath, true);

                // 앞 제출이 남아 있으면 지우고 새로 푼다.
                // 남겨 두면 이번에 빠진 파일이 앞 제출분으로 남아 헷갈린다.
                if (Directory.Exists(studentFolder)) Directory.Delete(studentFolder, true);
                Directory.CreateDirectory(studentFolder);

                string sevenZa = Path.Combine(AppContext.BaseDirectory, "7za.exe");
                int code = FileControlService.FC_ExtractDecrypt(sevenZa, archivePath, studentFolder, password);

                if (code == 0)
                {
                    // 잘 풀렸으면 묶음 파일은 필요 없다.
                    try { File.Delete(archivePath); } catch { }
                    return studentFolder;
                }

                // 못 풀었으면 묶음이라도 남긴다. 답안을 잃는 것보다 낫다.
                System.Diagnostics.Debug.WriteLine($"답안 압축 해제 실패 (코드 {code}) — 묶음 파일로 남깁니다.");
                try { Directory.Delete(studentFolder, true); } catch { }
                return archivePath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"답안을 저장하지 못했습니다: {ex.Message}");
                return null;
            }
        }

        // 교수가 탐색기에서 바로 알아볼 수 있는 이름으로 짓는다.
        //   202407021 김종범
        // 이름은 학생이 로그인할 때 직접 입력한 값이라 경로에 못 쓰는 문자가 섞일 수 있다.
        // 그대로 쓰면 저장이 실패하거나 엉뚱한 폴더에 쓰게 되므로 걸러 낸다.
        private static string BuildFolderName(string studentId, string studentName)
        {
            string safeName = studentName;
            foreach (char bad in Path.GetInvalidFileNameChars())
                safeName = safeName.Replace(bad, '_');

            return $"{studentId} {safeName}".Trim();
        }
    }
}
