#include "pch.h"
#include "PacketIO.h"

// ─── 정확히 len 바이트 전송 ────────────────────────────────────────
bool SendExact(SOCKET sock, const void* buf, uint32_t len)
{
    const char* ptr = static_cast<const char*>(buf);
    uint32_t sent = 0;
    while (sent < len)
    {
        int n = ::send(sock, ptr + sent, static_cast<int>(len - sent), 0);
        if (n <= 0) return false; // 연결 끊김 또는 오류
        sent += static_cast<uint32_t>(n);
    }
    return true;
}

// ─── 정확히 len 바이트 수신 ────────────────────────────────────────
bool RecvExact(SOCKET sock, void* buf, uint32_t len)
{
    char* ptr = static_cast<char*>(buf);
    uint32_t recvd = 0;
    while (recvd < len)
    {
        int n = ::recv(sock, ptr + recvd, static_cast<int>(len - recvd), 0);
        if (n <= 0) return false; // 연결 끊김 또는 오류
        recvd += static_cast<uint32_t>(n);
    }
    return true;
}

// ─── 패킷 헤더 수신 ────────────────────────────────────────────────
bool RecvHeader(SOCKET sock, NlPacketHeader& outHeader)
{
    return RecvExact(sock, &outHeader, sizeof(NlPacketHeader));
}

// ─── 패킷 전송 ─────────────────────────────────────────────────────
bool SendPacket(SOCKET sock, std::mutex& sendMtx, PacketType type, const void* payload, uint32_t payloadLen)
{
    NlPacketHeader hdr;
    hdr.type       = static_cast<uint32_t>(type);
    hdr.payloadLen = payloadLen;

    std::lock_guard<std::mutex> lock(sendMtx);
    if (!SendExact(sock, &hdr, sizeof(hdr)))        return false;
    if (payloadLen > 0 && payload != nullptr)
        if (!SendExact(sock, payload, payloadLen))  return false;
    return true;
}
