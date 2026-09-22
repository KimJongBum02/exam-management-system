using System;
using System.Collections.Generic;
using System.Linq;
using NetworkLib;
using ProfessorUI.ViewModel;

namespace ProfessorUI.Service
{
    // 시험을 시작하고 끝내는 절차를 맡는다.
    //   무엇을 누를 수 있는지·결과를 어떻게 보여 줄지 → 화면과 뷰모델
    //   어떤 패킷을 어떤 순서로 보낼지·단계를 언제 올릴지 → 여기
    //
    // 창은 띄우지 않는다. 결과만 돌려주고 보여 주는 것은 화면이 한다.
    public class ExamFlowService
    {
        public static ExamFlowService Instance { get; } = new ExamFlowService();

        private ExamFlowService() { }

        // ── 시험 시작 ──
        // 보내는 순서가 중요하다.
        //   ① 감시 목록 — 학생 쪽은 목록을 받아 둔 뒤 감시를 켠다. 뒤로 가면 빈 목록으로 감시가 시작된다.
        //   ② 시작 신호 — 압축 해제에 걸리는 시간은 PC마다 달라, 해제 뒤에 보내면 시작 시각이 학생마다 어긋난다.
        //   ③ 압축 해제 — 학생 쪽은 해제가 끝나면 감시·인터넷 차단을 켜고 시험 폴더를 띄운다.
        public void StartExam()
        {
            var network = NetworkService.Instance;

            network.Broadcast(PacketType.ProcessListUpdate,
                ProcessListPayload.Encode(ProgramControlStore.WhiteList, ProgramControlStore.BlackList));

            network.Broadcast(PacketType.ExamPhaseChange,
                ExamPhasePayload.Encode(ExamPhase.InProgress, "시험이 시작되었습니다."));

            network.Broadcast(PacketType.ExtractArchive, Array.Empty<byte>());

            // 단계를 올리면 시험 관리·종료 및 정산 메뉴가 열린다.
            ExamState.CurrentPhase = ExamPhase.InProgress;
        }

        // ── 시험 종료 ──
        // 학생 PC 는 이 신호를 받고 프로세스 감시를 멈춘다.
        // 이걸 보내지 않으면 시험이 끝나도 학생 PC 에서 메모장이 계속 강제 종료되고,
        // 답안 수집 때 압축에 쓰는 7za.exe 가 부정행위로 적발된다.
        public void EndExam()
        {
            NetworkService.Instance.Broadcast(PacketType.ExamPhaseChange,
                ExamPhasePayload.Encode(ExamPhase.SubmitRequested, "시험이 종료되었습니다."));

            ExamState.CurrentPhase = ExamPhase.SubmitRequested;
        }

        // ── 답안 일괄 수집 ──
        // 학생이 답안을 보내오면 AnswerCollectService 가 받아 저장한다.
        public void RequestAnswers()
        {
            NetworkService.Instance.Broadcast(PacketType.ExamSubmitRequest,
                ExamSubmitPayload.Encode("", SendFileState.Password ?? ""));
        }

        // ── 미수집 학생에게 답안 다시 요청 ──
        // 첫 수집이 실패한 학생(편집기를 열어 둠, 전송 중 끊김 등)을 다시 걷는 경로다.
        // 결과: 요청을 보낸 학생 수, 접속이 끊겨 보내지 못한 학생.
        public sealed record RecollectResult(int Requested, IReadOnlyList<string> Offline);

        public RecollectResult RequestMissingAnswers()
        {
            var offline = new List<string>();
            int requested = 0;
            byte[] payload = ExamSubmitPayload.Encode("", SendFileState.Password ?? "");

            // 이번 시험을 치른 학생만 대상이다. 파일도 받지 않은 학생에게 보내면 그 학생 화면에 '제출 실패' 창만 뜬다.
            foreach (var student in StudentStore.Instance.Students
                         .Where(s => !s.IsAnswerSubmitted && (s.HasEverStarted || s.IsFileReceived)))
            {
                if (!student.IsConnected || string.IsNullOrEmpty(student.SessionId))
                {
                    offline.Add($"{student.StudentId} {student.Name}");
                    continue;
                }

                NetworkService.Instance.SendToSession(student.SessionId, PacketType.ExamSubmitRequest, payload);
                requested++;
            }

            return new RecollectResult(requested, offline);
        }

