using NetworkLib;
using StudentUI.Model;
using StudentUI.Service;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace StudentUI.ViewModel
{
    public class SharedChatViewModel : INotifyPropertyChanged
    {
        public static SharedChatViewModel Instance { get; } = new SharedChatViewModel();

        private ObservableCollection<ChatMessageModel> _messages;
        public ObservableCollection<ChatMessageModel> Messages
        {
            get => _messages;
            set { _messages = value; OnPropertyChanged(); }
        }

        public bool HasMessages => Messages != null && Messages.Count > 0;
        public bool HasNoMessages => Messages == null || Messages.Count == 0;

        private int _unreadCount;
        public int UnreadCount
        {
            get => _unreadCount;
            set
            {
                _unreadCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasUnreadMessages));
            }
        }

        public bool HasUnreadMessages => _unreadCount > 0;

        public string LastSender { get; private set; } = string.Empty;
        public string LastMessage { get; private set; } = string.Empty;

        public void MarkAsRead()
        {
            UnreadCount = 0;
            SendReadReceipts();
        }

        // 아직 읽었다고 알리지 않은 교수 메시지의 번호. 화면 스레드에서만 건드린다.
        private readonly List<uint> _unreadChatIds = new();

        // 채팅창을 열어 본 시점에 그때까지 쌓인 것을 한꺼번에 돌려보낸다.
        // 교수 화면의 말풍선이 이 회신을 받아 '읽음'으로 바뀐다.
        //
        // 도착한 순간이 아니라 열어 본 순간에 보내는 것이 중요하다 —
        // 도착만으로 읽음이라고 하면 교수는 학생이 본 줄 알고 넘어가게 된다.
        private void SendReadReceipts()
        {
            if (_unreadChatIds.Count == 0) return;

            foreach (uint id in _unreadChatIds)
                NetworkService.Instance.SendPacket(PacketType.CommandAck,
                    CommandAckPayload.Encode(PacketType.ChatDirect, true, id.ToString()));

            _unreadChatIds.Clear();
        }

        private string _inputMessage = string.Empty;
        public string InputMessage
        {
            get => _inputMessage;
            set { _inputMessage = value; OnPropertyChanged(); }
        }

        public ICommand SendMessageCommand { get; }

        // 교수의 채팅·공지가 도착했을 때. 화면이 채팅 버튼을 깜빡이는 데 쓴다. 화면 스레드에서 올라온다.
        public event Action? MessageArrived;

        // 교수가 보낸 전체 공지. 화면 스레드에서 알리며, 받는 쪽(App)이 팝업을 띄운다.
        public event Action<string>? NoticeArrived;

        // 교수가 등록한 중요 공지. 시험 화면 상단 가운데에 교수가 내릴 때까지 계속 보인다. 비어 있으면 없는 것이다.
        private string _importantNotice = string.Empty;
        public string ImportantNotice
        {
            get => _importantNotice;
            private set
            {
                _importantNotice = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasImportantNotice));
            }
        }

        public bool HasImportantNotice => _importantNotice.Length > 0;

        private SharedChatViewModel()
        {
            _messages = new ObservableCollection<ChatMessageModel>();
            _messages.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(HasMessages));
                OnPropertyChanged(nameof(HasNoMessages));
            };
            SendMessageCommand = new RelayCommand(SendMessage);

            // 네트워크 수신 이벤트 구독
            NetworkService.Instance.PacketReceived += OnPacketReceived;
        }

        // 로그아웃할 때 비운다. 이 객체는 앱이 켜져 있는 동안 하나뿐이라,
        // 비우지 않으면 다시 로그인한 뒤에도 앞 로그인의 채팅이 그대로 보인다.
        public void Clear()
        {
            Messages.Clear();
            InputMessage = string.Empty;
            UnreadCount = 0;
            _unreadChatIds.Clear();
            LastSender = string.Empty;
            LastMessage = string.Empty;
            ImportantNotice = string.Empty;
        }

        private void SendMessage()
        {
            if (string.IsNullOrWhiteSpace(InputMessage)) return;

            string msgToSend = InputMessage;
            InputMessage = string.Empty; // 비우기

            // UI에 내 메시지 추가
            var myMsg = new ChatMessageModel
            {
                SenderName = "나",
                Message = msgToSend,
                Timestamp = DateTime.Now,
                IsMine = true
            };
            Messages.Add(myMsg);

            // 네트워크 전송 (StudentClient는 SendChat 메서드가 있으므로 이를 사용)
            NetworkService.Instance.SendChat(msgToSend);
        }

        private void OnPacketReceived(PacketType type, IntPtr payload, uint payloadLen)
        {
            if (type == PacketType.ImportantNotice)
            {
                // 수신 버퍼는 이 콜백이 끝나면 사라지므로 화면 스레드로 넘기기 전에 읽어 둔다.
                OnImportantNotice(ImportantNoticePayload.Decode(payload, payloadLen));
                return;
            }

            if (type != PacketType.ChatBroadcast && type != PacketType.ChatDirect) return;

            // 페이로드 길이로 읽기를 제한한다 (종료 문자가 없는 패킷이 와도 버퍼 밖을 읽지 않도록)
            if (payload == IntPtr.Zero || payloadLen == 0) return;
            string message = (Marshal.PtrToStringUTF8(payload, (int)payloadLen) ?? "").Split('\0')[0];
            bool isNotice = type == PacketType.ChatBroadcast;

            // 공지를 받았다고 교수에게 알린다. 교수 화면의 '수신 N명'이 이 회신으로 올라간다.
            // 받은 것 자체를 세는 것이라 화면에 띄울 때까지 기다리지 않는다.
            if (isNotice && NoticePayload.TryReadId(payload, payloadLen, out uint noticeId))
                NetworkService.Instance.SendPacket(PacketType.CommandAck,
                    CommandAckPayload.Encode(PacketType.ChatBroadcast, true, noticeId.ToString()));

            // 1:1 메시지에 붙은 번호. 읽었다고 알릴 때 쓴다.
            // 수신 버퍼는 이 콜백이 끝나면 사라지므로 화면 스레드로 넘기기 전에 읽어 둔다.
            uint chatId = 0;
            bool hasChatId = !isNotice && ChatMessagePayload.TryReadId(payload, payloadLen, out chatId);

            // 콜백은 네이티브 스레드에서 올라오므로 UI 스레드로 넘겨 처리한다.
            // 동기 Invoke는 종료 중 수신 스레드를 붙잡아 앱이 멈추므로 BeginInvoke를 쓴다.
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            dispatcher.BeginInvoke(() =>
            {
                // 전체 공지는 채팅에 쌓지 않고 팝업으로 띄운다 — 채팅창을 열어 보지 않아도 읽게 된다.
                if (isNotice)
                {
                    NoticeArrived?.Invoke(message);
                    ExamManager.Shared.UiSignal.FlashTaskbar();
                    return;
                }

                Messages.Add(new ChatMessageModel
                {
                    SenderName = "[교수님]",
                    Message = message,
                    Timestamp = DateTime.Now,
                    IsMine = false
                });

                LastSender = "[교수님]";
                LastMessage = message;
                UnreadCount++;

                // 아직 열어 보지 않았으므로 회신은 미뤄 둔다. MarkAsRead 가 한꺼번에 보낸다.
                if (hasChatId) _unreadChatIds.Add(chatId);

                // 창을 내려 두었거나 다른 창을 보고 있어도 알 수 있게 한다.
                MessageArrived?.Invoke();
                ExamManager.Shared.UiSignal.FlashTaskbar();
            });
        }

        // 교수가 중요 공지를 올리거나 바꾸거나 내렸다. 네이티브 스레드에서 올라온다.
        // 접속할 때마다 지금 공지가 다시 오므로 같은 공지면 그냥 넘긴다.
        // 시험 중에는 편집기를 보고 있어 상단 문구만 바뀌면 모르고 지나가므로, 새 공지면 작업표시줄을 깜빡인다.
        private void OnImportantNotice(string notice)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            dispatcher.BeginInvoke(() =>
            {
                if (notice == ImportantNotice) return;

                ImportantNotice = notice;
                if (notice.Length > 0) ExamManager.Shared.UiSignal.FlashTaskbar();
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
