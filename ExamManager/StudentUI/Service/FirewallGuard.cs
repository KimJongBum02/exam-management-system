using System;
using System.Diagnostics;

namespace StudentUI.Service
{
    // 학생 앱이 어떤 식으로 끝나든 네트워크 차단이 남지 않게 지키는 별도 프로세스.
    //
    // 정상 종료나 처리되지 않은 예외는 앱이 스스로 차단을 푼다(App.RegisterShutdownGuards).
    // 하지만 작업 관리자의 강제 종료나 '응답 없음' 뒤 닫기는 앱의 코드가 한 줄도 돌지 않는다.
    // 그러면 차단 규칙이 남아, 누군가 학생 앱을 다시 켤 때까지 그 PC 는 인터넷을 잃는다.
    // 그래서 차단을 걸 때 같은 실행 파일을 지킴이 모드로 하나 더 띄우고, 학생 앱이 사라지면 바로 차단을 푼다.
    //
    // 프로세스 감시는 학생 앱 안(ProcessControl.dll)에서 도는 것이라 앱이 사라지면 함께 멈춘다.
    // 지킴이가 챙길 것은 Windows 에 남는 방화벽 규칙뿐이다.
    public static class FirewallGuard
    {
        private const string GuardArgument = "--firewall-guard";

        private static bool _started;

        // 차단을 건 뒤 부른다. 앱이 도는 동안 한 번만 띄운다.
        public static void Start()
        {
            if (_started) return;

            try
            {
                string exe = Environment.ProcessPath!;

                // cmd 의 start 로 띄워 부모-자식 관계를 끊는다. 자식으로 두면 작업 관리자에서 학생 앱과
                // 한 묶음으로 보여, 학생 앱을 '작업 끝내기' 할 때 지킴이까지 함께 끝날 수 있다.
                // 학생 앱이 관리자 권한으로 돌고 있으므로 지킴이도 확인 창 없이 같은 권한으로 뜬다.
                Process.Start(new ProcessStartInfo("cmd.exe",
                    $"/c start \"\" \"{exe}\" {GuardArgument} {Environment.ProcessId}")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                });
                _started = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"방화벽 지킴이를 띄우지 못했습니다: {ex.Message}");
            }
        }

        // 지킴이로 켜진 것이면 학생 앱이 끝날 때까지 기다렸다가 차단을 풀고 true 를 돌려준다.
        // 앱이 뜰 때 가장 먼저 부른다(App.OnStartup).
        public static bool RunIfRequested(string[] args)
        {
            if (args.Length < 2 || args[0] != GuardArgument || !int.TryParse(args[1], out int pid))
                return false;

            try
            {
                using var target = Process.GetProcessById(pid);
                target.WaitForExit();
            }
            catch (ArgumentException)
            {
                // 이미 끝났다. 바로 푼다.
            }

            // 학생 앱이 정상 종료했다면 이미 풀려 있어 아무 일도 하지 않는다.
            FirewallPolicyService.Restore();
            return true;
        }
    }
}
