#pragma once

// ══════════════════════════════════════════════════════════════════════
//  NetworkLib_Native.h — C# P/Invoke용 공개 C API
//
//  C# 쪽에서 [DllImport("NetworkLib_Native.dll")] 로 호출합니다.
//  모든 함수는 extern "C", __cdecl 호출 규약을 사용합니다.
//  문자열은 UTF-8 인코딩입니다.
//
//  반환값 규약:
//    1  = 성공
//    0  = 실패
//   -1  = 초기화 안 됨
// ══════════════════════════════════════════════════════════════════════

#ifdef NETWORKLIB_EXPORTS
#  define NL_API extern "C" __declspec(dllexport)
#else
#  define NL_API extern "C" __declspec(dllimport)
#endif

#include <cstdint>

// ══════════════════════════════════════════════════════════════════════
//  콜백 함수 포인터 타입 (C# UnmanagedFunctionPointer)
// ══════════════════════════════════════════════════════════════════════

// 서버 → C# 콜백
typedef void(__cdecl* NL_OnStudentConnected)   (const char* sessionId, const char* studentId, const char* studentName, const char* remoteAddr);
typedef void(__cdecl* NL_OnStudentDisconnected)(const char* sessionId, const char* studentId, const char* studentName, int reason);
typedef void(__cdecl* NL_OnPacketReceived)     (const char* sessionId, const char* studentId, const char* studentName, uint32_t packetType, const uint8_t* payload, uint32_t payloadLen);
typedef void(__cdecl* NL_OnFileReceived)       (const char* transferId, const char* senderId,  const char* fileName,    const char* tempPath, int64_t fileSize, const char* archivePassword);
typedef void(__cdecl* NL_OnFileProgress)       (const char* transferId, const char* fileName,  int percent);
typedef void(__cdecl* NL_OnFileError)          (const char* transferId, const char* message);

// 교수 → 학생 파일 전송 진행률/오류.
// 위 NL_OnFileProgress 와 달리 "어느 학생에게 보내는 중인지" 를 함께 넘긴다.
// (한 번에 여러 학생에게 전송하므로 세션 정보가 없으면 진행률을 화면에 매핑할 수 없다)
typedef void(__cdecl* NL_OnSendProgress)       (const char* sessionId, const char* studentId, const char* transferId, const char* fileName, int percent);
typedef void(__cdecl* NL_OnSendError)          (const char* sessionId, const char* studentId, const char* transferId, const char* message);

// 클라이언트 → C# 콜백
typedef void(__cdecl* NL_OnClientConnected)    (const char* serverIp, int port);
typedef void(__cdecl* NL_OnClientDisconnected) (int reason);
typedef void(__cdecl* NL_OnClientPacket)       (uint32_t packetType, const uint8_t* payload, uint32_t payloadLen);
typedef void(__cdecl* NL_OnNetworkError)       (const char* message);

// ══════════════════════════════════════════════════════════════════════
//  라이브러리 초기화 / 정리
// ══════════════════════════════════════════════════════════════════════
NL_API int  NL_Initialize();   // WSAStartup — 프로그램 시작 시 한 번 호출
NL_API void NL_Cleanup();      // WSACleanup — 프로그램 종료 시 한 번 호출

// ══════════════════════════════════════════════════════════════════════
//  서버 API (교수 PC)
// ══════════════════════════════════════════════════════════════════════

/// <summary>서버 인스턴스를 생성합니다. NL_Initialize() 이후 호출하세요.</summary>
/// <param name="port">TCP 포트 (기본 9000)</param>
NL_API int  NL_Server_Create(int port);

/// <summary>서버를 시작합니다. 내부적으로 accept 루프가 백그라운드 스레드에서 실행됩니다.</summary>
NL_API int  NL_Server_Start();

/// <summary>서버를 중지하고 모든 연결을 종료합니다.</summary>
NL_API void NL_Server_Stop();

/// <summary>현재 로그인된 학생 수를 반환합니다.</summary>
NL_API int  NL_Server_GetConnectedCount();

