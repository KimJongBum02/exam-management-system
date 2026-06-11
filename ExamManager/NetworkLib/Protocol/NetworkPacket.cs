using System.Text.Json;

namespace NetworkLib.Protocol
{
    /// <summary>
    /// TCP 스트림으로 주고받는 기본 패킷 단위입니다.
    /// 전송 포맷: [4 bytes: Payload 길이 (little-endian)] [N bytes: JSON UTF-8]
    /// </summary>
    public class NetworkPacket
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        };

        /// <summary>메시지 타입</summary>
        public MessageType Type { get; set; }

        /// <summary>메시지 본문 (각 메시지 DTO를 JSON 직렬화한 문자열)</summary>
        public string Payload { get; set; } = string.Empty;

        /// <summary>패킷 생성 시각 (UTC)</summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // ─── 팩토리 메서드 ──────────────────────────────────────────

        /// <summary>
        /// 메시지 DTO를 JSON으로 직렬화하여 NetworkPacket을 생성합니다.
        /// </summary>
        public static NetworkPacket Create<T>(MessageType type, T message) where T : class
        {
            return new NetworkPacket
            {
                Type = type,
                Payload = JsonSerializer.Serialize(message, JsonOptions),
                Timestamp = DateTime.UtcNow,
            };
        }

        /// <summary>
        /// Payload JSON 문자열을 지정한 타입으로 역직렬화합니다.
        /// 실패 시 null을 반환합니다.
        /// </summary>
        public T? GetPayload<T>() where T : class
        {
            if (string.IsNullOrEmpty(Payload)) return null;
            try
            {
                return JsonSerializer.Deserialize<T>(Payload, JsonOptions);
            }
            catch
            {
                return null;
            }
        }
    }
}
