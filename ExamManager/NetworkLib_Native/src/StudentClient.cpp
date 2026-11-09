#include "pch.h"
#include "StudentClient.h"
#include "PacketIO.h"

// ─── 생성자 ────────────────────────────────────────────────────────
StudentClient::StudentClient()
{
    fileReceiver_.onFileReceived = [this](const std::string& tid, const std::string& sid,
        const std::string& fn, const std::string& tp,
        int64_t sz, const std::string& pw)
        { if (onFileReceived) onFileReceived(tid.c_str(), sid.c_str(), fn.c_str(), tp.c_str(), sz, pw.c_str()); };

    fileReceiver_.onFileProgress = [this](const std::string& tid, const std::string& fn, int pct)
        { if (onFileProgress) onFileProgress(tid.c_str(), fn.c_str(), pct); };

    fileReceiver_.onFileError = [this](const std::string& tid, const std::string& msg)
        { if (onFileError) onFileError(tid.c_str(), msg.c_str()); };
}

// ─── 소멸자 ────────────────────────────────────────────────────────
StudentClient::~StudentClient() { Disconnect(); }

// ─── 서버 연결 ─────────────────────────────────────────────────────
bool StudentClient::Connect(const std::string& serverIp, int port)
{
    if (connected_) return false;

    sock_ = ::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (sock_ == INVALID_SOCKET)
    {
        if (onError) onError("소켓 생성 실패");
        return false;
    }

    sockaddr_in addr{};
    addr.sin_family = AF_INET;
    addr.sin_port = htons(static_cast<u_short>(port));
    inet_pton(AF_INET, serverIp.c_str(), &addr.sin_addr);

    // 연결 시도에 제한을 둔다(CONNECT_TIMEOUT_MS). 소켓을 잠깐 논블로킹으로 바꿔 connect 를
    // 걸어 두고, select 로 정해진 시간만 기다린다. 끝나면 다시 블로킹으로 돌려놓는다 —
    // 받기 루프와 보내기(SO_SNDTIMEO)는 블로킹 소켓을 전제로 돈다.
    u_long mode = 1;                     // 1 = 논블로킹
    ::ioctlsocket(sock_, FIONBIO, &mode);

    bool ok = ::connect(sock_, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) != SOCKET_ERROR;
    if (!ok && ::WSAGetLastError() == WSAEWOULDBLOCK)
    {
        // 연결이 끝나면(성공이든 실패든) 쓰기 가능으로 깨어난다.
        fd_set writeSet;
        FD_ZERO(&writeSet);
        FD_SET(sock_, &writeSet);
        timeval wait{ CONNECT_TIMEOUT_MS / 1000, (CONNECT_TIMEOUT_MS % 1000) * 1000 };

        // 깨어난 것만으로는 성공이 아니다. 거절당해도 깨어나므로 소켓 오류를 따로 확인한다.
        if (::select(0, nullptr, &writeSet, nullptr, &wait) > 0)
        {
            int err = 0;
            int len = static_cast<int>(sizeof(err));
            ok = ::getsockopt(sock_, SOL_SOCKET, SO_ERROR,
                              reinterpret_cast<char*>(&err), &len) != SOCKET_ERROR && err == 0;
        }
    }

    mode = 0;                            // 0 = 블로킹으로 되돌림
    ::ioctlsocket(sock_, FIONBIO, &mode);

    if (!ok)
    {
        ::closesocket(sock_);
        sock_ = INVALID_SOCKET;
        if (onError) onError(("서버 연결 실패: " + serverIp).c_str());
        return false;
    }

    // TCP_NODELAY 설정
    int nodelay = 1;
    ::setsockopt(sock_, IPPROTO_TCP, TCP_NODELAY,
        reinterpret_cast<const char*>(&nodelay), sizeof(nodelay));

    // SO_SNDTIMEO — 보내기가 영원히 막히지 않게 한다.
    // 교수 PC 가 멈추거나 랜선이 빠지면 TCP 가 반쯤 열린 채 남는데, 타임아웃이 없으면
    // 화면 캡처를 보내던 스레드가 몇 분을 멈춘다. 실패는 false 로 돌아가고,
    // 받기 쪽이 끊김을 알아채면 자동 재연결이 돈다.
    int sendTimeoutMs = SEND_TIMEOUT_MS;
    ::setsockopt(sock_, SOL_SOCKET, SO_SNDTIMEO,
        reinterpret_cast<const char*>(&sendTimeoutMs), sizeof(sendTimeoutMs));

    connected_ = true;

    if (onConnected) onConnected(serverIp.c_str(), port);

    recvThread_ = std::thread(&StudentClient::RecvLoop, this);
    heartbeatThread_ = std::thread(&StudentClient::HeartbeatLoop, this);

    return true;
}

