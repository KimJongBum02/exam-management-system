using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ProfessorUI.Model;

namespace ProfessorUI.ViewModel
{
    public class ChatTabViewModel : INotifyPropertyChanged
    {
        private string _tabName = string.Empty;
        private string? _sessionId;
        private int _unreadCount = 0;
        private ObservableCollection<ChatMessageModel> _messages = new ObservableCollection<ChatMessageModel>();

        public ChatTabViewModel()
        {
            _messages.CollectionChanged += OnMessagesChanged;
        }

        public string TabName
        {
            get => _tabName;
            set { _tabName = value; OnPropertyChanged(); }
        }

        // 전체 공지인 경우 SessionId = null
        public string? SessionId
        {
            get => _sessionId;
            set { _sessionId = value; OnPropertyChanged(); }
        }

        // 학생 대화일 때만 값이 있다. 알림·채팅 화면의 대화 목록이 이 값으로 한 줄을 그린다.
        // 이름은 나중에 바뀔 수 있다 — 이름을 잘못 쳤다가 고쳐서 다시 로그인하는 경우가 있다.
        // 줄은 학번으로 찾으므로 여기서 고치지 않으면 처음 적힌 이름이 끝까지 남는다
        // (ChatViewModel.GetOrCreateTab 참고). 학번은 줄을 찾는 열쇠라 바뀌지 않는다.
        private string _studentName = string.Empty;
        public string StudentName
        {
            get => _studentName;
            set
            {
                if (_studentName == value) return;
                _studentName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Initial));
            }
        }

        public string StudentId { get; init; } = string.Empty;

        public int UnreadCount
        {
            get => _unreadCount;
            set
            {
                _unreadCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasUnread));
                OnPropertyChanged(nameof(ReadStateText));
            }
        }

        public bool HasUnread => _unreadCount > 0;

        public ObservableCollection<ChatMessageModel> Messages
        {
            get => _messages;
            set
            {
                _messages.CollectionChanged -= OnMessagesChanged;
                _messages = value;
                _messages.CollectionChanged += OnMessagesChanged;
                OnPropertyChanged();
                OnMessagesChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }
        }

        // ── 대화 목록 한 줄에 보일 값 ── 메시지가 올 때마다 다시 읽힌다.
        public string Initial => StudentName.Length > 0 ? StudentName.Substring(0, 1) : "?";
        // 아직 말이 오간 적 없는 학생은 읽음·미읽음을 적지 않는다.
        public bool HasMessages => _messages.Count > 0;
        public string ReadStateText => HasUnread ? $"미읽음 {_unreadCount}" : HasMessages ? "읽음" : string.Empty;
        public string LastMessageText => _messages.Count > 0 ? _messages[^1].Message : string.Empty;
        public string LastTimeText => _messages.Count > 0 ? _messages[^1].DisplayTime : string.Empty;
        // 목록 정렬용. 최근에 말이 오간 학생이 위로 온다.
        public DateTime LastTimestamp => _messages.Count > 0 ? _messages[^1].Timestamp : DateTime.MinValue;

        private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(LastMessageText));
            OnPropertyChanged(nameof(LastTimeText));
            OnPropertyChanged(nameof(LastTimestamp));
            OnPropertyChanged(nameof(HasMessages));
            OnPropertyChanged(nameof(ReadStateText));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
