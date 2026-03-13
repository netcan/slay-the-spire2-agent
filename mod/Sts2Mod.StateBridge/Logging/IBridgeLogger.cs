namespace Sts2Mod.StateBridge.Logging;

public interface IBridgeLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}
