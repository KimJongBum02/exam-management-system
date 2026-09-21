using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ExamManager.Shared;
using NetworkLib;
using ProfessorUI.Model;

namespace ProfessorUI.Service
{
    // 학생 PC 가 보내 온 보고를 해석해 현황판·경고 목록에 반영한다.
    //   접속·종료, 파일 수신, 감시 상태, 부정행위, 설치 프로그램 목록, 화면 캡처
    //
    // 화면에 묶인 목록을 건드리므로 화면 스레드에서 처리해야 한다.
    // 네이티브 수신 스레드에서 올라온 것을 넘겨 주는 일은 App 이 맡고(PostToUi),
    // 무엇을 어떻게 반영할지는 여기서 정한다.
    public class StudentReportService
    {
        public static StudentReportService Instance { get; } = new StudentReportService();

        private StudentReportService() { }

        // 학생이 접속했다.
        public void StudentConnected(string sessionId, string studentId, string name, string ip)
            => StudentStore.Instance.AddOrUpdateConnected(sessionId, studentId, name, ip);

        // 학생 접속이 끊겼다. 시험 중이었고 답안을 내지 않았으면 보안 경고로 남긴다.
        // 그냥 넘기면 답안을 못 걷은 학생을 '나갔나 보다' 하고 지나치게 된다.
        // 돌려주는 값이 true 면 교수가 알아채야 하는 상황이다(작업표시줄 깜빡임).
        public bool StudentDisconnected(string sessionId, string studentId, string name)
        {
            StudentStore.Instance.MarkDisconnected(sessionId);

            // 로그인을 거절한 접속은 학번이 비어 있다 — 학생이 아니므로 알리지 않는다.
            if (!ExamState.IsExamStarted || studentId.Length == 0) return false;

            // 학생 프로그램이 꺼진 것이라면 인터넷 차단도 함께 풀려 있다(FirewallGuard).
            bool submitted = StudentStore.Instance.Students
                .Any(s => s.StudentId == studentId && s.IsAnswerSubmitted);
            if (!submitted)
                AlertStore.Instance.Add(studentId, name, "시험 중 연결 끊김 (답안 미제출)", AlertKind.Security);

            return true;
        }

        // 학생 상태 보고 (파일 수신 / 시험 파일 정리 성공·실패)
        public void ExamStatusUpdate(string studentId, StudentStatus status)
        {
            switch (status)
            {
                case StudentStatus.FileReceived:
                    StudentStore.Instance.MarkFileReceived(studentId);
                    break;

                // 답안은 받았지만 학생 PC에 시험 파일이 남아 있다는 보고.
                // 그 자리에 앉는 다음 학생이 앞사람 답안을 보게 되므로 교수가 직접 확인해야 한다.
                case StudentStatus.CleanupFailed:
                    StudentStore.Instance.MarkCleanupFailed(studentId);
                    break;

                // 시험 파일까지 지웠다는 보고. 이게 와야 '완료'로 본다.
                case StudentStatus.CleanupSucceeded:
                    StudentStore.Instance.MarkCleanupSucceeded(studentId);
                    break;
            }
        }

        // 감시·차단이 실제로 켜졌는지 학생이 알려 온다.
        // 이게 없으면 교수는 감시가 도는 줄 알고 시험을 진행하게 된다.
        // 걸리지 않은 학생은 보안 경고로 올린다 — 학생 표에는 감시 칸이 없다.
        public void MonitorStatus(string studentId, string name, MonitorFlags flags, string detail)
        {
            // 답안 제출 뒤 네트워크 차단을 풀었다는 보고. 정상 절차라 경고로 올리지 않는다.
            // (아래 감시 상태 보고로 읽으면 두 감시가 모두 꺼진 것으로 보여 잘못된 경고가 뜬다)
            if (flags.HasFlag(MonitorFlags.NetworkReleased)) return;

            bool processOn = flags.HasFlag(MonitorFlags.ProcessMonitor);
            bool networkOn = flags.HasFlag(MonitorFlags.NetworkMonitor);

            StudentStore.Instance.MarkMonitorStatus(studentId, processOn, networkOn, detail);

            // 실패 이유는 학생이 차단 실패 때만 적어 보낸다.
            if (!networkOn)
                AlertStore.Instance.Add(studentId, name,
                    detail.Length > 0 ? $"인터넷 차단 실패: {detail}" : "인터넷 차단 실패", AlertKind.Security);
            if (!processOn)
                AlertStore.Instance.Add(studentId, name, "프로그램 감시를 켜지 못함", AlertKind.Security);
        }

