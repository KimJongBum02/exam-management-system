using Microsoft.Win32; // 파일 대화상자 사용을 위해 추가
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using ProfessorUI.Service;

namespace ProfessorUI.ViewModel
{
    // 압축할 목록의 한 줄. 파일이면 그 파일, 폴더면 폴더 전체(하위 구조 그대로)가 묶음에 들어간다.
    public class SelectedExamItem
    {
        public string FullPath { get; init; } = string.Empty;
        public bool IsFolder { get; init; }
        public int FileCount { get; init; }   // 폴더면 안의 파일 수, 파일이면 1

        public string Name => Path.GetFileName(FullPath);
        public string Display => IsFolder ? $"{Name}\\  (폴더 · 파일 {FileCount}개)" : Name;

        // 어디서 가져왔는지. 여러 폴더에서 더할 수 있어 줄마다 보여 준다.
        public string Location => Path.GetDirectoryName(FullPath) ?? string.Empty;
    }

    public class FileReadyViewModel : INotifyPropertyChanged
    {
        // 텍스트 박스에 보일 요약 메시지 ("항목 3개 · 파일 12개")
        private string _selectedFilesSummary = string.Empty;
        public string SelectedFilesSummary
        {
            get => _selectedFilesSummary;
            set { _selectedFilesSummary = value; OnPropertyChanged(); }
        }

        // 압축할 목록. [선택]을 누를 때마다 뒤에 더해지고, 줄마다 [✕]로 뺄 수 있다.
        // 빠뜨린 파일을 더할 때 처음 고른 것까지 다시 고르지 않아도 되게 하려는 것이다.
        public ObservableCollection<SelectedExamItem> SelectedItems { get; } = new();

        public bool HasSelectedItems => SelectedItems.Count > 0;

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

        // 배포용 묶음이 만들어지는 곳. 압축 전에도 어디에 생기는지 미리 알려 준다.
        public static string PackageFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "시험 파일");

        // 화면에는 바탕화면부터 짧게 보여 준다. 만들기 전에는 파일 이름 자리를 '...'로 둔다.
        private static readonly string PackageFolderText = @"바탕화면\" + Path.GetFileName(PackageFolder) + @"\";

        private string _packagePathText = PackageFolderText + "...";
        public string PackagePathText
        {
            get => _packagePathText;
            private set { _packagePathText = value; OnPropertyChanged(); }
        }

        private bool _isProcessing = false;

        public ICommand SelectCommand { get; }
        public ICommand RemoveItemCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand StartProcessCommand { get; }

        // 이번 실행에서 암호화·압축을 마친 묶음들. 같은 내용을 다시 압축하려 하면 경고하는 데 쓴다.
        // 파일로 남기지 않는다 — 암호는 메모리에만 있어서, 앱을 다시 켜면 이전 묶음은 배포에 쓸 수 없고
        // 어차피 새로 만들어야 한다.
        private readonly List<PackagedRecord> _packaged = new();

        private sealed record PackagedRecord(string Fingerprint, string OutputPath, DateTime PackagedAt);

        // 이번 실행에서 마지막으로 만든 묶음과, 그 묶음을 학생에게 보냈는지.
        // 보내기 전에 다시 압축하면 같은 번호를 새 내용으로 바꾸고, 보낸 뒤에는 다음 번호를 쓴다.
        // FileDeployState.IsFileDistributed 는 [새 시험 준비]에서 다시 false 가 되므로 따로 기억한다.
        private string? _currentOutput;
        private bool _currentDistributed;

        // 파일 선택 창 이름 칸의 기본값. 이 값 그대로 [열기]를 누르면 지금 들어가 있는 폴더를 고른 것으로 본다.
        private const string FolderPlaceholder = "폴더 선택";

        public FileReadyViewModel()
        {
            SelectCommand = new RelayCommand(ExecuteSelect);
            RemoveItemCommand = new RelayCommand(ExecuteRemoveItem);
            ClearAllCommand = new RelayCommand(ExecuteClearAll);
            StartProcessCommand = new RelayCommand(ExecuteStartProcess);

            FileDeployState.StateChanged += () =>
            {
                if (FileDeployState.IsFileDistributed && _currentOutput != null &&
                    string.Equals(FileDeployState.PackagePath, _currentOutput, StringComparison.OrdinalIgnoreCase))
                    _currentDistributed = true;
            };
        }

