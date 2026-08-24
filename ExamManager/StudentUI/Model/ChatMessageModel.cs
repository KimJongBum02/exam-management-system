using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StudentUI.Model
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
            set { _timestamp = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayTime)); }
        }

        public string DisplayTime => Timestamp.ToString("HH:mm");

        public bool IsMine
        {
            get => _isMine;
            set { _isMine = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
