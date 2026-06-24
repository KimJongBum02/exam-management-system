using System;
using System.Runtime.InteropServices;

namespace StudentUI.Service
{
    public class ProcessControlService : IDisposable
    {
        private const string DllName = "ProcessControl.dll";

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        public delegate void CheatCallbackDelegate(int type, string processName);

        // Native Methods
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private static extern bool PM_SetBlacklist(string blacklist);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private static extern bool PM_SetWhitelist(string whitelist);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        private static extern void PM_RegisterCallback(CheatCallbackDelegate callback);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        private static extern bool PM_Start();

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        private static extern void PM_Stop();

        // 델리게이트 참조를 유지하여 GC(가비지 컬렉터)에 의해 회수되지 않도록 방지
        private CheatCallbackDelegate? _cheatCallback;

        // C# 이벤트로 노출
        // type: 0 = 블랙리스트 프로세스 실행 시도, 1 = 필수 화이트리스트 프로세스 강제 종료됨
        public event Action<int, string>? CheatDetected;

        public ProcessControlService()
        {
            _cheatCallback = new CheatCallbackDelegate(OnCheatDetected);
            PM_RegisterCallback(_cheatCallback);
        }

        private void OnCheatDetected(int type, string processName)
        {
            CheatDetected?.Invoke(type, processName);
        }

        public bool SetBlacklist(string blacklist)
        {
            // 예: "notepad.exe|chrome.exe"
            return PM_SetBlacklist(blacklist);
        }

        public bool SetWhitelist(string whitelist)
        {
            return PM_SetWhitelist(whitelist);
        }

        public bool StartMonitoring()
        {
            return PM_Start();
        }

        public void StopMonitoring()
        {
            PM_Stop();
        }

        public void Dispose()
        {
            StopMonitoring();
            // GC가 회수할 수 있도록 참조 제거
            _cheatCallback = null;
        }
    }
}
