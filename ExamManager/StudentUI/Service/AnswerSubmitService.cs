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

        // ── 제출 본체 ──
        // 학생이 '시험 종료'를 누를 때도 이 메서드를 부르면 된다.
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
                archivePath = await Task.Run(() => CompressAnswers(archivePassword));
                if (archivePath == null)
                    return Fail("답안을 압축하지 못했습니다.");

                // 2. 교수 PC로 보낸다.
                SetState(AnswerSubmitState.Sending, "답안을 전송하는 중입니다...");
                if (!await SendAndWaitAsync(archivePath, archivePassword))
                    return Fail("답안을 전송하지 못했습니다.");

                // 3. 교수가 잘 받았는지 확인한다. 이 확인이 있어야 파일을 지울 수 있다.
                SetState(AnswerSubmitState.WaitingAck, "교수님 PC의 확인을 기다리는 중입니다...");
                if (!await WaitForAckAsync())
                    return Fail("교수님 PC에서 답안 수신 확인이 오지 않았습니다.");

                SetState(AnswerSubmitState.Succeeded, "답안이 정상적으로 제출되었습니다.");
                return true;
            }
            catch (Exception ex)
            {
                return Fail($"답안 제출 중 오류가 발생했습니다: {ex.Message}");
            }
            finally
            {
                // 보낼 때 쓴 임시 묶음은 성공이든 실패든 지운다. 100MB가 임시 폴더에 남을 이유가 없다.
                // 원본 답안 폴더는 어떤 경우에도 건드리지 않는다 — 그건 교수 확인을 받은 뒤 후속 작업에서 한다.
                if (archivePath != null) TryDeleteFile(archivePath);

                _sendingFileName = "";
                _ackWaiter = null;
                lock (_gate) { _inProgress = false; }
            }
        }

        // 시험 파일을 받아 푼 폴더를 그대로 묶는다.
        // 배포받은 원본 .7z는 압축 해제가 끝날 때 이미 지워지므로(ExamFileStore) 같이 들어가지 않는다.
        // 묶음 파일은 임시 폴더에 만든다 — 답안 폴더 안에 만들면 자기 자신을 압축하게 된다.
        private static string? CompressAnswers(string password)
        {
            string sourceFolder = ExamFileStore.Instance.ExtractFolder;
            if (!Directory.Exists(sourceFolder)) return null;

            string sevenZa = Path.Combine(AppContext.BaseDirectory, "7za.exe");
            string archivePath = Path.Combine(Path.GetTempPath(), $"answer_{Guid.NewGuid():N}.7z");

            int code = FileControlService.FC_CompressEncrypt(sevenZa, sourceFolder, archivePath, password);
            if (code != 0 || !File.Exists(archivePath))
            {
                TryDeleteFile(archivePath);
                return null;
            }
            return archivePath;
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
            var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ackWaiter = waiter;

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