// ─── 연결 해제 ─────────────────────────────────────────────────────
void StudentClient::Disconnect()
{
    // 아직 살아 있던 경우에만 정상 종료 패킷 전송 후 소켓을 닫는다.
    // (이미 연결이 끊긴 상태여도 아래 스레드 정리는 반드시 수행해야 한다)
    if (connected_.exchange(false))
    {
        DisconnectPayload dp{};
        strncpy_s(dp.reason, "클라이언트 정상 종료", _TRUNCATE);
        // 멤버 SendPacket은 connected_를 검사하므로 전역 함수로 직접 전송
        ::SendPacket(sock_, sendMutex_, PacketType::Disconnect, &dp, sizeof(dp));
    }

    if (sock_ != INVALID_SOCKET)
    {
        ::shutdown(sock_, SD_BOTH);
        ::closesocket(sock_);
        sock_ = INVALID_SOCKET;
    }

    // 연결 상태와 무관하게 스레드를 정리한다.
    // (join하지 않으면 joinable한 std::thread가 파괴될 때 std::terminate로 프로세스가 죽는다)
    if (recvThread_.joinable())      recvThread_.join();
    if (heartbeatThread_.joinable()) heartbeatThread_.join();
}

bool StudentClient::IsConnected() const { return connected_; }

// ─── 패킷 전송 ─────────────────────────────────────────────────────
// 보내기가 실패하면 이 연결을 버린다.
//
// 보내기에는 10초 제한(SEND_TIMEOUT_MS)이 걸려 있는데, 제한에 걸린 send 는 이미 일부 바이트를
// 내보낸 뒤에 실패로 돌아온다. 그 순간 이 연결의 패킷 경계는 어긋나 있다 —
// 그대로 쓰면 교수 쪽이 파일 조각 중간을 헤더로 읽는다. 그러면 이 학생의 전송은 전부 실패하고,
// 15초 뒤 하트비트 만료로 끊길 때까지 다시 내도 계속 실패한다. 하필 가장 바쁜 순간에 그렇게 된다.
//
// 끊어 버리면 이미 있는 자동 재연결(ReconnectService, 3초 간격)이 깨끗한 새 연결을 만들어 주고
// 재제출이 정상 흐름에서 돈다. 교수 쪽은 같은 판단을 이미 하고 있다(ClientSession::Send → sendFailed).
bool StudentClient::SendPacket(PacketType type, const void* payload, uint32_t payloadLen)
{
    if (!connected_ || sock_ == INVALID_SOCKET) return false;

    bool sent = ::SendPacket(sock_, sendMutex_, type, payload, payloadLen);
    if (!sent) HandleDisconnect(static_cast<int>(DisconnectReason::NetworkError));
    return sent;
}

// ─── 파일 전송 ─────────────────────────────────────────────────────
bool StudentClient::SendFile(const std::string& filePath, const std::string& password)
{
    if (!connected_) return false;

    std::thread([this, filePath, password]()
        {
            bool sent = FileTransferSender::SendFile(
                sock_, sendMutex_, filePath, password,
                [this](const std::string& tid, const std::string& fn, int pct)
                { if (onFileProgress) onFileProgress(tid.c_str(), fn.c_str(), pct); },
                [this](const std::string& tid, const std::string& msg)
                { if (onFileError) onFileError(tid.c_str(), msg.c_str()); });

            // 조각을 보내다 실패했으면 이 연결의 패킷 경계가 어긋나 있다(SendPacket 설명 참고).
            // 답안 전송이야말로 어긋난 연결로 다시 시도하면 안 되는 자리라 여기서도 끊는다.
            // 파일을 열지 못한 경우처럼 한 바이트도 안 나간 실패까지 끊게 되지만,
            // 그때 끊는 비용은 재연결 몇 초이고 안 끊었을 때의 비용은 제출 실패다.
            if (!sent) HandleDisconnect(static_cast<int>(DisconnectReason::NetworkError));
        }).detach();

    return true;
}

