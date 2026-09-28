// 설문 데이터: 문제 한 건(SurveyRound) · 학생 응답(SurveyResponse) · 학생별 누적(SurveyStudentTally)
// 정답이 없다. 학생이 O·X 중 무엇을 골랐는지만 모은다 (예: 실습 희망 명단 나누기).
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace ProfessorUI.Model
{
    // 설문 하나를 낸 기록.
    public class SurveyRound
    {
        public string QuizId { get; init; } = string.Empty;
        public string Question { get; init; } = string.Empty;
        public string AskedAt { get; init; } = string.Empty;

        public ObservableCollection<SurveyResponse> Responses { get; init; } = new();

        public int TargetCount => Responses.Count;
        public int OCount => Responses.Count(r => r.Answer == true);
        public int XCount => Responses.Count(r => r.Answer == false);
        public int MissedCount => Responses.Count(r => !r.HasAnswered);
    }

    // 한 학생의 한 설문에 대한 응답.
    //
    // 출제 시점에 접속해 있던 학생 전원을 미리 만들어 두고 Answer 를 비워 둔다.
    // 그래야 "안 낸 사람"과 "그때 없던 사람"이 섞이지 않는다 —
    public class SurveyResponse : INotifyPropertyChanged
    {
        public string StudentId { get; init; } = string.Empty;
        public string StudentName { get; init; } = string.Empty;

        private bool? _answer;
        public bool? Answer
        {
            get => _answer;
            set { _answer = value; NotifyAll(); }
        }

        private string _respondedAt = "-";
        public string RespondedAt
        {
            get => _respondedAt;
            set { _respondedAt = value; NotifyAll(); }
        }

        public bool HasAnswered => _answer.HasValue;

        // 화면에 그대로 나갈 문구. 색을 쓰지 않는 화면이라 기호로 구분한다.
        public string AnswerText => _answer == true ? "O" : _answer == false ? "X" : "− 미응답";

        // O → X → 미응답 순. 고른 답끼리 모아 명단처럼 보게 하기 위한 값이다.
        //
        // 목록을 이 값으로 늘 정렬하지는 않는다. 응답이 하나씩 들어오는 동안 줄이 계속
        // 튀면 오히려 읽기 어렵기 때문이다. 화면에서 정렬 여부를 고르게 두고, 이 값만 제공한다.
        public int SortRank => _answer == true ? 0 : _answer == false ? 1 : 2;

        private void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    // 학생 한 명의 누적. 설문에 얼마나 참여했는지 본다.
    public class SurveyStudentTally
    {
        public string StudentId { get; init; } = string.Empty;
        public string StudentName { get; init; } = string.Empty;
        public int AskedCount { get; set; }      // 그 학생이 대상이었던 문제 수
        public int AnsweredCount { get; set; }
        public int MissedCount => AskedCount - AnsweredCount;
    }
}
