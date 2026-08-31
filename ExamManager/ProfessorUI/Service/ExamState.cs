using NetworkLib;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProfessorUI.Service
{
    public static class ExamState
    {
        private static ExamPhase _currentPhase = ExamPhase.Waiting;

        // 시험 진행 단계. 시험 화면 카드들의 버튼 활성화가 모두 이 값 하나를 따라갑니다.
        //   Waiting(0)          시험 전
        //   InProgress(2)       시험 중 — 학생 PC에서 프로세스 감시가 돕니다
        //   SubmitRequested(3)  시험 종료 — 감시가 멈추고 답안을 걷을 수 있습니다
        //   Closed(4)           모든 학생 승인 완료
        // (Ready(1)은 프로토콜에만 있고 교수 화면에서는 쓰지 않습니다)
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

        public static event Action StateChanged;
    }
}
