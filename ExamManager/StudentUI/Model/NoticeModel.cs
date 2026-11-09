namespace StudentUI.Model
{
    // 교수가 보낸 공지 한 건.
    //
    // 전체 공지는 팝업으로 한 번 지나가고, 중요 공지는 시험 화면 상단 한 줄로만 보인다.
    // 자유 모드에서는 그 시험 화면이 통째로 숨어 있어, 팝업을 닫고 나면 다시 읽을 길이 없다.
    // 그래서 받은 공지를 이 모양으로 쌓아 두고 자유 모드 PIP 의 알림 패널이 보여 준다.
    //
    // 한 번 쌓이면 바뀌지 않으므로 UiBinding 을 물려받지 않는다.
    public class NoticeModel
    {
        public string Message { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; }
        public string DisplayTime => Timestamp.ToString("HH:mm");

        // 중요 공지인지. 교수가 내릴 때까지 상단에 붙어 있던 것이라 목록에서도 구분해 보여 준다.
        public bool IsImportant { get; init; }
    }
}