// ── 콜백 등록 ────────────────────────────────────────────────────────
NL_API void NL_Server_SetOnStudentConnected   (NL_OnStudentConnected    cb);
NL_API void NL_Server_SetOnStudentDisconnected(NL_OnStudentDisconnected cb);
NL_API void NL_Server_SetOnPacketReceived     (NL_OnPacketReceived      cb);
NL_API void NL_Server_SetOnFileReceived       (NL_OnFileReceived        cb);
NL_API void NL_Server_SetOnFileProgress       (NL_OnFileProgress        cb);
NL_API void NL_Server_SetOnFileError          (NL_OnFileError           cb);
NL_API void NL_Server_SetOnSendProgress       (NL_OnSendProgress        cb);
NL_API void NL_Server_SetOnSendError          (NL_OnSendError           cb);

// ── 패킷 전송 ────────────────────────────────────────────────────────

/// <summary>로그인된 모든 학생에게 패킷을 브로드캐스트합니다.</summary>
NL_API int  NL_Server_Broadcast        (uint32_t packetType, const uint8_t* payload, uint32_t payloadLen);

/// <summary>특정 세션(학생)에게 패킷을 전송합니다.</summary>
NL_API int  NL_Server_SendToSession    (const char* sessionId, uint32_t packetType, const uint8_t* payload, uint32_t payloadLen);

// ── 파일 전송 ────────────────────────────────────────────────────────

/// <summary>
/// 모든 학생에게 파일을 전송합니다 (비동기 — 함수는 즉시 반환됨).
/// 진행률은 NL_OnFileProgress 콜백으로 보고됩니다.
/// </summary>
NL_API int  NL_Server_BroadcastFile    (const char* filePath, const char* archivePassword);

/// <summary>특정 학생에게 파일을 전송합니다 (비동기).</summary>
NL_API int  NL_Server_SendFileToSession(const char* sessionId, const char* filePath, const char* archivePassword);

// ══════════════════════════════════════════════════════════════════════
//  클라이언트 API (학생 PC)
// ══════════════════════════════════════════════════════════════════════

/// <summary>클라이언트 인스턴스를 생성합니다. NL_Initialize() 이후 호출하세요.</summary>
NL_API int  NL_Client_Create();

/// <summary>
/// 교수 서버에 TCP 연결을 시도합니다.
/// 연결 성공 시 NL_OnClientConnected 콜백이 호출됩니다.
/// </summary>
/// <param name="serverIp">교수 PC IP 주소 (UTF-8)</param>
/// <param name="port">서버 포트 (기본 9000)</param>
NL_API int  NL_Client_Connect    (const char* serverIp, int port);

/// <summary>서버와의 연결을 종료합니다.</summary>
NL_API void NL_Client_Disconnect ();

/// <summary>현재 연결 상태를 반환합니다. 1=연결됨, 0=미연결</summary>
NL_API int  NL_Client_IsConnected();

// ── 콜백 등록 ────────────────────────────────────────────────────────
NL_API void NL_Client_SetOnConnected   (NL_OnClientConnected    cb);
NL_API void NL_Client_SetOnDisconnected(NL_OnClientDisconnected cb);
NL_API void NL_Client_SetOnPacketReceived(NL_OnClientPacket     cb);
NL_API void NL_Client_SetOnFileReceived(NL_OnFileReceived       cb);
NL_API void NL_Client_SetOnFileProgress(NL_OnFileProgress       cb);
NL_API void NL_Client_SetOnFileError   (NL_OnFileError          cb);
NL_API void NL_Client_SetOnError       (NL_OnNetworkError       cb);

// ── 패킷 전송 ────────────────────────────────────────────────────────

/// <summary>교수 서버에 패킷을 전송합니다.</summary>
NL_API int  NL_Client_SendPacket(uint32_t packetType, const uint8_t* payload, uint32_t payloadLen);

/// <summary>교수 서버에 파일을 전송합니다 (비동기).
/// 학생이 시험 파일을 제출할 때 사용합니다.
/// </summary>
NL_API int  NL_Client_SendFile  (const char* filePath, const char* archivePassword);

// ── 채팅 전송 ─────────────────────────────────────────────────────────

/// <summary>전체 학생에게 채팅 메시지를 전송합니다 (교수 전용).</summary>
NL_API int  NL_Server_BroadcastChat    (const char* message);

/// <summary>특정 학생에게 채팅 메시지를 전송합니다 (교수 전용).</summary>
NL_API int  NL_Server_SendChatToSession(const char* sessionId, const char* message);

/// <summary>교수 서버에 채팅 메시지를 전송합니다 (학생 전용).</summary>
NL_API int  NL_Client_SendChat         (const char* message);
