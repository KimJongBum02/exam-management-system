using NetworkLib;
using System.Configuration;
using System.Data;
using System.Windows;

namespace StudentUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // ── 클라이언트 테스트 ────────────────────
            NetworkLibrary.Initialize();
            var client = new StudentClient();
            client.Connected += (ip, port) => MessageBox.Show($"서버 연결 성공: {ip}:{port}");
            client.Disconnected += reason => MessageBox.Show($"연결 끊김: {reason}");
            client.PacketReceived += (type, payload, len) =>
            {
                if (type == PacketType.LoginResponse)
                    MessageBox.Show("로그인 승인됨!");
            };
            // 교수 PC IP로 변경
            bool connected = client.Connect("127.0.0.1", 9000);
            if (connected)
            {
                // 로그인 패킷 전송
                var loginPayload = new byte[80]; // LoginPayload 크기
                System.Text.Encoding.UTF8.GetBytes("20220001").CopyTo(loginPayload, 0);
                System.Text.Encoding.UTF8.GetBytes("홍길동").CopyTo(loginPayload, 16);
                client.SendPacket(PacketType.StudentLogin, loginPayload);
            }
            
        }
    }

}
