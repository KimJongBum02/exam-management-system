using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks; // ⭐ Task.Delay를 쓰기 위해 꼭 필요합니다!
using System.Windows.Input;

namespace ProfessorUI.ViewModels
{
    public class FileReadyViewModel : INotifyPropertyChanged
    {
        private string _selectedFolderPath;
        public string SelectedFolderPath
        {
            get => _selectedFolderPath;
            set
            {
                _selectedFolderPath = value;
                OnPropertyChanged();
            }
        }

        // ⭐ 1. 프로그레스 바를 위한 새로운 데이터들
        private int _progressValue = 0;
        public int ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnPropertyChanged(); }
        }

        private string _progressText = "0%";
        public string ProgressText
        {
            get => _progressText;
            set { _progressText = value; OnPropertyChanged(); }
        }

        private string _currentStatusMessage = "대기 중...";
        public string CurrentStatusMessage
        {
            get => _currentStatusMessage;
            set { _currentStatusMessage = value; OnPropertyChanged(); }
        }

        // 중복 실행 방지용 플래그
        private bool _isProcessing = false;

        public ICommand SelectFolderCommand { get; }
        public ICommand StartProcessCommand { get; }

        public FileReadyViewModel()
        {
            SelectFolderCommand = new RelayCommand(ExecuteSelectFolder);
            StartProcessCommand = new RelayCommand(ExecuteStartProcess);
        }

        private void ExecuteSelectFolder(object obj)
        {
            SelectedFolderPath = @"C:\Users\Professor\Documents\ExamFiles";
            CurrentStatusMessage = "폴더 선택 완료. 준비되었습니다.";
            ProgressValue = 0;
            ProgressText = "0%";
        }

        // ⭐ 2. async 키워드를 붙여서 비동기(백그라운드)로 동작하게 만듭니다.
        private async void ExecuteStartProcess(object obj)
        {
            if (string.IsNullOrEmpty(SelectedFolderPath))
            {
                System.Windows.MessageBox.Show("먼저 대상 폴더를 선택해주세요!", "알림");
                return;
            }

            if (_isProcessing) return; // 이미 진행 중이면 무시
            _isProcessing = true;

            CurrentStatusMessage = "압축 및 암호화 진행 중...";

            // ⭐ 3. 0부터 100까지 가짜 진행률 올리기
            for (int i = 0; i <= 100; i++)
            {
                ProgressValue = i;
                ProgressText = $"{i}%";

                // 화면이 멈추지 않도록 30밀리초(0.03초)씩 쉬어줍니다.
                // 나중에 실제 작업할 때는 이 부분에 진짜 압축 코드가 들어갑니다.
                await Task.Delay(30);
            }

            CurrentStatusMessage = "모든 작업이 완료되었습니다!";
            System.Windows.MessageBox.Show("파일 준비가 끝났습니다.", "완료");

            _isProcessing = false;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;
        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object parameter) => _execute(parameter);
    }
}