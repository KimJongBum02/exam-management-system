using NetworkLib;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StudentUI.Service
{
    // 답안 제출 진행 상태. 화면에 무엇을 보여줄지가 이 값으로 정해진다.
    public enum AnswerSubmitState
    {
        Idle,        // 아직 제출한 적 없음
        Compressing, // 답안 폴더를 압축·암호화하는 중
        Sending,     // 교수 PC로 전송하는 중
        WaitingAck,  // 전송은 끝났고 교수의 확인 회신을 기다리는 중
        Succeeded,   // 교수가 잘 받았다고 회신함 — 이때만 파일을 지워도 된다
        Failed,      // 어느 단계든 실패 — 답안 파일은 그대로 남아 있다
    }

    // 학생 답안을 교수 PC로 제출한다.
    //
    // 제출을 시작하는 경로는 두 가지다.
    //   ① 교수가 '답안 일괄 수집'을 누름   → ExamSubmitRequest(34) 수신
    //   ② 학생이 스스로 '시험 종료'를 누름 → SubmitAsync를 직접 호출 (화면 연결은 후속 작업)
    //
    // 어느 쪽이든 절차는 같다: 압축 → 전송 → 교수의 확인 회신 대기.
    // 확인을 받기 전에는 아무것도 지우지 않는다. 답안이 사라지면 되돌릴 방법이 없기 때문이다.
    public class AnswerSubmitService
    {
        public static AnswerSubmitService Instance { get; } = new AnswerSubmitService();

        // 전송이 이만큼 진행되지 않고 멈춰 있으면 실패로 본다.
        // 총 시간으로 재지 않는 이유: 답안이 100MB쯤 되고 30명이 동시에 내면
        // 정상인데도 몇 분씩 걸린다. 잘 가는 전송을 실패로 판정하는 쪽이 더 나쁘다.
        private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

        // 전송이 100%에 닿은 뒤 교수의 확인 회신을 기다리는 시간.
        // 교수 PC는 SHA-256 검증만 하면 되므로 이 정도면 넉넉하다.
        private static readonly TimeSpan AckTimeout = TimeSpan.FromSeconds(30);

        // 7za 종료 코드 1 = "일부 파일을 읽지 못해 빼고 묶었음". 실패로 다룬다.
        private const int SevenZipSomeFilesSkipped = 1;

        // 상태가 바뀔 때마다 알린다. 화면은 이걸 구독해 진행 상황을 보여주면 된다.
        public event Action<AnswerSubmitState, string>? StateChanged;

        public AnswerSubmitState State { get; private set; } = AnswerSubmitState.Idle;

        private readonly object _gate = new object();
        private bool _inProgress;

        // 전송 진행률 감시용. 콜백이 네이티브 스레드에서 올라오므로 volatile로 둔다.
        private volatile string _sendingFileName = "";
        private volatile int _sendPercent;
        private volatile bool _sendFailed;
        private long _lastProgressTicks;

        // 교수의 확인 회신을 기다리는 곳.
        private TaskCompletionSource<bool>? _ackWaiter;

        private AnswerSubmitService() { }

        // 앱 시작 시 한 번 호출 — 구독만 해둔다.
        public void Start()
        {
            NetworkService.Instance.PacketReceived += OnPacketReceived;
            NetworkService.Instance.FileProgress += OnFileProgress;
            NetworkService.Instance.FileError += OnFileError;
        }

        private void OnPacketReceived(PacketType type, IntPtr payload, uint payloadLen)
        {
            if (type == PacketType.ExamSubmitRequest) HandleSubmitRequest(payload, payloadLen);
            else if (type == PacketType.CommandAck) HandleCommandAck(payload, payloadLen);
        }

        // ① 교수가 '답안 일괄 수집'을 누름
        private void HandleSubmitRequest(IntPtr payload, uint payloadLen)
        {
            if (!ExamSubmitPayload.TryDecode(payload, payloadLen, out _, out string password, out _))
                return;

            // 수신 스레드를 붙잡으면 안 되므로 압축·전송은 따로 돌린다.
            _ = SubmitAsync(password);
        }

        // 교수의 확인 회신. 기다리는 쪽을 깨운다.
        private void HandleCommandAck(IntPtr payload, uint payloadLen)
        {
            if (!CommandAckPayload.TryDecode(payload, payloadLen,
                                             out PacketType commandType, out bool success, out _))
                return;

            // 다른 명령에 대한 회신이면 무시한다.
            if (commandType != PacketType.ExamSubmitRequest) return;

            _ackWaiter?.TrySetResult(success);
        }

        // 전송 진행률. 네이티브가 보내기와 받기에 같은 콜백을 쓰므로
        // 우리가 보내는 파일 이름일 때만 센다.
        private void OnFileProgress(string transferId, string fileName, int percent)
        {
            if (!string.Equals(fileName, _sendingFileName, StringComparison.OrdinalIgnoreCase)) return;

            _sendPercent = percent;
            Interlocked.Exchange(ref _lastProgressTicks, DateTime.UtcNow.Ticks);
        }

        private void OnFileError(string transferId, string message)
        {
            if (_sendingFileName.Length == 0) return;
            _sendFailed = true;
        }

        // 학생이 스스로 '시험 종료'를 누를 때 부르는 경로.
        // 교수 요청 없이 먼저 내는 것이므로 암호는 이번 시험을 받을 때 함께 온 것을 쓴다.
        public Task<bool> SubmitAsync() => SubmitAsync(ExamFileStore.Instance.ExamPassword);

        // ── 제출 본체 ──
        public async Task<bool> SubmitAsync(string archivePassword)
        {
            // 두 경로(교수 요청 / 학생 버튼)가 겹쳐 들어와도 한 번만 돌게 막는다.
            lock (_gate)
            {
                if (_inProgress) return false;
                _inProgress = true;
            }

            string? archivePath = null;
            try
            {
                if (!NetworkService.Instance.IsConnected)
                    return Fail("교수 PC와 연결이 끊어져 있습니다.");

                // 1. 답안 폴더를 통째로 압축·암호화한다.
                SetState(AnswerSubmitState.Compressing, "답안을 압축하는 중입니다...");
                (archivePath, int code) = await Task.Run(() => CompressAnswers(archivePassword));
                if (archivePath == null)
                {
                    // 7za는 못 읽은 파일이 있으면 코드 1을 주고 그 파일만 빼고 묶는다.
                    // 학생이 편집기를 켜 둔 채 제출하는 흔한 상황이라, 무엇을 해야 하는지 알려 준다.
                    return Fail(code == SevenZipSomeFilesSkipped
                        ? "답안 파일을 열어 둔 프로그램이 있어 일부가 빠질 뻔했습니다. 편집기를 모두 닫고 다시 제출해 주세요."
                        : "답안을 압축하지 못했습니다.");
                }

                // 2. 교수 PC로 보낸다.
                //
                // 회신을 받을 그릇은 보내기 전에 미리 만든다.
                // 전송이 끝난 뒤에 만들면, 그 사이에 도착한 회신을 받을 곳이 없어 놓친다.
                // 교수 PC는 마지막 조각을 받자마자 몇 ms 만에 회신하므로 실제로 자주 일어난다.
                _ackWaiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                SetState(AnswerSubmitState.Sending, "답안을 전송하는 중입니다...");
                if (!await SendAndWaitAsync(archivePath, archivePassword))
                    return Fail("답안을 전송하지 못했습니다.");

                // 3. 교수가 잘 받았는지 확인한다. 이 확인이 있어야 파일을 지울 수 있다.
                SetState(AnswerSubmitState.WaitingAck, "교수님 PC의 확인을 기다리는 중입니다...");
                if (!await WaitForAckAsync())
                    return Fail("교수님 PC에서 답안 수신 확인이 오지 않았습니다.");

                // 4. 여기서부터가 되돌릴 수 없는 구간이다.
                //    교수가 확실히 받았다고 회신한 뒤에만 시험 파일을 지운다.
                SetState(AnswerSubmitState.Succeeded, "답안이 정상적으로 제출되었습니다.");
                CleanupExamFiles();
                return true;
            }
            catch (Exception ex)
            {
                return Fail($"답안 제출 중 오류가 발생했습니다: {ex.Message}");
            }
            finally
            {
                // 보낼 때 쓴 임시 묶음은 성공이든 실패든 지운다. 100MB가 임시 폴더에 남을 이유가 없다.
                // 원본 답안 폴더는 실패했다면 그대로 둔다 — 교수가 못 받았을 수 있기 때문이다.
                if (archivePath != null) TryDeleteFile(archivePath);

                _sendingFileName = "";
                _ackWaiter = null;
                lock (_gate) { _inProgress = false; }
            }
        }

        // 시험 파일을 지운다. 답안이 교수 PC에 안전히 도착한 뒤에만 불린다.
        //
        // 폴더를 통째로 지운다. 다음 시험에서 압축을 풀 때 ExamFileStore가 다시 만들어 준다.
        // 삭제에 실패해도 답안은 이미 교수에게 가 있어 유실은 아니다. 다만 그 자리에 앉는
        // 다음 학생이 앞사람 답안을 보게 되므로, 교수에게 알려 직접 확인하도록 한다.
        //
        // 탐색기가 폴더를 열어 두었거나 학생이 편집기를 켜 둔 채면 삭제가 실패한다.
        // 창을 강제로 닫지는 않는다 — 학생이 열어 둔 다른 창까지 잘못 닫을 수 있다.
        private void CleanupExamFiles()
        {
            string examFolder = ExamFileStore.Instance.ExtractFolder;
            try
            {
                if (Directory.Exists(examFolder))
                    Directory.Delete(examFolder, true);

                // 성공은 따로 알리지 않는다. 교수 PC는 답안을 받은 시점에 이미 '제출완료'로 표시했다.
            }
            catch (Exception ex)
            {
                ReportStatus(StudentStatus.CleanupFailed, $"시험 파일을 지우지 못했습니다: {ex.Message}");
                SetState(AnswerSubmitState.Succeeded,
                         "답안은 제출되었으나 시험 파일이 남아 있습니다. 교수님께 알려 주세요.");
            }
        }

        // 학생 상태를 교수 PC로 보낸다.
        private static void ReportStatus(StudentStatus status, string detail)
        {
            NetworkService.Instance.SendPacket(
                PacketType.ExamStatusUpdate,
                ExamStatusUpdatePayload.Encode(status, detail));
        }

        // 시험 파일을 받아 푼 폴더를 그대로 묶는다.
        // 배포받은 원본 .7z는 압축 해제가 끝날 때 이미 지워지므로(ExamFileStore) 같이 들어가지 않는다.
        // 묶음 파일은 임시 폴더에 만든다 — 답안 폴더 안에 만들면 자기 자신을 압축하게 된다.
        //
        // 종료 코드가 0이 아니면 만들어진 묶음을 버린다. 특히 코드 1이 위험한데,
        // 7za가 못 읽은 파일만 빼고 나머지로 묶음을 만들어 주기 때문이다.
        // 테스트로 확인함: 답안 파일이 편집기에 열려 있으면 코드 1 + 답안이 빠진 묶음이 나온다.
        // 이걸 성공으로 보면 답안 없는 파일을 제출하고 원본까지 지우게 된다.
        private static (string? ArchivePath, int Code) CompressAnswers(string password)
        {
            string sourceFolder = ExamFileStore.Instance.ExtractFolder;
            if (!Directory.Exists(sourceFolder)) return (null, -1);

            string sevenZa = Path.Combine(AppContext.BaseDirectory, "7za.exe");
            string archivePath = Path.Combine(Path.GetTempPath(), $"answer_{Guid.NewGuid():N}.7z");

            int code = FileControlService.FC_CompressEncrypt(sevenZa, sourceFolder, archivePath, password);
            if (code != 0 || !File.Exists(archivePath))
            {
                TryDeleteFile(archivePath);
                return (null, code);
            }
            return (archivePath, code);
        }

        // 전송을 시작하고 100%가 될 때까지 지켜본다.
        // 진행률이 StallTimeout 동안 한 칸도 안 오르면 멈춘 것으로 본다.
        private async Task<bool> SendAndWaitAsync(string archivePath, string archivePassword)
        {
            _sendingFileName = Path.GetFileName(archivePath);
            _sendPercent = 0;
            _sendFailed = false;
            Interlocked.Exchange(ref _lastProgressTicks, DateTime.UtcNow.Ticks);

            // 암호를 함께 실어 보낸다. 교수 PC가 받은 묶음을 열 때 쓴다.
            NetworkService.Instance.SendFile(archivePath, archivePassword);

            while (true)
            {
                await Task.Delay(500);

                if (_sendFailed) return false;
                if (_sendPercent >= 100) return true;

                var lastProgress = new DateTime(Interlocked.Read(ref _lastProgressTicks), DateTimeKind.Utc);
                if (DateTime.UtcNow - lastProgress > StallTimeout) return false;
            }
        }

        // 확인 회신을 기다린다. 답이 없는 것도 실패로 친다 —
        // 회신을 못 받았는데 성공으로 넘기면 답안을 지워버리게 된다.
        private async Task<bool> WaitForAckAsync()
        {
            // 그릇은 보내기 전에 이미 만들어 두었다(SubmitAsync 참고).
            // 전송 중에 회신이 먼저 도착했다면 이 Task는 이미 완료돼 있어 바로 지나간다.
            TaskCompletionSource<bool>? waiter = _ackWaiter;
            if (waiter == null) return false;

            Task finished = await Task.WhenAny(waiter.Task, Task.Delay(AckTimeout));
            return finished == waiter.Task && waiter.Task.Result;
        }

        private bool Fail(string message)
        {
            SetState(AnswerSubmitState.Failed, message);
            return false;
        }

        private void SetState(AnswerSubmitState state, string message)
        {
            State = state;
            StateChanged?.Invoke(state, message);
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
        }
    }
}