        // 부정행위 알림.
        // 허용 프로그램 종료는 참고용이다. 빌드를 마쳤거나 할 일을 끝내 닫았을 수 있어
        // 알림 목록에만 남기고 학생을 '부정행위 감지'로 바꾸지 않는다.
        public void CheatingAlert(string studentId, string name, IntPtr payload, uint len)
        {
            // 누가 보냈는지는 로그인 때 등록된 세션 정보로 알 수 있으므로 페이로드에서 읽지 않는다.
            // 학생이 보낸 문구는 실행 파일 이름 기준이라, 아는 프로그램은 사람이 부르는 이름으로 바꿔 보여 준다.
            string description = FriendlyAlert(ReadAlertDescription(payload, len));
            bool isReference = ReadAlertType(payload, len) == CheatingAlertType.RequiredProcessTerminated;

            if (!isReference)
                StudentStore.Instance.MarkCheatingDetected(studentId);

            AlertStore.Instance.Add(studentId, name, description,
                isReference ? AlertKind.Reference : AlertKind.Cheating);
        }

        // 부정행위 알림 패킷에서 설명 문구만 꺼낸다.
        // CheatingAlertPayload = [studentId 16][studentName 64][alertType 4][description 256]
        private static string ReadAlertDescription(IntPtr payload, uint len)
        {
            const int DescriptionOffset = 84;
            if (payload == IntPtr.Zero || len <= DescriptionOffset) return "(내용 없음)";

            // 남은 길이만큼만 읽어 버퍼 밖으로 나가지 않도록 하고, 빈 칸(널)은 잘라낸다.
            int available = (int)len - DescriptionOffset;
            string text = Marshal.PtrToStringUTF8(payload + DescriptionOffset, available) ?? "";
            return text.Split('\0')[0];
        }

        // 부정행위 알림 패킷에서 종류를 꺼낸다. 길이가 모자라면 null.
        private static CheatingAlertType? ReadAlertType(IntPtr payload, uint len)
        {
            const int AlertTypeOffset = 80;
            if (payload == IntPtr.Zero || len < AlertTypeOffset + 4) return null;

            return (CheatingAlertType)(uint)Marshal.ReadInt32(payload + AlertTypeOffset);
        }

        // 알림 문구 속 실행 파일 이름을 사람이 부르는 이름으로 바꾼다.
        // 예: "금지된 프로그램 실행: Windows Command Processor (cmd.exe)" → "금지된 프로그램 실행: 명령 프롬프트".
        // 아는 프로그램(KnownPrograms)일 때만 바꾸고, 없으면 학생이 보낸 문구를 그대로 둔다.
        // 사이트 접속 시도(도메인)처럼 실행 파일 이름이 없는 알림은 그대로 지나간다.
        private static string FriendlyAlert(string description)
        {
            var match = Regex.Match(description, @"[^\s():]+\.exe", RegexOptions.IgnoreCase);
            if (!match.Success) return description;

            string? friendly = KnownPrograms.DisplayNameFor(match.Value);
            if (friendly == null) return description;

            // "…: <프로그램명>" 에서 이름 부분만 친숙한 이름으로 갈아 끼운다.
            int separator = description.LastIndexOf(": ", StringComparison.Ordinal);
            return separator < 0 ? friendly : description.Substring(0, separator + 2) + friendly;
        }
    }
}
