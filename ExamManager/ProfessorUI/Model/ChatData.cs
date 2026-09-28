// 채팅 데이터: 메시지 한 건(ChatMessageModel)
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProfessorUI.Model
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
            set { _isMine = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReadStateText)); }
        }

        // 교수가 보낸 1:1 메시지의 번호. 학생의 읽음 회신이 이 번호로 온다.
        // 학생이 보낸 메시지와 전체 공지는 번호가 없어 0 이다.
        public uint MessageId { get; init; }

        private bool _isRead;
        public bool IsRead
        {
            get => _isRead;
            set { _isRead = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReadStateText)); }
        }

        // 말풍선 아래에 붙는 읽음 표시.
        // 내가 보낸 1:1 메시지에만 나온다 — 학생이 보낸 말과 전체 공지에는 읽음이라는 것이 없다.
        public string ReadStateText => !IsMine || MessageId == 0 ? string.Empty
                                     : IsRead ? "읽음" : "안 읽음";

        public string DisplayTime => Timestamp.ToString("HH:mm");

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
