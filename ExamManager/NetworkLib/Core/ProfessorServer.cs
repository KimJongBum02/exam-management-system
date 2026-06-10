using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using NetworkLib.Events;
using NetworkLib.FileTransfer;
using NetworkLib.Protocol;
using NetworkLib.Protocol.Messages;

namespace NetworkLib.Core
{
    /// <summary>
    /// 교수 PC에서 실행되는 TCP 서버.
    /// 최대 40명의 학생 클라이언트 연결을 수락하고 관리합니다.
    ///
    /// 사용 예시:
    /// <code>
    /// var server = new ProfessorServer(port: 9000);
    /// server.StudentConnected    += (s, e) => Console.WriteLine($"{e.StudentName} 접속");
    /// server.StudentDisconnected += (s, e) => Console.WriteLine($"{e.StudentName} 종료");
    /// server.PacketReceived      += (s, e) => HandlePacket(e.SessionId, e.Packet);
    /// await server.StartAsync(cancellationToken);
    /// </code>
    /// </summary>
    public class ProfessorServer
    {
        private TcpListener? _listener;
        private CancellationTokenSource _serverCts = new();

        // 세션 ID → ClientSession 매핑 (스레드 안전)
        private readonly ConcurrentDictionary<string, ClientSession> _sessions = new();

        private Timer? _heartbeatTimer;
        private readonly int _port;

        /// <summary>Heartbeat 타임아웃. 이 시간 동안 응답 없으면 결석 처리 (기본 15초)</summary>
        public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>서버가 실행 중인지 여부</summary>
        public bool IsRunning { get; private set; }

        /// <summary>현재 연결된 모든 세션 (읽기 전용)</summary>
        public IReadOnlyDictionary<string, ClientSession> Sessions => _sessions;

        /// <summary>공유 파일 수신 관리자 (모든 세션이 공유)</summary>
        public FileTransferManager FileTransfer { get; } = new();

        // ─── 이벤트 ────────────────────────────────────────────────

        /// <summary>학생이 로그인하여 세션이 확립되었을 때 발생합니다.</summary>
        public event EventHandler<StudentConnectedEventArgs>? StudentConnected;

        /// <summary>학생 연결이 끊겼을 때 발생합니다.</summary>
        public event EventHandler<StudentDisconnectedEventArgs>? StudentDisconnected;

        /// <summary>
        /// 학생으로부터 패킷을 수신했을 때 발생합니다.
        /// Heartbeat 및 FileTransfer 패킷은 내부 처리되어 이 이벤트로 전달되지 않습니다.
        /// </summary>
        public event EventHandler<PacketReceivedEventArgs>? PacketReceived;

        // ─── 생성자 ────────────────────────────────────────────────

        /// <param name="port">서버가 열 TCP 포트 (기본값: 9000)</param>
        public ProfessorServer(int port = 9000)
        {
            _port = port;
        }

        // ─── 서버 시작/종료 ────────────────────────────────────────

        /// <summary>
        /// TCP 서버를 시작하고 학생 연결을 수락하기 시작합니다.
        /// cancellationToken이 취소될 때까지 실행됩니다.
        /// </summary>
        public async Task StartAsync(CancellationToken ct = default)
        {
            if (IsRunning) throw new InvalidOperationException("서버가 이미 실행 중입니다.");

            _serverCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            IsRunning = true;

            // Heartbeat 체크 타이머 시작 (5초마다)
            _heartbeatTimer = new Timer(
                CheckHeartbeats,
                null,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5));

            try
            {
                while (!_serverCts.Token.IsCancellationRequested)
                {
                    var tcpClient = await _listener.AcceptTcpClientAsync(_serverCts.Token)
                        .ConfigureAwait(false);

                    // 연결된 클라이언트를 백그라운드로 처리 (await 없이)
                    _ = Task.Run(() => HandleNewClientAsync(tcpClient, _serverCts.Token));
                }
            }
            catch (OperationCanceledException) { /* 정상 종료 */ }
            finally
            {
                Stop();
            }
        }

