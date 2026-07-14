using System;
using System.Runtime.InteropServices;

namespace StudentUI.Service
{
    public class NetworkControlService : IDisposable
    {
        private const string DllName = "NetworkControl.dll";

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        public delegate void DetectCallbackDelegate(string domain);

        // Native Methods
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private static extern bool NC_SetTargetDomains(string domains);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private static extern void NC_SetUpstream(string upstreamIp);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        private static extern void NC_RegisterCallback(DetectCallbackDelegate callback);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        private static extern bool NC_Start();

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        private static extern void NC_Stop();

        // 델리게이트 참조를 유지하여 GC(가비지 컬렉터)에 의해 회수되지 않도록 방지
        private DetectCallbackDelegate? _detectCallback;

        // 허가되지 않은 도메인 조회 감지 시 발생하는 C# 이벤트 (domain: 감지된 도메인)
        public event Action<string>? UnauthorizedDomainDetected;

        public NetworkControlService()
        {
            _detectCallback = new DetectCallbackDelegate(OnDomainDetected);
            NC_RegisterCallback(_detectCallback);
        }

        private void OnDomainDetected(string domain)
        {
            UnauthorizedDomainDetected?.Invoke(domain);
        }

        public bool SetTargetDomains(string domains)
        {
            // 예: "chatgpt.com|openai.com|gemini.google.com"
            return NC_SetTargetDomains(domains);
        }

        public void SetUpstream(string upstreamIp)
        {
            // 조회를 전달할 상위 DNS. 원래 PC의 DNS 를 넘기는 것을 권장 (미설정 시 8.8.8.8)
            NC_SetUpstream(upstreamIp);
        }

        public bool StartMonitoring()
        {
            return NC_Start();
        }

        public void StopMonitoring()
        {
            NC_Stop();
        }

        public void Dispose()
        {
            StopMonitoring();
            _detectCallback = null;
        }
    }
}