        // 선택 버튼 하나로 파일과 폴더를 모두 고른다.
        // Windows 선택 창은 파일과 폴더를 한 창에서 함께 고르지 못해서, 파일 열기 창을 이렇게 쓴다.
        //   파일을 클릭하고 [열기]           → 고른 파일들
        //   폴더 안으로 들어가 [열기]        → 그 폴더 (이름 칸이 '폴더 선택' 그대로일 때)
        // 이름 칸에는 없는 이름도 들어갈 수 있도록 존재·이름 검사를 끈다. 검사는 아래에서 직접 한다.
        private void ExecuteSelect(object? obj)
        {
            if (_isProcessing) return;

            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Title = "시험 파일을 고르거나, 시험 파일이 담긴 폴더 안으로 들어가 [열기]를 누르세요",
                Filter = "모든 파일 (*.*)|*.*",
                FileName = FolderPlaceholder,
                CheckFileExists = false,
                ValidateNames = false,
            };
            if (dialog.ShowDialog() != true) return;

            string[] picked = dialog.FileNames;

            // 이름 칸에 폴더 경로를 직접 적은 경우도 폴더로 받는다.
            if (picked.Length == 1 && Directory.Exists(picked[0]))
            {
                SelectFolder(picked[0]);
                return;
            }

            if (picked.Length == 1 && !File.Exists(picked[0]))
            {
                if (Path.GetFileName(picked[0]) == FolderPlaceholder)
                    SelectFolder(Path.GetDirectoryName(picked[0])!);
                else
                    System.Windows.MessageBox.Show($"파일을 찾을 수 없습니다.\n{picked[0]}", "선택");
                return;
            }

