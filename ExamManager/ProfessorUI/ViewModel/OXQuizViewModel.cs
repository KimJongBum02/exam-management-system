using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace ProfessorUI.ViewModel
{
    public class OXQuizViewModel : RightPanelViewMondel
    {
        private string _currentQuestion = string.Empty;
        private bool? _currentAnswer;
        private string _feedbackMessage = string.Empty;

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
            NewCommand = new RelayCommand(ExecuteNew);
        }

        private void ExecuteNew(object? parameter)
        {
            CurrentQuestion = string.Empty;
            CurrentAnswer = null;
        }
    }
}
