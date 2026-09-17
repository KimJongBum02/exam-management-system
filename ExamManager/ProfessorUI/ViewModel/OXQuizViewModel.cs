using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace ProfessorUI.ViewModel
{
    public class OXQuizViewModel : RightPanelViewMondel
    {
        private string _currentCategory = string.Empty;
        private string _currentQuestion = string.Empty;
        private bool? _currentAnswer;
        private string _feedbackMessage = string.Empty;

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

        public string FeedbackMessage
        {
            get => _feedbackMessage;
            set { _feedbackMessage = value; OnPropertyChanged(); }
        }

        public ICommand NewCommand { get; }

        public OXQuizViewModel()
        {
            Categories = new ObservableCollection<string> { "기본", "네트워크", "운영체제", "데이터베이스", "자료구조" };

            NewCommand = new RelayCommand(ExecuteNew);
        }

        private void ExecuteNew(object? parameter)
        {
            CurrentCategory = string.Empty;
            CurrentQuestion = string.Empty;
            CurrentAnswer = null;
        }
    }
}
