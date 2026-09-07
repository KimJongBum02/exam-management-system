using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ProfessorUI.Model
{
    public class OXQuizModel : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString();
        private string _category = string.Empty;
        private string _question = string.Empty;
        private bool? _answer;

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string Category
        {
            get => _category;
            set
            {
                _category = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsIncomplete));
                OnPropertyChanged(nameof(StateText));
            }
        }

        public string Question
        {
            get => _question;
            set
            {
                _question = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsIncomplete));
                OnPropertyChanged(nameof(StateText));
            }
        }

        public bool? Answer
        {
            get => _answer;
            set
            {
                _answer = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsIncomplete));
                OnPropertyChanged(nameof(AnswerText));
                OnPropertyChanged(nameof(StateText));
            }
        }

        [JsonIgnore]
        public bool IsIncomplete => string.IsNullOrWhiteSpace(Category) || string.IsNullOrWhiteSpace(Question) || !Answer.HasValue;

        // 목록 표에 그대로 나갈 문구
        [JsonIgnore]
        public string AnswerText => Answer == true ? "O" : Answer == false ? "X" : "-";

        [JsonIgnore]
        public string StateText => IsIncomplete ? "미완성" : "완성";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
