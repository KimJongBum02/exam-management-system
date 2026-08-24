using System;
using System.Collections.ObjectModel;
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

        public int UnreadCount
        {
            get => _unreadCount;
            set 
            { 
                _unreadCount = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(HasUnread)); 
            }
        }

        public bool HasUnread => _unreadCount > 0;

        public ObservableCollection<ChatMessageModel> Messages
        {
            get => _messages;
            set { _messages = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
