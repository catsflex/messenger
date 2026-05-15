using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Common;
using Common.Enums;
using Common.Logging;
using Common.Packets;
using Common.Payloads;

using Messenger.Server;

namespace Messenger.Client;

public class ChatClient {

	#region Fields

	private static readonly Logger _Logger = new("Client");

	// Сеть.
	private TcpClient? _tcpClient;
	private SslStream? _sslStream;
	private StreamReader? _reader;
	private StreamWriter? _writer;

	// Шифрование.
	private readonly RSA _rsa;
	private readonly string _myPublicKeyXml;

	// Публичные ключи всех участников.
	private readonly ConcurrentDictionary<string, RSA> _participantRsaKeys = new();

	// Общий симметричный ключ комнаты.
	private Aes? _roomAes;

	// Флаг создателя для раздачи AES-ключа.
	private bool _isRoomCreator;

	#endregion

	#region Properties

	public string UserName { get; }
	public string? CurrentRoomId { get; private set; }
	public bool IsInRoom => CurrentRoomId is not null;
	public bool IsConnected => _tcpClient?.Connected == true;

	#endregion

	#region Constructors

	public ChatClient(string userName) {
		UserNameValidator.Validate(userName);
		UserName = userName;

		// Создание своей пары ключей.
		_rsa = RSA.Create(2048);

		// Публичный ключ, который будем рассылать другим.
		_myPublicKeyXml = _rsa.ToXmlString(false);
	}

	#endregion

	#region Events

	public event Action<string[]>? OnLobbyUpdated;
	public event Action<string, string>? OnRoomJoined;
	public event Action<string>? OnSystemMessage;
	public event Action<string, string>? OnMessageReceived;
	public event Action? OnDisconnected;

	#endregion

	#region Public API

	public async Task ConnectAsync(string ip, ushort port) {
		_tcpClient = new TcpClient();
		await _tcpClient.ConnectAsync(ip, port);

		var netStream = _tcpClient.GetStream();
		_sslStream = new SslStream(netStream, false, (_, _, _, _) => true);
		await _sslStream.AuthenticateAsClientAsync(ip);

		_reader = new StreamReader(_sslStream, Encoding.UTF8);
		_writer = new StreamWriter(_sslStream, Encoding.UTF8) { AutoFlush = true };

		await SendPacketAsync(ClientPacketType.LobbyJoin, new LobbyJoinPayload(UserName));
		_ = Task.Run(ReceiveLoopAsync);
	}

	public async Task CreateRoomAsync(string[] targetUserNames) {
		if (IsInRoom) return;
		if (targetUserNames.Length == 0) return;

		// Запоминаем, что именно мы должны сгенерировать общий ключ.
		_isRoomCreator = true;

		await SendPacketAsync(ClientPacketType.RoomCreateRequest, new RoomCreatePayload(targetUserNames));
	}

	public async Task LeaveRoomAsync() {
		if (!IsInRoom) return;

		await SendPacketAsync(ClientPacketType.RoomLeaveRequest, new { });

		// Очистка данных комнаты при выходе.
		CurrentRoomId = null;
		_isRoomCreator = false;
		_roomAes?.Dispose();
		_roomAes = null;
		_participantRsaKeys.Clear();
	}

	public async Task<bool> SendMessageAsync(string text) {
		if (_roomAes is null) {
			_Logger.Warn("Установка защищенного соединения...");
			return false;
		}

		// Шифрование сообщения общим ключом AES.
		byte[] plainBytes = Encoding.UTF8.GetBytes(text);
		byte[] encryptedBytes;

		using (var encryptor = _roomAes.CreateEncryptor()) {
			encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
		}

		var msgPayload = new RoomMessagePayload(UserName, Convert.ToBase64String(encryptedBytes));

		await SendPacketAsync(ClientPacketType.RoomMessageRequest, msgPayload);
		return true;
	}

	public async Task SendPublicKeyAsync() {
		var rsaPayload = new RsaKeyPayload(UserName, _myPublicKeyXml);
		var msgPayload = new RoomKeyPayload(UserName, JsonSerializer.Serialize(rsaPayload));

		await SendPacketAsync(ClientPacketType.RoomKeyExchangeRequest, msgPayload);

		//Logger.Info("Публичный ключ отправлен.");
	}

	public void Disconnect() {
		_tcpClient?.Close();
		OnDisconnected?.Invoke();
	}

	#endregion

	#region Private API

	private async Task SendPacketAsync<T>(ClientPacketType type, T data) {
		if (_writer is null) return;

		try {
			string jsonPacket = ClientPacket.Serialize(type, data);
			await _writer.WriteLineAsync(jsonPacket);
		}
		catch (Exception) {
			_Logger.Warn("Попытка отправки в разорванное соединение проигнорирована.");
		}
	}

	private async Task ReceiveLoopAsync() {
		if (_reader is null) return;

		try {
			while (true) {
				string? json = await _reader.ReadLineAsync();
				if (json is null) break;

				var packet = JsonSerializer.Deserialize<ServerPacket>(json);
				if (packet is null) continue;

				ProcessServerPacket(packet);
			}
		}
		finally {
			Disconnect();
		}
	}

