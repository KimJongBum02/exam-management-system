using NetworkLib;
using System;
using System.Collections.Generic;
using System.Text;

namespace StudentUI.Service
{
    // 교수 PC의 명령에 맞춰 프로세스 감시를 켜고, 적발 내용을 교수 PC로 되돌려 보낸다.
    //
    // 시험 시작 때 두 가지 패킷이 순서대로 도착한다.
    //   ① ProcessListUpdate — 이번 시험의 감시 목록. 받아서 네이티브에 넣는다.
    //   ② ExtractArchive    — 시험 시작. 압축 해제가 끝난 뒤에 감시를 켠다.
    //
    // ②에서 '해제가 끝난 뒤'가 중요하다. 감시를 먼저 켜면 압축 해제에 쓰는
    // 7za.exe가 시험 중 새로 실행된 프로그램으로 적발된다.
    public class ExamMonitorService : IDisposable
    {
        public static ExamMonitorService Instance { get; } = new ExamMonitorService();

        // 네이티브 감시 DLL 래퍼. 시험이 시작될 때 처음 만들어진다(EnsureProcessControl 참고).
        private ProcessControlService? _processControl;

        private ExamMonitorService() { }

        // 앱 시작 시 한 번 호출 — 구독만 해두고 감시는 아직 켜지 않는다.
        public void Start()
        {
            NetworkService.Instance.PacketReceived += OnPacketReceived;
            ExamFileStore.Instance.ExamStartHandled += OnExamStartHandled;
        }

        // ── ① 감시 목록 수신 ──
        private void OnPacketReceived(PacketType type, IntPtr payload, uint payloadLen)
        {
            if (type != PacketType.ProcessListUpdate) return;

            // 형식이 깨진 패킷은 통째로 버린다.
            // 반쯤 읽힌 목록을 적용하면 금지 프로그램이 빠진 채로 감시가 돌 수 있다.
            if (!ProcessListPayload.TryDecode(payload, payloadLen,
                                              out List<string> whitelist,
                                              out List<string> blacklist))
                return;

            ProcessControlService? processControl = EnsureProcessControl();
            if (processControl == null) return;

            // 네이티브 DLL은 파이프로 이어 붙인 한 줄을 받는다 (네트워크 형식과 다르다).
            processControl.SetWhitelist(string.Join("|", whitelist));
            processControl.SetBlacklist(string.Join("|", blacklist));
        }

        // ── ② 압축 해제까지 끝났으니 감시 시작 ──
        private void OnExamStartHandled()
        {
            EnsureProcessControl()?.StartMonitoring();
        }

        // 네이티브 DLL은 여기서 처음 불린다.
        // 앱 시작 때가 아니라 시험이 시작될 때 만들어, DLL이 없더라도
        // 로그인·대기 화면은 정상 동작하게 한다.
        private ProcessControlService? EnsureProcessControl()
        {
            if (_processControl != null) return _processControl;

            try
            {
                ProcessControlService processControl = new ProcessControlService();
                processControl.CheatDetected += OnCheatDetected;
                _processControl = processControl;
            }
            catch (Exception ex)
            {
                // 이 메서드는 네트워크 수신 스레드에서 불린다.
                // DLL 로드 실패를 여기서 잡지 않으면 예외가 그대로 올라가 앱이 통째로 죽는다.
                System.Diagnostics.Debug.WriteLine($"프로세스 감시를 시작하지 못했습니다: {ex.Message}");
            }

            return _processControl;
        }

        // ── ③ 적발 내용을 교수 PC로 보고 ──
        // 네이티브 감시 스레드에서 불린다. 화면은 건드리지 않고 보내기만 한다.
        private void OnCheatDetected(int type, string processName)
        {
            NetworkService.Instance.SendPacket(PacketType.CheatingAlert, BuildAlertPayload(type, processName));
        }

        // Protocol.h의 CheatingAlertPayload 형식으로 만든다.
        //   [studentId 16][studentName 64][alertType 4][description 256] = 340바이트
        // 학번·이름 칸은 비워 둔다 — 교수 PC는 로그인 때 등록된 세션으로 누가 보냈는지 이미 안다.
        private static byte[] BuildAlertPayload(int type, string processName)
        {
            // type은 ProcessControlService.CheatDetected의 종류 값이다.
            (CheatingAlertType alertType, string description) = type switch
            {
                0 => (CheatingAlertType.BlacklistedProcessLaunched, $"금지된 프로그램 실행: {processName}"),
                1 => (CheatingAlertType.RequiredProcessTerminated, $"시험에 필요한 프로그램 종료: {processName}"),
                _ => (CheatingAlertType.UnauthorizedProcess, $"목록에 없는 프로그램 실행: {processName}"),
            };

            byte[] payload = new byte[340];
            BitConverter.GetBytes((uint)alertType).CopyTo(payload, 80);

            // 설명이 길면 잘라 담는다. 마지막 1바이트는 문자열 끝 표시로 남겨 둔다.
            byte[] text = Encoding.UTF8.GetBytes(description);
            Array.Copy(text, 0, payload, 84, Math.Min(text.Length, 255));

            return payload;
        }

        public void Dispose()
        {
            // 감시 스레드를 먼저 멈춘다. 앱이 내려가는 중에 콜백이 올라오면 안 되기 때문이다.
            _processControl?.Dispose();
            _processControl = null;
        }
    }
}
