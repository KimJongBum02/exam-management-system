using System.Net.Sockets;
using NetworkLib.Events;
using NetworkLib.FileTransfer;
using NetworkLib.Protocol;
using NetworkLib.Protocol.Messages;

namespace NetworkLib.Core
{
    /// <summary>
    /// 교수 서버 측에서 개별 학생과의 TCP 연결을 관리하는 세션 클래스.
    /// 수신 루프, 패킷 전송, Heartbeat 추적을 담당합니다.
    /// </summary>
    public class ClientSession
    {
        private readonly TcpClient _tcpClient;
        private readonly NetworkStream _stream;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly CancellationTokenSource _cts = new();
        private bool _isDisposed;

        // ─── 식별 정보 ─────────────────────────────────────────────

        /// <summary>이 세션의 고유 ID (Guid 문자열)</summary>
        public string SessionId { get; } = Guid.NewGuid().ToString();

        /// <summary>학번 (로그인 후 설정됨)</summary>
        public string StudentId { get; internal set; } = string.Empty;

        /// <summary>학생 이름 (로그인 후 설정됨)</summary>
        public string StudentName { get; internal set; } = string.Empty;

        /// <summary>학생 PC의 IP:Port 문자열</summary>
        public string RemoteEndPoint { get; }

        // ─── 상태 정보 ─────────────────────────────────────────────

        /// <summary>현재 학생 상태</summary>
        public StudentStatus Status { get; internal set; } = StudentStatus.Connected;

        /// <summary>연결된 시각 (UTC)</summary>
        public DateTime ConnectedAt { get; } = DateTime.UtcNow;

        /// <summary>마지막으로 Heartbeat를 수신한 시각 (UTC)</summary>
        public DateTime LastHeartbeatAt { get; private set; } = DateTime.UtcNow;

        /// <summary>세션이 살아있는지 여부</summary>
        public bool IsAlive => !_cts.IsCancellationRequested && !_isDisposed;

        // ─── 이벤트 ────────────────────────────────────────────────

        /// <summary>
        /// 이 세션에서 패킷이 수신되었을 때 발생합니다.
        /// </summary>
        public event EventHandler<NetworkPacket>? PacketReceived;

        /// <summary>이 세션이 끊겼을 때 발생합니다.</summary>
        public event EventHandler<DisconnectReason>? Disconnected;

        // ─── 파일 전송 ─────────────────────────────────────────────

        /// <summary>이 세션의 파일 수신 관리자</summary>
        public FileTransferManager FileTransfer { get; }

        // ─── 생성자 ────────────────────────────────────────────────

        internal ClientSession(TcpClient client, FileTransferManager fileTransferManager)
        {
            _tcpClient = client;
            _stream = client.GetStream();
            RemoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
            FileTransfer = fileTransferManager;
        }

        // ─── 수신 루프 ─────────────────────────────────────────────

        /// <summary>
        /// 패킷 수신 루프를 시작합니다.
        /// 연결이 끊기거나 취소될 때까지 계속 실행됩니다.
        /// </summary>
        internal async Task StartReceiveLoopAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var packet = await PacketSerializer.ReceivePacketAsync(_stream, _cts.Token)
                        .ConfigureAwait(false);

                    if (packet == null) break;

                    // Heartbeat는 내부에서 처리 (이벤트로 노출 안 함)
                    if (packet.Type == MessageType.Heartbeat)
                    {
                        LastHeartbeatAt = DateTime.UtcNow;
                        continue;
                    }

                    // 파일 전송 패킷은 FileTransferManager에 라우팅
                    if (IsFileTransferPacket(packet.Type))
                    {
                        FileTransfer.HandleIncomingPacket(SessionId, packet);
                        continue;
                    }

                    // 나머지 패킷은 상위 레이어(ProfessorServer)로 전달
                    PacketReceived?.Invoke(this, packet);
                }
            }
            catch (OperationCanceledException) { /* 정상 종료 */ }
            catch (EndOfStreamException) { Disconnect(DisconnectReason.ClientDisconnected); return; }
            catch (IOException) { Disconnect(DisconnectReason.NetworkError); return; }
            catch (Exception) { Disconnect(DisconnectReason.NetworkError); return; }

            Disconnect(DisconnectReason.ClientDisconnected);
        }

        // ─── 송신 ──────────────────────────────────────────────────

        /// <summary>
        /// 이 세션(학생)에게 패킷을 전송합니다.
        /// 스레드 안전하게 동작합니다.
        /// </summary>
        public async Task SendPacketAsync(NetworkPacket packet, CancellationToken ct = default)
        {
            if (!IsAlive)
                throw new InvalidOperationException($"세션 {SessionId}은(는) 이미 종료되었습니다.");

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
            await PacketSerializer.SendPacketAsync(_stream, packet, _sendLock, linked.Token)
                .ConfigureAwait(false);
        }

        // ─── Heartbeat 체크 ────────────────────────────────────────

        /// <summary>
        /// Heartbeat 타임아웃 여부를 확인합니다.
        /// </summary>
        /// <param name="timeout">허용 최대 무응답 시간</param>
        public bool IsHeartbeatExpired(TimeSpan timeout)
            => DateTime.UtcNow - LastHeartbeatAt > timeout;

        // ─── 연결 해제 ─────────────────────────────────────────────

        /// <summary>이 세션의 연결을 끊습니다.</summary>
        public void Disconnect(DisconnectReason reason = DisconnectReason.ServerShutdown)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try { _cts.Cancel(); } catch { }
            try { _tcpClient.Close(); } catch { }

            Disconnected?.Invoke(this, reason);
        }

        // ─── 내부 헬퍼 ────────────────────────────────────────────

        private static bool IsFileTransferPacket(MessageType type) =>
            type is MessageType.FileTransferStart
                 or MessageType.FileChunk
                 or MessageType.FileTransferComplete;
    }
}
