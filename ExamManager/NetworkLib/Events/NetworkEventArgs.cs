using NetworkLib.Protocol;

namespace NetworkLib.Events
{
    // ─── 학생 세션 이벤트 ─────────────────────────────────────────

    /// <summary>학생이 서버에 접속했을 때 발생하는 이벤트 인자</summary>
    public class StudentConnectedEventArgs : EventArgs
    {
        /// <summary>접속한 학생의 세션 ID</summary>
        public string SessionId { get; init; } = string.Empty;

        /// <summary>학번</summary>
        public string StudentId { get; init; } = string.Empty;

        /// <summary>학생 이름</summary>
        public string StudentName { get; init; } = string.Empty;

        /// <summary>학생 PC의 IP 주소 및 포트</summary>
        public string RemoteEndPoint { get; init; } = string.Empty;

        /// <summary>접속 시각 (UTC)</summary>
        public DateTime ConnectedAt { get; init; } = DateTime.UtcNow;
    }

    /// <summary>학생 연결이 끊겼을 때 발생하는 이벤트 인자</summary>
    public class StudentDisconnectedEventArgs : EventArgs
    {
        /// <summary>끊어진 학생의 세션 ID</summary>
        public string SessionId { get; init; } = string.Empty;

        /// <summary>학번</summary>
        public string StudentId { get; init; } = string.Empty;

        /// <summary>학생 이름</summary>
        public string StudentName { get; init; } = string.Empty;

        /// <summary>연결 해제 사유</summary>
        public DisconnectReason Reason { get; init; }
    }

    /// <summary>연결 해제 사유</summary>
    public enum DisconnectReason
    {
        /// <summary>학생이 직접 연결 해제</summary>
        ClientDisconnected,

        /// <summary>Heartbeat 타임아웃 (자리 이탈)</summary>
        HeartbeatTimeout,

        /// <summary>네트워크 오류</summary>
        NetworkError,

        /// <summary>서버 종료</summary>
        ServerShutdown,
    }

    // ─── 패킷 수신 이벤트 ─────────────────────────────────────────

    /// <summary>서버에서 학생으로부터 패킷을 수신했을 때 발생하는 이벤트 인자</summary>
    public class PacketReceivedEventArgs : EventArgs
    {
        /// <summary>보낸 학생의 세션 ID</summary>
        public string SessionId { get; init; } = string.Empty;

        /// <summary>학번</summary>
        public string StudentId { get; init; } = string.Empty;

        /// <summary>학생 이름</summary>
        public string StudentName { get; init; } = string.Empty;

        /// <summary>수신된 패킷</summary>
        public NetworkPacket Packet { get; init; } = null!;
    }

    // ─── 파일 전송 이벤트 ─────────────────────────────────────────

    /// <summary>파일 수신이 완료되었을 때 발생하는 이벤트 인자</summary>
    public class FileReceivedEventArgs : EventArgs
    {
        /// <summary>전송 세션 ID</summary>
        public string TransferId { get; init; } = string.Empty;

        /// <summary>보낸 쪽 세션 ID (학생 SessionId 또는 "server")</summary>
        public string SenderId { get; init; } = string.Empty;

        /// <summary>파일 이름</summary>
        public string FileName { get; init; } = string.Empty;

        /// <summary>
        /// 수신된 파일이 저장된 임시 파일 경로.
        /// FileControl에서 이 경로의 파일을 읽어 원하는 위치로 이동해야 합니다.
        /// </summary>
        public string TempFilePath { get; init; } = string.Empty;

        /// <summary>파일 크기 (bytes)</summary>
        public long FileSize { get; init; }

        /// <summary>
        /// 압축 파일 암호 (FileTransferStart에서 전달받은 값).
        /// FileControl에 전달하기 위한 메타데이터입니다.
        /// </summary>
        public string? ArchivePassword { get; init; }
    }

    /// <summary>파일 전송 진행률 이벤트 인자</summary>
    public class FileTransferProgressEventArgs : EventArgs
    {
        /// <summary>전송 세션 ID</summary>
        public string TransferId { get; init; } = string.Empty;

        /// <summary>파일 이름</summary>
        public string FileName { get; init; } = string.Empty;

        /// <summary>진행률 (0~100)</summary>
        public int Percent { get; init; }

        /// <summary>수신된 청크 수</summary>
        public int ReceivedChunks { get; init; }

        /// <summary>전체 청크 수</summary>
        public int TotalChunks { get; init; }
    }

    /// <summary>파일 전송 오류 이벤트 인자</summary>
    public class FileTransferErrorEventArgs : EventArgs
    {
        /// <summary>전송 세션 ID</summary>
        public string TransferId { get; init; } = string.Empty;

        /// <summary>오류 메시지</summary>
        public string Message { get; init; } = string.Empty;
    }

    // ─── 클라이언트(학생) 측 이벤트 ──────────────────────────────

    /// <summary>StudentClient에서 교수 서버와 연결되었을 때 발생하는 이벤트 인자</summary>
    public class ClientConnectedEventArgs : EventArgs
    {
        /// <summary>교수 서버 IP</summary>
        public string ServerIp { get; init; } = string.Empty;

        /// <summary>포트</summary>
        public int Port { get; init; }
    }

    /// <summary>StudentClient에서 교수 서버와 연결이 끊겼을 때 발생하는 이벤트 인자</summary>
    public class ClientDisconnectedEventArgs : EventArgs
    {
        /// <summary>연결 해제 사유</summary>
        public DisconnectReason Reason { get; init; }
    }

    /// <summary>StudentClient에서 오류가 발생했을 때 발생하는 이벤트 인자</summary>
    public class NetworkErrorEventArgs : EventArgs
    {
        /// <summary>오류 메시지</summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>원본 예외 (선택)</summary>
        public Exception? Exception { get; init; }
    }
}
