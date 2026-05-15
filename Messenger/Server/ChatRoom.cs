using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Common;
using Common.Enums;
using Common.Logging;
using Common.Payloads;

namespace Messenger.Server;

internal class ChatRoom {

	#region Fields

	// Список участников в чат-комнате.
	private readonly ConcurrentDictionary<string, ClientSession> _participants = new();

	// Токен для отмены самоуничтожения чат-комнаты.
	// Нужен для того, чтобы можно было безопасно удалить чат-комнату вручную.
	private readonly CancellationTokenSource _destroyCts = new();

	private readonly Logger _logger;

	#endregion

	#region Properties

	public string RoomId { get; }
	public int MemberCount => _participants.Count;

	#endregion

	#region Constants

	private static readonly TimeSpan _RoomLifeTime = TimeSpan.FromHours(1);

	#endregion

	#region Constructors

	public ChatRoom(string roomId) {
		RoomId = roomId;
		_logger = new Logger($"Room {roomId}");

		Task.Delay(_RoomLifeTime, _destroyCts.Token).ContinueWith(_ => {
			BroadcastSystemMessage("Время жизни комнаты истекло. Комната распущена.");
			CloseRoom();
		}, TaskContinuationOptions.OnlyOnRanToCompletion);
	}

	#endregion

	#region Events

	public event Action<string>? OnRoomClosed;

	#endregion

	#region Public API

	public void AddParticipant(ClientSession session) {
		if (session.UserName is null) return;

		string joinMessage = $"{session.UserName} вступил в чат.";
		_logger.Info(joinMessage);

		session.CurrentRoomId = RoomId;
		_participants.TryAdd(session.UserName, session);
	}

	public bool RemoveParticipant(string? userName) {
		if (userName is null) return false;
		if (!_participants.TryRemove(userName, out _)) return false;

		string leaveMessage = $"{userName} покинул чат.";
		_logger.Info(leaveMessage);
		BroadcastSystemMessage(leaveMessage);

		if (_participants.IsEmpty) {
			CloseRoom();
		}

		return true;
	}

	public bool HasParticipant(string userName) {
		return _participants.ContainsKey(userName);
	}

	public async Task BroadcastPayloadAsync(ServerPacketType type, string rawPayload, string? excludeUser = null) {
		var tasks = _participants.Values
			.Where(p => p.UserName != excludeUser)
			.Select(p => p.SendRawPayloadAsync(type, rawPayload));

		await Task.WhenAll(tasks);
	}

	public void CloseRoom() {
		_destroyCts.Cancel();
		OnRoomClosed?.Invoke(RoomId);

		foreach (var user in _participants.Values) {
			user.CurrentRoomId = null;
			_ = user.SendPacketAsync(
				ServerPacketType.RoomSystemEvent,
				new RoomSystemPayload("Комната была принудительно закрыта.")
			);
		}

		_participants.Clear();
	}

	#endregion

	#region Private API

	private void BroadcastSystemMessage(string text) {
		var payload = new RoomSystemPayload(text);

		foreach (var user in _participants.Values) {
			_ = user.SendPacketAsync(ServerPacketType.RoomSystemEvent, payload);
		}
	}

	#endregion
}
