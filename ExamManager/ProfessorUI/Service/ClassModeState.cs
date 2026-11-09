using NetworkLib;

namespace ProfessorUI.Service
{
    // 학생 PC 를 어느 상태(자유 · 통제 · 시험)로 두었는지 기억하고, 접속할 때마다 다시 알려 준다.
    //
    // 기억하지 않으면 끊겼다 돌아온 학생만 자유로 풀린 채 남는다. 학생 앱은 연결이 끊기는 순간
    // 잠금과 덮개를 스스로 푸는데(ClassControlService 의 Disconnected → ReleaseAll), 그것 자체는 맞다 —
    // 교수 앱이 꺼졌을 때 그 PC 가 잠긴 채 남으면 안 되기 때문이다. 문제는 다시 붙을 때
    // 아무도 상태를 알려 주지 않는 것이었다. 랜선이 한 번 흔들리거나 학생 앱을 다시 켠 학생은
    // 혼자 자유 상태가 되는데, 교수 화면에는 '접속 중'으로만 보여 알아챌 수도 없었다.
    //
    // 전체 전환과 개별 전환을 함께 담는다. 개별로 지정해 둔 학생이 재접속했을 때 전체 상태를
    // 덮어 보내면 교수가 따로 맞춰 둔 것이 조용히 풀린다.
    //
    // 화면 스레드에서만 쓴다 — 전환은 버튼에서, 접속 알림은 App 이 UiThread 로 넘겨 준다.
    public static class ClassModeState
    {
        // 전체에 보낸 마지막 상태. 교수가 아무것도 누르지 않았으면 자유다.
        public static ClassMode Current { get; private set; } = ClassMode.Free;

        // 전체와 다르게 지정해 둔 학생. 세션 번호는 재접속마다 바뀌므로 학번으로 기억한다.
        private static readonly Dictionary<string, ClassMode> Individual = new();

        // 접속 중인 모든 학생에게 보낸다. 개별 지정은 지운다 — 전체를 다시 맞춘다는 뜻이기 때문이다.
        public static void Broadcast(ClassMode mode)
        {
            Current = mode;
            Individual.Clear();
            NetworkService.Instance.Broadcast(PacketType.ClassModeChange, Payload(mode));
        }

        // 한 학생에게만 보낸다(화면 상세 보기의 상태 전환).
        public static void SendTo(string sessionId, string studentId, ClassMode mode)
        {
            // 전체와 같아졌으면 개별 지정으로 들고 있을 이유가 없다.
            if (mode == Current) Individual.Remove(studentId);
            else Individual[studentId] = mode;

            NetworkService.Instance.SendToSession(sessionId, PacketType.ClassModeChange, Payload(mode));
        }

        // 그 학생에게 지금 걸려 있어야 하는 상태.
        public static ClassMode ModeFor(string studentId)
            => Individual.TryGetValue(studentId, out ClassMode mode) ? mode : Current;

        // 학생이 접속할 때마다 부른다. 자유여도 보낸다 — 보내지 않으면 지난 접속에서 걸어 둔 것이
        // 학생 쪽에 남아 있는지 알 수 없다. 중요 공지를 접속할 때마다 다시 보내는 것과 같은 이유다
        // (ChatService.SendImportantNoticeTo).
        public static void SendOnConnect(string sessionId, string studentId)
            => NetworkService.Instance.SendToSession(
                   sessionId, PacketType.ClassModeChange, Payload(ModeFor(studentId)));

        private static byte[] Payload(ClassMode mode) => new[] { (byte)mode };
    }
}