        /// <summary>서버를 중지하고 모든 세션을 종료합니다.</summary>
        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;

            _serverCts.Cancel();
            _heartbeatTimer?.Dispose();
            _listener?.Stop();

            foreach (var session in _sessions.Values)
                session.Disconnect(DisconnectReason.ServerShutdown);

            _sessions.Clear();
        }

        // ─── 개별 클라이언트 처리 ──────────────────────────────────

        private async Task HandleNewClientAsync(TcpClient tcpClient, CancellationToken ct)
        {
            // 아직 로그인 전인 미인증 세션 생성
            var session = new ClientSession(tcpClient, FileTransfer);

            session.PacketReceived += (_, packet) => OnSessionPacketReceived(session, packet);
            session.Disconnected   += (_, reason)  => OnSessionDisconnected(session, reason);

            // 임시 세션 등록 (로그인 전)
            _sessions[session.SessionId] = session;

            // 수신 루프 실행 (로그인 패킷을 기다림)
            await session.StartReceiveLoopAsync().ConfigureAwait(false);
        }

        // ─── 패킷 라우팅 ───────────────────────────────────────────

        private void OnSessionPacketReceived(ClientSession session, NetworkPacket packet)
        {
            switch (packet.Type)
            {
                case MessageType.StudentLogin:
                    _ = HandleStudentLoginAsync(session, packet);
                    break;

                default:
                    // 로그인 안 된 세션의 패킷은 무시
                    if (string.IsNullOrEmpty(session.StudentId)) break;

                    PacketReceived?.Invoke(this, new PacketReceivedEventArgs
                    {
                        SessionId   = session.SessionId,
                        StudentId   = session.StudentId,
                        StudentName = session.StudentName,
                        Packet      = packet,
                    });
                    break;
            }
        }

        private async Task HandleStudentLoginAsync(ClientSession session, NetworkPacket packet)
        {
            var loginMsg = packet.GetPayload<StudentLoginMessage>();
            if (loginMsg == null) return;

            // 세션에 학생 정보 설정
            session.StudentId   = loginMsg.StudentId;
            session.StudentName = loginMsg.StudentName;
            session.Status      = StudentStatus.Connected;

            // 로그인 승인 응답 전송
            var response = NetworkPacket.Create(MessageType.LoginResponse, new LoginResponseMessage
            {
                Success = true,
                Message = $"접속이 승인되었습니다. 안녕하세요, {loginMsg.StudentName}님.",
            });

            try
            {
                await session.SendPacketAsync(response).ConfigureAwait(false);
            }
            catch { return; }

            // UI에 알림
            StudentConnected?.Invoke(this, new StudentConnectedEventArgs
            {
                SessionId      = session.SessionId,
                StudentId      = session.StudentId,
                StudentName    = session.StudentName,
                RemoteEndPoint = session.RemoteEndPoint,
                ConnectedAt    = session.ConnectedAt,
            });
        }

        private void OnSessionDisconnected(ClientSession session, DisconnectReason reason)
        {
            _sessions.TryRemove(session.SessionId, out _);

            StudentDisconnected?.Invoke(this, new StudentDisconnectedEventArgs
            {
                SessionId   = session.SessionId,
                StudentId   = session.StudentId,
                StudentName = session.StudentName,
                Reason      = reason,
            });
        }

        // ─── Heartbeat 감시 ────────────────────────────────────────

        private void CheckHeartbeats(object? state)
        {
            foreach (var session in _sessions.Values)
            {
                if (!session.IsAlive) continue;

                if (session.IsHeartbeatExpired(HeartbeatTimeout))
                {
                    session.Status = StudentStatus.Absent;
                    session.Disconnect(DisconnectReason.HeartbeatTimeout);
                }
            }
        }

        // ─── 브로드캐스트 / 개별 전송 ─────────────────────────────

        /// <summary>
        /// 현재 접속 중인 모든 학생에게 패킷을 브로드캐스트합니다.
        /// 로그인 완료된 세션에만 전송합니다.
        /// </summary>
        public async Task BroadcastAsync(NetworkPacket packet, CancellationToken ct = default)
        {
            var tasks = _sessions.Values
                .Where(s => s.IsAlive && !string.IsNullOrEmpty(s.StudentId))
                .Select(s => SendSafeAsync(s, packet, ct));

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>특정 세션(학생)에게 패킷을 전송합니다.</summary>
        public async Task SendToSessionAsync(
            string sessionId,
            NetworkPacket packet,
            CancellationToken ct = default)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
                await SendSafeAsync(session, packet, ct).ConfigureAwait(false);
        }

        /// <summary>특정 학번의 학생에게 패킷을 전송합니다.</summary>
        public async Task SendToStudentAsync(
            string studentId,
            NetworkPacket packet,
            CancellationToken ct = default)
        {
            var session = _sessions.Values.FirstOrDefault(s => s.StudentId == studentId);
            if (session != null)
                await SendSafeAsync(session, packet, ct).ConfigureAwait(false);
        }

        // ─── 파일 전송 ─────────────────────────────────────────────

        /// <summary>
        /// 모든 학생에게 파일을 전송합니다.
        /// 각 학생에게 동시 전송하며, progress는 (세션ID, 진행률%) 형태로 보고됩니다.
        /// </summary>
        /// <param name="filePath">전송할 파일의 로컬 경로</param>
        /// <param name="archivePassword">압축 파일 암호 (FileControl 메타데이터 전달용, null 가능)</param>
        /// <param name="progress">진행률 콜백</param>
        /// <param name="ct">취소 토큰</param>
        public async Task SendFileToAllAsync(
            string filePath,
            string? archivePassword = null,
            IProgress<(string SessionId, int Percent)>? progress = null,
            CancellationToken ct = default)
        {
            // 파일을 한 번만 읽어 모든 세션에 공유 (메모리 최적화)
            var (fileBytes, sha256Hash) = await FileTransferManager.PrepareFileAsync(filePath, ct)
                .ConfigureAwait(false);

            var aliveSessions = _sessions.Values
                .Where(s => s.IsAlive && !string.IsNullOrEmpty(s.StudentId))
                .ToList();

            var tasks = aliveSessions.Select(session =>
                FileTransferManager.SendFileBytesAsync(
                    packet => SendSafeAsync(session, packet, ct),
                    fileBytes,
                    sha256Hash,
                    Path.GetFileName(filePath),
                    Guid.NewGuid().ToString(),
                    archivePassword,
                    new Progress<int>(p => progress?.Report((session.SessionId, p))),
                    ct));

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>특정 학생에게만 파일을 전송합니다.</summary>
        public async Task SendFileToSessionAsync(
            string sessionId,
            string filePath,
            string? archivePassword = null,
            IProgress<int>? progress = null,
            CancellationToken ct = default)
        {
            if (!_sessions.TryGetValue(sessionId, out var session)) return;

            await FileTransferManager.SendFileAsync(
                packet => SendSafeAsync(session, packet, ct),
                filePath,
                Path.GetFileName(filePath),
                Guid.NewGuid().ToString(),
                archivePassword,
                progress,
                ct).ConfigureAwait(false);
        }

        // ─── 내부 헬퍼 ────────────────────────────────────────────

        /// <summary>
        /// 전송 실패 시 예외를 삼키는 안전한 전송 메서드.
        /// 브로드캐스트 시 일부 클라이언트 실패가 전체에 영향을 주지 않도록 합니다.
        /// </summary>
        private static async Task SendSafeAsync(
            ClientSession session,
            NetworkPacket packet,
            CancellationToken ct)
        {
            try
            {
                await session.SendPacketAsync(packet, ct).ConfigureAwait(false);
            }
            catch
            {
                // 개별 전송 실패는 무시 (연결 해제 이벤트로 처리됨)
            }
        }
    }
}
