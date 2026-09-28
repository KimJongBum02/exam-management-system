using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace StudentUI.Model
{
    // 수업 확인 퀴즈의 문제 하나와 학생이 고른 답.
    // 문제 칸과 오른쪽 답안 표기란이 같은 객체를 보므로, 어느 쪽에서 골라도 양쪽이 함께 바뀐다.
    public class ClassQuizItem : INotifyPropertyChanged
    {
        public ClassQuizItem(int number, string text, string[] options)
        {
            Number = number;
            Text = text;

            // 보기가 없으면 OX 문제다. 답은 교수에게 "O"·"X" 또는 보기 번호("1"~)로 보낸다.
            Options = options.Length == 0
                ? new List<ClassQuizChoice> { new(this, "O", "O", ""), new(this, "X", "X", "") }
                : options.Select((option, i) => new ClassQuizChoice(this, (i + 1).ToString(), (i + 1).ToString(), option)).ToList();
        }

        public int Number { get; }
        public string Text { get; }
        public string Title => $"{Number}. {Text}";
        public List<ClassQuizChoice> Options { get; }

        private string _answer = string.Empty;

        // 고른 답. 아직 고르지 않았으면 빈 문자열이다.
        public string Answer => _answer;
        public bool IsAnswered => _answer.Length > 0;

        // 제출 전에는 몇 번이든 바꿀 수 있다.
        public void Choose(ClassQuizChoice choice)
        {
            _answer = choice.Value;
            foreach (var option in Options) option.NotifySelected();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Answer)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAnswered)));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    // 보기 하나. OX 문제는 O·X 두 개를 보기처럼 둔다.
    public class ClassQuizChoice : INotifyPropertyChanged
    {
        public ClassQuizChoice(ClassQuizItem owner, string value, string mark, string text)
        {
            Owner = owner;
            Value = value;
            Mark = mark;
            Text = text;
        }

        public ClassQuizItem Owner { get; }
        public string Value { get; }
        public string Mark { get; }    // 동그라미 안에 들어가는 글자 (번호 또는 O·X)
        public string Text { get; }    // 보기 내용. OX 는 비어 있다
        public bool IsSelected => Owner.Answer == Value;

        internal void NotifySelected() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
