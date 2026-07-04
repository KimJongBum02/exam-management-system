using System;
using System.Collections.Generic;
using System.Text;

namespace ProfessorUI.Service
{
    public static class ExamState
    {
        private static bool _isExamStarted = false;

    // 시험 시작 여부 (이 값이 true가 되면 2, 3단계가 동시에 켜집니다)
    public static bool IsExamStarted
    {
        get => _isExamStarted;
        set
        {
            if (_isExamStarted != value)
            {
                _isExamStarted = value;
                StateChanged?.Invoke(); // 상태가 변했음을 모든 뷰모델에 알림
            }
        }
    }

    public static event Action StateChanged;
}
}
