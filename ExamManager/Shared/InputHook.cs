using System.Diagnostics;
using System.Runtime.InteropServices;

// 키보드 · 마우스 저수준 훅을 거는 일을 담당하는 파일입니다. 두 앱이 함께 씁니다
// (학생 키보드 잠금 · 학생 통제/시험 모드 · 교수 원격 제어의 한/영 가로채기).

namespace ExamManager.Shared
{
    // 훅 함수는 운영체제가 부르므로 대리자는 부르는 쪽이 필드에 보관해야 합니다.
    // 지역 변수로 두면 GC 가 수거해 가고, 그러면 훅이 아무 말 없이 죽습니다.
    public static class InputHook
    {
        public const int Keyboard = 13;   // WH_KEYBOARD_LL
        public const int Mouse    = 14;   // WH_MOUSE_LL

        public delegate IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam);

        // 훅을 거는 일을 담당합니다. 걸지 못하면 IntPtr.Zero 이고, 이유는 Marshal.GetLastWin32Error() 로 읽습니다.
        public static IntPtr Install(int type, Callback callback)
            => SetWindowsHookEx(type, callback, ModuleHandle(), 0);

        // 훅을 떼는 일을 담당합니다. 걸려 있지 않아도 안전합니다.
        public static void Remove(IntPtr hook)
        {
            if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        }

        // 처리하지 않은 입력을 다음 훅으로 넘기는 일을 담당합니다. 훅 함수의 마지막 줄에 씁니다.
        public static IntPtr Next(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam)
            => CallNextHookEx(hook, nCode, wParam, lParam);

        // 훅에 넘길 모듈 핸들을 구하는 일을 담당합니다.
        // GetModuleHandle(null) 이 0 을 돌려주면 실행 파일 이름으로 다시 묻습니다.
        private static IntPtr ModuleHandle()
        {
            IntPtr module = GetModuleHandle(null);
            if (module != IntPtr.Zero) return module;

            using var process = Process.GetCurrentProcess();
            using var main = process.MainModule;
            return GetModuleHandle(main?.ModuleName ?? "");
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, Callback lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
