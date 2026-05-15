namespace Common.Payloads;

public record LobbyJoinPayload(string UserName);

public record LobbyUpdatePayload(string[] Users);
