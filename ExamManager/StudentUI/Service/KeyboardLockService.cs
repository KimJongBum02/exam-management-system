using System;
using ExamManager.Shared;
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

        // 교수가 잠그라고 하면 부른다. 네이티브 수신 스레드에서 올라오므로 화면 일은 UiThread 로 넘긴다.
        // 기다리는 Invoke 를 쓰면, 화면 스레드가 수신 스레드가 끝나길 기다리는 순간(로그아웃·연결 끊김)
        // 서로 상대를 기다려 앱이 통째로 굳는다.
        public void Lock()
        {
            UiThread.Post(() =>
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

        // 잠금을 푼다. 교수의 해제 명령과 연결 끊김(종료 포함)에서 불린다 — Lock 과 같은 이유로 기다리지 않는다.
        // 앱이 끝나는 중이면 UiThread 가 넘기지 않아 훅을 떼는 줄이 돌지 않는데, 훅은 프로세스에 딸린 것이라
        // 프로세스가 사라지면 윈도우가 거둬 간다. 키보드가 잠긴 채 남지는 않는다.
        public void Unlock()
        {
            UiThread.Post(() =>
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
