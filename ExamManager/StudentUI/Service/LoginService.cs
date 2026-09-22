using NetworkLib;
using StudentUI.Model;
using System;
using System.Text;
using System.Threading.Tasks;

namespace StudentUI.Service
{
    // 교수 PC 에 연결한 뒤 학생임을 알리는 절차. 처음 로그인과 자동 재연결이 같은 절차를 쓴다.
    public static class LoginService
    {
        // 로그인 패킷을 보내고 교수 PC 의 승인을 기다린다. 승인되면 null, 거절되면 그 이유.
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

            // 학번(16바이트) + 이름을 담은 로그인 패킷 전송 → 교수 PC 현황판에 표시됨
            byte[] loginPayload = new byte[80];
            Encoding.UTF8.GetBytes(student.StudentNumber).CopyTo(loginPayload, 0);
            Encoding.UTF8.GetBytes(student.StudentName).CopyTo(loginPayload, 16);
            NetworkService.Instance.SendPacket(PacketType.StudentLogin, loginPayload);

            // 회신이 오지 않으면 승인된 것으로 보고 넘어간다. 회신 하나 때문에 로그인이 막히지 않게 한다.
            Task finished = await Task.WhenAny(response.Task, Task.Delay(TimeSpan.FromSeconds(3)));
            NetworkService.Instance.PacketReceived -= OnPacket;
            string? rejection = finished == response.Task ? response.Task.Result : null;
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
