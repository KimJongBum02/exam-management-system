using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using NetworkLib;

namespace StudentUI.Service
{
    // 교수의 지시에 따라 학생 PC의 키보드 입력을 OS 레벨에서 차단/해제하는 서비스.
    // SetWindowsHookEx(WH_KEYBOARD_LL) 저수준 키보드 훅을 사용합니다.
    public class KeyboardLockService
    {
        public static KeyboardLockService Instance { get; } = new();

        private const int WH_KEYBOARD_LL = 13;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private readonly LowLevelKeyboardProc _proc;
        private IntPtr _hookId = IntPtr.Zero;
        private bool _isLocked;
        private View.Shared.KeyboardLockBannerWindow? _bannerWindow;

        public bool IsLocked => _isLocked;

        private KeyboardLockService()
        {
            // GC 가 수거하지 않도록 인스턴스 필드에 대리자를 보관합니다.
            _proc = HookCallback;
        }

        public void Start()
        {
            NetworkService.Instance.PacketReceived += OnPacketReceived;
            NetworkService.Instance.Disconnected += _ => Unlock();
        }

        private void OnPacketReceived(PacketType type, IntPtr payload, uint payloadLen)
        {
            if (type != PacketType.LockKeyboard || payloadLen < 1) return;

            byte lockVal = Marshal.ReadByte(payload);
            if (lockVal == 1)
            {
                Lock();
            }
            else
            {
                Unlock();
            }
        }

        public void Lock()
        {
            var app = Application.Current;
            if (app == null) return;

            app.Dispatcher.Invoke(() =>
            {
                if (_isLocked) return;
                _isLocked = true;

                if (_hookId == IntPtr.Zero)
                {
                    IntPtr hMod = GetModuleHandle(null);
                    if (hMod == IntPtr.Zero)
                    {
                        using var curProcess = Process.GetCurrentProcess();
                        using var curModule = curProcess.MainModule;
                        string moduleName = curModule?.ModuleName ?? "";
                        hMod = GetModuleHandle(moduleName);
                    }

                    _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, hMod, 0);
                    if (_hookId == IntPtr.Zero)
                    {
                        int err = Marshal.GetLastWin32Error();
                        Debug.WriteLine($"[KeyboardLock] SetWindowsHookEx 실패 (Error: {err})");
                    }
                    else
                    {
                        Debug.WriteLine($"[KeyboardLock] 키보드 훅 설치 성공: {_hookId}");
                    }
                }

                if (_bannerWindow == null)
                {
                    _bannerWindow = new View.Shared.KeyboardLockBannerWindow();
                    _bannerWindow.Show();
                }
            });
        }

        public void Unlock()
        {
            var app = Application.Current;
            if (app == null) return;

            app.Dispatcher.Invoke(() =>
            {
                if (!_isLocked && _hookId == IntPtr.Zero) return;
                _isLocked = false;

                if (_hookId != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookId);
                    _hookId = IntPtr.Zero;
                    Debug.WriteLine("[KeyboardLock] 키보드 훅 해제 완료");
                }

                _bannerWindow?.Close();
                _bannerWindow = null;
            });
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _isLocked)
            {
                // 0이 아닌 값(1)을 반환하여 시스템 및 대상 윈도우로의 모든 키보드 메시지 전달 차단
                return (IntPtr)1;
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
