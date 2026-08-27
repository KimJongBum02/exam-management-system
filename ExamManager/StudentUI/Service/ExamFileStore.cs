using NetworkLib;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

        // 압축 해제 위치 (C:\Exam 고정 — 압축 해제할 때 없으면 만들어진다)
        public string ExtractFolder { get; } = @"C:\Exam";

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
            NetworkService.Instance.PacketReceived += OnPacketReceived;
        }

        // 교수 PC가 '시험 시작'을 누르면 오는 명령 — 받은 파일을 자동으로 풀고 해제 폴더를 띄운다.
        private void OnPacketReceived(PacketType type, IntPtr payload, uint payloadLen)
        {
            if (type != PacketType.ExtractArchive) return;

            Post(async () =>
            {
                if (CanExtract)
                    await ExtractAsync();   // 해제가 끝나면 ExtractAsync가 폴더를 연다
                else if (IsExtracted)
                    OpenExtractFolder();    // 이미 풀려 있으면 폴더만 다시 띄운다
                else if (!IsReceived)
                    StatusText = "시험이 시작됐지만 아직 시험 파일을 받지 못했습니다. 교수님께 재배포를 요청해 주세요.";
            });
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

        // 직전 해제가 만들어 놓은 파일 목록 (해제 폴더 기준 상대 경로).
        // 재배포 때 지워도 되는 파일을 이 목록으로 판별한다 — 학생이 저장한 답안은 여기에 없으므로 보존된다.
        // (ExtractedFiles 는 화면 표시용이라 새 전송이 시작되면 비워지므로 따로 보관한다)
        private List<string> _lastExtracted = new List<string>();

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
            // 이전 전송이 남긴 임시 .7z는 더 이상 쓰지 않으므로 지운다.
            // (재배포할 때마다 %TEMP%에 사본이 쌓이는 것을 막는다)
            DeleteArchive();

            _transferId = transferId;
            IsReceived = false;
            IsExtracted = false;
            ExtractedFiles.Clear();
        }

        // 수신된 임시 .7z를 지운다. 실패해도 기능에는 영향이 없으므로 무시한다.
        private void DeleteArchive()
        {
            if (string.IsNullOrEmpty(_archivePath)) return;

            try { File.Delete(_archivePath); } catch (Exception) { }
            _archivePath = string.Empty;
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

            var previous = _lastExtracted;
            var delivered = new List<string>();

            int code;
            try
            {
                // 7za.exe 실행이 끝날 때까지 블로킹되므로 백그라운드에서 수행
                code = await Task.Run(() =>
                {
                    Directory.CreateDirectory(outputFolder);

                    // 시험 폴더에 바로 풀지 않고 임시 폴더에 푼 뒤 항목 단위로 옮긴다.
                    // 바로 풀면 이번 배포에서 빠진 파일이 이전 배포분으로 남는다.
                    string staging = Path.Combine(Path.GetTempPath(), "ExamExtract_" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        Directory.CreateDirectory(staging);

                        int rc = FileControlService.FC_ExtractDecrypt(sevenZa, archive, staging, password);
                        if (rc != 0) return rc;

                        MergeIntoExamFolder(staging, outputFolder, previous, delivered);
                        return 0;
                    }
                    finally
                    {
                        if (Directory.Exists(staging))
                            Directory.Delete(staging, true);
                    }
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
                DeleteArchive(); // 해제까지 끝났으면 임시 .7z는 필요 없다
                _lastExtracted = delivered;

                ExtractedFiles.Clear();
                foreach (string rel in delivered)
                    ExtractedFiles.Add(rel);

                IsExtracted = true;
                StatusText = $"압축 해제 완료 · 파일 {ExtractedFiles.Count}개";

                OpenExtractFolder(); // 학생이 따로 찾지 않도록 해제된 폴더를 바로 띄운다
            }
            else
            {
                StatusText = $"압축 해제 실패 (코드 {code}) — 압축 해제 버튼을 다시 눌러 주세요.";
            }
        }

        // 압축 해제 폴더를 탐색기로 연다.
        // 해제 전에 눌러도 빈 폴더가 열리도록 없으면 만든다
        // (explorer.exe는 없는 경로를 받으면 엉뚱하게 문서 폴더를 연다).
        public void OpenExtractFolder()
        {
            try
            {
                Directory.CreateDirectory(ExtractFolder);

                // 포그라운드 전환 권한을 explorer에 넘긴 뒤 띄운다.
                // 이게 없으면 Windows가 포그라운드 앱(학생 UI)을 보호해서,
                // 탐색기 창이 뒤에 열리고 작업표시줄에서 깜빡이기만 한다.
                AllowSetForegroundWindow(ASFW_ANY);
                Process.Start("explorer.exe", ExtractFolder);
            }
            catch (Exception ex)
            {
                StatusText = $"폴더 열기 실패: {ex.Message} — {ExtractFolder} 를 직접 열어 주세요.";
            }
        }

        // 다른 프로세스가 자기 창을 포그라운드로 올릴 수 있게 허용한다.
        // ASFW_ANY = 모든 프로세스에 허용 (다음 포그라운드 전환까지만 유효).
        private const int ASFW_ANY = -1;

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        // 새로 받은 패키지를 시험 폴더에 최상위 항목 단위로 반영한다.
        //   같은 이름이 있으면 → 직전 배포가 만든 파일만 지우고 새 것으로 덮는다 (학생 답안은 보존)
        //   같은 이름이 없으면 → 기존 항목은 그대로 둔다 (A·B 배포 후 C만 따로 배포하는 경우)
        private static void MergeIntoExamFolder(
            string staging, string target, List<string> previous, List<string> delivered)
        {
            foreach (string entry in Directory.GetFileSystemEntries(staging))
            {
                string name = Path.GetFileName(entry);

                // 이 항목 아래에서 직전 배포가 만든 파일만 지운다.
                // 이번 패키지에서 빠진 파일은 여기서 정리되고, 학생이 저장한 파일은 목록에 없어 남는다.
                foreach (string rel in previous)
                {
                    if (!rel.Split(Path.DirectorySeparatorChar)[0].Equals(name, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string old = Path.Combine(target, rel);
                    if (File.Exists(old)) File.Delete(old);
                }

                string dest = Path.Combine(target, name);
                if (Directory.Exists(entry))
                    CopyDirectory(entry, dest);
                else
                    File.Copy(entry, dest, true);
            }

            // 이번 배포가 전달한 파일 목록 (staging 기준 상대 경로 = 시험 폴더 기준 상대 경로)
            foreach (string path in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
                delivered.Add(Path.GetRelativePath(staging, path));
        }

        // .NET에는 폴더 깊은 복사가 없어 직접 재귀 복사
        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
            foreach (string dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
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
