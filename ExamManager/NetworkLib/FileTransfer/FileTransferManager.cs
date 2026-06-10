using System.Collections.Concurrent;
using System.Security.Cryptography;
using NetworkLib.Events;
using NetworkLib.Protocol;
using NetworkLib.Protocol.Messages;

namespace NetworkLib.FileTransfer
{
    /// <summary>
    /// 파일을 청크 단위로 나누어 TCP 소켓을 통해 송수신합니다.
    ///
    /// 역할 범위:
    ///   - 파일 바이트 스트림의 청크 분할/전송 (교수→학생, 학생→교수 양방향)
    ///   - SHA-256 해시를 이용한 무결성 검증
    ///   - 수신 완료된 파일을 임시 경로에 저장 후 이벤트로 알림
    ///
    /// 역할 외:
    ///   - 실제 파일 압축/해제 → FileControl 프로젝트 담당
    ///   - 파일 이동/삭제 → 호출 측(UI 또는 FileControl) 담당
    ///
    /// 송신 흐름:
    ///   FileTransferStart → FileChunk × N → FileTransferComplete
    ///
    /// 수신 흐름:
    ///   HandleIncomingPacket() 호출 → 임시 파일에 순차 저장 → FileReceived 이벤트
    /// </summary>
    public class FileTransferManager
    {
        // 청크 크기: 64KB (JSON Base64 인코딩 후 약 88KB)
        private const int ChunkSize = 64 * 1024;

        // 진행 중인 파일 수신 컨텍스트 (TransferId → context)
        private readonly ConcurrentDictionary<string, FileReceiveContext> _activeReceives = new();

        // ─── 이벤트 ────────────────────────────────────────────────

        /// <summary>파일 수신이 완료되고 무결성 검증을 통과했을 때 발생합니다.</summary>
        public event EventHandler<FileReceivedEventArgs>? FileReceived;

        /// <summary>파일 수신 진행률이 업데이트될 때 발생합니다.</summary>
        public event EventHandler<FileTransferProgressEventArgs>? ReceiveProgress;

        /// <summary>파일 전송/수신 중 오류가 발생했을 때 발생합니다.</summary>
        public event EventHandler<FileTransferErrorEventArgs>? TransferError;

        // ─── 수신 측 처리 (클라이언트/서버 공통) ──────────────────

        /// <summary>
        /// ProfessorServer / StudentClient의 수신 루프에서 라우팅된
        /// 파일 전송 패킷을 처리합니다.
        /// </summary>
        /// <param name="senderId">보낸 쪽 식별자 (세션ID 또는 "server")</param>
        /// <param name="packet">FileTransferStart / FileChunk / FileTransferComplete 패킷</param>
        public void HandleIncomingPacket(string senderId, NetworkPacket packet)
        {
            switch (packet.Type)
            {
                case MessageType.FileTransferStart:
                    var startMsg = packet.GetPayload<FileTransferStartMessage>();
                    if (startMsg != null) OnFileTransferStart(senderId, startMsg);
                    break;

                case MessageType.FileChunk:
                    var chunkMsg = packet.GetPayload<FileChunkMessage>();
                    if (chunkMsg != null) OnFileChunk(chunkMsg);
                    break;

                case MessageType.FileTransferComplete:
                    var completeMsg = packet.GetPayload<FileTransferCompleteMessage>();
                    if (completeMsg != null) OnFileTransferComplete(senderId, completeMsg);
                    break;
            }
        }

        // ─── 수신 이벤트 핸들러 ────────────────────────────────────

        private void OnFileTransferStart(string senderId, FileTransferStartMessage msg)
        {
            // 임시 파일 경로 생성
            var tempPath = Path.Combine(Path.GetTempPath(), $"NetworkLib_{msg.TransferId}_{msg.FileName}");

            var context = new FileReceiveContext
            {
                TransferId      = msg.TransferId,
                SenderId        = senderId,
                FileName        = msg.FileName,
                TotalSize       = msg.TotalSize,
                TotalChunks     = msg.TotalChunks,
                Sha256Hash      = msg.Sha256Hash,
                ArchivePassword = msg.ArchivePassword,
                TempFilePath    = tempPath,
                FileStream      = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None),
                ReceivedChunks  = 0,
            };

