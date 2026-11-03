using ExamManager.Shared;

namespace StudentUI.Model
{
    // 보낸 사람 · 본문 · 시각 · 내 말인지와 표시용 시각은 공용 바탕(ExamManager.Shared.ChatMessage)에 있다.
    // 학생 쪽은 이어 말하기 표시만 더 붙인다.
    public class ChatMessageModel : ChatMessage
    {
        // 같은 사람이 이어서 말하면 얼굴과 이름을 다시 그리지 않는다. 쌓을 때 한 번 정해지고 바뀌지 않는다.
        public bool ShowSender { get; init; } = true;
    }
}
