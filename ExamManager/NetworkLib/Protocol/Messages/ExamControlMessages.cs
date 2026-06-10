namespace NetworkLib.Protocol.Messages
{
    // ─── 시험 단계 변경 (Server → Client) ─────────────────────────

    /// <summary>
    /// 교수가 시험 단계를 변경할 때 전체 학생에게 브로드캐스트하는 메시지.
    /// 학생 클라이언트는 이 메시지를 수신하면 ProcessControl에 알려야 합니다.
    /// </summary>
    public class ExamPhaseChangeMessage
    {
        /// <summary>변경된 시험 단계</summary>
        public ExamPhase Phase { get; set; }

        /// <summary>학생에게 보여줄 안내 메시지 (선택)</summary>
        public string? Message { get; set; }

        /// <summary>단계 변경 시각 (UTC)</summary>
        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    }

    // ─── 학생 시험 상태 보고 (Client → Server) ────────────────────

    /// <summary>학생이 자신의 현재 시험 상태를 서버에 보고하는 메시지</summary>
    public class ExamStatusUpdateMessage
    {
        /// <summary>학번</summary>
        public string StudentId { get; set; } = string.Empty;

        /// <summary>현재 상태</summary>
        public StudentStatus Status { get; set; }

        /// <summary>상세 설명 (선택)</summary>
        public string? Detail { get; set; }
    }

    // ─── 부정행위 감지 알림 (Client → Server) ─────────────────────

    /// <summary>
    /// ProcessControl이 부정행위를 감지했을 때 NetworkLib을 통해
    /// 교수 서버로 전달하는 알림 메시지.
    /// </summary>
    public class CheatingAlertMessage
    {
        /// <summary>학번</summary>
        public string StudentId { get; set; } = string.Empty;

        /// <summary>학생 이름</summary>
        public string StudentName { get; set; } = string.Empty;

        /// <summary>부정행위 유형</summary>
        public CheatingAlertType AlertType { get; set; }

        /// <summary>감지된 대상 (프로세스명, URL 등)</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>감지 시각 (UTC)</summary>
        public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>부정행위 감지 유형</summary>
    public enum CheatingAlertType
    {
        /// <summary>블랙리스트 프로세스 실행 시도</summary>
        BlacklistedProcessLaunched = 0,

        /// <summary>외부 네트워크 접근 시도 감지</summary>
        NetworkAccessAttempt = 1,

        /// <summary>화이트리스트 외 프로세스 실행</summary>
        UnauthorizedProcess = 2,

        /// <summary>기타 / 수동 신고</summary>
        ManualReport = 3,
    }
}
