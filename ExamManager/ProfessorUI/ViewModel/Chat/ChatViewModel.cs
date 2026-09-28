using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using NetworkLib;
using ProfessorUI.Service;
using ProfessorUI.Model;
using ProfessorUI.Common;

namespace ProfessorUI.ViewModel
{
    public class ChatViewModel : ViewModelBase
    {
        private readonly ChatService _chat = ChatService.Instance;

        public string Title => "채팅창";

        private ObservableCollection<ChatTabViewModel> _tabs;
        private ChatTabViewModel? _selectedTab;
        private string _inputMessage = string.Empty;

        public ObservableCollection<ChatTabViewModel> Tabs
        {
            get => _tabs;
            set { _tabs = value; OnPropertyChanged(); }
        }

        public ChatTabViewModel? SelectedTab
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
        public ObservableCollection<NoticeViewModel> Notices { get; } = new();
        public NoticeViewModel? LatestNotice => Notices.FirstOrDefault();

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
            _tabs = new ObservableCollection<ChatTabViewModel>();

            // 기본 전체 공지 탭 추가
            var globalTab = new ChatTabViewModel { TabName = "전체 공지 (Global)", SessionId = null };
            _tabs.Add(globalTab);
            SelectedTab = globalTab;

            SendMessageCommand = new RelayCommand(o => SendMessage());
            SendNoticeCommand = new RelayCommand(o => SendNotice());

            // 받은 메시지·공지 회신 구독 (보내고 받는 일은 ChatService 가 맡는다)
            _chat.MessageReceived += OnMessageReceived;
            _chat.NoticeAcknowledged += OnNoticeAcknowledged;
            _chat.ChatRead += OnChatRead;
        }

        private void SendMessage()
        {
            if (string.IsNullOrWhiteSpace(InputMessage) || SelectedTab == null) return;

            string msgToSend = InputMessage;
            InputMessage = string.Empty; // 보낸 후 지우기

            // 1:1 은 메시지마다 번호를 받아 둔다. 학생이 읽었다는 회신이 그 번호로 온다.
            // 전체 공지(SessionId == null)는 받는 사람이 여럿이라 말풍선 하나로 읽음을 셀 수 없다 —
            // 공지의 수신 인원은 [공지 전송 결과 요약]이 따로 센다.
            uint messageId = 0;
            if (SelectedTab.SessionId == null) _chat.SendToAll(msgToSend);
            else messageId = _chat.SendTo(SelectedTab.SessionId, msgToSend);

            SelectedTab.Messages.Add(new ChatMessageModel
            {
                SenderName = "나(교수)",
                Message = msgToSend,
                Timestamp = DateTime.Now,
                IsMine = true,
                MessageId = messageId
            });
        }

        // 전체 공지를 보낸다. 받은 학생을 세기 위해 번호를 붙인다(NoticePayload 참고).
        public bool SendNotice()
        {
            if (string.IsNullOrWhiteSpace(NoticeInput)) return false;

            string text = NoticeInput;
            NoticeInput = string.Empty;

            uint noticeId = _chat.SendNotice(text);

            var notice = new NoticeViewModel(noticeId, text,
                                        StudentStore.Instance.Students.Where(s => s.IsConnected));
            Notices.Insert(0, notice);
            OnPropertyChanged(nameof(LatestNotice));
            return true;
        }

        // 학생 한 명의 대화 줄을 찾는다. 없으면 만든다.
        // 학생이 말을 걸어 올 때도, 교수가 먼저 말을 걸 때도 같은 줄을 쓴다 —
        // 다시 접속해 세션이 바뀌어도 학번으로 찾으므로 대화가 이어진다.
        public ChatTabViewModel GetOrCreateTab(string studentId, string studentName, string? sessionId)
        {
            var tab = Tabs.FirstOrDefault(t => t.SessionId != null && t.StudentId == studentId);
            if (tab == null)
            {
                tab = new ChatTabViewModel
                {
                    TabName = $"{studentName}({studentId})",
                    SessionId = sessionId ?? string.Empty,
                    StudentName = studentName,
                    StudentId = studentId,
                };
                Tabs.Add(tab);
            }
            // 답장은 지금 접속해 있는 세션으로 가야 한다
            else if (!string.IsNullOrEmpty(sessionId)) tab.SessionId = sessionId;

            return tab;
        }

        // 알림·채팅 화면에서 한 학생과의 대화를 연다. 그 학생 몫의 안 읽음은 상단 배지에서도 뺀다.
        public void OpenConversation(ChatTabViewModel tab)
        {
            UnreadCount = Math.Max(0, UnreadCount - tab.UnreadCount);
            SelectedTab = tab;
        }

        // 대화를 닫는다. 열린 대화가 없어야 새 메시지가 안 읽음으로 쌓인다.
        public void CloseConversation() => SelectedTab = null;

        // 학생이 보낸 메시지. 네이티브 수신 스레드에서 올라오므로 화면 스레드로 넘겨 처리한다.
        // 동기 Invoke는 종료 중 수신 스레드를 붙잡아 앱이 멈추므로 BeginInvoke를 쓴다.
        private void OnMessageReceived(string sessionId, string studentId, string studentName, string message)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            dispatcher.BeginInvoke(() =>
            {
                // 학생 한 명에 대화 한 줄. 다시 접속해 세션이 바뀌어도 같은 줄을 이어 쓴다.
                var tab = GetOrCreateTab(studentId, studentName, sessionId);

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

        // 학생이 공지를 받았다는 회신. 그 공지의 '수신 N명'을 올린다.
        private void OnNoticeAcknowledged(uint noticeId, string studentId)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            dispatcher.BeginInvoke(() => Notices.FirstOrDefault(n => n.Id == noticeId)?.MarkReceived(studentId));
        }

        // 학생이 1:1 메시지를 읽었다는 회신. 그 말풍선을 '읽음'으로 바꾼다.
        //
        // 학생은 채팅창을 열 때 그때까지 쌓인 것을 한꺼번에 회신하므로 여러 건이 잇따라 들어온다.
        // 접속이 끊긴 채 보낸 메시지는 회신이 오지 않아 '안 읽음'으로 남는다 — 실제로 못 본 것이 맞다.
        private void OnChatRead(uint messageId, string studentId)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            dispatcher.BeginInvoke(() =>
            {
                var tab = Tabs.FirstOrDefault(t => t.SessionId != null && t.StudentId == studentId);
                var message = tab?.Messages.FirstOrDefault(m => m.MessageId == messageId);
                if (message != null) message.IsRead = true;
            });
        }

        // 메모리 누수 방지를 위해 이벤트 구독 해제 (UiContext 가 싱글턴으로 들고 있어 안 불릴 수도 있음)
        public void Cleanup()
        {
            _chat.MessageReceived -= OnMessageReceived;
            _chat.NoticeAcknowledged -= OnNoticeAcknowledged;
            _chat.ChatRead -= OnChatRead;
        }
    }
}
