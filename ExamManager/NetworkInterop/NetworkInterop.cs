// NetworkInterop.cs
// UI 팀이 이 파일을 ProfessorUI 또는 StudentUI 프로젝트에 추가해서 사용합니다.
// NetworkLib_Native.dll 이 실행 파일과 같은 폴더에 있어야 합니다.
//
// 사용법:
//   1. 이 파일을 프로젝트에 추가
//   2. NetworkLib_Native.dll 을 출력 폴더에 복사 (빌드 이벤트로 자동화 권장)
//   3. 아래 클래스를 통해 서버/클라이언트를 초기화

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace NetworkLib
{
    // ══════════════════════════════════════════════════════════════════
    //  패킷 타입 (Protocol.h의 PacketType enum과 동일한 값)
    // ══════════════════════════════════════════════════════════════════
    public enum PacketType : uint
    {
        StudentLogin            = 1,
        LoginResponse           = 2,
        Heartbeat               = 3,
        Disconnect              = 4,

        ExamPhaseChange         = 20,
        ExamStatusUpdate        = 21,
        CheatingAlert           = 22,
        FileTransferStart       = 30,
        FileChunk               = 31,
        FileTransferComplete    = 32,
        ExtractArchive          = 33,
        ExamSubmitRequest       = 34,
        ProcessListUpdate       = 40,
        ForceProcessKill        = 41,
        ShutdownPC              = 42,
        QuizQuestion            = 50,
        QuizAnswer              = 51,
        QuizResult              = 52,

        // 채팅
        ChatBroadcast           = 60,  // 교수 → 전체 학생
        ChatDirect              = 61,  // 교수 → 특정 학생
        ChatFromStudent         = 62,  // 학생 → 교수

        CommandAck              = 100,
    }

    public enum ExamPhase : uint
    {
        Waiting         = 0,
        Ready           = 1,
        InProgress      = 2,
        SubmitRequested = 3,
        Closed          = 4,
    }

    public enum StudentStatus : uint
    {
        NotConnected     = 0,
        Connected        = 1,
        FileReceived     = 2,
        InProgress       = 3,
        Submitted        = 4,
        Approved         = 5,
        CheatingDetected = 6,
        Absent           = 7,
    }

    // 부정행위 유형 (Protocol.h의 CheatingAlertType enum과 동일한 값)
    public enum CheatingAlertType : uint
    {
        BlacklistedProcessLaunched = 0,
        NetworkAccessAttempt       = 1,
        UnauthorizedProcess        = 2,
        ManualReport               = 3,
        RequiredProcessTerminated  = 4,   // 시험에 필요한 프로그램을 학생이 강제 종료함
    }

    public enum DisconnectReason : int
    {
        ClientDisconnected = 0,
        HeartbeatTimeout   = 1,
        NetworkError       = 2,
        ServerShutdown     = 3,
    }

    // ══════════════════════════════════════════════════════════════════
    //  ProcessListUpdate(40) 페이로드 — 교수가 배포하는 프로세스 감시 목록
    //
    //  목록 개수가 정해져 있지 않아 구조체로 못 만든다. Protocol.h에 정의된 형식:
    //    [uint32 화이트리스트 개수][uint32 블랙리스트 개수]
    //    화이트리스트 문자열들: 각 [uint16 길이][길이만큼의 UTF-8 바이트]
    //    블랙리스트 문자열들:   각 [uint16 길이][길이만큼의 UTF-8 바이트]
    //
    //  보내는 쪽(교수)과 읽는 쪽(학생)이 형식을 따로 구현하면 어긋나기 쉬우므로,
    //  두 프로젝트가 공유하는 이 파일에 한 벌만 둔다.
    // ══════════════════════════════════════════════════════════════════
    public static class ProcessListPayload
    {
        public static byte[] Encode(IReadOnlyList<string> whitelist, IReadOnlyList<string> blacklist)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            writer.Write((uint)whitelist.Count);
            writer.Write((uint)blacklist.Count);

            foreach (string name in whitelist) WriteString(writer, name);
            foreach (string name in blacklist) WriteString(writer, name);

            writer.Flush();
            return stream.ToArray();
        }

        // 네이티브 버퍼를 해석한다. 형식이 깨져 있으면 false를 돌려준다.
        // (실패했을 때 목록을 반쯤 채운 채로 쓰면 감시가 헐거워지므로 호출한 쪽에서 통째로 버려야 한다)
        public static bool TryDecode(IntPtr payload, uint payloadLen,
                                     out List<string> whitelist, out List<string> blacklist)
        {
            whitelist = new List<string>();
            blacklist = new List<string>();

            // 최소한 개수 두 개(4+4바이트)는 있어야 한다
            if (payload == IntPtr.Zero || payloadLen < 8) return false;

            byte[] buffer = new byte[payloadLen];
            Marshal.Copy(payload, buffer, 0, (int)payloadLen);

            int offset = 0;
            uint whiteCount = BitConverter.ToUInt32(buffer, offset); offset += 4;
            uint blackCount = BitConverter.ToUInt32(buffer, offset); offset += 4;

            return TryReadStrings(buffer, ref offset, whiteCount, whitelist)
                && TryReadStrings(buffer, ref offset, blackCount, blacklist);
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(value);
            writer.Write((ushort)utf8.Length);
            writer.Write(utf8);
        }

        // 읽기 전에 매번 남은 크기를 확인한다.
        // 길이 값을 그대로 믿으면 손상된 패킷 하나로 버퍼 밖을 읽게 된다.
        private static bool TryReadStrings(byte[] buffer, ref int offset, uint count, List<string> target)
        {
            for (uint i = 0; i < count; i++)
            {
                if (offset + 2 > buffer.Length) return false;
                int length = BitConverter.ToUInt16(buffer, offset);
                offset += 2;

                if (offset + length > buffer.Length) return false;
                target.Add(Encoding.UTF8.GetString(buffer, offset, length));
                offset += length;
            }
            return true;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  ExamPhaseChange(20) 페이로드 — 교수가 알리는 시험 진행 단계
    //
    //  Protocol.h의 ExamPhaseChangePayload와 같은 형식(고정 260바이트):
    //    [uint32 phase][char message[256]]
    //
    //  ProcessListPayload와 같은 이유로 여기에 한 벌만 둔다.
    //  보내는 쪽(교수)과 읽는 쪽(학생)이 형식을 따로 구현하면 어긋나기 쉽다.
    // ══════════════════════════════════════════════════════════════════
    public static class ExamPhasePayload
    {
        private const int MessageOffset = 4;
        private const int MessageSize   = 256;
        public  const int Size          = MessageOffset + MessageSize;   // 260

        public static byte[] Encode(ExamPhase phase, string message)
        {
            byte[] payload = new byte[Size];
            BitConverter.GetBytes((uint)phase).CopyTo(payload, 0);

            // 메시지가 길면 잘라 담는다. 마지막 1바이트는 문자열 끝 표시로 남겨 둔다.
            byte[] text = Encoding.UTF8.GetBytes(message);
            Array.Copy(text, 0, payload, MessageOffset, Math.Min(text.Length, MessageSize - 1));

            return payload;
        }

        // 크기가 모자라거나 모르는 단계 값이면 통째로 버린다.
        // 단계를 잘못 읽으면 시험 도중에 감시가 꺼질 수 있어, 의심스러우면 무시하는 편이 안전하다.
        public static bool TryDecode(IntPtr payload, uint payloadLen, out ExamPhase phase)
        {
            phase = ExamPhase.Waiting;
            if (payload == IntPtr.Zero || payloadLen < Size) return false;

            uint value = (uint)Marshal.ReadInt32(payload);
            if (!Enum.IsDefined(typeof(ExamPhase), value)) return false;

            phase = (ExamPhase)value;
            return true;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  ExamSubmitRequest(34) 페이로드 — 교수가 보내는 답안 제출 요청
    //
    //  Protocol.h의 ExamSubmitRequestPayload와 같은 형식(고정 396바이트):
    //    [char folderName[260]][char archivePassword[128]][int64 deadline]
    //
    //  folderName은 비워 보낸다. 학생마다 시험 폴더 경로가 다를 수 있어
    //  교수가 지정할 수 없고, 학생이 자기 해제 폴더를 압축하면 되기 때문이다.
    //  deadline(마감시각)은 아직 쓰지 않아 0으로 보낸다.
    // ══════════════════════════════════════════════════════════════════
    public static class ExamSubmitPayload
    {
        private const int FolderNameSize = 260;
        private const int PasswordSize   = 128;
        public  const int Size           = FolderNameSize + PasswordSize + 8;   // 396

        public static byte[] Encode(string folderName, string archivePassword, long deadline = 0)
        {
            byte[] payload = new byte[Size];
            WriteFixedString(payload, 0,               folderName,      FolderNameSize);
            WriteFixedString(payload, FolderNameSize,  archivePassword, PasswordSize);
            BitConverter.GetBytes(deadline).CopyTo(payload, FolderNameSize + PasswordSize);
            return payload;
        }

        public static bool TryDecode(IntPtr payload, uint payloadLen,
                                     out string folderName, out string archivePassword, out long deadline)
        {
            folderName = "";
            archivePassword = "";
            deadline = 0;
            if (payload == IntPtr.Zero || payloadLen < Size) return false;

            byte[] buffer = new byte[Size];
            Marshal.Copy(payload, buffer, 0, Size);

            folderName      = ReadFixedString(buffer, 0,              FolderNameSize);
            archivePassword = ReadFixedString(buffer, FolderNameSize, PasswordSize);
            deadline        = BitConverter.ToInt64(buffer, FolderNameSize + PasswordSize);
            return true;
        }

        // 고정 칸에 UTF-8로 채워 넣는다. 남는 칸은 0으로 남아 문자열 끝 표시가 된다.
        internal static void WriteFixedString(byte[] target, int offset, string value, int size)
        {
            byte[] text = Encoding.UTF8.GetBytes(value);
            Array.Copy(text, 0, target, offset, Math.Min(text.Length, size - 1));
        }

        // 고정 칸에서 0(문자열 끝)이 나올 때까지만 읽는다.
        internal static string ReadFixedString(byte[] buffer, int offset, int size)
        {
            int length = 0;
            while (length < size && buffer[offset + length] != 0) length++;
            return Encoding.UTF8.GetString(buffer, offset, length);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  CommandAck(100) 페이로드 — 명령을 잘 처리했는지 알려주는 회신
    //
    //  Protocol.h의 CommandAckPayload와 같은 형식(고정 261바이트):
    //    [uint32 commandType][uint8 success][char message[256]]
    //
    //  답안 제출에서 이 회신이 안전장치 역할을 한다.
    //  학생은 success=1을 받은 뒤에만 시험 파일을 지운다.
    // ══════════════════════════════════════════════════════════════════
    public static class CommandAckPayload
    {
        private const int SuccessOffset = 4;
        private const int MessageOffset = 5;
        private const int MessageSize   = 256;
        public  const int Size          = MessageOffset + MessageSize;   // 261

        public static byte[] Encode(PacketType commandType, bool success, string message)
        {
            byte[] payload = new byte[Size];
            BitConverter.GetBytes((uint)commandType).CopyTo(payload, 0);
            payload[SuccessOffset] = success ? (byte)1 : (byte)0;
            ExamSubmitPayload.WriteFixedString(payload, MessageOffset, message, MessageSize);
            return payload;
        }

        public static bool TryDecode(IntPtr payload, uint payloadLen,
                                     out PacketType commandType, out bool success, out string message)
        {
            commandType = default;
            success = false;
            message = "";
            if (payload == IntPtr.Zero || payloadLen < Size) return false;

            byte[] buffer = new byte[Size];
            Marshal.Copy(payload, buffer, 0, Size);

            commandType = (PacketType)BitConverter.ToUInt32(buffer, 0);
            success     = buffer[SuccessOffset] == 1;
            message     = ExamSubmitPayload.ReadFixedString(buffer, MessageOffset, MessageSize);
            return true;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  콜백 델리게이트 (C++ 함수 포인터와 매핑)
    //  반드시 멤버 변수로 보관해서 GC 수집을 막아야 합니다.
    // ══════════════════════════════════════════════════════════════════
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void StudentConnectedCallback(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sessionId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string studentId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string studentName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string remoteAddr);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void StudentDisconnectedCallback(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sessionId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string studentId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string studentName,
        DisconnectReason reason);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void PacketReceivedCallback(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sessionId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string studentId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string studentName,
        PacketType packetType,
        IntPtr payload,
        uint payloadLen);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void FileReceivedCallback(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string transferId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string senderId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string fileName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string tempPath,
        long fileSize,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string archivePassword);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void FileProgressCallback(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string transferId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string fileName,
        int percent);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void FileErrorCallback(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string transferId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string message);

    // 교수 → 학생 파일 전송 진행률/오류. 어느 학생에게 보내는 중인지 함께 전달된다.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void SendProgressCallback(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sessionId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string studentId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string transferId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string fileName,
        int percent);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void SendErrorCallback(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sessionId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string studentId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string transferId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string message);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ClientConnectedCallback(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string serverIp,
        int port);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ClientDisconnectedCallback(DisconnectReason reason);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ClientPacketReceivedCallback(
        PacketType packetType,
        IntPtr payload,
        uint payloadLen);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void NetworkErrorCallback(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string message);

    // ══════════════════════════════════════════════════════════════════
    //  NativeNetwork — DllImport 선언 모음
    // ══════════════════════════════════════════════════════════════════
    internal static class NativeNetwork
    {
        private const string DLL = "NetworkLib_Native.dll";

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern int  NL_Initialize();
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern void NL_Cleanup();

        // 서버
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern int  NL_Server_Create(int port);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern int  NL_Server_Start();
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern void NL_Server_Stop();
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern int  NL_Server_GetConnectedCount();
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern void NL_Server_SetOnStudentConnected   (StudentConnectedCallback    cb);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern void NL_Server_SetOnStudentDisconnected(StudentDisconnectedCallback cb);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern void NL_Server_SetOnPacketReceived     (PacketReceivedCallback      cb);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern void NL_Server_SetOnFileReceived       (FileReceivedCallback        cb);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern void NL_Server_SetOnFileProgress       (FileProgressCallback        cb);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern void NL_Server_SetOnFileError          (FileErrorCallback           cb);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern void NL_Server_SetOnSendProgress      (SendProgressCallback        cb);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern void NL_Server_SetOnSendError         (SendErrorCallback           cb);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern int  NL_Server_Broadcast        (PacketType type, byte[] payload, uint payloadLen);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern int  NL_Server_SendToSession    ([MarshalAs(UnmanagedType.LPUTF8Str)] string sessionId, PacketType type, byte[] payload, uint payloadLen);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern int  NL_Server_BroadcastFile    ([MarshalAs(UnmanagedType.LPUTF8Str)] string filePath, [MarshalAs(UnmanagedType.LPUTF8Str)] string archivePassword);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern int  NL_Server_SendFileToSession([MarshalAs(UnmanagedType.LPUTF8Str)] string sessionId, [MarshalAs(UnmanagedType.LPUTF8Str)] string filePath, [MarshalAs(UnmanagedType.LPUTF8Str)] string archivePassword);
        // 채팅
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern int  NL_Server_BroadcastChat    ([MarshalAs(UnmanagedType.LPUTF8Str)] string message);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern int  NL_Server_SendChatToSession([MarshalAs(UnmanagedType.LPUTF8Str)] string sessionId, [MarshalAs(UnmanagedType.LPUTF8Str)] string message);

        // 클라이언트
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern int  NL_Client_Create();
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern int  NL_Client_Connect    ([MarshalAs(UnmanagedType.LPUTF8Str)] string serverIp, int port);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern void NL_Client_Disconnect ();
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern int  NL_Client_IsConnected();
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern void NL_Client_SetOnConnected      (ClientConnectedCallback      cb);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern void NL_Client_SetOnDisconnected   (ClientDisconnectedCallback   cb);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern void NL_Client_SetOnPacketReceived (ClientPacketReceivedCallback cb);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern void NL_Client_SetOnFileReceived   (FileReceivedCallback         cb);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern void NL_Client_SetOnFileProgress   (FileProgressCallback         cb);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern void NL_Client_SetOnFileError      (FileErrorCallback            cb);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern void NL_Client_SetOnError          (NetworkErrorCallback         cb);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern int  NL_Client_SendPacket(PacketType type, byte[] payload, uint payloadLen);
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern int  NL_Client_SendFile  ([MarshalAs(UnmanagedType.LPUTF8Str)] string filePath, [MarshalAs(UnmanagedType.LPUTF8Str)] string archivePassword);
        // 채팅
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)] public static extern int  NL_Client_SendChat  ([MarshalAs(UnmanagedType.LPUTF8Str)] string message);
    }

    // ══════════════════════════════════════════════════════════════════
    //  PayloadHelper — 페이로드 바이트 배열 빌더 (구조체 → byte[])
    //  UI 팀이 패킷 데이터를 만들 때 사용합니다.
    // ══════════════════════════════════════════════════════════════════
    public static class PayloadHelper
    {
        /// <summary>구조체를 byte[]로 직렬화합니다.</summary>
        public static byte[] ToBytes<T>(T structure) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] arr = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(structure, ptr, false);
                Marshal.Copy(ptr, arr, 0, size);
            }
            finally { Marshal.FreeHGlobal(ptr); }
            return arr;
        }

        /// <summary>byte[]를 구조체로 역직렬화합니다.</summary>
        public static T FromBytes<T>(IntPtr payloadPtr, uint payloadLen) where T : struct
        {
            return Marshal.PtrToStructure<T>(payloadPtr);
        }

        /// <summary>UTF-8 문자열을 고정 크기 바이트 배열에 복사합니다.</summary>
        public static void CopyString(byte[] dest, string src, int maxBytes)
        {
            byte[] srcBytes = Encoding.UTF8.GetBytes(src);
            int count = Math.Min(srcBytes.Length, maxBytes - 1);
            Array.Copy(srcBytes, dest, count);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  ProfessorServer — UI팀이 사용하는 서버 래퍼 클래스
    // ══════════════════════════════════════════════════════════════════
    public class ProfessorServer : IDisposable
    {
        // 콜백 델리게이트를 GC 방지용으로 보관
        private StudentConnectedCallback?    _onConnected;
        private StudentDisconnectedCallback? _onDisconnected;
        private PacketReceivedCallback?      _onPacket;
        private FileReceivedCallback?        _onFileReceived;
        private FileProgressCallback?        _onFileProgress;
        private FileErrorCallback?           _onFileError;
        private SendProgressCallback?        _onSendProgress;
        private SendErrorCallback?           _onSendError;

        public event Action<string, string, string, string>?        StudentConnected;
        public event Action<string, string, string, DisconnectReason>? StudentDisconnected;
        public event Action<string, string, string, PacketType, IntPtr, uint>? PacketReceived;
        public event Action<string, string, string, string, long, string>?    FileReceived;
        public event Action<string, string, int>?                   FileProgress;
        public event Action<string, string>?                        FileError;
        /// <summary>교수 → 학생 전송 진행률 (sessionId, studentId, transferId, fileName, percent)</summary>
        public event Action<string, string, string, string, int>?   SendProgress;
        /// <summary>교수 → 학생 전송 오류 (sessionId, studentId, transferId, message)</summary>
        public event Action<string, string, string, string>?        SendError;

        public ProfessorServer(int port = 9000)
        {
            NativeNetwork.NL_Server_Create(port);

            _onConnected    = (sid, stid, name, ip)     => StudentConnected?.Invoke(sid, stid, name, ip);
            _onDisconnected = (sid, stid, name, reason) => StudentDisconnected?.Invoke(sid, stid, name, reason);
        // ── 임시 수신 확인 코드 (테스트용 — UI 완성 후 제거) ─────────────
        _onPacket = (sid, stid, name, t, p, l) =>
        {
            if (t == PacketType.ChatFromStudent)
            {
                string msg = Marshal.PtrToStringUTF8(p) ?? "(빈 메시지)";
                System.Diagnostics.Debug.WriteLine(
                    $"[채팅 수신-교수] {stid}({name}): {msg}");
            }
            PacketReceived?.Invoke(sid, stid, name, t, p, l);
        };
            _onFileReceived = (tid, senderId, fn, tp, sz, pw) => FileReceived?.Invoke(tid, senderId, fn, tp, sz, pw);
            _onFileProgress = (tid, fn, pct) => FileProgress?.Invoke(tid, fn, pct);
            _onFileError    = (tid, msg)     => FileError?.Invoke(tid, msg);
            _onSendProgress = (sid, stid, tid, fn, pct) => SendProgress?.Invoke(sid, stid, tid, fn, pct);
            _onSendError    = (sid, stid, tid, msg)     => SendError?.Invoke(sid, stid, tid, msg);

            NativeNetwork.NL_Server_SetOnStudentConnected   (_onConnected);
            NativeNetwork.NL_Server_SetOnStudentDisconnected(_onDisconnected);
            NativeNetwork.NL_Server_SetOnPacketReceived     (_onPacket);
            NativeNetwork.NL_Server_SetOnFileReceived       (_onFileReceived);
            NativeNetwork.NL_Server_SetOnFileProgress       (_onFileProgress);
            NativeNetwork.NL_Server_SetOnFileError          (_onFileError);
            NativeNetwork.NL_Server_SetOnSendProgress       (_onSendProgress);
            NativeNetwork.NL_Server_SetOnSendError          (_onSendError);
        }

        public bool Start()                => NativeNetwork.NL_Server_Start() == 1;
        public void Stop()                 => NativeNetwork.NL_Server_Stop();
        public int  GetConnectedCount()    => NativeNetwork.NL_Server_GetConnectedCount();

        public void Broadcast(PacketType type, byte[] payload)
            => NativeNetwork.NL_Server_Broadcast(type, payload, (uint)(payload?.Length ?? 0));

        public void SendToSession(string sessionId, PacketType type, byte[] payload)
            => NativeNetwork.NL_Server_SendToSession(sessionId, type, payload, (uint)(payload?.Length ?? 0));

        public void BroadcastFile(string filePath, string archivePassword = "")
            => NativeNetwork.NL_Server_BroadcastFile(filePath, archivePassword);

        /// <summary>전송을 시작했으면 true. 해당 세션이 없으면 false.</summary>
        public bool SendFileToSession(string sessionId, string filePath, string archivePassword = "")
            => NativeNetwork.NL_Server_SendFileToSession(sessionId, filePath, archivePassword) == 1;

        // ── 채팅 전송 ────────────────────────────────────────────────────
        /// <summary>전체 학생에게 채팅 메시지를 전송합니다.</summary>
        public bool BroadcastChat(string message)
            => NativeNetwork.NL_Server_BroadcastChat(message) == 1;

        /// <summary>특정 학생에게 채팅 메시지를 전송합니다.</summary>
        public bool SendChatToSession(string sessionId, string message)
            => NativeNetwork.NL_Server_SendChatToSession(sessionId, message) == 1;

        public void Dispose() => Stop();
    }

    // ══════════════════════════════════════════════════════════════════
    //  StudentClient — UI팀이 사용하는 클라이언트 래퍼 클래스
    // ══════════════════════════════════════════════════════════════════
    public class StudentClient : IDisposable
    {
        private ClientConnectedCallback?      _onConnected;
        private ClientDisconnectedCallback?   _onDisconnected;
        private ClientPacketReceivedCallback? _onPacket;
        private FileReceivedCallback?         _onFileReceived;
        private FileProgressCallback?         _onFileProgress;
        private FileErrorCallback?            _onFileError;
        private NetworkErrorCallback?         _onError;

        public event Action<string, int>?               Connected;
        public event Action<DisconnectReason>?          Disconnected;
        public event Action<PacketType, IntPtr, uint>?  PacketReceived;
        public event Action<string, string, string, string, long, string>? FileReceived;
        public event Action<string, string, int>?       FileProgress;
        public event Action<string, string>?            FileError;
        public event Action<string>?                    Error;

        public StudentClient()
        {
            NativeNetwork.NL_Client_Create();

            _onConnected    = (ip, port) => Connected?.Invoke(ip, port);
            _onDisconnected = reason     => Disconnected?.Invoke(reason);
            // ── 임시 수신 확인 코드 (테스트용 — UI 완성 후 제거) ─────────────
            _onPacket = (t, p, l) =>
            {
                if (t == PacketType.ChatBroadcast || t == PacketType.ChatDirect)
                {
                    string msg = Marshal.PtrToStringUTF8(p) ?? "(빈 메시지)";
                    System.Diagnostics.Debug.WriteLine(
                        $"[채팅 수신-학생] ({t}): {msg}");
                }
                PacketReceived?.Invoke(t, p, l);
            };
            _onFileReceived = (tid, sid, fn, tp, sz, pw) => FileReceived?.Invoke(tid, sid, fn, tp, sz, pw);
            _onFileProgress = (tid, fn, pct) => FileProgress?.Invoke(tid, fn, pct);
            _onFileError    = (tid, msg)     => FileError?.Invoke(tid, msg);
            _onError        = msg            => Error?.Invoke(msg);

            NativeNetwork.NL_Client_SetOnConnected      (_onConnected);
            NativeNetwork.NL_Client_SetOnDisconnected   (_onDisconnected);
            NativeNetwork.NL_Client_SetOnPacketReceived (_onPacket);
            NativeNetwork.NL_Client_SetOnFileReceived   (_onFileReceived);
            NativeNetwork.NL_Client_SetOnFileProgress   (_onFileProgress);
            NativeNetwork.NL_Client_SetOnFileError      (_onFileError);
            NativeNetwork.NL_Client_SetOnError          (_onError);
        }

        public bool Connect   (string serverIp, int port = 9000) => NativeNetwork.NL_Client_Connect(serverIp, port) == 1;
        public void Disconnect()                                  => NativeNetwork.NL_Client_Disconnect();
        public bool IsConnected                                   => NativeNetwork.NL_Client_IsConnected() == 1;

        public void SendPacket(PacketType type, byte[]? payload = null)
            => NativeNetwork.NL_Client_SendPacket(type, payload!, (uint)(payload?.Length ?? 0));

        public void SendFile(string filePath, string archivePassword = "")
            => NativeNetwork.NL_Client_SendFile(filePath, archivePassword);

        // ── 채팅 전송 ────────────────────────────────────────────────────
        /// <summary>교수에게 채팅 메시지를 전송합니다.</summary>
        public bool SendChat(string message)
            => NativeNetwork.NL_Client_SendChat(message) == 1;

        public void Dispose() => Disconnect();
    }

    // ══════════════════════════════════════════════════════════════════
    //  NetworkLibrary — 앱 시작/종료 시 한 번씩 호출
    // ══════════════════════════════════════════════════════════════════
    public static class NetworkLibrary
    {
        public static bool Initialize() => NativeNetwork.NL_Initialize() == 1;
        public static void Cleanup()    => NativeNetwork.NL_Cleanup();
    }
}
