namespace NetworkLib.Protocol
{
    /// <summary>
    /// 교수 UI에서 표시되는 개별 학생의 연결/시험 상태를 나타냅니다.
    /// </summary>
    public enum StudentStatus
    {
        /// <summary>미접속 — 아직 클라이언트가 연결되지 않음</summary>
        NotConnected = 0,

        /// <summary>접속됨 — 로그인 완료, 대기 중</summary>
        Connected = 1,

        /// <summary>파일수신 — 시험 파일 수신 완료</summary>
        FileReceived = 2,

        /// <summary>시험중 — 시험 진행 중</summary>
        InProgress = 3,

        /// <summary>제출완료 — 시험 파일 제출 완료, 교수 승인 대기</summary>
        Submitted = 4,

        /// <summary>승인완료 — 교수 승인 완료, PC 종료 중</summary>
        Approved = 5,

        /// <summary>부정행위감지 — 부정행위 의심 신호 수신됨</summary>
        CheatingDetected = 6,

        /// <summary>결석 — 접속 후 Heartbeat 끊김 (자리 이탈)</summary>
        Absent = 7,
    }
}
