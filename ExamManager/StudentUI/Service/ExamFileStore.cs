using NetworkLib;
using ExamManager.Shared;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace StudentUI.Service
{
    // 교수 PC가 배포한 시험 파일의 수신·압축 해제 상태를 앱 전체에서 공유한다.
    // 파일은 학생이 대기 화면에 머무는 동안 도착하므로, 시험 준비 화면이
    // 그 뒤에 열려도 상태를 볼 수 있도록 여기에 보관한다.
    public class ExamFileStore : UiBinding
    {
        public static ExamFileStore Instance { get; } = new ExamFileStore();

        // 시험 파일을 두는 곳. 학생이 바로 찾을 수 있도록 바탕화면에 둔다.
        // (없으면 압축을 풀 때 만들어진다)
        public string ExtractFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "시험 파일");

        // 압축을 푼 결과가 들어갈 폴더. 시험 파일 폴더 바로 밑에 묶음 이름으로 만든다.
        // 여기에 바로 풀면 폴더를 열자마자 파일이 흩어져 나와 보기 불편하다.
        public string ExtractedRoot =>
            _archiveName.Length == 0 ? ExtractFolder : Path.Combine(ExtractFolder, _archiveName);

        private string _archiveName = string.Empty;

        // 이번 실행에서 시험 파일을 받았는지.
        // 받은 파일 이름은 메모리에만 있어서, 프로그램을 다시 켜면 비어 있고
        // 그동안 ExtractedRoot 는 '시험 파일' 폴더 전체를 가리킨다.
        public bool HasExamFolder => _archiveName.Length > 0;

        // 지금 답안이 들어 있는 폴더. 보통은 ExtractedRoot 그대로다.
        //
        // 학생이 작업 폴더 이름을 바꾸면(교수님의 '답안 이름 형식' 안내를 폴더 이름으로 오해하는 일이 있다)
        // ExtractedRoot 가 없어져 제출이 통째로 실패한다. 그때 '시험 파일' 안에 폴더가 하나뿐이면
        // 그것이 이번 시험 폴더이므로 이름이 달라도 그대로 묶어 보낸다.
        // 폴더가 없거나 둘 이상이면 고르지 않는다 — 엉뚱한 폴더를 보내고 지우는 쪽이 더 나쁘다.
        // 찾지 못하면 빈 문자열. 부르는 쪽이 무엇을 되돌려야 하는지 안내한다.
        public string ResolveAnswerFolder()
        {
            if (Directory.Exists(ExtractedRoot)) return ExtractedRoot;
            if (!HasExamFolder || !Directory.Exists(ExtractFolder)) return string.Empty;

            string[] folders = Directory.GetDirectories(ExtractFolder);
            return folders.Length == 1 ? folders[0] : string.Empty;
        }

        private string _archivePath = string.Empty; // 수신된 .7z 경로 (보통 시험 파일 폴더, 옮기지 못했으면 %TEMP%)
        private string _password = string.Empty;    // 교수 PC가 함께 보낸 암호

        // 이번 시험의 암호. 답안을 제출할 때 같은 암호로 묶어야 교수가 열 수 있다.
        public string ExamPassword => _password;

        // 이어 받기 기록에 남길 값 (ExamSessionStore 참고)
        public string ArchiveName => _archiveName;
        public IReadOnlyList<string> DeliveredFiles => _lastExtracted;

        // 다시 켠 프로그램이 이어 받기 기록으로 진행 중이던 시험 폴더를 되살린다.
        // 파일은 이미 풀려 있으므로 받은 것·푼 것으로 표시만 한다. 앱이 뜰 때 화면 스레드에서 부른다.
        public void Resume(string archiveName, string password, IEnumerable<string> delivered)
        {
            _archiveName = archiveName;
            _password = password;
            // 받은 .7z도 시험 파일 폴더에 남아 있으므로, 제출 뒤 정리할 때 함께 지워지도록 경로를 되살린다.
            _archivePath = Path.Combine(ExtractFolder, archiveName + ".7z");
            _lastExtracted = new List<string>(delivered);

            ExtractedFiles.Clear();
            foreach (string rel in _lastExtracted)
                ExtractedFiles.Add(rel);

            FileName = archiveName + ".7z";
            Progress = 100;
            IsReceived = true;
            IsExtracted = true;
            StatusText = $"이어서 진행 중 · 시험 폴더를 다시 찾았습니다 (파일 {ExtractedFiles.Count}개)";
        }
        private string _transferId = string.Empty;  // 현재 전송 식별자 (재배포 판별용)

        // 교수의 시험 시작 신호가 왔는데 그때 아직 파일을 받지 못한 상태.
        // 파일이 도착하면 이 표시를 보고 스스로 압축을 푼다(OnFileReceived).
        private bool _startPending;

        private ExamFileStore() { }

        // 앱 시작 시 한 번 호출 — 파일 수신 이벤트를 구독하고, 받은 파일이 그 자리에 있는지 지켜본다.
        public void Start()
        {
            NetworkService.Instance.FileProgress += OnFileProgress;
            NetworkService.Instance.FileReceived += OnFileReceived;
            NetworkService.Instance.FileError += OnFileError;
            NetworkService.Instance.PacketReceived += OnPacketReceived;

            // 타이머는 부른 스레드에 묶이므로 반드시 여기(앱 시작, 화면 스레드)에서 만든다.
            _fileWatch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _fileWatch.Tick += (_, _) => CheckExamFilesStillThere();
            _fileWatch.Start();
        }

        // ── 받은 시험 파일이 아직 있는지 지켜보기 ──
        //
        // 학생이 시험 파일을 지우면(실수든 고의든) 교수 화면에는 여전히 '수신완료'로 남아,
        // 재배포 대상에서 빠져 파일을 다시 받을 길이 없어진다. 그래서 상태가 바뀌는 순간 교수에게 알린다.
        // 교수 표가 '파일 삭제됨'으로 바뀌면 그 학생은 다시 재배포 대상이 된다.
        private System.Windows.Threading.DispatcherTimer? _fileWatch;

        // 받았던 시험 파일이 지금 이 PC 에 없는 상태. 학생 화면의 '시험 파일' 줄과 교수 보고가 같이 쓴다.
        private bool _isMissing;
        public bool IsMissing
        {
            get => _isMissing;
            private set { _isMissing = value; OnPropertyChanged(); }
        }

        // 마지막 전송이 실패로 끝났는지. 새 전송이 시작되거나 파일을 받으면 풀린다.
        private bool _transferFailed;
        public bool TransferFailed
        {
            get => _transferFailed;
            private set { _transferFailed = value; OnPropertyChanged(); }
        }

        private void CheckExamFilesStillThere()
        {
            // 받은 적이 없으면 볼 것이 없다. 답안을 낸 뒤에는 우리가 일부러 지운 것이라 보고하지 않는다.
            if (!IsReceived || AnswerSubmitService.Instance.State == AnswerSubmitState.Succeeded) return;

            ReportMissing(IsExtracted ? !WorkFolderHasDeliveredFiles() : !File.Exists(_archivePath));
        }

        // 파일이 있고 없음이 바뀐 순간에만 교수에게 알린다. 같은 보고를 5초마다 반복하지 않는다.
        // '없음'을 받은 교수 화면은 그 학생을 '파일 삭제됨 · 미수신'으로 바꿔 재배포 대상에 다시 올린다.
        private void ReportMissing(bool missing)
        {
            if (missing == IsMissing) return;

            IsMissing = missing;
            NetworkService.Instance.SendPacket(
                PacketType.ExamStatusUpdate,
                ExamStatusUpdatePayload.Encode(missing ? StudentStatus.FileMissing : StudentStatus.FileReceived));
        }

        // 작업 폴더에 배포받은 파일이 하나라도 남아 있는지.
        // 하나도 없을 때만 '삭제됨'으로 본다 — 일부만 지운 학생에게 다시 보내면
        // 남아 있는 파일(학생이 고쳐 둔 것)까지 원본으로 덮어쓰기 때문이다.
        private bool WorkFolderHasDeliveredFiles()
        {
            string folder = ResolveAnswerFolder();
            if (folder.Length == 0) return false;
            if (_lastExtracted.Count == 0) return true;   // 무엇을 받았는지 모르면 폴더가 있는 것으로 친다

            foreach (string rel in _lastExtracted)
                if (File.Exists(Path.Combine(folder, rel))) return true;

            return false;
        }

        // 시험 시작 명령의 처리(압축 해제·폴더 열기)가 모두 끝나면 알린다.
        // 프로세스 감시는 이 신호를 받고 켜야 한다 — 먼저 켜면 압축 해제에 쓰는
        // 7za.exe와 탐색기가 '시험 중 새로 실행된 프로그램'으로 적발된다.
        public event Action? ExamStartHandled;

        // 교수 PC가 '시험 시작'을 누르면 오는 명령 — 받은 파일을 자동으로 풀고 해제 폴더를 띄운다.
        // 또한 교수가 시험을 초기화(Waiting)하면 파일 상태를 리셋한다.
        private void OnPacketReceived(PacketType type, IntPtr payload, uint payloadLen)
        {
            if (type == PacketType.ExamPhaseChange)
            {
                if (ExamPhasePayload.TryDecode(payload, payloadLen, out ExamPhase phase) && phase == ExamPhase.Waiting)
                {
                    Post(Reset);
                }
                return;
            }

            if (type != PacketType.ExtractArchive) return;

            Post(async () =>
            {
                if (CanExtract)
                    await ExtractAsync();   // 해제가 끝나면 ExtractAsync가 폴더를 연다
                else if (IsExtracted)
                    OpenExtractFolder();    // 이미 풀려 있으면 폴더만 다시 띄운다
                else if (!IsReceived)
                {
                    // 아직 받는 중이거나 전송이 늦은 학생이다. 교수의 시작 신호는 한 번뿐이므로,
                    // 파일이 도착하면 스스로 풀도록 표시해 둔다(OnFileReceived).
                    _startPending = true;
                    StatusText = "시험이 시작됐습니다. 시험 파일을 받는 대로 자동으로 열립니다.";
                }

                // 해제 성공 여부와 관계없이 시험은 시작된 것이므로 감시는 켠다
                ExamStartHandled?.Invoke();
            });
        }

        public void Reset()
        {
            _archiveName = string.Empty;
            _archivePath = string.Empty;
            _password = string.Empty;
            _transferId = string.Empty;
            _startPending = false;
            _lastExtracted.Clear();
            ExtractedFiles.Clear();
            FileName = "-";
            Progress = 0;
            IsReceived = false;
            IsExtracted = false;
            IsExtracting = false;
            IsMissing = false;
            TransferFailed = false;
            StatusText = "파일 수신 대기 중";
            OnPropertyChanged(nameof(HasExamFolder));
            OnPropertyChanged(nameof(ExtractedRoot));
            OnPropertyChanged(nameof(ExamPassword));
            OnPropertyChanged(nameof(ArchiveName));
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

            _password = archivePassword;

            // 압축을 풀 폴더 이름으로 쓴다. 확장자를 뗀 묶음 이름이 곧 폴더 이름이 된다.
            _archiveName = Path.GetFileNameWithoutExtension(fileName ?? "");

            // 받은 .7z는 시험 파일 폴더에 둔다. 압축을 푼 폴더와 나란히 남는다.
            // 네이티브는 받는 동안 %TEMP%에 쓰므로 다 받은 뒤에 옮긴다 — 받다 만 파일이 폴더에 보이지 않는다.
            // 옮기지 못하면(교수 앱을 같은 PC에서 돌려 교수가 보내는 중인 원본과 경로가 같은 경우 등)
            // %TEMP%에 둔 채로 압축을 푼다.
            _archivePath = tempPath;
            try
            {
                string dest = Path.Combine(ExtractFolder, Path.GetFileName(fileName ?? ""));
                Directory.CreateDirectory(ExtractFolder);
                File.Move(tempPath, dest, true);
                _archivePath = dest;
            }
            catch (Exception) { }

            FileName = fileName;
            Progress = 100;
            IsReceived = true;
            StatusText = $"수신 완료 · {FormatSize(fileSize)} · AES-256 암호화됨";

            // 시험 시작 신호가 먼저 왔고 그때는 파일이 없어 풀지 못한 경우다(전송이 아직 끝나지 않았던 학생).
            // 교수는 시작 신호를 한 번만 보내므로, 여기서 스스로 풀지 않으면 이 학생은 파일을 받고도
            // 문제를 열지 못한 채 타이머만 돌아간다.
            if (_startPending)
            {
                _startPending = false;
                _ = StartLateAsync();
            }
        });

        // 늦게 도착한 파일을 풀고, 시험 시작으로 켜야 할 것(감시·인터넷 차단)을 켠다.
        private async Task StartLateAsync()
        {
            await ExtractAsync();
            ExamStartHandled?.Invoke();
        }

        // 새 전송이 시작되면 이전 수신·해제 결과를 버린다.
        // (이렇게 하지 않으면 재배포된 파일을 받고도 IsExtracted가 true로 남아 다시 해제할 수 없다)
        private void BeginNewTransfer(string transferId)
        {
            // 이전 전송이 남긴 .7z는 더 이상 쓰지 않으므로 지운다.
            // (재배포할 때마다 이전 묶음이 쌓이는 것을 막는다)
            DeleteArchive();

            _transferId = transferId;
            IsReceived = false;
            IsExtracted = false;
            ExtractedFiles.Clear();

            // 다시 받기 시작했으므로 지난 전송의 실패·삭제 표시를 지운다.
            TransferFailed = false;
            IsMissing = false;

            // 새 전송은 교수가 다시 보낸 것이라, 받고 나면 교수 쪽이 시작 신호를 따로 보내 준다
            // (SendFileService.StartLateStudent). 스스로도 풀면 두 번 풀게 되므로 표시를 내린다.
            _startPending = false;
        }

        // 수신된 .7z를 지운다. 실패해도 기능에는 영향이 없으므로 무시한다.
        public void DeleteArchive()
        {
            if (string.IsNullOrEmpty(_archivePath)) return;

            try { File.Delete(_archivePath); } catch (Exception) { }
            _archivePath = string.Empty;
        }

        // 폴더를 통째로 지운다. 읽기 전용 항목이 하나라도 있으면 Directory.Delete 가 거기서 멈춰
        // 폴더가 남는다(.NET 10 에서 확인). 7za 는 배포 파일의 읽기 전용 속성을 그대로 풀어 주므로 먼저 벗긴다.
        // 제출 뒤 시험 폴더, 압축을 푸는 임시 폴더, 지난 실행이 남긴 임시 폴더를 모두 이걸로 지운다.
        // 두 앱이 함께 쓰는 삭제다 (ExamManager\Shared\FileSystem.cs).
        public static void DeleteFolder(string folder) => ExamManager.Shared.FileSystem.DeleteFolder(folder);

        private void OnFileError(string transferId, string message) => Post(() =>
        {
            StatusText = $"파일 수신 실패: {message}";
            TransferFailed = true;   // 학생 화면의 '시험 파일' 줄에 '전송 실패'로 보인다
        });

        // 답안 제출이 끝나면 시험 파일이 지워지므로 상태 문구도 그에 맞게 바꾼다.
        // 이걸 하지 않으면 제출을 마친 뒤에도 화면에 "수신 완료 100%"가 그대로 남아
        // 학생이 제출이 안 된 줄 안다.
        // 제출은 배경 작업에서 끝나므로, 화면 데이터는 반드시 화면 스레드로 넘겨 고친다.
        // (ExtractedFiles 같은 목록은 다른 스레드에서 건드리면 예외가 난다)
        public void MarkAnswerSubmitted(bool cleanupSucceeded) => Post(() =>
        {
            Progress = 100;
            StatusText = cleanupSucceeded
                ? "답안 전송 완료 · 시험 파일이 정리되었습니다"
                : "답안 전송 완료 · 시험 파일이 남아 있습니다. 교수님께 알려 주세요";

            ExtractedFiles.Clear();
        });

        // ── 압축 해제 ──
        public async Task ExtractAsync()
        {
            if (!CanExtract) return;

            // 받아 둔 묶음이 아직 있는지 먼저 본다.
            // 학생이 .7z 를 지운 뒤 교수가 [시험 시작]을 누르면, 예전에는 그대로 풀려 들어가
            // 7za 는 실패하는데 만들어 둔 **빈 시험 폴더만 남았다**. 그 빈 폴더가 작업 폴더로 잡혀
            // 답안 제출 때 빈 채로 나갈 수 있어, 풀기 전에 끊는다.
            if (!File.Exists(_archivePath))
            {
                StatusText = "받은 시험 파일이 없습니다 — 교수님께 재배포를 요청해 주세요.";
                ReportMissing(true);   // 교수 화면이 '파일 삭제됨'이 되어야 다시 보낼 수 있다
                return;
            }

            IsExtracting = true;
            StatusText = "압축 해제 중...";

            string archive = _archivePath;
            string password = _password;
            string outputFolder = ExtractedRoot;

            var previous = _lastExtracted;
            var delivered = new List<string>();

            int code;
            try
            {
                // 7za.exe 실행이 끝날 때까지 블로킹되므로 백그라운드에서 수행
                code = await Task.Run(() =>
                {
                    // 시험 폴더는 7za 가 성공한 뒤에 만든다(MergeIntoExamFolder 가 만든다).
                    // 먼저 만들어 두면 해제가 실패했을 때 빈 폴더만 남는다.

                    // 시험 폴더에 바로 풀지 않고 임시 폴더에 푼 뒤 항목 단위로 옮긴다.
                    // 바로 풀면 이번 배포에서 빠진 파일이 이전 배포분으로 남는다.
                    string staging = Path.Combine(Path.GetTempPath(), "ExamExtract_" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        Directory.CreateDirectory(staging);

                        int rc = FileSystem.ExtractDecrypt(archive, staging, password);
                        if (rc != 0) return rc;

                        MergeIntoExamFolder(staging, outputFolder, previous, delivered);
                        return 0;
                    }
                    finally
                    {
                        // 암호가 풀린 시험 파일이 들어 있어 반드시 지운다. 읽기 전용 파일이 섞여 있으면
                        // Directory.Delete 만으로는 실패해, 옮기기까지 끝났는데도 '압축 해제 실패'로 끝났다.
                        if (Directory.Exists(staging))
                            DeleteFolder(staging);
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
                // 받은 .7z는 지우지 않는다 — 시험 파일 폴더에 압축 파일과 푼 폴더가 함께 있어야 한다.
                // 답안 제출 뒤 정리(AnswerSubmitService)나 재배포(BeginNewTransfer) 때 지워진다.
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
            // 압축을 풀었으면 그 폴더를, 아직이면 시험 파일 폴더를 연다.
            //
            // 학생이 작업 폴더 이름을 바꿨으면 바뀐 폴더를 연다. 예전처럼 ExtractedRoot 를 그대로 쓰면
            // 아래 CreateDirectory 가 없어진 이름으로 빈 폴더를 새로 만들고, 제출할 때 답안이 든 폴더 대신
            // 그 빈 폴더가 묶여 나간다. 찾지 못하면 '시험 파일' 폴더를 열어 무엇이 있는지 보게 한다.
            string target = IsExtracted ? ResolveAnswerFolder() : ExtractFolder;
            if (target.Length == 0) target = ExtractFolder;
            try
            {
                Directory.CreateDirectory(target);

                // 포그라운드 전환 권한을 explorer에 넘긴 뒤 띄운다.
                // 이게 없으면 Windows가 포그라운드 앱(학생 UI)을 보호해서,
                // 탐색기 창이 뒤에 열리고 작업표시줄에서 깜빡이기만 한다.
                AllowSetForegroundWindow(ASFW_ANY);
                Process.Start("explorer.exe", target);
            }
            catch (Exception ex)
            {
                StatusText = $"폴더 열기 실패: {ex.Message} — {target} 를 직접 열어 주세요.";
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
            // 여기까지 왔다는 것은 7za 가 성공했다는 뜻이다. 그때 시험 폴더를 만든다.
            Directory.CreateDirectory(target);

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

        // 네이티브 스레드에서 온 알림을 UI 스레드로 넘긴다 (두 앱이 함께 쓰는 Shared\UiThread.cs)
        private static void Post(Action action) => UiThread.Post(action);

        private static string FormatSize(long bytes)
            => bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:0.#} MB" : $"{bytes / 1024.0:0.#} KB";
    }
}
