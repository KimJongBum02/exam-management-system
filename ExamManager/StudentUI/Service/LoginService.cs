using NetworkLib;
using StudentUI.Model;

namespace StudentUI.Service
{
    // 교수 PC 에 연결한 뒤 학생임을 알리는 절차. 처음 로그인과 자동 재연결이 같은 절차를 쓴다.
    public static class LoginService
    {
        // 교수 PC 가 회신하지 않았을 때 돌려주는 값. 부르는 쪽이 이것만 따로 안내한다.
        public const string NoResponse = "NO_RESPONSE";

        // 승인을 이만큼 기다린다. 교수 쪽은 네이티브가 로그인 패킷을 받는 즉시 회신하므로
        // (ProfessorServer::HandleLogin — 교수 화면이 바빠도 영향이 없다) 강의실 랜에서는 수십 ms 면 온다.
        private static readonly TimeSpan ResponseWait = TimeSpan.FromSeconds(5);

        // 로그인 패킷을 보내고 교수 PC 의 승인을 기다린다. 승인되면 null, 아니면 그 이유.
        // 승인되면 로그인 뒤에 보내야 하는 것들까지 보낸다 — 교수 PC 는 로그인 전에 온 패킷을 버린다.
        public static async Task<string?> LoginAsync(Student student)
        {
            // 교수 PC 의 승인·거절을 기다린다. 회신이 빨리 오므로 로그인 패킷보다 먼저 구독해 둔다.
            var response = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnPacket(PacketType type, IntPtr payload, uint payloadLen)
            {
                if (type == PacketType.LoginResponse &&
                    LoginResponsePayload.TryDecode(payload, payloadLen, out bool approved, out string reason))
                    response.TrySetResult(approved ? null : reason);
            }
            NetworkService.Instance.PacketReceived += OnPacket;

            // 학번 + 이름을 담은 로그인 패킷 전송 → 교수 PC 현황판에 표시됨
            NetworkService.Instance.SendPacket(PacketType.StudentLogin,
                LoginPayload.Encode(student.StudentNumber, student.StudentName));

            // 회신이 없으면 실패로 본다.
            //
            // 예전에는 승인으로 치고 넘어갔는데, 그러면 **그 포트를 쓰는 다른 프로그램**에 붙어도
            // 학생 화면이 그대로 열렸다. 교수 화면에는 그 학생이 없고 채팅도 가지 않는데
            // 학생만 멀쩡한 줄 알고 시험을 기다린다 — 실제로 2026-10-28 에 그렇게 당했다.
            // 교수 앱은 승인이든 거절이든 반드시 회신하므로, 회신이 없다는 것은 우리 교수 앱이 아니라는 뜻이다.
            // 학생 앱은 교수 앱 없이는 아무 뜻이 없으니, 애매하면 들여보내지 않는 쪽이 맞다.
            Task finished = await Task.WhenAny(response.Task, Task.Delay(ResponseWait));
            NetworkService.Instance.PacketReceived -= OnPacket;
            if (finished != response.Task) return NoResponse;

            string? rejection = response.Task.Result;
            if (rejection != null) return rejection;

            // 시험 파일을 이미 갖고 있으면 다시 알린다. 교수 PC 는 다시 들어온 학생을 '미수신'으로 되돌려 두므로,
            // 알리지 않으면 파일이 있는데도 배포 표에 '대기 중'으로 남는다.
            if (ExamFileStore.Instance.IsReceived)
                NetworkService.Instance.SendPacket(PacketType.ExamStatusUpdate,
                    ExamStatusUpdatePayload.Encode(StudentStatus.FileReceived));

            // 이 PC 에 설치된 프로그램 목록을 보낸다. 교수의 프로그램 선택창에 강의실 PC 것도 뜨게 한다.
            InstalledProgramReport.Send();

            // 누가 로그인했는지 알리고, 시험 도중 꺼졌다 다시 켠 경우 진행 중이던 시험의 감시와 차단을 다시 건다.
            // 로그인 뒤에 해야 교수 PC 가 감시 상태 보고를 받는다.
            ExamMonitorService.Instance.OnLoggedIn(student.StudentNumber, student.StudentName);

            // 퀴즈 응답에 학번·이름을 실어 보낼 수 있도록 학생 정보를 넘겨 둔다.
            QuizService.Instance.Student = student;
            return null;
        }
    }
}
