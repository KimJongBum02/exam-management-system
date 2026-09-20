using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ProfessorUI.Service
{
    // 학생 PC 가 교수 PC 를 스스로 찾게 해 주는 응답기.
    //
    // 학생은 로그인할 때 강의실 전체에 "교수 PC 어디 있나요" 를 UDP 로 뿌린다.
    // 여기서 그 물음을 받아 시험 서버 포트를 알려 주면, 학생은 IP 를 몰라도 바로 붙는다.
    // 회신은 물어 온 학생에게만 보내므로, 답장에 실린 보낸 이 주소가 곧 교수 PC 의 IP 다.
    //
    // 듣는 자리(UDP 9000)는 시험 서버 포트(TCP)와 따로 고정이다.
    // 교수가 시험 서버 포트를 바꿔도 학생은 늘 같은 자리로 묻고, 회신에 담긴 새 포트로 붙는다.
    public static class DiscoveryResponder
    {
        public const int DiscoveryPort = 9000;

        private const string Request  = "EXAM-FIND";
        private const string Response = "EXAM-HERE ";   // 뒤에 시험 서버 포트를 붙인다

        private static UdpClient? _listener;

        public static void Start()
        {
            if (_listener != null) return;

            try
            {
                _listener = new UdpClient(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                _ = RespondLoop(_listener);
            }
            catch (SocketException ex)
            {
                // 이 자리를 다른 프로그램이 쓰고 있는 경우. 자동 연결만 안 될 뿐,
                // 학생은 IP 를 직접 입력해 들어올 수 있으므로 서버는 그대로 연다.
                _listener = null;
                Debug.WriteLine($"학생 자동 연결을 열지 못했습니다: {ex.Message}");
            }
        }

        public static void Stop()
        {
            // 기다리고 있던 수신이 풀리면서 아래 루프가 끝난다.
            _listener?.Dispose();
            _listener = null;
        }

        private static async Task RespondLoop(UdpClient listener)
        {
            while (true)
            {
                try
                {
                    UdpReceiveResult received = await listener.ReceiveAsync();
                    if (Encoding.UTF8.GetString(received.Buffer).Trim() != Request) continue;

                    // 지금 열려 있는 포트를 그때그때 읽는다. 포트를 바꿔도 이 루프는 그대로 둔다.
                    byte[] reply = Encoding.UTF8.GetBytes(Response + ServerControl.Port);
                    await listener.SendAsync(reply, reply.Length, received.RemoteEndPoint);
                }
                catch (ObjectDisposedException) { return; }   // Stop()
                catch (SocketException)         { }           // 한 번 실패해도 계속 듣는다
            }
        }
    }
}
