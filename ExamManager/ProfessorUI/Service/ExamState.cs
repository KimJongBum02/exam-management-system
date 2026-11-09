using NetworkLib;

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
                    // 시험을 끝낸 시각을 남긴다. 답안이 종료 뒤 얼마 만에 들어왔는지 보여 주는 데 쓴다.
                    if (value >= ExamPhase.SubmitRequested && _currentPhase < ExamPhase.SubmitRequested)
                        EndedAt = DateTime.Now;
                    else if (value < ExamPhase.SubmitRequested)
                        EndedAt = null;

                    _currentPhase = value;
                    StateChanged?.Invoke(); // 상태가 변했음을 모든 뷰모델에 알림
                }
            }
        }

        // 시험이 시작된 뒤인지 여부.
        // 단계가 생기기 전부터 화면들이 쓰던 이름이라, 뜻이 같으므로 그대로 남겨둡니다.
        public static bool IsExamStarted => _currentPhase >= ExamPhase.InProgress;

        // 지금 시험을 치르는 중인지. 시험을 끝내면(SubmitRequested) false 가 되어
        // 다음 시험을 위한 준비 화면이 다시 열린다.
        public static bool IsExamRunning => _currentPhase == ExamPhase.InProgress;

        // 교수가 [시험 종료]를 누른 시각. 시험 전·중에는 null.
        public static DateTime? EndedAt { get; private set; }

        // 이번 시험의 로그(출석·부정행위)를 엑셀로 한 번이라도 저장했는지.
        // [처음 화면으로] 가 이 값으로 잠기고 풀린다 — 정리하면 로그가 사라지기 때문이다.
        //
        // 화면 쪽 ExamLogViewModel.IsSaved 와 다르다. 그쪽은 학생 목록이 바뀔 때마다 풀리는데
        // (표가 다시 만들어지므로 저장본이 더 이상 지금 목록이 아니다), 그것을 버튼 잠금에 쓰면
        // 저장한 뒤 학생이 한 명 들어오기만 해도 다시 잠겨 버린다.
        public static bool IsLogSaved
        {
            get => _isLogSaved;
            set
            {
                if (_isLogSaved == value) return;
                _isLogSaved = value;
                StateChanged?.Invoke();
            }
        }
        private static bool _isLogSaved;

        // 프로그램을 다시 켤 때 진행 중이던 시험을 그대로 되살린다(ExamSessionStore).
        // 끝낸 시각은 기록에 있는 값을 그대로 쓴다 — 위 setter 를 거치면 지금 시각으로 덮어써진다.
        public static void Restore(ExamPhase phase, DateTime? endedAt)
        {
            _currentPhase = phase;
            EndedAt = endedAt;
            StateChanged?.Invoke();
        }

        public static event Action StateChanged;
    }
}
