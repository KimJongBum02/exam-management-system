#include "pch.h"
#include "NetworkLib_Native.h"
#include "ProfessorServer.h"
#include "StudentClient.h"
#include "Protocol.h"

// ══════════════════════════════════════════════════════════════════════
//  Exports.cpp — extern "C" DLL 함수 구현체
//  C# 쪽에서 [DllImport("NetworkLib_Native.dll")] 로 호출합니다.
// ══════════════════════════════════════════════════════════════════════

// 전역 인스턴스 (DLL 당 하나)
static std::unique_ptr<ProfessorServer> g_server;
static std::unique_ptr<StudentClient>   g_client;

// ══════════════════════════════════════════════════════════════════════
//  초기화 / 정리
// ══════════════════════════════════════════════════════════════════════

NL_API int NL_Initialize()
{
    WSADATA wsa;
    return WSAStartup(MAKEWORD(2, 2), &wsa) == 0 ? 1 : 0;
}

NL_API void NL_Cleanup()
{
    g_server.reset();
    g_client.reset();
    WSACleanup();
}

// ══════════════════════════════════════════════════════════════════════
//  서버 API
// ══════════════════════════════════════════════════════════════════════

NL_API int NL_Server_Create(int port)
{
    try {
        g_server = std::make_unique<ProfessorServer>(port);
        return 1;
    } catch (...) { return 0; }
}

NL_API int NL_Server_Start()
{
    if (!g_server) return -1;
    return g_server->Start() ? 1 : 0;
}

NL_API void NL_Server_Stop()
{
    if (g_server) g_server->Stop();
}

NL_API int NL_Server_GetConnectedCount()
{
    if (!g_server) return 0;
    return g_server->GetConnectedCount();
}

// ── 콜백 등록 ────────────────────────────────────────────────────────

NL_API void NL_Server_SetOnStudentConnected(NL_OnStudentConnected cb)
{
    if (!g_server) return;
    g_server->onStudentConnected = cb
        ? std::function<void(const char*, const char*, const char*, const char*)>(cb)
        : nullptr;
}

NL_API void NL_Server_SetOnStudentDisconnected(NL_OnStudentDisconnected cb)
{
    if (!g_server) return;
    g_server->onStudentDisconnected = cb
        ? std::function<void(const char*, const char*, const char*, int)>(cb)
        : nullptr;
}

NL_API void NL_Server_SetOnPacketReceived(NL_OnPacketReceived cb)
{
    if (!g_server) return;
    g_server->onPacketReceived = cb
        ? std::function<void(const char*, const char*, const char*, uint32_t, const uint8_t*, uint32_t)>(cb)
        : nullptr;
}

NL_API void NL_Server_SetOnFileReceived(NL_OnFileReceived cb)
{
    if (!g_server) return;
    g_server->onFileReceived = cb
        ? FileReceivedCb([cb](const std::string& tid, const std::string& sid,
                              const std::string& fn,  const std::string& tp,
                              int64_t sz, const std::string& pw)
          { cb(tid.c_str(), sid.c_str(), fn.c_str(), tp.c_str(), sz, pw.c_str()); })
        : nullptr;
}

NL_API void NL_Server_SetOnFileProgress(NL_OnFileProgress cb)
{
    if (!g_server) return;
    g_server->onFileProgress = cb
        ? FileProgressCb([cb](const std::string& tid, const std::string& fn, int pct)
          { cb(tid.c_str(), fn.c_str(), pct); })
        : nullptr;
}

NL_API void NL_Server_SetOnFileError(NL_OnFileError cb)
{
    if (!g_server) return;
    g_server->onFileError = cb
        ? FileErrorCb([cb](const std::string& tid, const std::string& msg)
          { cb(tid.c_str(), msg.c_str()); })
        : nullptr;
}

// ── 패킷 전송 ────────────────────────────────────────────────────────

NL_API int NL_Server_Broadcast(uint32_t packetType, const uint8_t* payload, uint32_t payloadLen)
{
    if (!g_server) return -1;
    return g_server->Broadcast(static_cast<PacketType>(packetType), payload, payloadLen);
}

NL_API int NL_Server_SendToSession(const char* sessionId, uint32_t packetType,
                                    const uint8_t* payload, uint32_t payloadLen)
{
    if (!g_server) return -1;
    return g_server->SendToSession(sessionId, static_cast<PacketType>(packetType), payload, payloadLen);
}

NL_API int NL_Server_BroadcastFile(const char* filePath, const char* archivePassword)
{
    if (!g_server) return -1;
    return g_server->BroadcastFile(filePath, archivePassword ? archivePassword : "");
}

NL_API int NL_Server_SendFileToSession(const char* sessionId, const char* filePath,
                                        const char* archivePassword)
{
    if (!g_server) return -1;
    return g_server->SendFileToSession(sessionId, filePath, archivePassword ? archivePassword : "");
}

