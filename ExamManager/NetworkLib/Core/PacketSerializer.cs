using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NetworkLib.Protocol;

namespace NetworkLib.Core
{
    /// <summary>
    /// TCP 스트림에서 NetworkPacket을 직렬화/역직렬화합니다.
    /// 
    /// 전송 포맷:
    ///   [4 bytes: payload 길이 (int32, little-endian)]
    ///   [N bytes: NetworkPacket JSON (UTF-8)]
    /// </summary>
    public static class PacketSerializer
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        };

        // 최대 허용 패킷 크기: 256MB (파일 청크를 포함하므로 넉넉하게 설정)
        private const int MaxPacketSize = 256 * 1024 * 1024;

        // ─── 송신 ──────────────────────────────────────────────────

        /// <summary>
        /// NetworkPacket을 직렬화하여 스트림에 씁니다.
        /// sendLock을 사용해 동시 송신 시 패킷이 섞이지 않도록 보장합니다.
        /// </summary>
        public static async Task SendPacketAsync(
            Stream stream,
            NetworkPacket packet,
            SemaphoreSlim sendLock,
            CancellationToken ct = default)
        {
            var json = JsonSerializer.Serialize(packet, JsonOptions);
            var jsonBytes = Encoding.UTF8.GetBytes(json);
            var lengthBytes = BitConverter.GetBytes(jsonBytes.Length); // 4 bytes, little-endian

            // sendLock으로 직렬화된 쓰기를 보장 (동시에 여러 스레드가 쓰는 경우 대비)
            await sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await stream.WriteAsync(lengthBytes, ct).ConfigureAwait(false);
                await stream.WriteAsync(jsonBytes, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                sendLock.Release();
            }
        }

        // ─── 수신 ──────────────────────────────────────────────────

        /// <summary>
        /// 스트림에서 다음 NetworkPacket을 읽어 역직렬화합니다.
        /// 연결이 끊기면 EndOfStreamException을 발생시킵니다.
        /// </summary>
        public static async Task<NetworkPacket?> ReceivePacketAsync(
            Stream stream,
            CancellationToken ct = default)
        {
            // 1단계: 4바이트 길이 헤더 읽기
            var lengthBuffer = new byte[4];
            await ReadExactAsync(stream, lengthBuffer, ct).ConfigureAwait(false);
            int length = BitConverter.ToInt32(lengthBuffer, 0);

            if (length <= 0 || length > MaxPacketSize)
                throw new InvalidDataException($"잘못된 패킷 크기: {length} bytes");

            // 2단계: JSON 페이로드 읽기
            var jsonBuffer = new byte[length];
            await ReadExactAsync(stream, jsonBuffer, ct).ConfigureAwait(false);

            var json = Encoding.UTF8.GetString(jsonBuffer);
            return JsonSerializer.Deserialize<NetworkPacket>(json, JsonOptions);
        }

        // ─── 내부 헬퍼 ────────────────────────────────────────────

        /// <summary>
        /// 스트림에서 buffer.Length만큼 정확히 읽습니다.
        /// TCP 스트림은 한 번의 ReadAsync로 원하는 만큼 읽히지 않을 수 있으므로
        /// 루프로 반복 읽기를 수행합니다.
        /// </summary>
        private static async Task ReadExactAsync(
            Stream stream,
            byte[] buffer,
            CancellationToken ct)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await stream.ReadAsync(
                    buffer.AsMemory(offset, buffer.Length - offset), ct)
                    .ConfigureAwait(false);

                if (read == 0)
                    throw new EndOfStreamException("원격 호스트가 연결을 종료했습니다.");

                offset += read;
            }
        }
    }
}
