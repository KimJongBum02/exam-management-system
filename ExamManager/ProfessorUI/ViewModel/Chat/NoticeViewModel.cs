using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace ProfessorUI.ViewModel
{
    // 전체 공지 한 건. 누구에게 보냈고 누가 받았는지 함께 들고 있다.
    //
    // 받을 사람은 보내는 순간 접속해 있던 학생으로 정한다. 그 뒤에 접속한 학생의 회신은 세지 않는다 —
    // 그 학생은 이 공지를 받은 적이 없으므로, 섞어 세면 '수신 41 / 40명' 같은 숫자가 나온다.
    public class NoticeViewModel : INotifyPropertyChanged
    {
        private readonly Dictionary<string, string> _targets;   // 학번 → 이름
        private readonly HashSet<string> _received = new();

        public NoticeViewModel(uint id, string text, IEnumerable<StudentStatusViewModel> targets)
        {
            Id = id;
            Text = text;
            Time = DateTime.Now.ToString("HH:mm");
            _targets = targets.ToDictionary(s => s.StudentId, s => s.Name);
        }

        public uint Id { get; }
        public string Text { get; }
        public string Time { get; }

        public int TargetCount => _targets.Count;
        public int ReceivedCount => _received.Count;
        public int MissingCount => TargetCount - ReceivedCount;
        public bool HasMissing => MissingCount > 0;

        // ── 화면에 그대로 나갈 문구 ──
        public string ReceiptText => $"수신 {ReceivedCount} / {TargetCount}명";
        public string SummaryText => $"마지막 전송 {Time} · 수신 {ReceivedCount}명 / 전송 {TargetCount}명";
        public string TargetText => $"{TargetCount}명";
        public string ReceivedText => $"{ReceivedCount}명";
        public string MissingCountText => $"{MissingCount}명";

        // 아직 못 받은 학생. 누구에게 따로 알려야 하는지 바로 보이도록 이름을 적는다.
        public string MissingText => "미수신: " + string.Join(", ",
            _targets.Where(t => !_received.Contains(t.Key)).Select(t => t.Value));

        public void MarkReceived(string studentId)
        {
            if (!_targets.ContainsKey(studentId) || !_received.Add(studentId)) return;

            // 숫자와 문구가 모두 이 두 목록에서 나오므로 전부 다시 읽게 한다
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
