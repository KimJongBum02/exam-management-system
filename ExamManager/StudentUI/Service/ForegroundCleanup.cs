using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StudentUI.Service
{
    // 시험 진입 시(덮개 올리기 직전) 학생이 자유 시간에 켜 둔 프로그램을 정리한다. 부하를 줄이기 위함이다.
    //
    // 대상은 '보이는 제목 있는 창을 가진 프로세스' — 게임·브라우저·메신저·편집기 등 학생이 직접 띄운 것.
    // 창 없이 도는 백그라운드·서비스(백신·입력기 등)는 창이 없어 걸리지 않고,
    // SYSTEM 권한 프로세스는 Kill 이 막혀(예외) 건너뛴다. 탐색기와 윈도우 셸 UI 는 끄면 안 되니 따로 막는다.
    //
    // 네이티브(ProcessControl)에도 같은 '창 가진 프로세스' 판별이 있지만(GetPidsWithVisibleWindow),
    // 여기서 C# 로 처리해 네이티브 재빌드 없이 쓴다.
    internal static class ForegroundCleanup
    {
        public static void KillUserWindows()
        {
            int myPid;
            try { myPid = Process.GetCurrentProcess().Id; }
            catch { return; }

            var pids = new HashSet<uint>();
            EnumWindows((hwnd, _) =>
            {
                if (IsWindowVisible(hwnd) && GetWindowTextLength(hwnd) > 0)
                {
                    GetWindowThreadProcessId(hwnd, out uint pid);
                    if (pid != 0) pids.Add(pid);
                }
                return true;
            }, IntPtr.Zero);

            foreach (uint pid in pids)
            {
                if (pid == (uint)myPid) continue;   // 우리 앱은 끄지 않는다
                try
                {
                    using var p = Process.GetProcessById((int)pid);
                    if (IsProtected(p.ProcessName)) continue;
                    p.Kill();
                }
                catch { }   // 접근 거부(SYSTEM)·이미 종료 등은 건너뛴다
            }
        }

        // 끄면 화면이 먹통이 되는 탐색기·윈도우 셸 UI, 그리고 우리 두 앱(교수·학생)은 제외한다.
        // 학생 앱은 자기 PID 로도 걸러지지만, 한 PC 에서 교수·학생을 함께 띄워 테스트할 때
        // 교수 앱까지 죽는 것을 막는다(실제 배포에선 교수 앱이 학생 PC 에 없어 무해).
        private static bool IsProtected(string name) => name.ToLowerInvariant() switch
        {
            "explorer" or "textinputhost" or "applicationframehost"
                or "shellexperiencehost" or "searchhost" or "startmenuexperiencehost"
                or "professorui" or "studentui" => true,
            _ => false,
        };

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
