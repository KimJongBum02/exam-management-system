// 접속 기록 한 건(ConnectionLogEntry)
using System;

namespace ProfessorUI.Model
{
    // 학생이 접속한 사건 하나.
    //
    // 처음 들어온 것인지, 끊겼다 다시 들어온 것인지를 나눠 둔다 —
    // 재접속이 잦은 학생은 자리나 랜선을 확인해 봐야 하므로 교수가 구분할 수 있어야 한다.
    public class ConnectionLogEntry
    {
        public DateTime Time { get; init; }
        public string StudentId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Ip { get; init; } = string.Empty;

        // 이 시험에서 처음 접속한 것이면 true.
        public bool IsFirst { get; init; }

        // 표에 그대로 나갈 문구. 날짜까지 적는다 — 시험이 자정을 넘기거나
        // 앞 교시 기록이 섞여 있을 때 시각만으로는 언제인지 알 수 없다.
        public string TimeText => Time.ToString("yyyy-MM-dd HH:mm:ss");
        public string KindText => IsFirst ? "최초 접속" : "재접속";
    }
}