// ══════════════════════════════════════════════════════════════════════
//  클라이언트 API
// ══════════════════════════════════════════════════════════════════════

NL_API int NL_Client_Create()
{
    try {
        g_client = std::make_unique<StudentClient>();
        return 1;
    } catch (...) { return 0; }
}

NL_API int NL_Client_Connect(const char* serverIp, int port)
{
    if (!g_client) return -1;
    return g_client->Connect(serverIp, port) ? 1 : 0;
}

NL_API void NL_Client_Disconnect()
{
    if (g_client) g_client->Disconnect();
}

NL_API int NL_Client_IsConnected()
{
    if (!g_client) return 0;
    return g_client->IsConnected() ? 1 : 0;
}

// ── 콜백 등록 ────────────────────────────────────────────────────────

NL_API void NL_Client_SetOnConnected(NL_OnClientConnected cb)
{
    if (!g_client) return;
    g_client->onConnected = cb
        ? std::function<void(const char*, int)>(cb)
        : nullptr;
}

NL_API void NL_Client_SetOnDisconnected(NL_OnClientDisconnected cb)
{
    if (!g_client) return;
    g_client->onDisconnected = cb
        ? std::function<void(int)>(cb)
        : nullptr;
}

NL_API void NL_Client_SetOnPacketReceived(NL_OnClientPacket cb)
{
    if (!g_client) return;
    g_client->onPacketReceived = cb
        ? std::function<void(uint32_t, const uint8_t*, uint32_t)>(cb)
        : nullptr;
}

NL_API void NL_Client_SetOnFileReceived(NL_OnFileReceived cb)
{
    if (!g_client) return;
    g_client->onFileReceived = cb
        ? FileReceivedCb([cb](const std::string& tid, const std::string& sid,
                              const std::string& fn,  const std::string& tp,
                              int64_t sz, const std::string& pw)
          { cb(tid.c_str(), sid.c_str(), fn.c_str(), tp.c_str(), sz, pw.c_str()); })
        : nullptr;
}

NL_API void NL_Client_SetOnFileProgress(NL_OnFileProgress cb)
{
    if (!g_client) return;
    g_client->onFileProgress = cb
        ? FileProgressCb([cb](const std::string& tid, const std::string& fn, int pct)
          { cb(tid.c_str(), fn.c_str(), pct); })
        : nullptr;
}

NL_API void NL_Client_SetOnFileError(NL_OnFileError cb)
{
    if (!g_client) return;
    g_client->onFileError = cb
        ? FileErrorCb([cb](const std::string& tid, const std::string& msg)
          { cb(tid.c_str(), msg.c_str()); })
        : nullptr;
}

NL_API void NL_Client_SetOnError(NL_OnNetworkError cb)
{
    if (!g_client) return;
    g_client->onError = cb
        ? std::function<void(const char*)>(cb)
        : nullptr;
}

// ── 패킷 전송 ────────────────────────────────────────────────────────

NL_API int NL_Client_SendPacket(uint32_t packetType, const uint8_t* payload, uint32_t payloadLen)
{
    if (!g_client) return -1;
    return g_client->SendPacket(static_cast<PacketType>(packetType), payload, payloadLen) ? 1 : 0;
}

NL_API int NL_Client_SendFile(const char* filePath, const char* archivePassword)
{
    if (!g_client) return -1;
    return g_client->SendFile(filePath, archivePassword ? archivePassword : "") ? 1 : 0;
}

// ══════════════════════════════════════════════════════════════════════
//  채팅 API
// ══════════════════════════════════════════════════════════════════════

NL_API int NL_Server_BroadcastChat(const char* message)
{
    if (!g_server || !message) return -1;
    ChatBroadcastPayload p{};
    strncpy_s(p.message, message, _TRUNCATE);
    return g_server->Broadcast(PacketType::ChatBroadcast,
        reinterpret_cast<const uint8_t*>(&p), sizeof(p));
}

NL_API int NL_Server_SendChatToSession(const char* sessionId, const char* message)
{
    if (!g_server || !sessionId || !message) return -1;
    ChatDirectPayload p{};
    strncpy_s(p.message, message, _TRUNCATE);
    return g_server->SendToSession(sessionId, PacketType::ChatDirect,
        reinterpret_cast<const uint8_t*>(&p), sizeof(p));
}

NL_API int NL_Client_SendChat(const char* message)
{
    if (!g_client || !message) return -1;
    ChatFromStudentPayload p{};
    strncpy_s(p.message, message, _TRUNCATE);
    return g_client->SendPacket(PacketType::ChatFromStudent,
        reinterpret_cast<const uint8_t*>(&p), sizeof(p)) ? 1 : 0;
}
