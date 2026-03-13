namespace Sts2Mod.StateBridge.Logging;

public sealed class ConsoleBridgeLogger : IBridgeLogger
{
    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? exception = null)
    {
        Write("ERROR", exception is null ? message : $"{message}: {exception.Message}");
    }

    private static void Write(string level, string message)
    {
        Console.WriteLine($"[{DateTimeOffset.Now:O}] [{level}] {message}");
    }
}
