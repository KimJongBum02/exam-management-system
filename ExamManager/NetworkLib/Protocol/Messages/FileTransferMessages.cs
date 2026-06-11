namespace NetworkLib.Protocol.Messages
{
    // ─── 파일 전송 시작 메타데이터 (양방향) ───────────────────────

    /// <summary>
    /// 파일 전송을 시작하기 전에 수신 측에게 전달하는 메타데이터.
    /// 실제 파일 데이터(FileChunk)를 보내기 전에 반드시 먼저 전송해야 합니다.
    /// </summary>
    public class FileTransferStartMessage
    {
        /// <summary>이번 전송 세션 고유 ID (Guid 문자열)</summary>
        public string TransferId { get; set; } = string.Empty;

        /// <summary>파일 이름 (확장자 포함, 예: "OS_중간고사.zip")</summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>파일 전체 크기 (bytes)</summary>
        public long TotalSize { get; set; }

        /// <summary>전체 청크 수</summary>
        public int TotalChunks { get; set; }

        /// <summary>파일 전체 SHA-256 해시 (무결성 검증용, 대문자 Hex 문자열)</summary>
        public string Sha256Hash { get; set; } = string.Empty;

        /// <summary>
        /// 압축 파일의 암호 힌트 전달용 (실제 암호화는 FileControl에서 처리).
        /// 수신 측 FileControl에 전달하기 위한 메타데이터 역할만 합니다.
        /// </summary>
        public string? ArchivePassword { get; set; }
    }

    // ─── 파일 청크 (양방향) ────────────────────────────────────────

    /// <summary>
    /// 파일을 64KB 단위로 나눈 청크 데이터.
    /// TCP는 순서를 보장하므로 ChunkIndex는 검증 및 진행률 계산에 사용됩니다.
    /// </summary>
    public class FileChunkMessage
    {
        /// <summary>대응하는 TransferId</summary>
        public string TransferId { get; set; } = string.Empty;

        /// <summary>청크 순서 (0부터 시작)</summary>
        public int ChunkIndex { get; set; }

        /// <summary>청크 바이너리 데이터 (System.Text.Json이 Base64로 자동 직렬화)</summary>
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }

    // ─── 파일 전송 완료 (양방향) ──────────────────────────────────

    /// <summary>모든 청크 전송이 완료되었음을 알리는 신호</summary>
    public class FileTransferCompleteMessage
    {
        /// <summary>대응하는 TransferId</summary>
        public string TransferId { get; set; } = string.Empty;

        /// <summary>최종 저장 파일 이름</summary>
        public string FileName { get; set; } = string.Empty;
    }

    // ─── 압축 해제 명령 (Server → Client) ─────────────────────────

    /// <summary>
    /// 교수가 압축 해제 명령을 내릴 때 전송하는 메시지.
    /// 실제 해제는 학생 측 FileControl이 담당하며,
    /// NetworkLib은 이 명령을 전달하는 역할만 합니다.
    /// </summary>
    public class ExtractArchiveMessage
    {
        /// <summary>해제할 파일 이름 (학생 PC에 수신된 파일 이름)</summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>압축 해제 후 생성할 목적 폴더 이름 (바탕화면 기준)</summary>
        public string DestinationFolderName { get; set; } = string.Empty;

        /// <summary>압축 파일 암호 (FileControl에 전달됨)</summary>
        public string? Password { get; set; }

        /// <summary>압축 해제 후 원본 zip 파일 자동 삭제 여부</summary>
        public bool DeleteAfterExtract { get; set; } = true;
    }

    // ─── 시험 제출 요청 (Server → Client) ─────────────────────────

    /// <summary>
    /// 교수가 시험 종료를 선언하며 학생에게 파일 제출을 요청하는 메시지.
    /// 학생 측 FileControl이 폴더를 압축한 뒤 NetworkLib을 통해 서버로 전송합니다.
    /// </summary>
    public class ExamSubmitRequestMessage
    {
        /// <summary>제출 폴더 이름 (학생이 압축해야 할 폴더명)</summary>
        public string FolderName { get; set; } = string.Empty;

        /// <summary>제출 기한 (null이면 즉시 제출)</summary>
        public DateTime? Deadline { get; set; }

        /// <summary>제출 파일에 사용할 암호 (FileControl에서 압축 시 사용)</summary>
        public string? ArchivePassword { get; set; }
    }
}
