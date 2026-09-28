using System;
using System.Runtime.InteropServices;
using NetworkLib;

namespace ProfessorUI.Service
{
    // 교수와 학생 사이의 채팅·공지를 주고받는다.
    //   대화 탭·안 읽음·목록 표시  → ChatViewModel
    //   패킷을 보내고 받아 해석하는 것 → 여기
    //
    // 받은 것은 네이티브 수신 스레드에서 그대로 알린다. 화면 스레드로 넘기는 것은 뷰모델이 한다.
    public class ChatService
    {
        public static ChatService Instance { get; } = new ChatService();

        // 학생이 보낸 메시지 (세션, 학번, 이름, 내용)
        public event Action<string, string, string, string>? MessageReceived;

        // 학생이 공지를 받았다는 회신 (공지 번호, 학번)
        public event Action<uint, string>? NoticeAcknowledged;

        // 학생이 1:1 메시지를 읽었다는 회신 (메시지 번호, 학번)
        public event Action<uint, string>? ChatRead;

        // 공지 번호. 학생의 수신 회신이 어느 공지에 대한 것인지 가르는 데 쓴다.
        private uint _lastNoticeId;

        // 1:1 메시지 번호. 읽음 회신이 어느 메시지에 대한 것인지 가른다.
        private uint _lastChatId;

        private ChatService()
        {
            NetworkService.Instance.PacketReceived += OnPacketReceived;
        }

        // 전체 학생에게 채팅을 보낸다(전체 공지 탭에서 말할 때).
        public void SendToAll(string message) => NetworkService.Instance.BroadcastChat(message);

        // 학생 한 명에게 보내고 그 메시지의 번호를 돌려준다.
        // 번호를 붙여야 학생이 읽었을 때 어느 말풍선을 '읽음'으로 바꿀지 알 수 있다.
        public uint SendTo(string sessionId, string message)
        {
            uint id = ++_lastChatId;
            NetworkService.Instance.SendToSession(
                sessionId, PacketType.ChatDirect, ChatMessagePayload.Encode(id, message));
            return id;
        }

        // 전체 공지를 보내고 그 공지의 번호를 돌려준다.
        // 번호를 붙여야 누가 받았는지 회신으로 셀 수 있다(NoticePayload 참고).
        public uint SendNotice(string text)
        {
            uint id = ++_lastNoticeId;
            NetworkService.Instance.Broadcast(PacketType.ChatBroadcast, NoticePayload.Encode(id, text));
            return id;
        }

        private void OnPacketReceived(string sessionId, string studentId, string studentName,
                                      PacketType type, IntPtr payload, uint payloadLen)
        {
            if (type == PacketType.ChatFromStudent)
            {
                // 페이로드 길이로 읽기를 제한한다 (종료 문자가 없는 패킷이 와도 버퍼 밖을 읽지 않도록)
                if (payload == IntPtr.Zero || payloadLen == 0) return;
                string message = (Marshal.PtrToStringUTF8(payload, (int)payloadLen) ?? "").Split('\0')[0];

                MessageReceived?.Invoke(sessionId, studentId, studentName, message);
            }
            else if (type == PacketType.CommandAck)
            {
                // 채팅 회신 두 가지를 여기서 다룬다. 회신에 적힌 번호로 어느 것인지 찾는다.
                //   ChatBroadcast — 공지를 받았다 (학생 PC 에 도착한 순간)
                //   ChatDirect    — 1:1 메시지를 읽었다 (학생이 채팅창을 열어 본 순간)
                if (!CommandAckPayload.TryDecode(payload, payloadLen, out PacketType command, out _, out string message) ||
                    !uint.TryParse(message, out uint id))
                    return;

                if (command == PacketType.ChatBroadcast) NoticeAcknowledged?.Invoke(id, studentId);
                else if (command == PacketType.ChatDirect) ChatRead?.Invoke(id, studentId);
            }
        }
    }
}
