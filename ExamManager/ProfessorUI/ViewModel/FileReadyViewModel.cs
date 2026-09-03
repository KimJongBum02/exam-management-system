using Microsoft.Win32; // 파일 대화상자 사용을 위해 추가
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using ProfessorUI.Service;

namespace ProfessorUI.ViewModel
{
    public class FileReadyViewModel : INotifyPropertyChanged
    {
        // 텍스트 박스에 보일 요약 메시지 ("O개의 파일이 선택되었습니다")
        private string _selectedFilesSummary = string.Empty;
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

            // 시험이 끝나 전체가 초기화되면 골라 둔 파일과 진행률도 함께 비운다.
            FileDeployState.Cleared += ResetView;
        }

        // 화면을 파일 선택 전 상태로 되돌린다.
        private void ResetView()
        {
            SelectedFilePaths.Clear();
            SelectedFileNames.Clear();
            SelectedFilesSummary = string.Empty;
            CurrentStatusMessage = "대기 중...";
            CurrentFileNameDisplay = "선택된 파일 없음";
            ProgressValue = 0;
            ProgressText = "0%";
        }

        private void ExecuteSelectFiles(object? obj)
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

        private async void ExecuteStartProcess(object? obj)
        {
            if (SelectedFilePaths.Count == 0)
            {
                System.Windows.MessageBox.Show("먼저 대상 파일을 선택해주세요!", "알림");
                return;
            }

            if (_isProcessing) return;
            _isProcessing = true;

            CurrentStatusMessage = "압축 및 암호화 진행 중...";
            ProgressValue = 0;
            ProgressText = "0%";

            string examId = "Exam_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string packageDir = @"C:\Exam";
            string output = Path.Combine(packageDir, examId + ".7z");

            string? password;
            try
            {
                Directory.CreateDirectory(packageDir);

                // 선택 파일들을 스테이징 폴더로 묶어 7za로 압축+암호화 (백그라운드 실행)
                password = await Task.Run(() =>
                    ExamPackager.Package(SelectedFilePaths, output));
            }
            catch (Exception ex)
            {
                // 배포가 진행 중이면 이전 아카이브가 잠겨 삭제되지 않는 등으로 실패할 수 있다.
                // 이 메서드는 async void 라서 여기서 잡지 않으면 앱이 그대로 종료된다.
                CurrentStatusMessage = "실패";
                _isProcessing = false;
                System.Windows.MessageBox.Show(
                    $"압축/암호화 실패: {ex.Message}\n배포가 진행 중이면 끝난 뒤 다시 시도해 주세요.", "오류");
                return;
            }

            if (password != null)
            {
                ProgressValue = 100;
                ProgressText = "100%";
                CurrentFileNameDisplay = "모든 파일 처리 완료";
                CurrentStatusMessage = "압축 및 암호화 완료!";

                // 배포 단계가 읽도록 공용 저장소에 보관.
                // 새 파일이므로 이전 배포 기록은 여기서 지워지고, 배포 목록도 다시 처음 상태가 된다.
                FileDeployState.SetPackage(examId, output, password);
            }
            else
            {
                CurrentStatusMessage = "실패";
                System.Windows.MessageBox.Show("압축/암호화 실패. 7za.exe와 입력을 확인하세요.", "오류");
            }

            _isProcessing = false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;
        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }
        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object? parameter) => _execute(parameter);
    }
}