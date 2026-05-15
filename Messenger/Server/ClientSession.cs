using System;
using System.IO;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Common;
using Common.Enums;
using Common.Packets;

namespace Messenger.Server;

internal class ClientSession {

	#region Fields

	// Защищённый канал связи между клиентом и сервером
	private readonly SslStream _sslStream;
	private readonly StreamReader _reader;
	private readonly StreamWriter _writer;

	#endregion

	#region Properties

	public string? UserName { get; set; }
	public string? CurrentRoomId { get; set; }
	public bool IsInRoom => CurrentRoomId is not null;

	#endregion

	#region Constructors

	public ClientSession(SslStream sslStream) {
		_sslStream = sslStream;
		_reader = new StreamReader(sslStream, Encoding.UTF8);

		// Обязательно включаем AutoFlush, чтобы сообщения отправлялись
		// сразу же без ожидания заполнения внутреннего буфера.
		_writer = new StreamWriter(sslStream, Encoding.UTF8) { AutoFlush = true };
	}

	#endregion

	#region Events

	public event Action<ClientSession, ClientPacket>? OnPacketReceived;
	public event Action<ClientSession>? OnDisconnected;

	#endregion

	#region Public API

	public async Task StartListeningAsync() {
		try {
			while (true) {
				string? json = await _reader.ReadLineAsync();
				if (json is null) break;

				var packet = JsonSerializer.Deserialize<ClientPacket>(json);
				if (packet is null) continue;

				OnPacketReceived?.Invoke(this, packet);
			}
		}
		finally {
			OnDisconnected?.Invoke(this);
		}
	}

	public async Task SendPacketAsync<T>(ServerPacketType type, T data) {
		try {
			string jsonPacket = ServerPacket.Serialize(type, data);
			await _writer.WriteLineAsync(jsonPacket);
		}
		catch {
			OnDisconnected?.Invoke(this);
		}
	}

	public async Task SendRawPayloadAsync(ServerPacketType type, string rawPayload) {
		try {
			var packet = new ServerPacket { Type = type, Payload = rawPayload };
			string jsonPacket = JsonSerializer.Serialize(packet);
			await _writer.WriteLineAsync(jsonPacket);
		}
		catch {
			OnDisconnected?.Invoke(this);
		}
	}

	public void Close() {
		_sslStream.Close();
	}

	#endregion
}
