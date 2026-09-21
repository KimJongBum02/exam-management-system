// 퀴즈 데이터: 문제 한 건(QuizRound) · 학생 응답(QuizResponse) · 학생별 누적(QuizStudentTally)
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ProfessorUI.Model
{
    // 문제 하나를 낸 기록.
    public class QuizRound : INotifyPropertyChanged
    {
        public string QuizId { get; init; } = string.Empty;
        public string Question { get; init; } = string.Empty;
        public bool CorrectAnswer { get; init; }
        public string AskedAt { get; init; } = string.Empty;

        public ObservableCollection<QuizResponse> Responses { get; init; } = new();

        [JsonIgnore] public int TargetCount => Responses.Count;
        [JsonIgnore] public int AnsweredCount => Responses.Count(r => r.HasAnswered);
        [JsonIgnore] public int CorrectCount => Responses.Count(r => r.IsCorrect);
        [JsonIgnore] public int WrongCount => Responses.Count(r => r.HasAnswered && !r.IsCorrect);
        [JsonIgnore] public int MissedCount => Responses.Count(r => !r.HasAnswered);

        [JsonIgnore] public string CorrectAnswerText => CorrectAnswer ? "O" : "X";
        [JsonIgnore] public string SummaryText => $"정답 {CorrectCount} · 오답 {WrongCount} · 미응답 {MissedCount}";

        // 응답이 하나 들어올 때마다 위 집계를 다시 읽게 한다.
        public void NotifyCounts() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    // 한 학생의 한 문제에 대한 응답.
    //
    // 출제 시점에 접속해 있던 학생 전원을 미리 만들어 두고 Answer 를 비워 둔다.
    // 그래야 "안 낸 사람"과 "그때 없던 사람"이 섞이지 않는다 —
    // 수업 태도를 보는 것이 목적이라 이 둘을 구분하지 못하면 쓸모가 없다.
    public class QuizResponse : INotifyPropertyChanged
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

        // 이 응답이 붙어 있는 문제의 정답. 정오 판정에 쓴다.
        [JsonIgnore]
        public bool CorrectAnswer { get; set; }

        [JsonIgnore] public bool HasAnswered => _answer.HasValue;
        [JsonIgnore] public bool IsCorrect => _answer.HasValue && _answer.Value == CorrectAnswer;

        // 화면에 그대로 나갈 문구. 색을 쓰지 않는 화면이라 기호로 구분한다.
        [JsonIgnore] public string AnswerText => _answer == true ? "O" : _answer == false ? "X" : "−";
        [JsonIgnore] public string ResultText => !_answer.HasValue ? "− 미응답" : IsCorrect ? "✓ 정답" : "✕ 오답";

        // 오답 → 미응답 → 정답 순. 교수가 봐야 할 학생이 위로 오게 하기 위한 값이다.
        //
        // 목록을 이 값으로 늘 정렬하지는 않는다. 응답이 하나씩 들어오는 동안 줄이 계속
        // 튀면 오히려 읽기 어렵기 때문이다. 화면에서 정렬 여부를 고르게 두고, 이 값만 제공한다.
        [JsonIgnore] public int SortRank => !_answer.HasValue ? 1 : IsCorrect ? 2 : 0;

        private void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    // 학생 한 명의 누적. 수업 태도 평가에 실제로 쓰이는 것은 이 표다.
    public class QuizStudentTally
    {
        public string StudentId { get; init; } = string.Empty;
        public string StudentName { get; init; } = string.Empty;
        public int AskedCount { get; set; }      // 그 학생이 대상이었던 문제 수
        public int AnsweredCount { get; set; }
        public int CorrectCount { get; set; }
        public int MissedCount => AskedCount - AnsweredCount;
    }
}
