#include "pch.h"
#include "StudentClient.h"
#include "PacketIO.h"

// ─── 생성자 ────────────────────────────────────────────────────────
StudentClient::StudentClient()
{
    fileReceiver_.onFileReceived = [this](const std::string& tid, const std::string& sid,
                                          const std::string& fn,  const std::string& tp,
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
    addr.sin_port   = htons(static_cast<u_short>(port));
    inet_pton(AF_INET, serverIp.c_str(), &addr.sin_addr);

    if (::connect(sock_, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) == SOCKET_ERROR)
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

    connected_ = true;

    if (onConnected) onConnected(serverIp.c_str(), port);

    recvThread_      = std::thread(&StudentClient::RecvLoop,      this);
    heartbeatThread_ = std::thread(&StudentClient::HeartbeatLoop, this);

    return true;
}

// ─── 연결 해제 ─────────────────────────────────────────────────────
void StudentClient::Disconnect()
{
    if (!connected_) return;
    connected_ = false;

    // Disconnect 패킷 전송 시도 (무시해도 무방)
    DisconnectPayload dp{};
    strncpy_s(dp.reason, "클라이언트 정상 종료", _TRUNCATE);
    SendPacket(PacketType::Disconnect, &dp, sizeof(dp));

    if (sock_ != INVALID_SOCKET)
    {
        ::shutdown(sock_, SD_BOTH);
        ::closesocket(sock_);
        sock_ = INVALID_SOCKET;
    }

    if (recvThread_.joinable())      recvThread_.join();
    if (heartbeatThread_.joinable()) heartbeatThread_.join();
}

bool StudentClient::IsConnected() const { return connected_; }

// ─── 패킷 전송 ─────────────────────────────────────────────────────
bool StudentClient::SendPacket(PacketType type, const void* payload, uint32_t payloadLen)
{
    if (!connected_ || sock_ == INVALID_SOCKET) return false;
    return ::SendPacket(sock_, sendMutex_, type, payload, payloadLen);
}

// ─── 파일 전송 ─────────────────────────────────────────────────────
bool StudentClient::SendFile(const std::string& filePath, const std::string& password)
{
    if (!connected_) return false;

    std::thread([this, filePath, password]()
    {
        FileTransferSender::SendFile(
            sock_, sendMutex_, filePath, password,
            [this](const std::string& tid, const std::string& fn, int pct)
            { if (onFileProgress) onFileProgress(tid.c_str(), fn.c_str(), pct); },
            [this](const std::string& tid, const std::string& msg)
            { if (onFileError) onFileError(tid.c_str(), msg.c_str()); });
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
        { fileReceiver_.HandleStart("server", payload.data(), hdr.payloadLen); continue; }
        if (type == PacketType::FileChunk)
        { fileReceiver_.HandleChunk(payload.data(), hdr.payloadLen); continue; }
        if (type == PacketType::FileTransferComplete)
        { fileReceiver_.HandleComplete(payload.data(), hdr.payloadLen); continue; }

        if (onPacketReceived)
            onPacketReceived(static_cast<uint32_t>(type), payload.data(), hdr.payloadLen);
    }
}

// ─── Heartbeat 전송 루프 (5초마다) ─────────────────────────────────
void StudentClient::HeartbeatLoop()
{
    while (connected_)
    {
        std::this_thread::sleep_for(std::chrono::seconds(5));
        if (!connected_) break;
        SendPacket(PacketType::Heartbeat); // 페이로드 없음
    }
}

// ─── 예기치 않은 연결 끊김 ─────────────────────────────────────────
void StudentClient::HandleDisconnect(int reason)
{
    if (!connected_) return;
    connected_ = false;

    if (sock_ != INVALID_SOCKET)
    {
        ::closesocket(sock_);
        sock_ = INVALID_SOCKET;
    }

    if (onDisconnected) onDisconnected(reason);
}
