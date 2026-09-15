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

        // 고른 파일들이 있던 폴더.
        // 파일 열기 창은 한 폴더 안에서만 여러 개를 고를 수 있으므로 폴더는 언제나 하나다.
        private string _sourceFolder = "선택된 파일 없음";
        public string SourceFolder
        {
            get => _sourceFolder;
            private set { _sourceFolder = value; OnPropertyChanged(); }
        }

        // 배포용 묶음이 만들어지는 곳. 압축 전에도 어디에 생기는지 미리 알려 준다.
        public static string PackageFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "시험 파일");

        private string _packagePathText = PackageFolder + @"\  (압축 실행 시 생성)";
        public string PackagePathText
        {
            get => _packagePathText;
            private set { _packagePathText = value; OnPropertyChanged(); }
        }

        private bool _isProcessing = false;

        public ICommand SelectFilesCommand { get; }
        public ICommand SelectFolderCommand { get; }
        public ICommand StartProcessCommand { get; }

        public FileReadyViewModel()
        {
            SelectFilesCommand = new RelayCommand(ExecuteSelectFiles);
            SelectFolderCommand = new RelayCommand(ExecuteSelectFolder);
            StartProcessCommand = new RelayCommand(ExecuteStartProcess);
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
                ApplySelection(openFileDialog.FileNames,
                               Path.GetDirectoryName(openFileDialog.FileNames[0]) ?? "-",
                               $"{openFileDialog.FileNames.Length}개의 파일이 선택되었습니다.");
            }
        }

        // 시험 파일이 담긴 폴더를 통째로 고른다. 교수는 문제를 폴더 하나에 모아 두는 경우가 많다.
        //
        // 폴더 자체가 아니라 폴더 안의 항목을 담는다. 폴더째 담으면 학생 쪽에서
        // 시험 폴더 안에 같은 이름의 폴더가 한 겹 더 생겨 문제를 찾기 불편하다.
        // 하위 폴더는 구조 그대로 들어간다(ExamPackager 가 폴더를 통째로 복사한다).
        private void ExecuteSelectFolder(object? obj)
        {
            var dialog = new OpenFolderDialog { Title = "시험 파일이 담긴 폴더를 선택하세요" };
            if (dialog.ShowDialog() != true) return;

            string folder = dialog.FolderName;

            // 숨김 파일(desktop.ini, Thumbs.db 등)은 학생에게 보낼 이유가 없다.
            string[] entries = Directory.GetFileSystemEntries(folder)
                .Where(entry => (File.GetAttributes(entry) & FileAttributes.Hidden) == 0)
                .OrderBy(entry => !Directory.Exists(entry))     // 폴더 먼저
                .ThenBy(entry => Path.GetFileName(entry))
                .ToArray();

            if (entries.Length == 0)
            {
                System.Windows.MessageBox.Show("선택한 폴더가 비어 있습니다.", "폴더 선택");
                return;
            }

            int fileCount = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Length;
            ApplySelection(entries, folder, $"'{Path.GetFileName(folder)}' 폴더 · 파일 {fileCount}개");
        }

        // 고른 항목을 화면과 압축 목록에 담는다. 파일 선택과 폴더 선택이 함께 쓴다.
        private void ApplySelection(IEnumerable<string> paths, string sourceFolder, string summary)
        {
            SelectedFilePaths.Clear();
            SelectedFileNames.Clear();

            foreach (string path in paths)
            {
                SelectedFilePaths.Add(path);
                SelectedFileNames.Add(Directory.Exists(path)
                    ? $"- {Path.GetFileName(path)}\\  (폴더)"
                    : $"- {Path.GetFileName(path)}");
            }

            SourceFolder = sourceFolder;
            SelectedFilesSummary = summary;
            CurrentStatusMessage = "파일 선택 완료. 준비되었습니다.";
            CurrentFileNameDisplay = "대기 중...";
            ProgressValue = 0;
            ProgressText = "0%";
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

            // 예: 20260915_시험문제.7z — 학생 PC 에서는 이 이름이 그대로 시험 폴더 이름이 된다.
            // 같은 날 다시 압축하면 같은 이름이라 앞 파일을 덮어쓴다(ExamPackager 가 먼저 지운다).
            string examId = DateTime.Now.ToString("yyyyMMdd") + "_시험문제";
            // 배포용 묶음을 만들어 두는 곳. 교수가 바로 확인할 수 있도록 바탕화면에 둔다.
            string packageDir = PackageFolder;
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
                PackagePathText = output;   // 실제로 만들어진 파일의 전체 경로

                // 배포 단계가 읽도록 공용 저장소에 보관
                FileDeployState.ExamId = examId;
                FileDeployState.PackagePath = output;
                FileDeployState.Password = password;
                FileDeployState.IsFilePrepared = true;
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