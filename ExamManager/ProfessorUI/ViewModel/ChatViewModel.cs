using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using NetworkLib;
using ProfessorUI.Service;

namespace ProfessorUI.ViewModel
{
    public class ChatViewModel : RightPanelViewMondel
    {
        public string Title => "채팅창";

        private ObservableCollection<ChatTabItem> _tabs;
        private ChatTabItem? _selectedTab;
        private string _inputMessage = string.Empty;

        public ObservableCollection<ChatTabItem> Tabs
        {
            get => _tabs;
            set { _tabs = value; OnPropertyChanged(); }
        }

        public ChatTabItem? SelectedTab
        {
            get => _selectedTab;
            set
            {
                _selectedTab = value;
                if (_selectedTab != null)
                {
                    _selectedTab.UnreadCount = 0; // 탭을 선택하면 안 읽음 뱃지 초기화
                }
                OnPropertyChanged();
            }
        }

        public string InputMessage
        {
            get => _inputMessage;
            set { _inputMessage = value; OnPropertyChanged(); }
        }

        public ICommand SendMessageCommand { get; }

        // 전체 공지 입력. 1:1 대화 입력(InputMessage)과 따로 둔다.
        // 하나를 같이 쓰면, 알림·채팅 화면에서 학생과 대화하는 동안 공지 칸에 적은 글이 그 학생에게만 간다.
        private string _noticeInput = string.Empty;
        public string NoticeInput
        {
            get => _noticeInput;
            set { _noticeInput = value; OnPropertyChanged(); }
        }

        public ICommand SendNoticeCommand { get; }

        // 보낸 전체 공지. 최근 것이 위로 오도록 앞에 넣는다.
        public ObservableCollection<NoticeItem> Notices { get; } = new();
        public NoticeItem? LatestNotice => Notices.FirstOrDefault();

        // 공지 번호. 학생의 수신 회신이 어느 공지에 대한 것인지 가르는 데 쓴다.
        private uint _lastNoticeId;

        // 탭과 무관하게 모아 둔 학생 수신 메시지.
        // 셸은 여기에 새 메시지가 들어오는 것만 보고 작업표시줄을 깜빡인다.
        public ObservableCollection<ChatMessageModel> RecentMessages { get; } = new();

        private int _unreadCount;
        public int UnreadCount
        {
            get => _unreadCount;
            private set { _unreadCount = value; OnPropertyChanged(); }
        }

        public ChatViewModel()
        {
            _tabs = new ObservableCollection<ChatTabItem>();

            // 기본 전체 공지 탭 추가
            var globalTab = new ChatTabItem { TabName = "전체 공지 (Global)", SessionId = null };
            _tabs.Add(globalTab);
            SelectedTab = globalTab;

            SendMessageCommand = new RelayCommand(o => SendMessage());
            SendNoticeCommand = new RelayCommand(o => SendNotice());

            // 네트워크 수신 이벤트 구독
            NetworkService.Instance.PacketReceived += OnPacketReceived;
        }

        private void SendMessage()
        {
            if (string.IsNullOrWhiteSpace(InputMessage) || SelectedTab == null) return;

            string msgToSend = InputMessage;
            InputMessage = string.Empty; // 보낸 후 지우기

            // UI에 내 메시지 추가
            var myMsg = new ChatMessageModel
            {
                SenderName = "나(교수)",
                Message = msgToSend,
                Timestamp = DateTime.Now,
                IsMine = true
            };
            SelectedTab.Messages.Add(myMsg);

            // 실제 네트워크 전송
            if (SelectedTab.SessionId == null) // 전체 공지
            {
                NetworkService.Instance.BroadcastChat(msgToSend);
            }
            else // 특정 학생 1:1
            {
                NetworkService.Instance.SendChatToSession(SelectedTab.SessionId, msgToSend);
            }
        }

        // 전체 공지를 보낸다. 받은 학생을 세기 위해 번호를 붙인다(NoticePayload 참고).
        public bool SendNotice()
        {
            if (string.IsNullOrWhiteSpace(NoticeInput)) return false;

            string text = NoticeInput;
            NoticeInput = string.Empty;

            var notice = new NoticeItem(++_lastNoticeId, text,
                                        StudentStore.Instance.Students.Where(s => s.IsConnected));
            Notices.Insert(0, notice);
            OnPropertyChanged(nameof(LatestNotice));

            NetworkService.Instance.Broadcast(PacketType.ChatBroadcast, NoticePayload.Encode(notice.Id, text));
            return true;
        }

