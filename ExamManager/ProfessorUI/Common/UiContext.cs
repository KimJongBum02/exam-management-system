using ProfessorUI.Service;
using ProfessorUI.ViewModel;

namespace ProfessorUI.Common
{
    // 새 화면들이 함께 쓰는 뷰모델 모음.
    //
    // 화면은 메뉴를 옮길 때마다 새로 만들어진다. 뷰모델을 화면이 직접 들고 있으면
    // 네트워크 이벤트 구독이 화면을 연 횟수만큼 쌓이고, 배포 진행률처럼 쌓아 온 상태도
    // 화면을 옮길 때마다 사라진다. 그래서 한 번만 만들어 여기서 나눠 준다.
    //
    // 대부분 기존 뷰모델을 그대로 쓴다. 새로 만든 것은 서버 열기(ServerStatusViewModel)와
    // 여러 화면이 함께 쓰는 집계(ExamSummaryViewModel) 둘뿐이다.
    public class UiContext
    {
        public static UiContext Instance { get; } = new UiContext();

        private UiContext() { }

        // ── 기존 뷰모델 재사용 ──
        public ExamReadyViewModel FileReady { get; } = new();
        public SendFileViewModel Distribute { get; } = new();
        public ExamStartViewModel ExamStart { get; } = new();
        public ExamEndViewModel ExamEnd { get; } = new();
        public ProgramControlViewModel WhiteList { get; } = new(ProgramListKind.White);
        public ProgramControlViewModel BlackList { get; } = new(ProgramListKind.Black);
        public ChatViewModel Chat { get; } = new();
        public SurveyViewModel Survey { get; } = new();
        public QuizViewModel Quiz { get; } = new();

        // ── 새 흐름에서 필요해진 것 ──
        public ServerStatusViewModel Server { get; } = new();
        public ExamSummaryViewModel Overview { get; } = new();

        // 시험 로그 표. 학생 목록과 경고 목록을 학번으로 이어 붙여 들고 있다.
        public ExamLogViewModel ExamLog { get; } = new();

        // 대시보드 학생 칸. 수강생 명단과 접속한 학생을 학번으로 이어 붙여 들고 있다.
        public StudentBoardViewModel Board { get; } = new();

        // ── 다음 시험 준비 ──
        // 시험 종료·답안 수집 화면의 [처음 화면으로] 가 부르는 한 지점.
        //
        // 저장소 쪽(시험 단계·배포 상태·학생별 기록·경고·접속 기록)은 ExamFlowService 가 되돌리고,
        // 화면이 따로 들고 있는 값은 그 뷰모델이 스스로 되돌린다 — 고른 시험 파일과 압축 결과,
        // 배포 결과 문구, 대화 목록, 시험 로그의 저장 표시가 그것이다. 저장소만 되돌리면 마법사 1·2단계와 채팅에
        // 지난 시험의 값이 그대로 남는다(2026-11-04 확인).
        public void ResetForNextExam()
        {
            ExamFlowService.Instance.ResetForNextExam();

            FileReady.ResetForNextExam();
            Distribute.ResetForNextExam();
            Chat.ResetForNextExam();
            ExamLog.ResetForNextExam();
        }
    }
}