// ─── 수신 루프 ─────────────────────────────────────────────────────
void StudentClient::RecvLoop()
{
    NlPacketHeader hdr;
    while (connected_)
    {
        if (!RecvHeader(sock_, hdr))
        {
            HandleDisconnect(static_cast<int>(DisconnectReason::NetworkError));
            return;
        }

        // 길이는 상대가 적어 보낸 값이다. 그대로 믿고 할당하면 깨진 값 하나로 수 GB 를 잡다 죽는다.
        // 정상 패킷은 MAX_PAYLOAD_SIZE 를 넘지 않으므로, 넘으면 흐름이 어긋난 것으로 보고 연결을 끊는다.
        if (hdr.payloadLen > MAX_PAYLOAD_SIZE)
        {
            HandleDisconnect(static_cast<int>(DisconnectReason::NetworkError));
            return;
        }

        std::vector<uint8_t> payload(hdr.payloadLen);
        if (hdr.payloadLen > 0)
        {
            if (!RecvExact(sock_, payload.data(), hdr.payloadLen))
            {
                HandleDisconnect(static_cast<int>(DisconnectReason::NetworkError));
                return;
            }
        }

        PacketType type = static_cast<PacketType>(hdr.type);

        // 파일 전송 패킷 라우팅
        if (type == PacketType::FileTransferStart)
        {
            fileReceiver_.HandleStart("server", payload.data(), hdr.payloadLen); continue;
        }
        if (type == PacketType::FileChunk)
        {
            fileReceiver_.HandleChunk(payload.data(), hdr.payloadLen); continue;
        }
        if (type == PacketType::FileTransferComplete)
        {
            fileReceiver_.HandleComplete(payload.data(), hdr.payloadLen); continue;
        }

        if (onPacketReceived)
            onPacketReceived(static_cast<uint32_t>(type), payload.data(), hdr.payloadLen);
    }
}

// ─── Heartbeat 전송 루프 (5초마다) ─────────────────────────────────
// 5초를 한 번에 자지 않고 0.1초씩 나눠 잔다. Disconnect 가 이 스레드를 join 으로 기다리므로,
// 5초를 내리 자면 부른 쪽이 그만큼 멈춘다 — 그 자리가 학생 앱의 화면 스레드여서
// 로그인 거절 팝업이 뜨기 전에 창이 5초 굳고 윈도우가 '응답 없음'을 띄운다(2026-11-05 실측 4996ms).
void StudentClient::HeartbeatLoop()
{
    // 깨어날 때마다 셈을 올려 50번째(=5초)에 한 번만 보낸다.
    int ticks = 0;
    while (connected_)
    {
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
        if (!connected_) break;
        if (++ticks < 50) continue;
        ticks = 0;
        SendPacket(PacketType::Heartbeat); // 페이로드 없음
    }
}

// ─── 예기치 않은 연결 끊김 ─────────────────────────────────────────
// 이제 받기 스레드 말고 보내는 쪽(하트비트·화면 캡처·답안 전송)에서도 불린다.
// 두 스레드가 동시에 들어와도 한 번만 지나가게 한다 — 둘 다 통과하면 소켓을 두 번 닫고,
// 그사이 새 연결이 같은 번호를 받았으면 엉뚱한 연결을 닫는다.
void StudentClient::HandleDisconnect(int reason)
{
    if (!connected_.exchange(false)) return;

    if (sock_ != INVALID_SOCKET)
    {
        ::closesocket(sock_);
        sock_ = INVALID_SOCKET;
    }

    // 시험 파일을 받던 중에 끊겼으면 그 기록을 치운다. 안 치우면 열린 핸들과
    // 반쯤 받은 임시 파일이 남아, 다시 받을 때 그 파일을 지우지도 못한다.
    fileReceiver_.AbortBySender("server");

    if (onDisconnected) onDisconnected(reason);
}
