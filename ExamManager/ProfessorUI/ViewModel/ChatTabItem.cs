using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProfessorUI.ViewModel
{
    public class ChatMessageModel : INotifyPropertyChanged
    {
        private string _senderName = string.Empty;
        private string _message = string.Empty;
        private DateTime _timestamp;
        private bool _isMine;

        public string SenderName
        {
            get => _senderName;
            set { _senderName = value; OnPropertyChanged(); }
        }

        public string Message
        {
            get => _message;
            set { _message = value; OnPropertyChanged(); }
        }

        public DateTime Timestamp
        {
            get => _timestamp;
            set { _timestamp = value; OnPropertyChanged(); }
        }

        public bool IsMine
        {
            get => _isMine;
            set { _isMine = value; OnPropertyChanged(); }
        }

        public string DisplayTime => Timestamp.ToString("HH:mm");

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ChatTabItem : INotifyPropertyChanged
    {
        private string _tabName = string.Empty;
        private string? _sessionId;
        private int _unreadCount = 0;
        private ObservableCollection<ChatMessageModel> _messages = new ObservableCollection<ChatMessageModel>();

        public ChatTabItem()
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
        public string StudentName { get; init; } = string.Empty;
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
        public string ReadStateText => HasUnread ? $"미읽음 {_unreadCount}" : "읽음";
        public string LastMessageText => _messages.Count > 0 ? _messages[^1].Message : string.Empty;
        public string LastTimeText => _messages.Count > 0 ? _messages[^1].DisplayTime : string.Empty;
        // 목록 정렬용. 최근에 말이 오간 학생이 위로 온다.
        public DateTime LastTimestamp => _messages.Count > 0 ? _messages[^1].Timestamp : DateTime.MinValue;

        private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(LastMessageText));
            OnPropertyChanged(nameof(LastTimeText));
            OnPropertyChanged(nameof(LastTimestamp));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
