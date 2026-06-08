namespace NetworkLib.Protocol.Messages
{
    // ─── 학생 로그인 (Client → Server) ────────────────────────────

    /// <summary>학생이 서버에 처음 접속할 때 보내는 로그인 메시지</summary>
    public class StudentLoginMessage
    {
        /// <summary>학번 (9자리)</summary>
        public string StudentId { get; set; } = string.Empty;

        /// <summary>학생 이름</summary>
        public string StudentName { get; set; } = string.Empty;
    }

    // ─── 로그인 응답 (Server → Client) ────────────────────────────

    /// <summary>서버가 로그인 요청에 응답하는 메시지</summary>
    public class LoginResponseMessage
    {
        /// <summary>승인 여부</summary>
        public bool Success { get; set; }

        /// <summary>안내 메시지 (예: "접속이 승인되었습니다.")</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>거부 사유 (Success = false일 때)</summary>
        public string? RejectionReason { get; set; }
    }

    // ─── Heartbeat (Client → Server) ─────────────────────────────

    /// <summary>
    /// 학생 클라이언트가 5초마다 전송하는 생존 신호.
    /// 서버에서 15초 이상 수신되지 않으면 결석으로 처리합니다.
    /// </summary>
    public class HeartbeatMessage
    {
        // 페이로드 없음 — 타입(MessageType.Heartbeat)만으로 식별
    }

    // ─── 연결 해제 (양방향) ───────────────────────────────────────

    /// <summary>정상적으로 연결을 종료할 때 보내는 메시지</summary>
    public class DisconnectMessage
    {
        /// <summary>종료 사유 (예: "정상 종료", "시험 완료")</summary>
        public string Reason { get; set; } = string.Empty;
    }
}
