#pragma once
#include <cstdint>

// ══════════════════════════════════════════════════════════════════════
//  Protocol.h — 바이너리 패킷 프로토콜 정의
//
//  패킷 와이어 포맷:
//    [uint32_t type (4 bytes)] [uint32_t payloadLen (4 bytes)] [payload (N bytes)]
//
//  JSON/Base64 없음 — 구조체를 메모리 그대로 전송합니다.
// ══════════════════════════════════════════════════════════════════════

// ─── 패킷 타입 ─────────────────────────────────────────────────────
enum class PacketType : uint32_t
{
    // 연결 (1~9)
    StudentLogin             = 1,
    LoginResponse            = 2,
    Heartbeat                = 3,
    Disconnect               = 4,

    // 출결 (10~19)
    AttendanceCheckRequest   = 10,
    AttendanceCheckResponse  = 11,

    // 시험 제어 (20~29)
    ExamPhaseChange          = 20,
    ExamStatusUpdate         = 21,
    CheatingAlert            = 22,

    // 파일 전송 (30~39)
    FileTransferStart        = 30,
    FileChunk                = 31,
    FileTransferComplete     = 32,
    ExtractArchive           = 33,
    ExamSubmitRequest        = 34,

    // 프로세스 제어 (40~49)
    ProcessListUpdate        = 40,   // 가변 길이 페이로드
    ForceProcessKill         = 41,
    ShutdownPC               = 42,

    // 퀴즈 (50~59)
    QuizQuestion             = 50,   // 가변 길이 페이로드
    QuizAnswer               = 51,
    QuizResult               = 52,

    // 공통 응답
    CommandAck               = 100,
};

// ─── 시험 단계 ─────────────────────────────────────────────────────
enum class ExamPhase : uint32_t
{
    Waiting        = 0,
    Ready          = 1,
    InProgress     = 2,
    SubmitRequested= 3,
    Closed         = 4,
};

// ─── 학생 상태 ─────────────────────────────────────────────────────
enum class StudentStatus : uint32_t
{
    NotConnected   = 0,
    Connected      = 1,
    FileReceived   = 2,
    InProgress     = 3,
    Submitted      = 4,
    Approved       = 5,
    CheatingDetected = 6,
    Absent         = 7,
};

// ─── 부정행위 유형 ─────────────────────────────────────────────────
enum class CheatingAlertType : uint32_t
{
    BlacklistedProcessLaunched = 0,
    NetworkAccessAttempt       = 1,
    UnauthorizedProcess        = 2,
    ManualReport               = 3,
};

// ─── 연결 해제 사유 ────────────────────────────────────────────────
enum class DisconnectReason : int
{
    ClientDisconnected = 0,
    HeartbeatTimeout   = 1,
    NetworkError       = 2,
    ServerShutdown     = 3,
};

// ══════════════════════════════════════════════════════════════════════
//  바이너리 페이로드 구조체
//  #pragma pack(push, 1) 으로 패딩 없이 직렬화합니다.
// ══════════════════════════════════════════════════════════════════════
#pragma pack(push, 1)

// ─── 패킷 헤더 (모든 패킷 앞에 붙음) ──────────────────────────────
struct NlPacketHeader
{
    uint32_t type;       // PacketType
    uint32_t payloadLen; // 뒤따르는 payload 바이트 수
};

// ─── 연결 관련 ────────────────────────────────────────────────────
struct LoginPayload
{
    char studentId[16];    // 학번 (UTF-8, null 종료)
    char studentName[64];  // 이름 (UTF-8, null 종료)
};

struct LoginResponsePayload
{
    uint8_t success;          // 1=승인, 0=거부
    char    message[128];     // 안내 메시지
    char    rejectionReason[128]; // 거부 사유 (success=0일 때)
};

struct DisconnectPayload
{
    char reason[128];
};