            _activeReceives[msg.TransferId] = context;
        }

        private void OnFileChunk(FileChunkMessage msg)
        {
            if (!_activeReceives.TryGetValue(msg.TransferId, out var context)) return;

            try
            {
                // TCP는 순서를 보장하므로 순차적으로 씁니다
                context.FileStream?.Write(msg.Data, 0, msg.Data.Length);
                context.ReceivedChunks++;

                int percent = (int)((double)context.ReceivedChunks / context.TotalChunks * 100);
                ReceiveProgress?.Invoke(this, new FileTransferProgressEventArgs
                {
                    TransferId     = msg.TransferId,
                    FileName       = context.FileName,
                    Percent        = Math.Min(percent, 99), // 완료 이벤트 전까지 99%로 제한
                    ReceivedChunks = context.ReceivedChunks,
                    TotalChunks    = context.TotalChunks,
                });
            }
            catch (Exception ex)
            {
                AbortTransfer(msg.TransferId, $"청크 저장 실패: {ex.Message}");
            }
        }

        private void OnFileTransferComplete(string senderId, FileTransferCompleteMessage msg)
        {
            if (!_activeReceives.TryRemove(msg.TransferId, out var context)) return;

            // FileStream 닫기
            context.FileStream?.Flush();
            context.FileStream?.Dispose();
            context.FileStream = null;

            // SHA-256 무결성 검증
            try
            {
                using var sha256 = SHA256.Create();
                using var fs = File.OpenRead(context.TempFilePath);
                var hash = Convert.ToHexString(sha256.ComputeHash(fs));

                if (!string.Equals(hash, context.Sha256Hash, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(context.TempFilePath);
                    TransferError?.Invoke(this, new FileTransferErrorEventArgs
                    {
                        TransferId = msg.TransferId,
                        Message    = "SHA-256 해시 불일치 — 파일이 손상되었습니다.",
                    });
                    return;
                }
            }
            catch (Exception ex)
            {
                TransferError?.Invoke(this, new FileTransferErrorEventArgs
                {
                    TransferId = msg.TransferId,
                    Message    = $"해시 검증 실패: {ex.Message}",
                });
                return;
            }

            // 성공 이벤트
            ReceiveProgress?.Invoke(this, new FileTransferProgressEventArgs
            {
                TransferId     = msg.TransferId,
                FileName       = context.FileName,
                Percent        = 100,
                ReceivedChunks = context.TotalChunks,
                TotalChunks    = context.TotalChunks,
            });

            FileReceived?.Invoke(this, new FileReceivedEventArgs
            {
                TransferId      = msg.TransferId,
                SenderId        = context.SenderId,
                FileName        = context.FileName,
                TempFilePath    = context.TempFilePath,
                FileSize        = new FileInfo(context.TempFilePath).Length,
                ArchivePassword = context.ArchivePassword,
            });
        }

        private void AbortTransfer(string transferId, string reason)
        {
            if (!_activeReceives.TryRemove(transferId, out var context)) return;

            context.FileStream?.Dispose();
            if (!string.IsNullOrEmpty(context.TempFilePath) && File.Exists(context.TempFilePath))
                File.Delete(context.TempFilePath);

            TransferError?.Invoke(this, new FileTransferErrorEventArgs
            {
                TransferId = transferId,
                Message    = reason,
            });
        }

        // ─── 송신 측 (static 메서드) ───────────────────────────────

        /// <summary>
        /// 파일을 읽어 SHA-256 해시를 계산하고 바이트 배열로 반환합니다.
        /// 여러 클라이언트에 동일 파일을 브로드캐스트할 때 한 번만 호출합니다.
        /// </summary>
        public static async Task<(byte[] Bytes, string Sha256Hash)> PrepareFileAsync(
            string filePath,
            CancellationToken ct = default)
        {
            var bytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);

            using var sha256 = SHA256.Create();
            var hash = Convert.ToHexString(sha256.ComputeHash(bytes));

            return (bytes, hash);
        }

