#include "pch.h"
#include "ClientSession.h"
#include "PacketIO.h"
#include "FileTransfer.h"

// ─── 생성자 ────────────────────────────────────────────────────────
ClientSession::ClientSession(SOCKET s, const std::string& addr)
    : sock(s), remoteAddr(addr)
{
    lastHeartbeat = std::chrono::steady_clock::now();
}

// ─── 소멸자 ────────────────────────────────────────────────────────
ClientSession::~ClientSession()
{
    if (sock != INVALID_SOCKET)
    {
        ::shutdown(sock, SD_BOTH);
        ::closesocket(sock);
        sock = INVALID_SOCKET;
    }
    if (recvThread_.joinable()) recvThread_.join();
}

// ─── 수신 루프 시작 ────────────────────────────────────────────────
void ClientSession::StartRecvLoop()
{
    recvThread_ = std::thread(&ClientSession::RecvLoop, this);
    recvThread_.detach(); // ProfessorServer가 세션 수명을 관리
}

// ─── 수신 루프 ─────────────────────────────────────────────────────
void ClientSession::RecvLoop()
{
    NlPacketHeader hdr;
    while (alive)
    {
        // 1. 헤더 수신
        if (!RecvHeader(sock, hdr))
        {
            Close(static_cast<int>(DisconnectReason::NetworkError));
            return;
        }

        // 2. 페이로드 수신
        std::vector<uint8_t> payload(hdr.payloadLen);
        if (hdr.payloadLen > 0)
        {
            if (!RecvExact(sock, payload.data(), hdr.payloadLen))
            {
                Close(static_cast<int>(DisconnectReason::NetworkError));
                return;
            }
        }

        PacketType type = static_cast<PacketType>(hdr.type);

        // 3. Heartbeat 는 내부 처리
        if (type == PacketType::Heartbeat)
        {
            lastHeartbeat = std::chrono::steady_clock::now();
            continue;
        }

        // 4. 연결 해제 패킷
        if (type == PacketType::Disconnect)
        {
            Close(static_cast<int>(DisconnectReason::ClientDisconnected));
            return;
        }

        // 5. 파일 전송 패킷은 FileTransferReceiver로 라우팅
        if (HandleFileChunk(type, payload.data(), hdr.payloadLen)) continue;

        // 6. 나머지는 ProfessorServer의 콜백으로 전달
        if (onPacket) onPacket(this, type, payload.data(), hdr.payloadLen);
    }
}

// ─── 파일 청크 라우팅 ──────────────────────────────────────────────
bool ClientSession::HandleFileChunk(PacketType type, const uint8_t* payload, uint32_t len)
{
    if (!fileReceiver) return false;

    switch (type)
    {
    case PacketType::FileTransferStart:
        fileReceiver->HandleStart(sessionId, payload, len);
        return true;
    case PacketType::FileChunk:
        fileReceiver->HandleChunk(payload, len);
        return true;
    case PacketType::FileTransferComplete:
        fileReceiver->HandleComplete(payload, len);
        return true;
    default:
        return false;
    }
}

// ─── 패킷 전송 ─────────────────────────────────────────────────────
bool ClientSession::Send(PacketType type, const void* payload, uint32_t payloadLen)
{
    if (!alive) return false;
    return SendPacket(sock, sendMutex, type, payload, payloadLen);
}

// ─── Heartbeat 타임아웃 확인 ───────────────────────────────────────
bool ClientSession::IsHeartbeatExpired(int timeoutSecs) const
{
    auto elapsed = std::chrono::steady_clock::now() - lastHeartbeat;
    return std::chrono::duration_cast<std::chrono::seconds>(elapsed).count() >= timeoutSecs;
}

// ─── 연결 종료 ─────────────────────────────────────────────────────
void ClientSession::Close(int reason)
{
    bool expected = true;
    if (!alive.compare_exchange_strong(expected, false)) return; // 이미 종료됨

    ::shutdown(sock, SD_BOTH);
    ::closesocket(sock);
    sock = INVALID_SOCKET;

    if (onDisconnected) onDisconnected(this, reason);
}
