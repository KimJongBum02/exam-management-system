#include "pch.h"
#include "ClientSession.h"
#include "PacketIO.h"
#include "FileTransfer.h"

namespace {

// 답안을 저장하는 동안 이 세션을 '바쁨'으로 표시한다(ClientSession::busy 설명 참고).
//
// 되돌리는 일을 소멸자에 맡기는 이유: 저장 중에 예외로 빠져나가면 busy 가 켜진 채 남아
// 그 세션은 다시는 하트비트 검사를 받지 못한다 — 학생 PC 가 꺼져도 영영 '접속 중'이 된다.
//
// 풀 때 하트비트 시각도 지금으로 당긴다. 막혀 있던 동안 쌓인 '조용했던 시간'을 그대로 두면
// busy 를 끄자마자 감시 스레드가 만료로 보고 닫는다 — 1초만 늦게 돌아도 고친 의미가 없어진다.
struct SaveGuard
{
    ClientSession& session;

    explicit SaveGuard(ClientSession& s) : session(s) { session.busy = true; }
    ~SaveGuard()
    {
        session.lastHeartbeat = std::chrono::steady_clock::now();
        session.busy = false;
    }
};

} // namespace

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
        //    길이는 학생 PC 가 적어 보낸 값이다. 그대로 믿고 할당하면, 헤더 8바이트만 꾸며 보내도
        //    교수 서버가 수 GB 를 잡다 죽어 시험 전체가 멈춘다. 넘으면 그 학생 연결만 끊는다.
        if (hdr.payloadLen > MAX_PAYLOAD_SIZE)
        {
            Close(static_cast<int>(DisconnectReason::NetworkError));
            return;
        }

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
    {
        // 이 안에서 SHA-256 검사와 교수 앱의 저장(복사 + 7za 해제)이 끝까지 돈다.
        // 그동안 이 스레드는 패킷을 못 읽어 하트비트가 멈추므로 감시에서 빼 둔다.
        SaveGuard guard(*this);
        fileReceiver->HandleComplete(payload, len);
        return true;
    }
    default:
        return false;
    }
}

// ─── 패킷 전송 ─────────────────────────────────────────────────────
bool ClientSession::Send(PacketType type, const void* payload, uint32_t payloadLen)
{
    if (!alive) return false;

    // 실패하면 흔적을 남긴다. 보내기 타임아웃(SEND_TIMEOUT_MS)이 걸린 뒤에는 보낸 바이트가
    // 중간에서 끊겨 이 연결의 패킷 경계가 어긋나 있다. 그대로 두면 상대가 쓰레기를 헤더로 읽는다.
    bool sent = SendPacket(sock, sendMutex, type, payload, payloadLen);
    if (!sent) sendFailed = true;
    return sent;
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

    // 이 학생이 받던 중이던 전송을 치운다. 안 치우면 열린 파일 핸들과
    // 반쯤 받은 임시 파일이 그대로 남는다(FileTransferReceiver::AbortBySender).
    if (fileReceiver) fileReceiver->AbortBySender(sessionId);

    if (onDisconnected) onDisconnected(this, reason);
}
