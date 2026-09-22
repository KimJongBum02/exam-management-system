using System;
using System.Collections.Generic;
using System.Linq;
using NetworkLib;

namespace ProfessorUI.Service
{
    // 준비된 시험 묶음을 학생 PC 로 보낸다.
    //   누구에게 보낼지 고르는 것(표·선택)  → SendFileViewModel
    //   실제로 보내고 결과를 판단하는 것     → 여기
    //
    // 전송 진행률·실패는 네이티브에서 올라오므로 이 서비스가 그대로 흘려보낸다.
    // 창은 띄우지 않는다 — 문구는 돌려주고, 보여 주는 것은 화면이 한다.
    public class SendFileService
    {
        public static SendFileService Instance { get; } = new SendFileService();

        // 전송 진행률 (세션, 학번, 퍼센트). 네이티브 스레드에서 올라온다.
        public event Action<string, string, int>? Progress;

        // 전송 실패 (세션, 학번). 네이티브 스레드에서 올라온다.
        public event Action<string, string>? Failed;

        // 시험 중에 다시 보낸 학생. 받았다는 응답이 오면 그 학생에게만 시험 시작을 알린다.
        // [시험 시작] 신호는 누른 순간 접속해 있던 학생에게만 갔으므로, 늦게 받은 학생은
        // 이게 없으면 파일을 받고도 풀지 못하고 감시·인터넷 차단·타이머도 켜지지 않는다.
        private readonly HashSet<string> _startOnReceive = new HashSet<string>();

        // 학생마다 마지막으로 보낸 묶음과 그때의 접속. 받은 학생에게 같은 묶음을 또 보내지 않는 데 쓴다.
        // 묶음을 새로 만들면(다음 T 번호) 경로가 달라지므로 그때는 다시 보낼 수 있다.
        // 학생 앱을 다시 켜면 받은 파일을 모르는 채로 새로 접속하므로, 접속이 바뀌어도 다시 보낼 수 있다.
        private readonly Dictionary<string, (string SessionId, string Package)> _sentPackage = new();

        private SendFileService()
        {
            var network = NetworkService.Instance;
            network.SendProgress += (sessionId, studentId, _, _, percent) => Progress?.Invoke(sessionId, studentId, percent);
            network.SendError += (sessionId, studentId, _, _) => Failed?.Invoke(sessionId, studentId);

            // 학생이 '수신 완료'를 보내오면, 시험 중에 다시 받은 학생은 그 자리에서 시험을 시작시킨다.
            StudentStore.Instance.FileReceivedConfirmed += StartLateStudent;
        }

        // 보낼 준비가 됐는지. 안 됐으면 이유, 됐으면 null.
        public string? PackageProblem()
            => !SendFileState.IsFilePrepared || string.IsNullOrEmpty(SendFileState.PackagePath)
               ? "먼저 시험 파일을 암호화·압축해 주세요." : null;

        // 보내지 못한 이유. 화면은 이 값으로 표의 칸 문구를 정한다.
        public enum SendProblem
        {
            None,
            NotPrepared,     // 아직 압축·암호화를 하지 않음
            NotConnected,    // 접속 중인 학생이 아님
            AlreadySending,  // 같은 세션에 이미 보내는 중
            SendFailed,      // 전송을 시작하지 못함
        }

        // 한 명에게 보낸 결과. Ok 면 SessionId 에 전송을 시작한 세션이 담긴다.
        // Message 는 교수에게 그대로 보여 줄 수 있는 문구다.
        public sealed record SendResult(bool Ok, string SessionId, SendProblem Problem, string Message);

        // 학생 한 명에게 시험 묶음을 보낸다.
        //   alreadySendingSessionId — 이 학생에게 지금 보내는 중인 세션(없으면 null).
        //   같은 세션에 겹쳐 보내지 않도록 화면이 들고 있는 값을 그대로 넘긴다.
        public SendResult Send(string studentId, string? alreadySendingSessionId)
        {
            string? problem = PackageProblem();
            if (problem != null) return new SendResult(false, string.Empty, SendProblem.NotPrepared, problem);

            // 접속이 끊겨도 SessionId 는 남으므로 SessionId 유무로 판단하면 안 된다
            var connected = StudentStore.Instance.Students
                .FirstOrDefault(s => s.StudentId == studentId && s.IsConnected);
            if (connected == null)
                return new SendResult(false, string.Empty, SendProblem.NotConnected, "접속 중인 학생이 아닙니다.");

            // 재접속하면 세션이 달라지므로, 같은 세션에 중복으로 보내는 경우만 막는다.
            if (alreadySendingSessionId == connected.SessionId)
                return new SendResult(false, connected.SessionId, SendProblem.AlreadySending, "이미 전송 중입니다.");

            if (!NetworkService.Instance.SendFileToSession(
                    connected.SessionId, SendFileState.PackagePath!, SendFileState.Password ?? ""))
                return new SendResult(false, connected.SessionId, SendProblem.SendFailed, "파일을 보내지 못했습니다.");

            SendFileState.IsFileDistributed = true;
            _sentPackage[studentId] = (connected.SessionId, SendFileState.PackagePath!);

            // 시험 중이면 받는 대로 이 학생의 시험을 시작시킨다(StartLateStudent).
            if (ExamState.CurrentPhase == ExamPhase.InProgress)
                _startOnReceive.Add(studentId);

            return new SendResult(true, connected.SessionId, SendProblem.None, string.Empty);
        }

        // 지금 준비된 묶음을 지금 접속으로 이미 받았는지. 받았다는 응답까지 온 학생만 해당한다.
        public bool HasCurrentPackage(string studentId, string sessionId, bool isFileReceived)
            => isFileReceived
               && _sentPackage.TryGetValue(studentId, out var sent)
               && sent.SessionId == sessionId
               && sent.Package == SendFileState.PackagePath;

        // 시험 중에 다시 받은 학생에게만 [시험 시작]과 같은 순서로 보낸다(ExamStartViewModel).
        // 감시 목록 → 시험 시작(타이머) → 압축 해제(해제가 끝나면 감시·인터넷 차단을 켠다)
        private void StartLateStudent(string studentId)
        {
            if (!_startOnReceive.Remove(studentId) || ExamState.CurrentPhase != ExamPhase.InProgress) return;

            var connected = StudentStore.Instance.Students
                .FirstOrDefault(s => s.StudentId == studentId && s.IsConnected);
            if (connected == null) return;

            var network = NetworkService.Instance;
            network.SendToSession(connected.SessionId, PacketType.ProcessListUpdate,
                ProcessListPayload.Encode(ProgramControlStore.WhiteList, ProgramControlStore.BlackList));
            network.SendToSession(connected.SessionId, PacketType.ExamPhaseChange,
                ExamPhasePayload.Encode(ExamPhase.InProgress, "시험이 시작되었습니다."));
            network.SendToSession(connected.SessionId, PacketType.ExtractArchive, Array.Empty<byte>());
        }

        // 재배포 결과 안내에 붙이는 한 줄. 시험 중에만 보인다.
        public static string StartNote => ExamState.CurrentPhase == ExamPhase.InProgress
            ? "\n학생이 파일을 받으면 그 학생의 시험이 바로 시작됩니다."
            : string.Empty;
    }
}
