using NetworkLib;
using System;

namespace ProfessorUI.Service
{
    public static class ExamState
    {
        private static ExamPhase _currentPhase = ExamPhase.Waiting;

        // 시험 진행 단계. 시험 화면 카드들의 버튼 활성화가 모두 이 값 하나를 따라갑니다.
        //   Waiting(0)          시험 전
        //   Ready(1)            학생 PC가 시험 준비 화면으로 넘어감
        //   InProgress(2)       시험 중 — 학생 PC에서 프로세스 감시가 돕니다
        //   SubmitRequested(3)  시험 종료 — 감시가 멈추고 답안을 걷을 수 있습니다
        //   Closed(4)           모든 학생 승인 완료
        public static ExamPhase CurrentPhase
        {
            get => _currentPhase;
            set
            {
                if (_currentPhase != value)
                {
                    _currentPhase = value;
                    StateChanged?.Invoke(); // 상태가 변했음을 모든 뷰모델에 알림
                }
            }
        }

        // 시험이 시작된 뒤인지 여부.
        // 단계가 생기기 전부터 화면들이 쓰던 이름이라, 뜻이 같으므로 그대로 남겨둡니다.
        public static bool IsExamStarted => _currentPhase >= ExamPhase.InProgress;

        public static event Action? StateChanged;

        // 모든 학생의 승인이 끝나 시험이 완전히 마무리됐다.
        // 단계 화면은 이 신호를 받고 첫 단계로 돌아간다.
        public static event Action? ExamCompleted;

        // 시험을 처음 상태로 되돌린다.
        public static void CompleteAndReset()
        {
            CurrentPhase = ExamPhase.Waiting;
            ExamCompleted?.Invoke();
        }
    }
}
