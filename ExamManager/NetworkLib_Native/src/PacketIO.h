#pragma once
#include "pch.h"
#include "Protocol.h"

// ─── 정확히 len 바이트 전송 (TCP 분할 전송 대비) ───────────────────
bool SendExact(SOCKET sock, const void* buf, uint32_t len);

// ─── 정확히 len 바이트 수신 (TCP 분할 수신 대비) ───────────────────
bool RecvExact(SOCKET sock, void* buf, uint32_t len);

// ─── 패킷 전송: [PacketHeader][payload] ────────────────────────────
// sendMutex로 보호해야 합니다 (동시 전송 시 혼용 방지)
bool SendPacket(SOCKET sock, std::mutex& sendMtx, PacketType type, const void* payload, uint32_t payloadLen);

// ─── 패킷 헤더 수신 ────────────────────────────────────────────────
bool RecvHeader(SOCKET sock, NlPacketHeader& outHeader);
