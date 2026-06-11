using Microsoft.Win32; // 파일 대화상자 사용을 위해 추가
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using ProfessorUI.Stores;

namespace ProfessorUI.ViewModels
{
    public class FileReadyViewModel : INotifyPropertyChanged
    {
        // 텍스트 박스에 보일 요약 메시지 ("O개의 파일이 선택되었습니다")
        private string _selectedFilesSummary;
        public string SelectedFilesSummary
        {
            get => _selectedFilesSummary;
            set { _selectedFilesSummary = value; OnPropertyChanged(); }
        }

        // 선택된 실제 파일들의 전체 경로 리스트 (압축 로직에서 사용)
        public ObservableCollection<string> SelectedFilePaths { get; } = new ObservableCollection<string>();

        // 화면 아래에 보여줄 파일 이름만 담은 리스트
        public ObservableCollection<string> SelectedFileNames { get; } = new ObservableCollection<string>();

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

        // 현재 처리 중인 파일 이름을 보여주기 위한 속성
        private string _currentFileNameDisplay = "선택된 파일 없음";
        public string CurrentFileNameDisplay
        {
            get => _currentFileNameDisplay;
            set { _currentFileNameDisplay = value; OnPropertyChanged(); }
        }

        private bool _isProcessing = false;

        public ICommand SelectFilesCommand { get; }
        public ICommand StartProcessCommand { get; }

        public FileReadyViewModel()
        {
            SelectFilesCommand = new RelayCommand(ExecuteSelectFiles);
            StartProcessCommand = new RelayCommand(ExecuteStartProcess);
        }

        private void ExecuteSelectFiles(object obj)
        {
            // 윈도우 기본 파일 열기 창 띄우기
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Multiselect = true, // 여러 파일 선택 가능
                Title = "압축 및 암호화할 파일을 선택하세요",
                Filter = "모든 파일 (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SelectedFilePaths.Clear();
                SelectedFileNames.Clear();

                foreach (string filePath in openFileDialog.FileNames)
                {
                    SelectedFilePaths.Add(filePath);
                    SelectedFileNames.Add($"- {Path.GetFileName(filePath)}"); // 파일 이름만 추출
                }

                SelectedFilesSummary = $"{SelectedFilePaths.Count}개의 파일이 선택되었습니다.";
                CurrentStatusMessage = "파일 선택 완료. 준비되었습니다.";
                CurrentFileNameDisplay = "대기 중...";
                ProgressValue = 0;
                ProgressText = "0%";
            }
        }

        private async void ExecuteStartProcess(object obj)
        {
            if (SelectedFilePaths.Count == 0)
            {
                System.Windows.MessageBox.Show("먼저 대상 파일을 선택해주세요!", "알림");
                return;
            }

            if (_isProcessing) return;
            _isProcessing = true;

            CurrentStatusMessage = "압축 및 암호화 진행 중...";

            int totalFiles = SelectedFilePaths.Count;
            int currentProgress = 0;

            // 파일 개수만큼 나눠서 프로그레스 바를 올립니다.
            for (int i = 0; i < totalFiles; i++)
            {
                string currentFile = SelectedFilePaths[i];
                CurrentFileNameDisplay = $"현재 파일: {Path.GetFileName(currentFile)}";

                // 각 파일당 할당된 진행도 (예: 4개면 25%씩)
                int fileProgressTarget = (int)(((double)(i + 1) / totalFiles) * 100);

                // 부드러운 애니메이션 효과를 위해 1%씩 딜레이를 주며 상승
                while (currentProgress < fileProgressTarget)
                {
                    currentProgress++;
                    ProgressValue = currentProgress;
                    ProgressText = $"{currentProgress}%";

                    await Task.Delay(20); // 0.02초 딜레이
                }
            }

            // 모든 작업이 끝난 후 (100% 달성 시)
            ProgressValue = 100;
            ProgressText = "100%";
            CurrentFileNameDisplay = "모든 파일 처리 완료";
            CurrentStatusMessage = "모든 작업이 완료되었습니다!";

            // ⭐ 압축이 완료되었음을 공용 저장소에 기록합니다!
            FileDeployState.IsFilePrepared = true;

            System.Windows.MessageBox.Show("파일 암호화 및 압축이 끝났습니다.", "완료");

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