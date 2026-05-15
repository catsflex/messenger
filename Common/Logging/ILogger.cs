namespace Common.Logging;

public interface ILogger {
	public void Info(string message, bool newLine = true);
	public void Warn(string message, bool newLine = true);
	public void Error(string message, bool newLine = true);
}