            AddItems(picked);
        }

        // 시험 파일이 담긴 폴더를 통째로 고른다. 교수는 문제를 폴더 하나에 모아 두는 경우가 많다.
        //
        // 폴더 자체가 아니라 폴더 안의 항목을 담는다. 폴더째 담으면 학생 쪽에서
        // 시험 폴더 안에 같은 이름의 폴더가 한 겹 더 생겨 문제를 찾기 불편하다.
        // 하위 폴더는 구조 그대로 들어간다(ExamPackager 가 폴더를 통째로 복사한다).
        private void SelectFolder(string folder)
        {
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

            AddItems(entries);
        }

        // 고른 항목을 목록 뒤에 더한다. 파일 선택과 폴더 선택이 함께 쓴다.
        //   같은 경로       → 이미 있으므로 조용히 건너뛴다
        //   이름만 같음     → 넣지 않고 알린다. 묶음 안에서는 이름이 겹치면 하나가 덮어써져 사라진다.
        private void AddItems(IEnumerable<string> paths)
        {
            var conflicts = new List<string>();

            foreach (string path in paths)
            {
                if (SelectedItems.Any(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase)))
                    continue;

                SelectedExamItem? sameName = SelectedItems.FirstOrDefault(
                    i => string.Equals(i.Name, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));
                if (sameName != null)
                {
                    conflicts.Add($"{Path.GetFileName(path)}  ({Path.GetDirectoryName(path)})");
                    continue;
                }

                bool isFolder = Directory.Exists(path);
                SelectedItems.Add(new SelectedExamItem
                {
                    FullPath = path,
                    IsFolder = isFolder,
                    FileCount = isFolder ? Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length : 1,
                });
            }

            OnSelectionChanged();

            if (conflicts.Count > 0)
                System.Windows.MessageBox.Show(
                    "목록에 같은 이름이 이미 있어 넣지 않았습니다. 묶음 안에서는 같은 이름이 하나만 남습니다.\n" +
                    "이름을 바꾸거나 목록에서 먼저 빼 주세요.\n\n" + string.Join("\n", conflicts),
                    "같은 이름");
        }

        private void ExecuteRemoveItem(object? parameter)
        {
            if (_isProcessing || parameter is not SelectedExamItem item) return;

            SelectedItems.Remove(item);
            OnSelectionChanged();
        }

        // [모두 지우기] — 아예 잘못 골랐을 때 처음부터 다시 시작한다.
        // 이미 만든 묶음을 아직 보내지 않았다면 그 묶음도 지운다. 남겨 두면 잘못된 파일이 배포될 수 있다.
        // 보낸 뒤라면 묶음은 남긴다. 학생이 받은 묶음의 암호가 답안 수집과 재배포에 필요하다.
        private void ExecuteClearAll(object? obj)
        {
            if (_isProcessing) return;

            bool deletePackage = _currentOutput != null && !_currentDistributed && File.Exists(_currentOutput);
            if (!deletePackage && SelectedItems.Count == 0) return;

            string message = deletePackage
                ? $"선택한 항목을 모두 지우고, 만들어 둔 시험 압축 파일({Path.GetFileName(_currentOutput)})도 지웁니다.\n계속할까요?"
                : "선택한 항목을 모두 지웁니다. 계속할까요?";
            var answer = System.Windows.MessageBox.Show(message, "모두 지우기",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning,
                System.Windows.MessageBoxResult.No);
            if (answer != System.Windows.MessageBoxResult.Yes) return;

            if (deletePackage)
            {
                try
                {
                    File.Delete(_currentOutput!);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        $"시험 압축 파일을 지우지 못해 목록도 그대로 두었습니다: {ex.Message}", "오류");
                    return;
                }

                _packaged.RemoveAll(r => string.Equals(r.OutputPath, _currentOutput, StringComparison.OrdinalIgnoreCase));
                _currentOutput = null;
                PackagePathText = PackageFolderText + "...";

                // 배포 단계가 지운 묶음을 보내지 않도록 준비 상태를 되돌린다.
                FileDeployState.ExamId = null;
                FileDeployState.PackagePath = null;
                FileDeployState.Password = null;
                FileDeployState.IsFilePrepared = false;
            }

            SelectedItems.Clear();
            OnSelectionChanged();
        }

        // 목록이 바뀔 때마다 요약과 상태 문구를 맞춘다.
        // 압축한 뒤에 목록을 고쳤으면, 다시 압축해야 배포에 반영된다는 것을 알린다.
        private void OnSelectionChanged()
        {
            int fileCount = SelectedItems.Sum(i => i.FileCount);
            SelectedFilesSummary = SelectedItems.Count == 0
                ? string.Empty
                : $"항목 {SelectedItems.Count}개 · 파일 {fileCount}개";
            OnPropertyChanged(nameof(HasSelectedItems));

            if (SelectedItems.Count == 0)
                CurrentStatusMessage = "대기 중...";
            else if (_currentOutput != null)
                CurrentStatusMessage = "목록이 바뀌었습니다. 다시 암호화·압축해야 배포에 반영됩니다.";
            else
                CurrentStatusMessage = "파일 선택 완료. 준비되었습니다.";

            ProgressValue = 0;
            ProgressText = "0%";
        }

        private async void ExecuteStartProcess(object? obj)
        {
            if (SelectedItems.Count == 0)
            {
                System.Windows.MessageBox.Show("먼저 대상 파일을 선택해주세요!", "알림");
                return;
            }

            if (_isProcessing) return;
            _isProcessing = true;

            // 압축은 백그라운드에서 돈다. 그동안 목록이 바뀌어도 섞이지 않게 지금 목록을 떠 둔다.
            var items = SelectedItems.Select(i => i.FullPath).ToList();

            // 같은 내용을 이미 압축했으면 경고한다. 다시 만들면 새 암호가 생기고 기존 묶음을 바꿔치기하게 된다.
            // 폴더가 크면 파일을 훑는 데 시간이 걸려 백그라운드에서 센다.
            string fingerprint;
            try
            {
                fingerprint = await Task.Run(() => ComputeFingerprint(items));
            }
            catch (Exception ex)
            {
                _isProcessing = false;
                System.Windows.MessageBox.Show($"선택한 파일을 읽지 못했습니다: {ex.Message}", "오류");
                return;
            }

            // 묶음 파일을 지웠으면 다시 만들어야 하므로 경고하지 않는다.
            PackagedRecord? previous = _packaged.LastOrDefault(
                r => r.Fingerprint == fingerprint && File.Exists(r.OutputPath));
            if (previous != null)
            {
                var answer = System.Windows.MessageBox.Show(
                    "이미 암호화·압축을 마친 파일입니다. 다시 만들 필요가 없습니다.\n\n" +
                    $"위치: {previous.OutputPath}\n" +
                    $"만든 시각: {previous.PackagedAt:HH:mm:ss}\n\n" +
                    "다시 만들면 새 암호가 생기고 기존 묶음을 바꿔 놓습니다. 그래도 다시 만들까요?",
                    "이미 압축된 파일",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning,
                    System.Windows.MessageBoxResult.No);
                if (answer != System.Windows.MessageBoxResult.Yes)
                {
                    _isProcessing = false;
                    return;
                }
            }

            CurrentStatusMessage = "압축 및 암호화 진행 중...";
            ProgressValue = 0;
            ProgressText = "0%";

            // 배포용 묶음을 만들어 두는 곳. 교수가 바로 확인할 수 있도록 바탕화면에 둔다.
            string packageDir = PackageFolder;
            string output;
            try
            {
                Directory.CreateDirectory(packageDir);
                output = ChooseOutputPath(packageDir);
            }
            catch (Exception ex)
            {
                CurrentStatusMessage = "실패";
                _isProcessing = false;
                System.Windows.MessageBox.Show($"시험 파일 폴더를 준비하지 못했습니다: {ex.Message}", "오류");
                return;
            }
            // 예: 0917_T1 — 학생 PC 에서는 이 이름이 그대로 시험 폴더 이름이 된다.
            string examId = Path.GetFileNameWithoutExtension(output);

            string? password;
            try
            {
                // 선택 파일들을 스테이징 폴더로 묶어 7za로 압축+암호화 (백그라운드 실행)
                password = await Task.Run(() => ExamPackager.Package(items, output));
            }
            catch (Exception ex)
            {
                // 배포가 진행 중이면 이전 아카이브가 잠겨 바꿔 놓지 못하는 등으로 실패할 수 있다.
                // 이때 이전 아카이브와 배포 정보는 그대로 남는다(ExamPackager).
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
                CurrentStatusMessage = "압축 및 암호화 완료!";
                PackagePathText = PackageFolderText + Path.GetFileName(output);

                // 배포 단계가 읽도록 공용 저장소에 보관
                FileDeployState.ExamId = examId;
                FileDeployState.PackagePath = output;
                FileDeployState.Password = password;
                FileDeployState.IsFilePrepared = true;

                _currentOutput = output;
                _currentDistributed = false;

                // 같은 자리의 이전 기록은 이 묶음으로 바뀌었으므로 버린다.
                _packaged.RemoveAll(r => string.Equals(r.OutputPath, output, StringComparison.OrdinalIgnoreCase));
                _packaged.Add(new PackagedRecord(fingerprint, output, DateTime.Now));
            }
            else
            {
                CurrentStatusMessage = "실패";
                System.Windows.MessageBox.Show("압축/암호화 실패. 7za.exe와 입력을 확인하세요.", "오류");
            }

            _isProcessing = false;
        }

        // 묶음 이름을 정한다. 하루에 시험을 두 번(오전·오후) 보므로 날짜 뒤에 회차를 붙인다: 0917_T1, 0917_T2 …
        //   오늘 만든 묶음을 아직 보내지 않았으면 → 같은 번호를 새 내용으로 바꾼다 (빠뜨린 파일을 더한 경우)
        //   보냈거나, 이번 실행에서 만든 적이 없으면 → 폴더에 있는 오늘 번호 중 가장 큰 것 + 1
        // 앱을 다시 켜면 보냈는지 알 수 없어 늘 다음 번호를 쓴다.
        private string ChooseOutputPath(string packageDir)
        {
            string prefix = DateTime.Now.ToString("MMdd") + "_T";

            if (_currentOutput != null && !_currentDistributed &&
                Path.GetFileName(_currentOutput).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return _currentOutput;

            var pattern = new Regex("^" + Regex.Escape(prefix) + @"(\d+)\.7z$", RegexOptions.IgnoreCase);
            int last = Directory.GetFiles(packageDir)
                .Select(file => pattern.Match(Path.GetFileName(file)))
                .Where(match => match.Success)
                .Select(match => int.Parse(match.Groups[1].Value))
                .DefaultIfEmpty(0)
                .Max();

            return Path.Combine(packageDir, $"{prefix}{last + 1}.7z");
        }

        // 선택한 항목의 내용을 한 줄로 요약한다. 두 번의 선택이 같은 내용인지 비교하는 데 쓴다.
        // 파일마다 경로·크기·수정 시각을 담는다 — 원본을 고쳐 저장하면 수정 시각이 바뀌어 새 내용으로 본다.
        // 해시를 내지 않는 이유: 시험 폴더가 수백 MB여도 선택할 때마다 기다리게 할 수는 없다.
        private static string ComputeFingerprint(IEnumerable<string> items)
        {
            var lines = new List<string>();
            foreach (string item in items)
            {
                if (Directory.Exists(item))
                {
                    lines.Add(item + "\\");
                    foreach (string file in Directory.GetFiles(item, "*", SearchOption.AllDirectories))
                        lines.Add(Describe(file));
                }
                else
                {
                    lines.Add(Describe(item));
                }
            }

            lines.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join("\n", lines).ToUpperInvariant();

            static string Describe(string file)
            {
                var info = new FileInfo(file);
                return $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            }
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
