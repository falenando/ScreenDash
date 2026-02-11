using System.Diagnostics;

namespace RemoteSupport.Service;

public sealed class ServiceLogger
{
    private readonly string _logPath;
    private readonly object _sync = new();
    private const string EventSource = "ScreenDash.RemoteSupport";

    public ServiceLogger()
    {
        _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "service.log");
        EnsureEventLogSource();
    }

    public void Info(string message) => Write("INFO", message);

    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {level} {message}";
        lock (_sync)
        {
            try
            {
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch { }
        }

        try
        {
            EventLog.WriteEntry(EventSource, line, level == "ERROR" ? EventLogEntryType.Error : EventLogEntryType.Information);
        }
        catch { }
    }

    private static void EnsureEventLogSource()
    {
        try
        {
            if (!EventLog.SourceExists(EventSource))
            {
                EventLog.CreateEventSource(EventSource, "Application");
            }
        }
        catch { }
    }
}
