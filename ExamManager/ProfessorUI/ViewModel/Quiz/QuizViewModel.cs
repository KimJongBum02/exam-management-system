using System;
using System.Collections.ObjectModel;
using System.Linq;
using ProfessorUI.Model;

namespace ProfessorUI.ViewModel
{
    // 수업 확인 퀴즈 작성 칸. 여러 문제를 모아 두었다가 한 번에 낸다.
    // 왼쪽 목록에서 문제를 고르고 오른쪽에서 그 문제 하나만 고친다(Kahoot 방식) — 문제가 늘어도 화면이 길어지지 않는다.
    //
    // 화면은 메뉴를 옮길 때마다 새로 만들어지므로, 쓰던 문제가 사라지지 않게 UiContext 에 한 벌만 둔다.
    public class QuizViewModel : ViewModelBase
    {
        public ObservableCollection<QuizQuestionDraft> Questions { get; } = new();

        private QuizQuestionDraft? _selectedQuestion;

        // 오른쪽 편집 칸에 올라와 있는 문제. 문제가 하나도 없으면 null.
        public QuizQuestionDraft? SelectedQuestion
        {
            get => _selectedQuestion;
            set { _selectedQuestion = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelection)); }
        }

        public bool HasSelection => _selectedQuestion != null;

        // 문제·보기·정답이 바뀔 때마다 알린다. 화면이 출제 버튼 상태를 다시 계산한다.
        public event Action? Edited;

        public void AddOX() => Add(new QuizQuestionDraft(isOX: true, OnEdited));
        public void AddChoice() => Add(new QuizQuestionDraft(isOX: false, OnEdited));

        private void Add(QuizQuestionDraft question)
        {
            Questions.Add(question);
            Renumber();
            SelectedQuestion = question;   // 새로 넣은 문제를 바로 쓰게 한다
        }

        // 지운 자리의 다음 문제(마지막이었으면 앞 문제)를 이어서 보여 준다.
        public void Remove(QuizQuestionDraft question)
        {
            int index = Questions.IndexOf(question);
            Questions.Remove(question);
            Renumber();
            SelectedQuestion = Questions.Count == 0 ? null : Questions[Math.Min(index, Questions.Count - 1)];
        }

        public void Clear()
        {
            Questions.Clear();
            SelectedQuestion = null;
            OnEdited();
        }

        // 출제를 막는 첫 번째 이유. 없으면 null.
        public string? Problem =>
            Questions.Count == 0 ? "문제를 하나 이상 추가해 주십시오."
            : Questions.Select(q => q.Problem).FirstOrDefault(p => p != null);

        // 출제한 순간의 내용을 떼어 낸다. 이후 작성 칸을 고쳐도 기록은 바뀌지 않는다.
        public System.Collections.Generic.List<QuizQuestion> Snapshot() =>
            Questions.Select(q => new QuizQuestion
            {
                Number = q.Number,
                Text = q.Text.Trim(),
                Options = q.Options.Select(o => o.Text.Trim()).ToArray(),
                CorrectAnswer = q.CorrectAnswer ?? string.Empty,
            }).ToList();

        private void Renumber()
        {
            for (int i = 0; i < Questions.Count; i++) Questions[i].Number = i + 1;
            OnEdited();
        }

        private void OnEdited() => Edited?.Invoke();
    }

    // 작성 중인 문제 하나. OX 는 보기가 없고, N지선다는 보기를 2~10개 둔다.
    public class QuizQuestionDraft : ViewModelBase
    {
        public const int MinOptions = 2;
        public const int MaxOptions = 10;   // 보기 번호 ①~⑩
        private const int DefaultOptions = 4;

        private readonly Action _edited;
        private int _number;
        private string _text = string.Empty;
        private string? _correctAnswer;

        public QuizQuestionDraft(bool isOX, Action edited)
        {
            IsOX = isOX;
            _edited = edited;
            if (!isOX)
                for (int i = 0; i < DefaultOptions; i++) AddOption();
        }

        public bool IsOX { get; }
        public bool IsChoice => !IsOX;
        public string TypeText => IsOX ? "OX" : "N지선다";

        public int Number
        {
            get => _number;
            set { _number = value; OnPropertyChanged(); OnPropertyChanged(nameof(Header)); }
        }

        public string Header => $"{Number}번 · {TypeText}";

        // 왼쪽 목록에 보이는 문제 앞부분. 비어 있으면 무엇을 채워야 하는지 보인다.
        public string Preview => string.IsNullOrWhiteSpace(Text) ? "(문제 내용 없음)" : Text.Trim().Replace("\r", "").Replace('\n', ' ');

        // 문제·보기·정답이 모두 채워졌는지. 왼쪽 목록에 ✓ / ! 로 보인다.
        public bool IsComplete => Problem == null;

        public string Text
        {
            get => _text;
            set { _text = value; OnPropertyChanged(); Changed(); }
        }

        public ObservableCollection<QuizOptionDraft> Options { get; } = new();

        // "O"·"X" 또는 보기 번호("1"~). 아직 고르지 않았으면 null.
        public string? CorrectAnswer
        {
            get => _correctAnswer;
            private set
            {
                _correctAnswer = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsOCorrect));
                OnPropertyChanged(nameof(IsXCorrect));
                foreach (var option in Options) option.NotifyCorrect();
                Changed();
            }
        }

        public bool IsOCorrect
        {
            get => _correctAnswer == "O";
            set { if (value) CorrectAnswer = "O"; }
        }

        public bool IsXCorrect
        {
            get => _correctAnswer == "X";
            set { if (value) CorrectAnswer = "X"; }
        }

        public bool CanAddOption => Options.Count < MaxOptions;
        public bool CanRemoveOption => Options.Count > MinOptions;

        public void AddOption()
        {
            if (!CanAddOption) return;
            Options.Add(new QuizOptionDraft(this));
            RenumberOptions();
        }

        public void RemoveOption(QuizOptionDraft option)
        {
            if (!CanRemoveOption) return;

            // 정답 보기를 지우면 정답을 비우고, 앞 보기를 지우면 정답 번호가 따라 당겨진다.
            string? correct = _correctAnswer;
            if (correct == option.Value) correct = null;
            else if (correct != null && int.Parse(correct) > option.Number) correct = (int.Parse(correct) - 1).ToString();

            Options.Remove(option);
            RenumberOptions();
            CorrectAnswer = correct;
        }

        internal void SetCorrect(QuizOptionDraft option) => CorrectAnswer = option.Value;

        internal void NotifyEdited() => Changed();

        // 왼쪽 목록의 완성 표시와 미리보기도 함께 바뀌게 한다.
        private void Changed()
        {
            OnPropertyChanged(nameof(IsComplete));
            OnPropertyChanged(nameof(Preview));
            _edited();
        }

        private void RenumberOptions()
        {
            for (int i = 0; i < Options.Count; i++) Options[i].Number = i + 1;
            OnPropertyChanged(nameof(CanAddOption));
            OnPropertyChanged(nameof(CanRemoveOption));
            Changed();
        }

        public string? Problem =>
            string.IsNullOrWhiteSpace(Text) ? $"{Number}번 문제 내용을 채워 주십시오."
            : Options.Any(o => string.IsNullOrWhiteSpace(o.Text)) ? $"{Number}번 문제의 빈 보기를 채우거나 지워 주십시오."
            : CorrectAnswer == null ? $"{Number}번 문제의 정답을 골라 주십시오."
            : null;
    }

    // 작성 중인 보기 하나.
    public class QuizOptionDraft : ViewModelBase
    {
        private readonly QuizQuestionDraft _owner;
        private int _number;
        private string _text = string.Empty;

        public QuizOptionDraft(QuizQuestionDraft owner) => _owner = owner;

        public int Number
        {
            get => _number;
            set { _number = value; OnPropertyChanged(); OnPropertyChanged(nameof(Mark)); OnPropertyChanged(nameof(IsCorrect)); }
        }

        public string Value => Number.ToString();

        // ①~⑩. 학생 화면과 같은 번호 모양을 쓴다.
        public string Mark => ((char)('①' + Number - 1)).ToString();

        public string Text
        {
            get => _text;
            set { _text = value; OnPropertyChanged(); _owner.NotifyEdited(); }
        }

        public bool IsCorrect
        {
            get => _owner.CorrectAnswer == Value;
            set { if (value) _owner.SetCorrect(this); }
        }

        internal void NotifyCorrect() => OnPropertyChanged(nameof(IsCorrect));
    }
}
