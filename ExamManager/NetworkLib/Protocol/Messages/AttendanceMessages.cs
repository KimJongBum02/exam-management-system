namespace NetworkLib.Protocol.Messages
{
    // ─── 출결 체크 요청 (Server → Client) ─────────────────────────

    /// <summary>
    /// 교수가 긴급 출결 체크를 요청할 때 보내는 메시지.
    /// 학생은 일정 시간 내에 AttendanceCheckResponseMessage로 응답해야 합니다.
    /// </summary>
    public class AttendanceCheckRequestMessage
    {
        /// <summary>출결 체크 고유 ID (응답 매칭에 사용)</summary>
        public string CheckId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>학생에게 보여줄 안내 메시지</summary>
        public string Message { get; set; } = "출석을 확인해 주세요.";

        /// <summary>응답 제한 시간 (초). 0이면 제한 없음</summary>
        public int TimeoutSeconds { get; set; } = 30;
    }

    // ─── 출결 체크 응답 (Client → Server) ─────────────────────────

    /// <summary>학생이 출결 체크 요청에 응답하는 메시지</summary>
    public class AttendanceCheckResponseMessage
    {
        /// <summary>대응하는 출결 체크 ID</summary>
        public string CheckId { get; set; } = string.Empty;

        /// <summary>학번</summary>
        public string StudentId { get; set; } = string.Empty;

        /// <summary>학생 이름</summary>
        public string StudentName { get; set; } = string.Empty;

        /// <summary>응답 시각 (UTC)</summary>
        public DateTime RespondedAt { get; set; } = DateTime.UtcNow;
    }
}
