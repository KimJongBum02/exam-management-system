using System.Net.Sockets;
using NetworkLib.Events;
using NetworkLib.FileTransfer;
using NetworkLib.Protocol;
using NetworkLib.Protocol.Messages;

namespace NetworkLib.Core
{
    /// <summary>
    /// 학생 PC에서 실행되는 TCP 클라이언트.
    /// 교수 서버에 연결하고, 메시지를 송수신하며, 5초마다 Heartbeat를 전송합니다.
    ///
    /// 사용 예시:
    /// <code>
    /// var client = new StudentClient();
    /// client.Connected       += (s, e) => Console.WriteLine("서버에 연결됨");
    /// client.Disconnected    += (s, e) => Console.WriteLine("연결 끊김");
    /// client.PacketReceived  += (s, e) => HandlePacket(e.Packet);
    ///
    /// bool ok = await client.ConnectAsync("192.168.1.100", port: 9000);
    /// if (ok) await client.LoginAsync("20220001", "홍길동");
    /// </code>
    /// </summary>
    public class StudentClient
    {
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private CancellationTokenSource _cts = new();
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private Timer? _heartbeatTimer;

        // ─── 상태 ──────────────────────────────────────────────────

        /// <summary>현재 교수 서버에 연결된 상태인지 여부</summary>
        public bool IsConnected => _tcpClient?.Connected ?? false;

        /// <summary>연결된 서버 IP (미연결 시 null)</summary>
        public string? ServerIp { get; private set; }

        /// <summary>연결된 서버 포트</summary>
        public int ServerPort { get; private set; }

        /// <summary>파일 수신 관리자</summary>
        public FileTransferManager FileTransfer { get; } = new();

        // ─── 이벤트 ────────────────────────────────────────────────

        /// <summary>교수 서버에 성공적으로 연결되었을 때 발생합니다.</summary>
        public event EventHandler<ClientConnectedEventArgs>? Connected;

        /// <summary>교수 서버와 연결이 끊겼을 때 발생합니다.</summary>
        public event EventHandler<ClientDisconnectedEventArgs>? Disconnected;

        /// <summary>
        /// 교수 서버로부터 패킷을 수신했을 때 발생합니다.
        /// Heartbeat 및 FileTransfer 패킷은 내부에서 처리됩니다.
        /// </summary>
        public event EventHandler<NetworkPacket>? PacketReceived;

        /// <summary>네트워크 오류가 발생했을 때 발생합니다.</summary>
        public event EventHandler<NetworkErrorEventArgs>? Error;

        // ─── 연결 ──────────────────────────────────────────────────

        /// <summary>
        /// 교수 서버에 TCP 연결을 시도합니다.
        /// </summary>
        /// <param name="professorIp">교수 PC IP 주소</param>
        /// <param name="port">서버 포트 (기본값: 9000)</param>
        /// <param name="ct">취소 토큰</param>
        /// <returns>연결 성공 시 true, 실패 시 false</returns>
        public async Task<bool> ConnectAsync(
            string professorIp,
            int port = 9000,
            CancellationToken ct = default)
        {
            try
            {
                _cts = new CancellationTokenSource();
                _tcpClient = new TcpClient();
                _tcpClient.NoDelay = true; // Nagle 알고리즘 비활성화 (응답성 향상)

                await _tcpClient.ConnectAsync(professorIp, port, ct).ConfigureAwait(false);
                _stream = _tcpClient.GetStream();

                ServerIp   = professorIp;
                ServerPort = port;

                // 5초마다 Heartbeat 전송 시작
                _heartbeatTimer = new Timer(
                    SendHeartbeatCallback,
                    null,
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(5));

                // 수신 루프 백그라운드 시작
                _ = Task.Run(StartReceiveLoopAsync);

                Connected?.Invoke(this, new ClientConnectedEventArgs
                {
                    ServerIp = professorIp,
                    Port     = port,
                });

                return true;
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, new NetworkErrorEventArgs
                {
                    Message   = $"서버 연결 실패: {ex.Message}",
                    Exception = ex,
                });
                CleanupConnection();
                return false;
            }
        }

        // ─── 송신 ──────────────────────────────────────────────────

