namespace NetworkLib.Protocol
{
    /// <summary>
    /// 교수-학생 간 TCP 통신에 사용되는 모든 메시지 타입을 정의합니다.
    /// </summary>
    public enum MessageType
    {
        // ─── 연결 관련 (1~9) ──────────────────────────────────────
        /// <summary>학생 로그인 (학번 + 이름) — Client → Server</summary>
        StudentLogin = 1,
        /// <summary>로그인 응답 (승인/거부) — Server → Client</summary>
        LoginResponse = 2,
        /// <summary>생존 확인 신호 (5초마다) — Client → Server</summary>
        Heartbeat = 3,
        /// <summary>연결 해제 알림 — 양방향</summary>
        Disconnect = 4,

        // ─── 출결 관련 (10~19) ────────────────────────────────────
        /// <summary>긴급 출결 체크 요청 — Server → Client</summary>
        AttendanceCheckRequest = 10,
        /// <summary>학생 출결 응답 — Client → Server</summary>
        AttendanceCheckResponse = 11,

        // ─── 시험 제어 (20~29) ────────────────────────────────────
        /// <summary>시험 단계 변경 브로드캐스트 — Server → Client</summary>
        ExamPhaseChange = 20,
        /// <summary>학생 시험 상태 보고 — Client → Server</summary>
        ExamStatusUpdate = 21,
        /// <summary>부정행위 감지 알림 — Client → Server</summary>
        CheatingAlert = 22,

        // ─── 파일 전송 (30~39) ────────────────────────────────────
        /// <summary>파일 전송 시작 메타데이터 — 양방향</summary>
        FileTransferStart = 30,
        /// <summary>파일 청크 데이터 — 양방향</summary>
        FileChunk = 31,
        /// <summary>파일 전송 완료 신호 — 양방향</summary>
        FileTransferComplete = 32,
        /// <summary>압축 해제 명령 — Server → Client</summary>
        ExtractArchive = 33,
        /// <summary>시험 제출 요청 — Server → Client</summary>
        ExamSubmitRequest = 34,

        // ─── 프로세스 제어 (40~49) ────────────────────────────────
        /// <summary>화이트/블랙리스트 전송 — Server → Client</summary>
        ProcessListUpdate = 40,
        /// <summary>특정 프로세스 강제 종료 명령 — Server → Client</summary>
        ForceProcessKill = 41,
        /// <summary>PC 종료 명령 — Server → Client</summary>
        ShutdownPC = 42,

        // ─── 퀴즈 (50~59) ─────────────────────────────────────────
        /// <summary>단답형/OX 퀴즈 전송 — Server → Client</summary>
        QuizQuestion = 50,
        /// <summary>학생 퀴즈 답변 — Client → Server</summary>
        QuizAnswer = 51,
        /// <summary>퀴즈 정답 공개 — Server → Client</summary>
        QuizResult = 52,

        // ─── 공통 응답 (100~) ─────────────────────────────────────
        /// <summary>일반 명령 처리 확인 응답</summary>
        CommandAck = 100,
    }
}