        // ── 감시 목록 재전송 ──
        // 시험 도중 목록을 갈아 끼우는 유일한 경로다. 시험 전에는 StartExam 이 함께 보낸다.
        public void SendProgramPolicy()
        {
            NetworkService.Instance.Broadcast(PacketType.ProcessListUpdate,
                ProcessListPayload.Encode(ProgramControlStore.WhiteList, ProgramControlStore.BlackList));
        }

        // ── 학생 승인 ──
        // 승인 결과. Offline 은 접속이 끊겨 종료 명령을 보내지 못한 학생들이다.
        public sealed record ApproveResult(int Approved, IReadOnlyList<string> Offline);

        // 고른 학생을 승인하고 그 PC 에 종료 명령을 보낸다.
        // 시험 흔적 삭제는 답안 회신을 받은 학생 쪽에서 이미 진행되므로 여기서는 종료만 지시한다.
        public ApproveResult ApproveStudents(IEnumerable<StudentStatusViewModel> students)
        {
            var offline = new List<string>();
            int approved = 0;

            foreach (var student in students)
            {
                if (!ShutdownStudentPc(student))
                    offline.Add($"{student.StudentId} {student.Name}");

                student.IsApproved = true;
                student.IsSelected = false; // 승인한 학생은 더 고를 수 없으므로 체크도 푼다
                student.Status = "종료";
                approved++;
            }

            return new ApproveResult(approved, offline);
        }

        // 학생 PC 에 종료 명령을 보낸다. 접속이 끊겨 보내지 못했으면 false.
        private static bool ShutdownStudentPc(StudentStatusViewModel student)
        {
            if (!student.IsConnected || string.IsNullOrEmpty(student.SessionId)) return false;

            NetworkService.Instance.SendToSession(
                student.SessionId, PacketType.ShutdownPC, Array.Empty<byte>());
            return true;
        }

        // ── 새 시험 준비 ──
        // 모든 과정이 끝난 뒤 학생별 기록과 시험 단계를 되돌린다. 걷은 답안 파일은 건드리지 않는다.
        // 시험 중에는 되돌리지 않는다 — 아직 푸는 학생이 있는데 단계가 대기로 돌아가기 때문이다.
        public bool ResetForNextExam()
        {
            if (ExamState.CurrentPhase < ExamPhase.SubmitRequested) return false;

            foreach (var student in StudentStore.Instance.Students)
            {
                student.IsSelected = false;
                student.IsFileReceived = false;
                student.IsAnswerSubmitted = false;
                student.SubmittedAt = null;
                student.IsApproved = false;
                student.IsCleanupFailed = false;
                student.IsCleanupDone = false;
                student.HasEverStarted = false;
                student.Status = student.IsConnected ? "대기" : "미접속";

                // 배포 표도 처음 상태로. 그대로 두면 다음 시험 준비 화면에 지난 시험의 '수신완료'가 남는다.
                student.IsDeployTarget = true;
                student.DeployStatus = "대기 중";
                student.DeployProgress = 0;
                student.SendingSessionId = null;
            }

            AlertStore.Instance.Clear();
            SendFileState.IsFileDistributed = false;
            ExamState.CurrentPhase = ExamPhase.Waiting;

            // 접속 중인 학생들에게도 시험 초기화(대기 상태)를 알린다.
            NetworkService.Instance.Broadcast(PacketType.ExamPhaseChange,
                ExamPhasePayload.Encode(ExamPhase.Waiting, "시험이 초기화되었습니다."));
            return true;
        }
    }
}
