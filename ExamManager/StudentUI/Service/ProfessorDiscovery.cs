using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StudentUI.Service
{
    // 교수 PC 를 학생이 직접 찾는다.
    //
    // 강의실 전체에 "교수 PC 어디 있나요" 를 UDP 로 뿌리면 교수 프로그램(DiscoveryResponder)이
    // 시험 서버 포트를 담아 회신한다. 회신을 보낸 주소가 곧 교수 PC 의 IP 다.
    // 학번·이름만 치면 들어갈 수 있게 하기 위한 것으로, IP 를 물어보는 화면은 이것이 실패했을 때만 쓴다.
    public static class ProfessorDiscovery
    {
        // 교수 프로그램이 듣고 있는 자리. 시험 서버 포트(TCP)를 바꿔도 이 자리는 그대로다.
        private const int DiscoveryPort = 9000;

        private const string Request        = "EXAM-FIND";
        private const string ResponsePrefix = "EXAM-HERE ";

        // 한 번 뿌리고 이만큼 기다린다. 못 받으면 한 번 더 뿌린다(UDP 는 한 통쯤 사라질 수 있다).
        private static readonly TimeSpan WaitPerAttempt = TimeSpan.FromSeconds(1);
        private const int Attempts = 2;

        // 찾으면 교수 PC 의 주소와 포트, 못 찾으면 null.
        public static async Task<(string Ip, int Port)?> FindAsync()
        {
            // 회신을 받아야 하므로 보내기 전에 자리를 잡아 둔다(포트는 윈도우가 남는 것으로 준다).
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
            byte[] request = Encoding.UTF8.GetBytes(Request);

            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                foreach (IPAddress target in BroadcastTargets())
                {
                    try { await udp.SendAsync(request, request.Length, new IPEndPoint(target, DiscoveryPort)); }
                    catch (SocketException) { }   // 이 랜 카드로는 못 나간다. 나머지로 계속 뿌린다.
                }

                (string, int)? found = await WaitForReply(udp);
                if (found != null) return found;
            }

            return null;
        }

        private static async Task<(string, int)?> WaitForReply(UdpClient udp)
        {
            using var timeout = new CancellationTokenSource(WaitPerAttempt);

            while (true)
            {
                try
                {
                    UdpReceiveResult received = await udp.ReceiveAsync(timeout.Token);

                    // 우리가 뿌린 물음이 되돌아오는 등 엉뚱한 것이 섞일 수 있어 회신만 골라낸다.
                    string text = Encoding.UTF8.GetString(received.Buffer).Trim();
                    if (text.StartsWith(ResponsePrefix) &&
                        int.TryParse(text.Substring(ResponsePrefix.Length), out int port))
                        return (received.RemoteEndPoint.Address.ToString(), port);
                }
                catch (OperationCanceledException) { return null; }   // 이번엔 아무도 답하지 않았다
                catch (SocketException)            { return null; }
            }
        }

        // 뿌릴 곳들. 255.255.255.255 하나만 쓰면 가상 랜 카드(VMware 등)로 나가 버리는 PC 가 있어,
        // 랜 카드마다 그 대역 전체 주소(예: 192.168.0.255)를 따로 구해 함께 뿌린다.
        private static IEnumerable<IPAddress> BroadcastTargets()
        {
            yield return IPAddress.Broadcast;

            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (UnicastIPAddressInformation info in nic.GetIPProperties().UnicastAddresses)
                {
                    if (info.Address.AddressFamily != AddressFamily.InterNetwork || info.IPv4Mask == null)
                        continue;

                    byte[] address = info.Address.GetAddressBytes();
                    byte[] mask    = info.IPv4Mask.GetAddressBytes();
                    for (int i = 0; i < address.Length; i++)
                        address[i] |= (byte)~mask[i];

                    yield return new IPAddress(address);
                }
            }
        }
    }
}
