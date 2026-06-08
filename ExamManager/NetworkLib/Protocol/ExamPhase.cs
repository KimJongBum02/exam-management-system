namespace NetworkLib.Protocol
{
    /// <summary>
    /// 시험의 진행 단계를 나타냅니다.
    /// 교수 측에서 단계를 변경하면 모든 학생에게 브로드캐스트됩니다.
    /// </summary>
    public enum ExamPhase
    {
        /// <summary>대기 — 학생 접속 대기 상태 (초기값)</summary>
        Waiting = 0,

        /// <summary>준비 — 시험 파일 배포 완료, 프로세스 제어 시작</summary>
        Ready = 1,

        /// <summary>시험중 — 압축 해제 완료, 시험 진행 중</summary>
        InProgress = 2,

        /// <summary>제출 요청 — 교수가 제출 명령을 내린 상태</summary>
        SubmitRequested = 3,

        /// <summary>종료 — 교수 승인 완료, PC 종료 예정</summary>
        Closed = 4,
    }
}
