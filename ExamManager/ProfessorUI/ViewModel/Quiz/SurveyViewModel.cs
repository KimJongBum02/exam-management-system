using System.Windows.Input;
using ProfessorUI.Common;

namespace ProfessorUI.ViewModel
{
    // 설문 문제 작성 칸. 정답이 없어 문제 내용만 들고 있다.
    public class SurveyViewModel : ViewModelBase
    {
        private string _currentQuestion = string.Empty;

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

        public ICommand NewCommand { get; }

        public SurveyViewModel()
        {
            NewCommand = new RelayCommand(ExecuteNew);
        }

        private void ExecuteNew(object? parameter)
        {
            CurrentQuestion = string.Empty;
        }
    }
}
