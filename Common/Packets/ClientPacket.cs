using System.Text.Json;

using Common.Enums;

namespace Common.Packets;

public class ClientPacket {
	public ClientPacketType Type { get; set; }
	public string Payload { get; set; } = string.Empty;

	public static string Serialize<T>(ClientPacketType type, T data) {
		var packet = new ClientPacket {
			Type = type,
			Payload = JsonSerializer.Serialize(data)
		};
		return JsonSerializer.Serialize(packet);
	}
}
