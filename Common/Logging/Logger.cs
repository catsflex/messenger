namespace Common.Logging;

public class Logger : ILogger {

	private readonly string _id;
	private static readonly object Lock = new();

	public bool ShouldShowTime { get; set; }
	public bool ShouldShowId { get; set; }

	private const ConsoleColor TimestampColor = ConsoleColor.Green;
	private const ConsoleColor IdColor = ConsoleColor.Cyan;
	private const ConsoleColor InfoColor = ConsoleColor.Gray;
	private const ConsoleColor WarningColor = ConsoleColor.DarkYellow;
	private const ConsoleColor ErrorColor = ConsoleColor.Red;

	public Logger(string id, bool shouldShowTime = true, bool shouldShowId = true) {
		_id = id;
		ShouldShowTime = shouldShowTime;
		ShouldShowId = shouldShowId;
	}

	public void Info(string message, bool newLine = true) {
		Log(InfoColor, message, newLine);
	}

	public void Warn(string message, bool newLine = true) {
		Log(WarningColor, message, newLine);
	}

	public void Error(string message, bool newLine = true) {
		Log(ErrorColor, message, newLine);
	}

	public static void EmptyLine() {
		lock (Lock) {
			Console.WriteLine();
		}
	}

	private void Log(ConsoleColor color, string message, bool newLine) {
		lock (Lock) {

			// Время
			if (ShouldShowTime) {
				Console.ForegroundColor = TimestampColor;
				Console.Write($"[{DateTime.Now:HH:mm:ss.fff}] ");
			}

			// Айди
			if (ShouldShowId) {
				Console.ForegroundColor = IdColor;
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
