namespace Common.Logging;

public class Logger : ILogger {

	private readonly string _id;
	private static readonly object _Lock = new();

	public bool ShouldShowTime { get; set; }
	public bool ShouldShowId { get; set; }

	private const ConsoleColor _TimestampColor = ConsoleColor.Green;
	private const ConsoleColor _IdColor = ConsoleColor.Cyan;
	private const ConsoleColor _InfoColor = ConsoleColor.Gray;
	private const ConsoleColor _WarningColor = ConsoleColor.DarkYellow;
	private const ConsoleColor _ErrorColor = ConsoleColor.Red;

	public Logger(string id, bool shouldShowTime = true, bool shouldShowId = true) {
		_id = id;
		ShouldShowTime = shouldShowTime;
		ShouldShowId = shouldShowId;
	}

	public void Info(string message, bool newLine = true) {
		Log(_InfoColor, message, newLine);
	}

	public void Warn(string message, bool newLine = true) {
		Log(_WarningColor, message, newLine);
	}

	public void Error(string message, bool newLine = true) {
		Log(_ErrorColor, message, newLine);
	}

	public static void EmptyLine() {
		lock (_Lock) {
			Console.WriteLine();
		}
	}

	private void Log(ConsoleColor color, string message, bool newLine) {
		lock (_Lock) {

			// Время
			if (ShouldShowTime) {
				Console.ForegroundColor = _TimestampColor;
				Console.Write($"[{DateTime.Now:HH:mm:ss.fff}] ");
			}

			// Айди
			if (ShouldShowId) {
				Console.ForegroundColor = _IdColor;
				Console.Write($"[{_id}] ");
			}

			// Сообщение
			Console.ForegroundColor = color;
			Console.Write(message);

			if (newLine) {
				Console.WriteLine();
			}

			Console.ResetColor();
		}
	}
}
