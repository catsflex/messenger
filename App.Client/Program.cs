using System;
using System.Linq;
using System.Threading.Tasks;

using Common;
using Common.Logging;

using Messenger.Client;

namespace App.Client;

internal static class Program {

	private static readonly Logger _ClientLogger = new("Client");
	private static readonly Logger _SystemLogger = new("System");

	private static string PromptForUserName() {
		Console.WriteLine("Установите отображаемое имя пользователя.");
		Console.WriteLine("Ограничения:");
		Console.WriteLine($" * От {UserNameValidator.MinLength} до {UserNameValidator.MaxLength} символов;");
		Console.WriteLine(" * Не должно содержать пробелов.");
		Console.WriteLine();

		while (true) {
			Console.Write("Имя: ");

			string? input = Console.ReadLine();
			if (UserNameValidator.IsValid(input)) {
				Console.WriteLine();
				Console.WriteLine();
				return input!;
			}

			_ClientLogger.Error("Введённое имя не соответствует правилам! Попробуйте снова.");
			Console.WriteLine();
		}
	}

	private static string PromptForServerIp() {
		Console.WriteLine("Укажите IP-адрес сервера (IPv4) для подключения.");
		Console.WriteLine("Оставьте поле пустым для подключения по локальной сети.");
		Console.WriteLine();

		Console.Write("IP-адрес: ");

		string? input = Console.ReadLine()?.Trim();
		Console.WriteLine();
		Console.WriteLine();
		return string.IsNullOrWhiteSpace(input) ? Constants.LocalHostIPv4 : input;
	}

	private static void SubscribeToClientEvents(ChatClient client) {
		client.OnLobbyUpdated += users => {
			Console.Clear();

			Console.WriteLine("Вы находитесь в лобби.");
			Console.WriteLine($" * Ваше имя: {client.UserName}.");
			Console.WriteLine($" * Пользователей онлайн: {users.Length}.");
			Console.WriteLine($" * Список: {{ {string.Join(", ", users)} }}.");
			Console.WriteLine($" * Ознакомьтесь с функционалом чат-сервера: {InputHandler.CommandPrefix}{InputHandler.CmdHelp}.");

			Console.WriteLine();
		};

		client.OnRoomJoined += (roomId, message) => {
			Console.Clear();

			Console.WriteLine("Вы находитесь в чат-комнате.");
			Console.WriteLine($" * {message}");
			Console.WriteLine($" * ID комнаты: {roomId}.");
			Console.WriteLine($" * Для выхода напишите: {InputHandler.CommandPrefix}{InputHandler.CmdLeave}.");

			Console.WriteLine();
		};

		client.OnSystemMessage += text => { _SystemLogger.Info(text); };

		client.OnMessageReceived += (sender, text) => { Console.WriteLine($"[{sender}]: {text}"); };

		client.OnDisconnected += () => {
			Console.WriteLine();
			_ClientLogger.Info("Соединение потеряно. Нажмите 'Enter' для выхода.");
			Console.ReadKey();
			Environment.Exit(0);
		};
	}

	private static async Task Main() {
		string userName = PromptForUserName();
		string serverIp = PromptForServerIp();

		var client = new ChatClient(userName);
		var inputHandler = new InputHandler(client);

		SubscribeToClientEvents(client);

		try {
			_ClientLogger.Info("Подключение...");
			await client.ConnectAsync(serverIp, Constants.ServerPort);
		}
		catch (Exception ex) {
			_ClientLogger.Error(ex.Message);
			Console.ReadKey();
			return;
		}

		while (true) {
			string? input = Console.ReadLine();
			if (!client.IsConnected) break;
			await inputHandler.ProcessInputAsync(input);
		}
	}
}
