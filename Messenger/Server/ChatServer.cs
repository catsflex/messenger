using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

using Common;
using Common.Enums;
using Common.Logging;
using Common.Packets;
using Common.Payloads;

namespace Messenger.Server;

public class ChatServer {

	#region Fields

	private static readonly Logger _Logger = new("Server");

	private readonly ushort _port;
	private readonly X509Certificate2 _cert;

	private readonly Lobby _lobby = new();
	private readonly ConcurrentDictionary<string, ChatRoom> _rooms = new();

	#endregion

	#region Properties

	public int RoomCount => _rooms.Count;

	#endregion

	#region Constructors

	public ChatServer(ushort port, string certPassword) {
		_port = port;
		_cert = new X509Certificate2("server.pfx", certPassword);
	}

	#endregion

	#region Public API

	public async Task StartAsync(CancellationToken ct) {
		var listener = new TcpListener(IPAddress.Any, _port);
		listener.Start();

		_Logger.Info($"Сервер запущен на порту {_port}.");

		try {
			// Сервер живёт, пока не послан сигнал об остановке.
			while (!ct.IsCancellationRequested) {
				var client = await listener.AcceptTcpClientAsync(ct);
				_ = HandleNewConnectionAsync(client);
			}
		}
		catch (OperationCanceledException) {
			_Logger.Warn("Получен сигнал остановки...");
		}
		finally {
			listener.Stop();
			_Logger.Info("Порт закрыт. Сервер успешно остановлен.");
		}
	}

	#endregion

	#region Private API

	private async Task HandleNewConnectionAsync(TcpClient client) {
		try {
			// Шифрование.
			var sslStream = new SslStream(client.GetStream(), false);
			await sslStream.AuthenticateAsServerAsync(_cert, false, true);

			// Создание сессии.
			var session = new ClientSession(sslStream);
			session.OnPacketReceived += RouteMessage;
			session.OnDisconnected += HandleDisconnect;

			// Прослушивание.
			await session.StartListeningAsync();
		}
		catch {
			client.Close();

			string remoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "Неизвестный IP";
			_Logger.Error($"Сбой подключения [{remoteEndPoint}].");
		}
	}

	private void RouteMessage(ClientSession sender, ClientPacket packet) {
		switch (packet.Type) {

			case ClientPacketType.LobbyJoin:
				HandleLobbyJoin(sender, packet.Payload);
				break;

			case ClientPacketType.RoomCreateRequest:
				HandleRoomCreate(sender, packet.Payload);
				break;

			case ClientPacketType.RoomMessageRequest:
				if (sender.CurrentRoomId is not null && _rooms.TryGetValue(sender.CurrentRoomId, out var targetRoom)) {
					_ = targetRoom.BroadcastPayloadAsync(ServerPacketType.RoomMessageEvent, packet.Payload, sender.UserName);
				}

				break;

			case ClientPacketType.RoomKeyExchangeRequest:
				if (sender.CurrentRoomId is not null && _rooms.TryGetValue(sender.CurrentRoomId, out var keyRoom)) {
					_ = keyRoom.BroadcastPayloadAsync(ServerPacketType.RoomKeyExchangeEvent, packet.Payload, sender.UserName);
				}

				break;

			case ClientPacketType.RoomLeaveRequest:
				if (sender.CurrentRoomId is not null && _rooms.TryGetValue(sender.CurrentRoomId, out var leavingRoom)) {
					leavingRoom.RemoveParticipant(sender.UserName);
					_lobby.AddUser(sender);
				}

				break;
		}
	}

	private void HandleLobbyJoin(ClientSession sender, string payload) {
		var joinData = JsonSerializer.Deserialize<LobbyJoinPayload>(payload);

		// Защита от кривого JSON.
		if (joinData is null || string.IsNullOrWhiteSpace(joinData.UserName)) {
			_Logger.Warn("Попытка входа с некорректными данными. Сессия закрыта.");
			sender.Close();
			return;
		}

		string name = joinData.UserName;

		if (!UserNameValidator.IsValid(name)) {
			_Logger.Warn($"Отказано в доступе: некорректный ник '{name}'.");
			sender.Close();
			return;
		}

		if (IsUsernameTaken(name)) {
			_Logger.Warn($"Отказано в доступе: ник '{name}' уже занят.");
			sender.Close();
			return;
		}

		sender.UserName = name;
		_lobby.AddUser(sender);
		_Logger.Info($"{sender.UserName} зашёл на сервер.");
	}

	private void HandleRoomCreate(ClientSession sender, string payload) {
		var createData = JsonSerializer.Deserialize<RoomCreatePayload>(payload);

		if (createData is null || createData.TargetUserNames.Length == 0) {
			_Logger.Error($"Некорректный пакет. Пользователю {sender.UserName ?? "Unknown"} отказано в создании чат-комнаты.");
			return;
		}

		CreateRoom(sender, createData.TargetUserNames);
	}

	private void CreateRoom(ClientSession creator, string[] targetNames) {
		if (creator.UserName is null) return;

		string roomId = Guid.NewGuid().ToString();
		var room = new ChatRoom(roomId);

		// Действия при закрытии комнаты.
		room.OnRoomClosed += id => {
			_rooms.TryRemove(id, out _);
			_Logger.Info($"Комната {id} удалена.");
		};

		_rooms.TryAdd(room.RoomId, room);

		// Создатель.
		_lobby.RemoveUser(creator.UserName);
		room.AddParticipant(creator);
		_ = creator.SendPacketAsync(
			ServerPacketType.RoomJoinEvent,
			new RoomJoinPayload(room.RoomId, "Вы создали комнату.")
		);

		// Приглашённые.
		foreach (string targetName in targetNames) {
			if (!_lobby.TryGetUser(targetName, out var targetSession)) continue;

			_lobby.RemoveUser(targetName);
			room.AddParticipant(targetSession);
			_ = targetSession.SendPacketAsync(
				ServerPacketType.RoomJoinEvent,
				new RoomJoinPayload(room.RoomId, $"Вас пригласил {creator.UserName}.")
			);
		}

		_Logger.Info($"Комната {room.RoomId} создана.");
		_Logger.Info($"Количество участников: {room.MemberCount}.");
	}

	private bool IsUsernameTaken(string username) {

		// Проверка в лобби.
		if (_lobby.TryGetUser(username, out _)) return true;

		// Проверка во всех активных комнатах.
		if (_rooms.Values.Any(r => r.HasParticipant(username))) return true;

		return false;
	}

	private void HandleDisconnect(ClientSession session) {

		if (session.UserName is not null) {

			// Если пользователь не в чат-комнате (в лобби) -- удаляем оттуда.
			if (!session.IsInRoom) {
				_lobby.RemoveUser(session.UserName);
			}

			// Если пользователь в чат-комнате -- удаляем из чат-комнаты.
			else if (_rooms.TryGetValue(session.CurrentRoomId!, out var room)) {
				room.RemoveParticipant(session.UserName);
			}
		}

		string displayName = session.UserName ?? "Unknown";
		_Logger.Info($"Отключился: {displayName}");
		session.Close();
	}

	#endregion
}
