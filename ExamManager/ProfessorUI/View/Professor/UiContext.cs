using ProfessorUI.ViewModel;

namespace ProfessorUI.View.Professor
{
    // 새 화면들이 함께 쓰는 뷰모델 모음.
    //
    // 화면은 메뉴를 옮길 때마다 새로 만들어진다. 뷰모델을 화면이 직접 들고 있으면
    // 네트워크 이벤트 구독이 화면을 연 횟수만큼 쌓이고, 배포 진행률처럼 쌓아 온 상태도
    // 화면을 옮길 때마다 사라진다. 그래서 한 번만 만들어 여기서 나눠 준다.
    //
    // 대부분 기존 뷰모델을 그대로 쓴다. 새로 만든 것은 서버 열기(ServerViewModel)와
    // 여러 화면이 함께 쓰는 집계(ExamOverviewViewModel) 둘뿐이다.
    public class UiContext
    {
        public static UiContext Instance { get; } = new UiContext();

        private UiContext() { }

        // ── 기존 뷰모델 재사용 ──
        public FileReadyViewModel FileReady { get; } = new();
        public FileDistributeViewModel Distribute { get; } = new();
        public ExamStartViewModel ExamStart { get; } = new();
        public AnswerCollectViewModel AnswerCollect { get; } = new();
        public ExamEndViewModel ExamEnd { get; } = new();
        public WhiteListViewModel WhiteList { get; } = new();
        public BlackListViewModel BlackList { get; } = new();
        public ChatViewModel Chat { get; } = new();
        public OXQuizViewModel Quiz { get; } = new();

        // 출제와 응답 기록은 서비스가 맡는다. 화면은 그것을 보여 주기만 한다.
        public Service.QuizSessionService QuizSession => Service.QuizSessionService.Instance;

        // ── 새 흐름에서 필요해진 것 ──
        public ServerViewModel Server { get; } = new();
        public ExamOverviewViewModel Overview { get; } = new();
    }
}