        /// <summary>
        /// 파일을 청크로 나누어 sendAction을 통해 전송합니다.
        /// </summary>
        /// <param name="sendAction">패킷 전송 함수 (ClientSession.SendPacketAsync 등)</param>
        /// <param name="filePath">전송할 파일의 로컬 경로</param>
        /// <param name="fileName">수신 측에 전달할 파일 이름</param>
        /// <param name="transferId">이 전송 세션의 고유 ID</param>
        /// <param name="archivePassword">압축 파일 암호 (메타데이터 전달용, null 가능)</param>
        /// <param name="progress">진행률 콜백 (0~100)</param>
        /// <param name="ct">취소 토큰</param>
        public static async Task SendFileAsync(
            Func<NetworkPacket, Task> sendAction,
            string filePath,
            string fileName,
            string transferId,
            string? archivePassword = null,
            IProgress<int>? progress = null,
            CancellationToken ct = default)
        {
            var (bytes, hash) = await PrepareFileAsync(filePath, ct).ConfigureAwait(false);
            await SendFileBytesAsync(sendAction, bytes, hash, fileName, transferId,
                archivePassword, progress, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 미리 준비된 바이트 배열을 청크로 나누어 전송합니다.
        /// 브로드캐스트 시 PrepareFileAsync를 한 번만 호출하고 이 메서드를 재사용합니다.
        /// </summary>
        public static async Task SendFileBytesAsync(
            Func<NetworkPacket, Task> sendAction,
            byte[] fileBytes,
            string sha256Hash,
            string fileName,
            string transferId,
            string? archivePassword = null,
            IProgress<int>? progress = null,
            CancellationToken ct = default)
        {
            int totalChunks = (int)Math.Ceiling((double)fileBytes.Length / ChunkSize);
            if (totalChunks == 0) totalChunks = 1; // 빈 파일 처리

            // 1단계: FileTransferStart 전송
            var startMsg = new FileTransferStartMessage
            {
                TransferId      = transferId,
                FileName        = fileName,
                TotalSize       = fileBytes.Length,
                TotalChunks     = totalChunks,
                Sha256Hash      = sha256Hash,
                ArchivePassword = archivePassword,
            };
            await sendAction(NetworkPacket.Create(MessageType.FileTransferStart, startMsg))
                .ConfigureAwait(false);

            // 2단계: FileChunk 순차 전송
            for (int i = 0; i < totalChunks; i++)
            {
                ct.ThrowIfCancellationRequested();

                int offset = i * ChunkSize;
                int size   = Math.Min(ChunkSize, fileBytes.Length - offset);
                var chunk  = new byte[size];
                Buffer.BlockCopy(fileBytes, offset, chunk, 0, size);

                var chunkMsg = new FileChunkMessage
                {
                    TransferId = transferId,
                    ChunkIndex = i,
                    Data       = chunk,
                };
                await sendAction(NetworkPacket.Create(MessageType.FileChunk, chunkMsg))
                    .ConfigureAwait(false);

                progress?.Report((int)((i + 1.0) / totalChunks * 100));
            }

            // 3단계: FileTransferComplete 전송
            var completeMsg = new FileTransferCompleteMessage
            {
                TransferId = transferId,
                FileName   = fileName,
            };
            await sendAction(NetworkPacket.Create(MessageType.FileTransferComplete, completeMsg))
                .ConfigureAwait(false);
        }

        // ─── 내부 컨텍스트 ─────────────────────────────────────────

        private sealed class FileReceiveContext
        {
            public string TransferId      { get; init; } = string.Empty;
            public string SenderId        { get; init; } = string.Empty;
            public string FileName        { get; init; } = string.Empty;
            public long   TotalSize       { get; init; }
            public int    TotalChunks     { get; init; }
            public string Sha256Hash      { get; init; } = string.Empty;
            public string? ArchivePassword { get; init; }
            public string TempFilePath    { get; init; } = string.Empty;

            public FileStream? FileStream { get; set; }
            public int ReceivedChunks    { get; set; }
        }
    }
}