	private void ProcessServerPacket(ServerPacket packet) {
		switch (packet.Type) {

			case ServerPacketType.LobbyUpdate:
				var lobby = JsonSerializer.Deserialize<LobbyUpdatePayload>(packet.Payload);
				if (lobby is null) break;

				OnLobbyUpdated?.Invoke(lobby.Users);
				break;

			case ServerPacketType.RoomJoinEvent:
				var joinInfo = JsonSerializer.Deserialize<RoomJoinPayload>(packet.Payload);
				if (joinInfo is null) break;

				// Генерация AES-ключа.
				if (_isRoomCreator) {
					EnsureAesKeyGenerated();
				}

				CurrentRoomId = joinInfo.RoomId;
				OnRoomJoined?.Invoke(joinInfo.RoomId, joinInfo.Message);
				_ = SendPublicKeyAsync();

				break;

			case ServerPacketType.RoomMessageEvent:
				var msg = JsonSerializer.Deserialize<RoomMessagePayload>(packet.Payload);
				if (msg is null) break;

				HandleIncomingMessage(msg);
				break;

			case ServerPacketType.RoomKeyExchangeEvent:
				var keyMsg = JsonSerializer.Deserialize<RoomKeyPayload>(packet.Payload);
				if (keyMsg is null) break;

				ProcessKeyExchange(keyMsg.KeyPacketJson);
				break;

			case ServerPacketType.RoomSystemEvent:
				var sysEvent = JsonSerializer.Deserialize<RoomSystemPayload>(packet.Payload);
				if (sysEvent is null) break;

				OnSystemMessage?.Invoke(sysEvent.Text);
				break;
		}
	}

	private void HandleIncomingMessage(RoomMessagePayload msg) {
		if (_roomAes is null) return;

		try {
			// Расшифровка обычного сообщения ключом AES.
			byte[] encryptedBytes = Convert.FromBase64String(msg.EncryptedData);

			using var decryptor = _roomAes.CreateDecryptor();
			byte[] decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);

			string plainText = Encoding.UTF8.GetString(decryptedBytes);
			OnMessageReceived?.Invoke(msg.SenderName, plainText);
		}
		catch {
			_Logger.Error($"Ошибка расшифровки от {msg.SenderName}.");
		}
	}

	private void ProcessKeyExchange(string keyPacketJson) {
		// Определяем тип пакета по наличию уникальных свойств.

		// Если прилетел публичный RSA ключ.
		if (keyPacketJson.Contains("PublicKeyXml")) {
			var rsaData = JsonSerializer.Deserialize<RsaKeyPayload>(keyPacketJson);
			if (rsaData is null || rsaData.SenderName == UserName) return;

			// Сохраняем публичный ключ нового участника.
			var participantRsa = RSA.Create();
			participantRsa.FromXmlString(rsaData.PublicKeyXml);
			_participantRsaKeys[rsaData.SenderName] = participantRsa;

			// Даём AES-ключ новичку (только если мы создатель).
			if (_isRoomCreator && _participantRsaKeys.TryGetValue(rsaData.SenderName, out var storedRsa)) {
				_ = SendAesKeyToUserAsync(rsaData.SenderName, storedRsa);
			}
		}

		// Если прилетел общий AES ключ.
		else if (keyPacketJson.Contains("EncryptedAesKeyBase64")) {
			var aesData = JsonSerializer.Deserialize<AesKeyPayload>(keyPacketJson);
			if (aesData is null || aesData.TargetUserName != UserName) return;

			// Расшифровка AES-ключа своим приватным RSA-ключом.
			byte[] encKey = Convert.FromBase64String(aesData.EncryptedAesKeyBase64);
			byte[] encIv = Convert.FromBase64String(aesData.EncryptedAesIvBase64);

			byte[] aesKey = _rsa.Decrypt(encKey, RSAEncryptionPadding.OaepSHA256);
			byte[] aesIv = _rsa.Decrypt(encIv, RSAEncryptionPadding.OaepSHA256);

			_roomAes = Aes.Create();
			_roomAes.Key = aesKey;
			_roomAes.IV = aesIv;

			//Logger.Info("Общий ключ комнаты установлен.");
		}
	}

	private async Task SendAesKeyToUserAsync(string targetUserName, RSA targetRsa) {

		// Убеждаемся, что AES-ключ чат-комнаты существует.
		EnsureAesKeyGenerated();

		// Шифрование частей AES публичным ключом получателя.
		byte[] encKey = targetRsa.Encrypt(_roomAes.Key, RSAEncryptionPadding.OaepSHA256);
		byte[] encIv = targetRsa.Encrypt(_roomAes.IV, RSAEncryptionPadding.OaepSHA256);

		var aesPayload = new AesKeyPayload(UserName, targetUserName, Convert.ToBase64String(encKey), Convert.ToBase64String(encIv));
		var msgPayload = new RoomKeyPayload(UserName, JsonSerializer.Serialize(aesPayload));

		await SendPacketAsync(ClientPacketType.RoomKeyExchangeRequest, msgPayload);
	}

	private void EnsureAesKeyGenerated() {
		if (_roomAes is not null) return;

		_roomAes = Aes.Create();
		_roomAes.GenerateKey();
		_roomAes.GenerateIV();
	}

	#endregion
}
