using System;
using System.Net.Sockets;
using System.Threading.Tasks;

using Common;
using Common.Logging;

using Messenger.Server;

namespace App.Server;

internal static class Program {

	private static readonly Logger _Logger = new("Server App");

	private static Task CreateServerCanceler(CancellationTokenSource cts) {
		return Task.Run(() => {
			while (!cts.Token.IsCancellationRequested) {
				var keyInfo = Console.ReadKey(true);
				if (keyInfo.Key != ConsoleKey.Escape) continue;

				cts.Cancel();
				break;
			}
		});
	}

	private static string? PromptForCertificatePassword() {
		return "secret_password";

		const ConsoleColor hideColor = ConsoleColor.Black;

		_Logger.Info("Введите пароль от сертификата server.pfx: ", false);
		Console.ForegroundColor = hideColor;
		Console.BackgroundColor = hideColor;
		string? certPassword = Console.ReadLine();

		Console.ResetColor();

		return string.IsNullOrWhiteSpace(certPassword) ? null : certPassword;
	}

	private static async Task Main() {
		string? certPassword = PromptForCertificatePassword();
		if (certPassword is null) return;

		using var cts = new CancellationTokenSource();
		_ = CreateServerCanceler(cts);

		try {
			var server = new ChatServer(Constants.ServerPort, certPassword);
			await server.StartAsync(cts.Token);
		}
		catch (Exception ex) {
			_Logger.Error("Критическая ошибка!");
			_Logger.Error(ex.Message);
		}
		finally {
			Console.ReadKey(true);
		}
	}
}
