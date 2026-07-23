using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;

namespace StudentUI.Service
{
    // 교수 PC가 배포한 시험 파일의 수신·압축 해제 상태를 앱 전체에서 공유한다.
    // 파일은 학생이 대기 화면에 머무는 동안 도착하므로, 시험 준비 화면이
    // 그 뒤에 열려도 상태를 볼 수 있도록 여기에 보관한다.
    public class ExamFileStore : INotifyPropertyChanged
    {
        public static ExamFileStore Instance { get; } = new ExamFileStore();

        // 압축 해제 위치 (바탕화면\ExamFiles 고정)
        public string ExtractFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ExamFiles");

        private string _archivePath = string.Empty; // 수신된 .7z 임시 경로
        private string _password = string.Empty;    // 교수 PC가 함께 보낸 암호
        private string _transferId = string.Empty;  // 현재 전송 식별자 (재배포 판별용)

        private ExamFileStore() { }

        // 앱 시작 시 한 번 호출 — 파일 수신 이벤트를 구독한다.
        public void Start()
        {
            NetworkService.Instance.FileProgress += OnFileProgress;
            NetworkService.Instance.FileReceived += OnFileReceived;
            NetworkService.Instance.FileError += OnFileError;
        }

        // ── 바인딩용 상태 ──
        private string _fileName = "-";
        public string FileName
        {
            get => _fileName;
            private set { _fileName = value; OnPropertyChanged(); }
        }

        private string _statusText = "파일 수신 대기 중";
        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value; OnPropertyChanged(); }
        }

        private int _progress;
        public int Progress
        {
            get => _progress;
            private set { _progress = value; OnPropertyChanged(); }
        }

        private bool _isReceived;
        public bool IsReceived
        {
            get => _isReceived;
            private set { _isReceived = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanExtract)); }
        }

        private bool _isExtracted;
        public bool IsExtracted
        {
            get => _isExtracted;
            private set { _isExtracted = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanExtract)); }
        }

        private bool _isExtracting;
        public bool IsExtracting
        {
            get => _isExtracting;
            private set { _isExtracting = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanExtract)); }
        }

        public bool CanExtract => IsReceived && !IsExtracting && !IsExtracted;

        // 압축 해제로 복원된 파일 목록 (해제 폴더 기준 상대 경로)
        public ObservableCollection<string> ExtractedFiles { get; } = new ObservableCollection<string>();

        // ── 수신 이벤트 (네이티브 스레드에서 호출됨) ──
        private void OnFileProgress(string transferId, string fileName, int percent) => Post(() =>
        {
            if (transferId != _transferId)
                BeginNewTransfer(transferId);      // 교수가 파일을 다시 배포한 경우
            else if (IsReceived)
                return;                            // 같은 전송의 잔여 알림은 무시

            FileName = fileName;
            Progress = percent;
            StatusText = $"파일 수신 중... {percent}%";
        });

        private void OnFileReceived(string transferId, string senderId, string fileName,
                                    string tempPath, long fileSize, string archivePassword) => Post(() =>
        {
            // 진행률 알림 없이 완료만 도착하는 경우에도 재배포를 놓치지 않도록 여기서도 확인한다
            if (transferId != _transferId)
                BeginNewTransfer(transferId);

            _archivePath = tempPath;
            _password = archivePassword;

            FileName = fileName;
            Progress = 100;
            IsReceived = true;
            StatusText = $"수신 완료 · {FormatSize(fileSize)} · AES-256 암호화됨";
        });

        // 새 전송이 시작되면 이전 수신·해제 결과를 버린다.
        // (이렇게 하지 않으면 재배포된 파일을 받고도 IsExtracted가 true로 남아 다시 해제할 수 없다)
        private void BeginNewTransfer(string transferId)
        {
            _transferId = transferId;
            IsReceived = false;
            IsExtracted = false;
            ExtractedFiles.Clear();
        }

        private void OnFileError(string transferId, string message) => Post(() =>
        {
            StatusText = $"파일 수신 실패: {message}";
        });

        // ── 압축 해제 ──
        public async Task ExtractAsync()
        {
            if (!CanExtract) return;

            IsExtracting = true;
            StatusText = "압축 해제 중...";

            string sevenZa = Path.Combine(AppContext.BaseDirectory, "7za.exe");
            string archive = _archivePath;
            string password = _password;
            string outputFolder = ExtractFolder;

            int code;
            try
            {
                // 7za.exe 실행이 끝날 때까지 블로킹되므로 백그라운드에서 수행
                code = await Task.Run(() =>
                {
                    Directory.CreateDirectory(outputFolder);
                    return FileControlService.FC_ExtractDecrypt(sevenZa, archive, outputFolder, password);
                });
            }
            catch (Exception ex)
            {
                IsExtracting = false;
                StatusText = $"압축 해제 실패: {ex.Message} — 압축 해제 버튼을 다시 눌러 주세요.";
                return;
            }

            IsExtracting = false;

            if (code == 0)
            {
                LoadExtractedFiles(outputFolder);
                IsExtracted = true;
                StatusText = $"압축 해제 완료 · 파일 {ExtractedFiles.Count}개";
            }
            else
            {
                StatusText = $"압축 해제 실패 (코드 {code}) — 압축 해제 버튼을 다시 눌러 주세요.";
            }
        }

        // 해제된 파일을 화면에 보여주기 위해 목록을 읽어온다
        private void LoadExtractedFiles(string outputFolder)
        {
            ExtractedFiles.Clear();
            try
            {
                foreach (string path in Directory.GetFiles(outputFolder, "*", SearchOption.AllDirectories))
                    ExtractedFiles.Add(Path.GetRelativePath(outputFolder, path));
            }
            catch (Exception)
            {
                // 목록 표시는 부가 정보이므로 실패해도 해제 자체는 성공으로 둔다
            }
        }

        // 네이티브 스레드에서 온 알림을 UI 스레드로 넘긴다
        private static void Post(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(action);
        }

        private static string FormatSize(long bytes)
            => bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:0.#} MB" : $"{bytes / 1024.0:0.#} KB";

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
