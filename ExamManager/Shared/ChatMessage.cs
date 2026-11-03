// 교수와 학생이 주고받는 말 한 건을 담당하는 파일입니다. 두 앱의 말풍선이 같은 값을 봅니다.

namespace ExamManager.Shared
{
    // 앱마다 더 필요한 것은 물려받아 붙입니다 —
    // 교수는 읽음 표시(MessageId · IsRead), 학생은 이어 말하기(ShowSender)를 더 답니다.
    public class ChatMessage : UiBinding
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

        // 말풍선 옆에 붙는 시각을 담당합니다.
        public string DisplayTime => Timestamp.ToString("HH:mm");

        // 오른쪽(내 말)과 왼쪽(상대 말)을 가르는 일을 담당합니다.
        // 교수 쪽은 이 값이 바뀌면 읽음 표시도 다시 그려야 해서 물려받아 늘립니다.
        public virtual bool IsMine
        {
            get => _isMine;
            set { _isMine = value; OnPropertyChanged(); }
        }
    }
}
