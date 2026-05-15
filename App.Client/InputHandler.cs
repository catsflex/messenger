using System;
using System.Linq;
using System.Threading.Tasks;

using Common.Logging;

using Messenger.Client;

namespace App.Client;

internal class InputHandler {

	#region Fields

	private static readonly Logger _Logger = new("Input Handler", false, true);

	private readonly ChatClient _client;
	private string[] _onlineUsers = [];

	#endregion

	#region Commands

	public const string CommandPrefix = "/";

	public const string CmdHelp = "help";

	public const string CmdChat = "chat";
	public const string CmdClear = "clear";
	public const string CmdLeave = "leave";
	public const string CmdList = "list";

	#endregion

	#region Constructors

	public InputHandler(ChatClient client) {
		_client = client;
		_client.OnLobbyUpdated += onlineUsers => _onlineUsers = onlineUsers;
	}

	#endregion

	#region Public API

	public async Task ProcessInputAsync(string? input) {
		if (string.IsNullOrWhiteSpace(input)) return;

		// Обычное сообщение.
		if (!input.StartsWith(CommandPrefix)) {
			if (_client.IsInRoom) {
				await _client.SendMessageAsync(input);
			}
			else {
				SuggestHelp();
			}

			return;
		}

		// Все части команды, включая саму команду.
		string[] parts = input.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries);

		// Команда без префикса команды (например, 'chat' вместо '/chat').
		string command = parts[0][CommandPrefix.Length..];

		await ProcessCommandAsync(command, parts);
	}

	#endregion

	#region Private API

	private static void SuggestHelp() {
		_Logger.Warn($"Используйте '{CommandPrefix}{CmdHelp}' для просмотра списка доступных команд.");
		Logger.EmptyLine();
	}

	private async Task ProcessCommandAsync(string command, string[] parts) {
		switch (command) {

			case CmdChat:

				// Команда 'chat' не работает в комнате.
				if (_client.IsInRoom) {
					_Logger.Warn($"Вы уже в комнате. Сначала напишите '{CommandPrefix}{CmdLeave}'.");
					Logger.EmptyLine();
					break;
				}

				// Аргументы команды (пользователи) без неё самой.
				string[] targets = parts.Skip(1).ToArray();

				if (targets.Length == 0) {
					_Logger.Warn("Недостаточно аргументов.");
					SuggestHelp();
					break;
				}

				if (targets.Length == 1 && targets[0] == _client.UserName) {
					_Logger.Warn("Вы не можете создать чат-комнату с самим собой.");
					Logger.EmptyLine();
					break;
				}

				await _client.CreateRoomAsync(targets);
				break;

			case CmdClear:
				Console.Clear();
				break;

			case CmdLeave:
				if (!_client.IsInRoom) {
					_Logger.Warn("Вы не состоите в чат-комнате.");
					Logger.EmptyLine();
					break;
				}

				await _client.LeaveRoomAsync();
				Console.Clear();
				break;

			case CmdList:
				_Logger.Info(_onlineUsers.Length == 0
					? "В лобби никого нет. Вы не должны видеть это сообщение."
					: $"В сети: {{ {string.Join(", ", _onlineUsers)} }}."
				);

				Logger.EmptyLine();
				break;

			case CmdHelp:
				_Logger.Info("Список доступных команд:");
				_Logger.Info($" * '{CommandPrefix}{CmdChat} <Имя1> <Имя2> ...' - создание чат-комнаты с указанными пользователями;");
				_Logger.Info($" * '{CommandPrefix}{CmdClear}' - очищение истории;");
				_Logger.Info($" * '{CommandPrefix}{CmdLeave}' - выход из чат-комнаты;");
				_Logger.Info($" * '{CommandPrefix}{CmdList}' - список пользователей в сети.");
				Logger.EmptyLine();
				break;

			default:
				_Logger.Warn($"Неизвестная команда: '{CommandPrefix}{command}'.");
				SuggestHelp();
				break;
		}
	}

	#endregion
}
