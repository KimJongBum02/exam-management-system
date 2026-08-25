using ProfessorUI.Model;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ProfessorUI.ViewModel
{
    public class OXQuizViewModel : RightPanelViewMondel
    {
        private string _currentCategory = string.Empty;
        private string _currentQuestion = string.Empty;
        private bool? _currentAnswer;
        private OXQuizModel? _selectedQuiz;
        private string _feedbackMessage = string.Empty;
        private bool _isFeedbackVisible;
        private readonly string _saveFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OXQuizzes.json");

        public ObservableCollection<OXQuizModel> QuizList { get; set; }
        public ObservableCollection<string> Categories { get; set; }

        public string CurrentCategory
        {
            get => _currentCategory;
            set
            {
                _currentCategory = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string CurrentQuestion
        {
            get => _currentQuestion;
            set
            {
                _currentQuestion = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool? CurrentAnswer
        {
            get => _currentAnswer;
            set
            {
                _currentAnswer = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsOSelected));
                OnPropertyChanged(nameof(IsXSelected));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsOSelected
        {
            get => _currentAnswer == true;
            set
            {
                if (value) CurrentAnswer = true;
            }
        }

        public bool IsXSelected
        {
            get => _currentAnswer == false;
            set
            {
                if (value) CurrentAnswer = false;
            }
        }

        public OXQuizModel? SelectedQuiz
        {
            get => _selectedQuiz;
            set
            {
                _selectedQuiz = value;
                OnPropertyChanged();
                if (_selectedQuiz != null)
                {
                    CurrentCategory = _selectedQuiz.Category;
                    CurrentQuestion = _selectedQuiz.Question;
                    CurrentAnswer = _selectedQuiz.Answer;
                }
            }
        }

        public string FeedbackMessage
        {
            get => _feedbackMessage;
            set { _feedbackMessage = value; OnPropertyChanged(); }
        }

        public bool IsFeedbackVisible
        {
            get => _isFeedbackVisible;
            set { _isFeedbackVisible = value; OnPropertyChanged(); }
        }

        public ICommand SaveCommand { get; }
        public ICommand SubmitCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand NewCommand { get; }

        public OXQuizViewModel()
        {
            QuizList = new ObservableCollection<OXQuizModel>();
            Categories = new ObservableCollection<string> { "기본", "네트워크", "운영체제", "데이터베이스", "자료구조" };

            LoadData();

            SaveCommand = new RelayCommand(ExecuteSave);
            SubmitCommand = new RelayCommand(ExecuteSubmit);
            DeleteCommand = new RelayCommand(ExecuteDelete, (p) => SelectedQuiz != null);
            NewCommand = new RelayCommand(ExecuteNew);
        }

        private void LoadData()
        {
            if (File.Exists(_saveFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_saveFilePath);
                    var loaded = JsonSerializer.Deserialize<ObservableCollection<OXQuizModel>>(json);
                    if (loaded != null)
                    {
                        QuizList = loaded;
                        foreach (var quiz in QuizList)
                        {
                            if (!Categories.Contains(quiz.Category))
                            {
                                Categories.Add(quiz.Category);
                            }
                        }
                    }
                }
                catch { /* Ignore load errors */ }
            }
        }

        private void SaveData()
        {
            try
            {
                string json = JsonSerializer.Serialize(QuizList, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_saveFilePath, json);
            }
            catch { /* Ignore save errors */ }
        }

        private void ExecuteSave(object? parameter)
        {
            bool isIncomplete = string.IsNullOrWhiteSpace(CurrentQuestion) || !CurrentAnswer.HasValue || string.IsNullOrWhiteSpace(CurrentCategory);
            
            if (isIncomplete)
            {
                var result = System.Windows.MessageBox.Show("문제를 다 작성하지 않았습니다 저장하시겠습니까?", "저장 확인", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                if (result != System.Windows.MessageBoxResult.Yes)
                {
                    return;
                }
            }

            var targetId = _selectedQuiz?.Id;
            var existing = targetId != null ? QuizList.FirstOrDefault(q => q.Id == targetId) : null;

            if (existing != null)
            {
                existing.Category = string.IsNullOrWhiteSpace(CurrentCategory) ? "미지정" : CurrentCategory;
                existing.Question = CurrentQuestion;
                existing.Answer = CurrentAnswer;
            }
            else
            {
                var newQuiz = new OXQuizModel
                {
                    Category = string.IsNullOrWhiteSpace(CurrentCategory) ? "미지정" : CurrentCategory,
                    Question = CurrentQuestion,
                    Answer = CurrentAnswer
                };
                QuizList.Add(newQuiz);
                SelectedQuiz = newQuiz;
            }

            if (!string.IsNullOrWhiteSpace(CurrentCategory) && !Categories.Contains(CurrentCategory))
            {
                Categories.Add(CurrentCategory);
            }

            SaveData();
            ShowFeedback("문제가 저장되었습니다.");
        }

        private void ExecuteDelete(object? parameter)
        {
            if (SelectedQuiz != null)
            {
                QuizList.Remove(SelectedQuiz);
                SaveData();
                ExecuteNew(null);
                ShowFeedback("삭제되었습니다.");
            }
        }

        private void ExecuteNew(object? parameter)
        {
            SelectedQuiz = null;
            CurrentCategory = string.Empty;
            CurrentQuestion = string.Empty;
            CurrentAnswer = null;
        }

        private async void ExecuteSubmit(object? parameter)
        {
            ShowFeedback("문제가 전송되었습니다.");
            await Task.CompletedTask;
        }

        private async void ShowFeedback(string message)
        {
            FeedbackMessage = message;
            IsFeedbackVisible = true;
            await Task.Delay(2000);
            IsFeedbackVisible = false;
        }
    }
}
