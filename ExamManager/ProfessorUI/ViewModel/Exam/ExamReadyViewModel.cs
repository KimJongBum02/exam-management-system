using Microsoft.Win32; // 파일 대화상자 사용을 위해 추가
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ProfessorUI.Service;
using ProfessorUI.Common;


namespace ProfessorUI.ViewModel
{
    // 시험 준비 1단계 화면의 뷰모델.
    // 고른 목록을 들고 화면에 보여 주는 일까지만 한다 —
    // 이름 짓기·같은 내용 판정·압축 실행은 ExamReadyService 가 맡는다.
    public class ExamReadyViewModel : INotifyPropertyChanged
    {
        private readonly ExamReadyService _package = ExamReadyService.Instance;

        // 텍스트 박스에 보일 요약 메시지 ("항목 3개 · 파일 12개")
        private string _selectedFilesSummary = string.Empty;
        public string SelectedFilesSummary
        {
            get => _selectedFilesSummary;
            set { _selectedFilesSummary = value; OnPropertyChanged(); }
        }

        // 압축할 목록. [선택]을 누를 때마다 뒤에 더해지고, 줄마다 [✕]로 뺄 수 있다.
        // 빠뜨린 파일을 더할 때 처음 고른 것까지 다시 고르지 않아도 되게 하려는 것이다.
        public ObservableCollection<SelectedFileViewModel> SelectedItems { get; } = new();

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

        // 화면에는 바탕화면부터 짧게 보여 준다. 만들기 전에는 파일 이름 자리를 '...'로 둔다.
        private static readonly string PackageFolderText =
            @"바탕화면\" + Path.GetFileName(ExamReadyService.PackageFolder) + @"\";

        private string _packagePathText = PackageFolderText + "...";
        public string PackagePathText
        {
            get => _packagePathText;
            private set { _packagePathText = value; OnPropertyChanged(); }
        }

        // 압축이 끝난 뒤인지. 목록을 고쳤을 때 "다시 압축해야 한다"고 알리는 데 쓴다.
        private bool _isPackaged;
        private bool _isProcessing = false;

        public ICommand SelectCommand { get; }
        public ICommand RemoveItemCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand StartProcessCommand { get; }

        // 파일 선택 창 이름 칸의 기본값. 이 값 그대로 [열기]를 누르면 지금 들어가 있는 폴더를 고른 것으로 본다.
        private const string FolderPlaceholder = "폴더 선택";

        public ExamReadyViewModel()
        {
            SelectCommand = new RelayCommand(ExecuteSelect);
            RemoveItemCommand = new RelayCommand(ExecuteRemoveItem);
            ClearAllCommand = new RelayCommand(ExecuteClearAll);
            StartProcessCommand = new RelayCommand(ExecuteStartProcess);
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
        // 하위 폴더는 구조 그대로 들어간다(ZipService 가 폴더를 통째로 복사한다).
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

                SelectedFileViewModel? sameName = SelectedItems.FirstOrDefault(
                    i => string.Equals(i.Name, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));
                if (sameName != null)
                {
                    conflicts.Add($"{Path.GetFileName(path)}  ({Path.GetDirectoryName(path)})");
                    continue;
                }

                bool isFolder = Directory.Exists(path);
                SelectedItems.Add(new SelectedFileViewModel
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
            if (_isProcessing || parameter is not SelectedFileViewModel item) return;

            SelectedItems.Remove(item);
            OnSelectionChanged();
        }

        // [모두 지우기] — 아예 잘못 골랐을 때 처음부터 다시 시작한다.
        // 이미 만든 묶음을 아직 보내지 않았다면 그 묶음도 지운다(ExamReadyService).
        // 보낸 뒤라면 묶음은 남긴다. 학생이 받은 묶음의 암호가 답안 수집과 재배포에 필요하다.
        private void ExecuteClearAll(object? obj)
        {
            if (_isProcessing) return;

            string? packageName = _package.UndistributedPackageName;
            if (packageName == null && SelectedItems.Count == 0) return;

            string message = packageName != null
                ? $"선택한 항목을 모두 지우고, 만들어 둔 시험 압축 파일({packageName})도 지웁니다.\n계속할까요?"
                : "선택한 항목을 모두 지웁니다. 계속할까요?";
            var answer = System.Windows.MessageBox.Show(message, "모두 지우기",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning,
                System.Windows.MessageBoxResult.No);
            if (answer != System.Windows.MessageBoxResult.Yes) return;

            if (packageName != null)
            {
                string? error = _package.DeleteUndistributedPackage();
                if (error != null)
                {
                    System.Windows.MessageBox.Show($"{error} 목록도 그대로 두었습니다.", "오류");
                    return;
                }

                _isPackaged = false;
                PackagePathText = PackageFolderText + "...";
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
            else if (_isPackaged)
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
            ExamReadyService.PackagePlan plan;
            try
            {
                plan = await _package.PlanAsync(items);
            }
            catch (Exception ex)
            {
                _isProcessing = false;
                System.Windows.MessageBox.Show($"선택한 파일을 읽지 못했습니다: {ex.Message}", "오류");
                return;
            }

            if (plan.Previous != null && !ConfirmRepackage(plan.Previous))
            {
                _isProcessing = false;
                return;
            }

            CurrentStatusMessage = "압축 및 암호화 진행 중...";
            ProgressValue = 0;
            ProgressText = "0%";

            // 이 메서드는 async void 라서 서비스에서 예외가 새면 앱이 그대로 종료된다.
            // 서비스는 실패를 예외 대신 결과로 돌려준다.
            ExamReadyService.PackageResult result = await _package.CreateAsync(items, plan.Fingerprint);

            if (result.Ok)
            {
                ProgressValue = 100;
                ProgressText = "100%";
                CurrentStatusMessage = "압축 및 암호화 완료!";
                PackagePathText = PackageFolderText + Path.GetFileName(result.OutputPath);
                _isPackaged = true;
            }
            else
            {
                CurrentStatusMessage = "실패";
                System.Windows.MessageBox.Show(result.Error, "오류");
            }

            _isProcessing = false;
        }

        // 같은 내용을 다시 압축할지 교수에게 묻는다. 기본 버튼은 [아니요]다.
        private static bool ConfirmRepackage(ExamReadyService.PackagedRecord previous)
            => System.Windows.MessageBox.Show(
                   "이미 암호화·압축을 마친 파일입니다. 다시 만들 필요가 없습니다.\n\n" +
                   $"위치: {previous.OutputPath}\n" +
                   $"만든 시각: {previous.PackagedAt:HH:mm:ss}\n\n" +
                   "다시 만들면 새 암호가 생기고 기존 묶음을 바꿔 놓습니다. 그래도 다시 만들까요?",
                   "이미 압축된 파일",
                   System.Windows.MessageBoxButton.YesNo,
                   System.Windows.MessageBoxImage.Warning,
                   System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
