namespace Common.Payloads;

public record RoomCreatePayload(string[] TargetUserNames);

public record RoomJoinPayload(string RoomId, string Message);

public record RoomMessagePayload(string SenderName, string EncryptedData);

public record RoomKeyPayload(string SenderName, string KeyPacketJson);

public record RoomSystemPayload(string Text);