        // 알림·채팅 화면에서 한 학생과의 대화를 연다. 그 학생 몫의 안 읽음은 상단 배지에서도 뺀다.
        public void OpenConversation(ChatTabItem tab)
        {
            UnreadCount = Math.Max(0, UnreadCount - tab.UnreadCount);
            SelectedTab = tab;
        }

        // 대화를 닫는다. 열린 대화가 없어야 새 메시지가 안 읽음으로 쌓인다.
        public void CloseConversation() => SelectedTab = null;

        private void OnPacketReceived(string sessionId, string studentId, string studentName, PacketType type, IntPtr payload, uint payloadLen)
        {
            if (type == PacketType.CommandAck)
            {
                OnNoticeAck(studentId, payload, payloadLen);
            }
            else if (type == PacketType.ChatFromStudent)
            {
                // 페이로드 길이로 읽기를 제한한다 (종료 문자가 없는 패킷이 와도 버퍼 밖을 읽지 않도록)
                if (payload == IntPtr.Zero || payloadLen == 0) return;
                string message = (Marshal.PtrToStringUTF8(payload, (int)payloadLen) ?? "").Split('\0')[0];

                // UI 스레드에서 처리
                // 동기 Invoke는 종료 중 수신 스레드를 붙잡아 앱이 멈추므로 BeginInvoke를 쓴다.
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                dispatcher.BeginInvoke(() =>
                {
                    // 학생 한 명에 대화 한 줄. 다시 접속해 세션이 바뀌어도 같은 줄을 이어 쓴다.
                    var tab = Tabs.FirstOrDefault(t => t.SessionId != null && t.StudentId == studentId);
                    if (tab == null)
                    {
                        // 없으면 새 탭 생성
                        tab = new ChatTabItem
                        {
                            TabName = $"{studentName}({studentId})",
                            SessionId = sessionId,
                            StudentName = studentName,
                            StudentId = studentId,
                        };
                        Tabs.Add(tab);
                    }
                    else
                    {
                        // 답장은 지금 접속해 있는 세션으로 가야 한다
                        tab.SessionId = sessionId;
                    }

                    // 메시지 추가
                    tab.Messages.Add(new ChatMessageModel
                    {
                        SenderName = studentName,
                        Message = message,
                        Timestamp = DateTime.Now,
                        IsMine = false
                    });

                    // 열어 둔 대화가 아니면 안 읽음으로 센다. 보고 있는 대화는 바로 읽은 것이다.
                    if (SelectedTab != tab)
                    {
                        tab.UnreadCount++;
                        UnreadCount++;
                    }

                    // 셸이 새 메시지 도착을 알아채는 목록. 최근 것이 위로 오게 넣는다.
                    RecentMessages.Insert(0, new ChatMessageModel
                    {
                        SenderName = $"{studentName}({studentId})",
                        Message = message,
                        Timestamp = DateTime.Now,
                        IsMine = false
                    });
                });
            }
        }

        // 학생이 공지를 받았다는 회신. 회신에 적힌 공지 번호로 어느 공지인지 찾는다.
        private void OnNoticeAck(string studentId, IntPtr payload, uint payloadLen)
        {
            if (!CommandAckPayload.TryDecode(payload, payloadLen, out PacketType command, out _, out string message) ||
                command != PacketType.ChatBroadcast ||
                !uint.TryParse(message, out uint noticeId))
                return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            dispatcher.BeginInvoke(() => Notices.FirstOrDefault(n => n.Id == noticeId)?.MarkReceived(studentId));
        }

        // 메모리 누수 방지를 위해 이벤트 구독 해제 (현재는 MainViewModel에서 싱글턴으로 관리될 예정이므로 안 불릴 수도 있음)
        public void Cleanup()
        {
            NetworkService.Instance.PacketReceived -= OnPacketReceived;
        }
    }
}
