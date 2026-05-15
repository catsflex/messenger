using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using Common;
using Common.Enums;
using Common.Payloads;

namespace Messenger.Server;

internal class Lobby {

	#region Fields

	// Список пользователей в зале ожидания.
	// При подключении на сервер ИЛИ при выходе из чат-комнаты человек попадает сюда.
	// При заходе в чат-комнату человек удаляется отсюда.
	private readonly ConcurrentDictionary<string, ClientSession> _idleUsers = new();

	#endregion

	#region Public API

	public bool AddUser(ClientSession session) {
		if (session.UserName is null) return false;

		// Перевод пользователя в лобби.
		session.CurrentRoomId = null;

		// Если пользователь с таким ником уже существует в лобби.
		if (!_idleUsers.TryAdd(session.UserName, session)) return false;

		BroadcastOnlineList();
		return true;
	}

	public bool RemoveUser(string? userName) {
		if (userName is null) return false;
		if (!_idleUsers.TryRemove(userName, out _)) return false;

		BroadcastOnlineList();
		return true;
	}

	public bool TryGetUser(string? userName, [NotNullWhen(true)] out ClientSession? session) {
		if (userName is not null) return _idleUsers.TryGetValue(userName, out session);

		session = null;
		return false;
	}

	#endregion

	#region Private API

	private void BroadcastOnlineList() {
		string[] userArray = _idleUsers.Keys.ToArray();
		var payload = new LobbyUpdatePayload(userArray);

		foreach (var user in _idleUsers.Values) {
			_ = user.SendPacketAsync(ServerPacketType.LobbyUpdate, payload);
		}
	}

	#endregion
}