// ─── 출결 관련 ────────────────────────────────────────────────────
struct AttendanceCheckRequestPayload
{
    char     checkId[37];    // UUID 문자열 (36 + null)
    char     message[256];
    uint32_t timeoutSeconds;
};

struct AttendanceCheckResponsePayload
{
    char checkId[37];
    char studentId[16];
    char studentName[64];
};

// ─── 시험 제어 ────────────────────────────────────────────────────
struct ExamPhaseChangePayload
{
    uint32_t phase;       // ExamPhase
    char     message[256];
};

struct ExamStatusUpdatePayload
{
    char     studentId[16];
    uint32_t status;      // StudentStatus
    char     detail[256];
};

struct CheatingAlertPayload
{
    char     studentId[16];
    char     studentName[64];
    uint32_t alertType;   // CheatingAlertType
    char     description[256];
};

// ─── 파일 전송 ────────────────────────────────────────────────────

// FileTransferStart: 파일 메타데이터 (실제 바이트 전에 전송)
struct FileTransferStartPayload
{
    char    transferId[37];     // UUID 문자열
    char    fileName[260];      // 파일 이름 (MAX_PATH)
    int64_t totalSize;          // 파일 전체 크기 (bytes)
    uint32_t totalChunks;       // 전체 청크 수
    char    sha256Hash[65];     // SHA-256 hex 문자열 (64 + null)
    char    archivePassword[128]; // 압축 암호 (메타데이터 전달용)
};

// FileChunk: 가변 길이 — 이 헤더 뒤에 dataSize 바이트의 원본 파일 데이터가 붙음
struct NlFileChunkHeader
{
    char     transferId[37];
    uint32_t chunkIndex;
    uint32_t dataSize;    // 뒤따르는 raw 파일 데이터 크기
    // 이 구조체 뒤에 dataSize 바이트의 raw 파일 바이트가 붙음
};

struct FileTransferCompletePayload
{
    char transferId[37];
    char fileName[260];
};

struct ExtractArchivePayload
{
    char    fileName[260];
    char    destinationFolderName[260];
    char    password[128];
    uint8_t deleteAfterExtract; // 1=압축 해제 후 zip 삭제
};

struct ExamSubmitRequestPayload
{
    char    folderName[260];
    char    archivePassword[128];
    int64_t deadline;           // Unix timestamp (0 = 기한 없음)
};

// ─── 프로세스 제어 ────────────────────────────────────────────────
// ProcessListUpdate는 가변 길이입니다.
// 페이로드 형식:
//   [uint32_t whitelistCount]
//   [uint32_t blacklistCount]
//   whitelistCount 개의 문자열: 각 [uint16_t len][len bytes UTF-8]
//   blacklistCount 개의 문자열: 각 [uint16_t len][len bytes UTF-8]

struct ForceProcessKillPayload
{
    char processName[260]; // 예: "chrome.exe"
};

struct ShutdownPCPayload
{
    uint32_t delaySeconds;
    char     message[256];
};

// ─── 퀴즈 ─────────────────────────────────────────────────────────
// QuizQuestion은 가변 길이입니다.
// 페이로드 형식:
//   [QuizQuestionHeader]
//   optionCount 개의 문자열: 각 [uint16_t len][len bytes UTF-8]

struct QuizQuestionHeader
{
    char     quizId[37];
    uint32_t questionType;    // 0=OX, 1=ShortAnswer, 2=MultipleChoice
    char     question[512];
    uint32_t optionCount;
    uint32_t timeoutSeconds;  // 0 = 제한 없음
};

struct QuizAnswerPayload
{
    char quizId[37];
    char studentId[16];
    char studentName[64];
    char answer[256];
};

struct QuizResultPayload
{
    char quizId[37];
    char correctAnswer[256];
    char explanation[512];
};

// ─── 공통 응답 ────────────────────────────────────────────────────
struct CommandAckPayload
{
    uint32_t commandType; // PacketType
    uint8_t  success;
    char     message[256];
};

#pragma pack(pop)
