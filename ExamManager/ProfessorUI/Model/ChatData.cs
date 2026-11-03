// 채팅 데이터: 메시지 한 건(ChatMessageModel)
using ExamManager.Shared;

namespace ProfessorUI.Model
{
    // 보낸 사람 · 본문 · 시각 · 내 말인지와 표시용 시각은 공용 바탕(ExamManager.Shared.ChatMessage)에 있다.
    // 교수 쪽은 1:1 메시지의 읽음 표시를 더 붙인다.
    public class ChatMessageModel : ChatMessage
    {
        // 교수가 보낸 1:1 메시지의 번호. 학생의 읽음 회신이 이 번호로 온다.
        // 학생이 보낸 메시지와 전체 공지는 번호가 없어 0 이다.
        public uint MessageId { get; init; }

        private bool _isRead;
        public bool IsRead
        {
            get => _isRead;
            set { _isRead = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReadStateText)); }
        }

        // 내 말인지에 따라 읽음 표시가 나타나거나 사라지므로, 바탕의 알림에 한 줄을 더한다.
        public override bool IsMine
        {
            get => base.IsMine;
            set { base.IsMine = value; OnPropertyChanged(nameof(ReadStateText)); }
        }

        // 말풍선 아래에 붙는 읽음 표시.
        // 내가 보낸 1:1 메시지에만 나온다 — 학생이 보낸 말과 전체 공지에는 읽음이라는 것이 없다.
        public string ReadStateText => !IsMine || MessageId == 0 ? string.Empty
                                     : IsRead ? "읽음" : "안 읽음";
    }
}
