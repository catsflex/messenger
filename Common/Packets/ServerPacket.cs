using System.Text.Json;

using Common.Enums;

namespace Common.Packets;

public class ServerPacket {
	public ServerPacketType Type { get; set; }
	public string Payload { get; set; } = string.Empty;

	public static string Serialize<T>(ServerPacketType type, T data) {
		var packet = new ServerPacket {
			Type = type,
			Payload = JsonSerializer.Serialize(data)
		};
		return JsonSerializer.Serialize(packet);
	}
}
