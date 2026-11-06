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

            // 단계를 올리면 시험 관리·시험 종료·답안 수집 메뉴가 열린다.
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

        // ── 학생 확인 완료 처리 ──
        // 고른 학생을 '확인 완료'로 기록하고 그 수를 돌려준다.
        // **학생 PC 를 건드리지 않는다** — 흔적 삭제는 학생이 답안 회신을 받은 뒤 스스로 한다.
        // 2026-09-22 교수님 지시로 여기 있던 PC 종료(셧다운)를 들어내고 기록만 남겼다.
        // 시험 흔적 삭제는 답안 회신을 받은 학생 쪽에서 이미 진행된다.
        public int ApproveStudents(IEnumerable<StudentStatusViewModel> students)
        {
            int approved = 0;

            foreach (var student in students)
            {
                student.IsApproved = true;
                student.IsSelected = false; // 승인한 학생은 더 고를 수 없으므로 체크도 푼다
                student.Status = "시험 종료";
                approved++;
            }

            return approved;
        }

        // ── 다음 시험 준비 ──
        // 시험 종료·답안 수집 화면의 [처음 화면으로] 가 부른다.
        //
        // 이것이 없으면 단계가 '시험 종료'(SubmitRequested)에 머문다. 그러면 파일을 다시 배포해도
        // 준비 마법사 3단계의 [시험 시작 실행]이 잠긴 채로 남고(ExamStartViewModel), 앱을 다시 켜도
        // 이어 받기 기록이 그 단계를 되살려 3시간 동안 풀리지 않는다.
        //
        // 학생 PC 에는 아무것도 보내지 않는다 — 학생 쪽 시험은 이미 끝났고, 다음 시험은
        // [시험 시작 실행]이 알린다. 걷은 답안과 시험 파일 묶음도 지우지 않는다.
        // 지우는 것은 화면에 남은 이번 시험의 기록뿐이다.
        public void ResetForNextExam()
        {
            StudentStore.Instance.ResetForNextExam();
            AlertStore.Instance.Clear();
            ConnectionLogStore.Instance.Clear();

            // 금지·허용 목록도 기본값으로. 학생 PC 에는 다음 시험을 시작할 때 다시 보낸다(StartExam).
            // 그 사이 통제 모드로 묶여 있는 학생은 옛 목록으로 막힌 채 있다 — 시험 전이라 문제되지 않는다.
            ProgramControlStore.ResetForNextExam();

            // 다음 시험 로그는 아직 저장한 적이 없다. 끄지 않으면 다음 정리가 그냥 열린다.
            ExamState.IsLogSaved = false;

            // 같은 파일을 다시 골랐을 때 "이미 압축했다"고 알리지 않게 한다 -- 아래에서 암호를 지우므로
            // 그 묶음은 더 쓸 수 없다(ExamReadyService.ResetForNextExam 설명 참고).
            ExamReadyService.Instance.ResetForNextExam();

            SendFileState.IsFilePrepared = false;
            SendFileState.IsFileDistributed = false;
            SendFileState.ExamId = null;
            SendFileState.PackagePath = null;
            SendFileState.Password = null;

            // 단계는 맨 마지막에 되돌린다. 이 값이 바뀌면 화면들이 한꺼번에 다시 그려지므로
            // 그때는 위의 것이 이미 비워져 있어야 한다. 끝낸 시각은 이 setter 가 함께 지운다.
            ExamState.CurrentPhase = ExamPhase.Waiting;

            // 이어 받기 기록도 지운다. 남겨 두면 다음에 앱을 켤 때 끝난 시험으로 다시 들어간다.
            ExamSessionStore.Clear();
        }
    }
}
