namespace NetworkLib.Protocol.Messages
{
    // ─── 프로세스 목록 업데이트 (Server → Client) ─────────────────

    /// <summary>
    /// 교수가 화이트리스트/블랙리스트를 학생 PC에 전송하는 메시지.
    /// 실제 프로세스 제어는 학생 측 ProcessControl이 담당하며,
    /// NetworkLib은 이 메시지를 전달하는 역할만 합니다.
    /// </summary>
    public class ProcessListUpdateMessage
    {
        /// <summary>
        /// 허용 프로세스 목록 (화이트리스트).
        /// 이 목록에 있는 프로세스만 실행 허용.
        /// 비어있으면 모든 프로세스 허용.
        /// </summary>
        public List<string> Whitelist { get; set; } = new();

        /// <summary>
        /// 차단 프로세스 목록 (블랙리스트).
        /// 이 목록에 있는 프로세스는 즉시 강제 종료.
        /// 예: "chrome.exe", "KakaoTalk.exe"
        /// </summary>
        public List<string> Blacklist { get; set; } = new();
    }

    // ─── 특정 프로세스 강제 종료 명령 (Server → Client) ──────────

    /// <summary>교수가 특정 프로세스를 즉시 종료하도록 명령하는 메시지</summary>
    public class ForceProcessKillMessage
    {
        /// <summary>강제 종료할 프로세스 이름 (예: "chrome.exe")</summary>
        public string ProcessName { get; set; } = string.Empty;
    }

    // ─── PC 종료 명령 (Server → Client) ───────────────────────────

    /// <summary>
    /// 교수가 시험 제출을 승인한 후 학생 PC를 종료하도록 명령하는 메시지.
    /// 실제 종료는 학생 측 ProcessControl이 shutdown.exe를 호출하여 처리합니다.
    /// </summary>
    public class ShutdownPCMessage
    {
        /// <summary>종료 전 대기 시간 (초). 학생에게 완료 안내를 보여줄 시간</summary>
        public int DelaySeconds { get; set; } = 10;

        /// <summary>학생에게 보여줄 종료 메시지</summary>
        public string Message { get; set; } = "시험이 완료되었습니다. PC가 종료됩니다.";
    }

    // ─── 공통 명령 확인 응답 (Client → Server) ────────────────────

    /// <summary>학생 PC가 명령 수신 및 처리 결과를 교수에게 알리는 응답 메시지</summary>
    public class CommandAckMessage
    {
        /// <summary>응답하는 명령 타입</summary>
        public MessageType CommandType { get; set; }

        /// <summary>처리 성공 여부</summary>
        public bool Success { get; set; }

        /// <summary>결과 메시지 (오류 원인 등)</summary>
        public string? Message { get; set; }
    }
}
