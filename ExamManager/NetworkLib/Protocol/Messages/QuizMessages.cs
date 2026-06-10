namespace NetworkLib.Protocol.Messages
{
    // ─── 퀴즈 문제 전송 (Server → Client) ─────────────────────────

    /// <summary>교수가 단답형 또는 OX 퀴즈를 학생에게 전송하는 메시지</summary>
    public class QuizQuestionMessage
    {
        /// <summary>퀴즈 고유 ID (응답 매칭에 사용)</summary>
        public string QuizId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>문제 유형</summary>
        public QuizType QuestionType { get; set; }

        /// <summary>문제 내용</summary>
        public string Question { get; set; } = string.Empty;

        /// <summary>
        /// 선택지 목록 (객관식일 경우 사용).
        /// OX 퀴즈: ["O", "X"]
        /// 단답형: null 또는 빈 목록
        /// </summary>
        public List<string>? Options { get; set; }

        /// <summary>응답 제한 시간 (초). 0이면 제한 없음</summary>
        public int TimeoutSeconds { get; set; } = 0;
    }

    /// <summary>퀴즈 문제 유형</summary>
    public enum QuizType
    {
        /// <summary>OX 퀴즈</summary>
        OX = 0,

        /// <summary>단답형 (직접 입력)</summary>
        ShortAnswer = 1,

        /// <summary>객관식</summary>
        MultipleChoice = 2,
    }

    // ─── 퀴즈 답변 (Client → Server) ──────────────────────────────

    /// <summary>학생이 퀴즈에 응답하는 메시지</summary>
    public class QuizAnswerMessage
    {
        /// <summary>대응하는 퀴즈 ID</summary>
        public string QuizId { get; set; } = string.Empty;

        /// <summary>학번</summary>
        public string StudentId { get; set; } = string.Empty;

        /// <summary>학생 이름</summary>
        public string StudentName { get; set; } = string.Empty;

        /// <summary>학생 답변 ("O", "X", 또는 단답 텍스트)</summary>
        public string Answer { get; set; } = string.Empty;

        /// <summary>제출 시각 (UTC)</summary>
        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    }

    // ─── 퀴즈 정답 공개 (Server → Client) ─────────────────────────

    /// <summary>교수가 퀴즈 정답을 학생에게 공개할 때 보내는 메시지 (선택적 사용)</summary>
    public class QuizResultMessage
    {
        /// <summary>대응하는 퀴즈 ID</summary>
        public string QuizId { get; set; } = string.Empty;

        /// <summary>정답</summary>
        public string CorrectAnswer { get; set; } = string.Empty;

        /// <summary>해설 (선택)</summary>
        public string? Explanation { get; set; }
    }
}