        /// <summary>교수 서버에 패킷을 전송합니다.</summary>
        public async Task SendPacketAsync(NetworkPacket packet, CancellationToken ct = default)
        {
            if (_stream == null || !IsConnected)
                throw new InvalidOperationException("서버에 연결되어 있지 않습니다.");

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
            await PacketSerializer.SendPacketAsync(_stream, packet, _sendLock, linked.Token)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 학번과 이름으로 교수 서버에 로그인합니다.
        /// ConnectAsync 성공 후 반드시 호출해야 합니다.
        /// </summary>
        public async Task LoginAsync(string studentId, string studentName, CancellationToken ct = default)
        {
            var loginPacket = NetworkPacket.Create(MessageType.StudentLogin, new StudentLoginMessage
            {
                StudentId   = studentId,
                StudentName = studentName,
            });
            await SendPacketAsync(loginPacket, ct).ConfigureAwait(false);
        }

        /// <summary>출결 체크에 응답합니다.</summary>
        public async Task RespondAttendanceAsync(
            string checkId,
            string studentId,
            string studentName,
            CancellationToken ct = default)
        {
            var packet = NetworkPacket.Create(MessageType.AttendanceCheckResponse,
                new AttendanceCheckResponseMessage
                {
                    CheckId     = checkId,
                    StudentId   = studentId,
                    StudentName = studentName,
                });
            await SendPacketAsync(packet, ct).ConfigureAwait(false);
        }

        /// <summary>부정행위 감지 신호를 교수 서버에 전송합니다.</summary>
        public async Task SendCheatingAlertAsync(
            CheatingAlertMessage alertMessage,
            CancellationToken ct = default)
        {
            var packet = NetworkPacket.Create(MessageType.CheatingAlert, alertMessage);
            await SendPacketAsync(packet, ct).ConfigureAwait(false);
        }

        /// <summary>시험 상태를 교수 서버에 보고합니다.</summary>
        public async Task UpdateExamStatusAsync(
            string studentId,
            StudentStatus status,
            string? detail = null,
            CancellationToken ct = default)
        {
            var packet = NetworkPacket.Create(MessageType.ExamStatusUpdate, new ExamStatusUpdateMessage
            {
                StudentId = studentId,
                Status    = status,
                Detail    = detail,
            });
            await SendPacketAsync(packet, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 시험 파일을 교수 서버에 전송합니다.
        /// FileControl에서 압축한 파일 경로를 전달받아 전송합니다.
        /// </summary>
        public async Task SendExamFileAsync(
            string filePath,
            string? archivePassword = null,
            IProgress<int>? progress = null,
            CancellationToken ct = default)
        {
            await FileTransferManager.SendFileAsync(
                packet => SendPacketAsync(packet, ct),
                filePath,
                Path.GetFileName(filePath),
                Guid.NewGuid().ToString(),
                archivePassword,
                progress,
                ct).ConfigureAwait(false);
        }

        /// <summary>퀴즈에 답변합니다.</summary>
        public async Task SendQuizAnswerAsync(
            QuizAnswerMessage answer,
            CancellationToken ct = default)
        {
            var packet = NetworkPacket.Create(MessageType.QuizAnswer, answer);
            await SendPacketAsync(packet, ct).ConfigureAwait(false);
        }

        // ─── 수신 루프 ─────────────────────────────────────────────

        private async Task StartReceiveLoopAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested && _stream != null)
                {
                    var packet = await PacketSerializer.ReceivePacketAsync(_stream, _cts.Token)
                        .ConfigureAwait(false);

                    if (packet == null) break;

                    // 파일 전송 패킷은 FileTransferManager에 라우팅
                    if (IsFileTransferPacket(packet.Type))
                    {
                        FileTransfer.HandleIncomingPacket("server", packet);
                        continue;
                    }

                    PacketReceived?.Invoke(this, packet);
                }
            }
            catch (OperationCanceledException) { /* 정상 종료 */ }
            catch (EndOfStreamException) { HandleUnexpectedDisconnect(DisconnectReason.ClientDisconnected); return; }
            catch (IOException)          { HandleUnexpectedDisconnect(DisconnectReason.NetworkError); return; }
            catch (Exception)            { HandleUnexpectedDisconnect(DisconnectReason.NetworkError); return; }

            HandleUnexpectedDisconnect(DisconnectReason.ClientDisconnected);
        }

        // ─── Heartbeat ─────────────────────────────────────────────

        private async void SendHeartbeatCallback(object? state)
        {
            if (!IsConnected) return;
            try
            {
                await SendPacketAsync(NetworkPacket.Create(
                    MessageType.Heartbeat,
                    new HeartbeatMessage())).ConfigureAwait(false);
            }
            catch
            {
                // Heartbeat 전송 실패 시 수신 루프에서 끊김 처리됨
            }
        }

        // ─── 연결 해제 ─────────────────────────────────────────────

        /// <summary>교수 서버와의 연결을 정상적으로 종료합니다.</summary>
        public async Task DisconnectAsync(CancellationToken ct = default)
        {
            try
            {
                if (IsConnected && _stream != null)
                {
                    var disconnectPacket = NetworkPacket.Create(
                        MessageType.Disconnect,
                        new DisconnectMessage { Reason = "학생 측 정상 종료" });
                    await SendPacketAsync(disconnectPacket, ct).ConfigureAwait(false);
                }
            }
            catch { }
            finally
            {
                _cts.Cancel();
                CleanupConnection();

                Disconnected?.Invoke(this, new ClientDisconnectedEventArgs
                {
                    Reason = DisconnectReason.ClientDisconnected,
                });
            }
        }

        private void HandleUnexpectedDisconnect(DisconnectReason reason)
        {
            CleanupConnection();
            Disconnected?.Invoke(this, new ClientDisconnectedEventArgs { Reason = reason });
        }

        private void CleanupConnection()
        {
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
            try { _tcpClient?.Close(); } catch { }
            _tcpClient = null;
            _stream = null;
        }

        // ─── 내부 헬퍼 ────────────────────────────────────────────

        private static bool IsFileTransferPacket(MessageType type) =>
            type is MessageType.FileTransferStart
                 or MessageType.FileChunk
                 or MessageType.FileTransferComplete;
    }
}
