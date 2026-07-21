using System;
using System.Collections.Generic;
using System.IO;

namespace Spectre;

public enum LogLevel
{
	Info,
	Warning,
	Error,
	Success
}

public struct LogMessage
{
	public string Timestamp { get; set; }
	public string Message { get; set; }
	public LogLevel Level { get; set; }

	public string Formatted => $"[{Timestamp}] {Message}";
}

public static class AppLogger
{
	private static readonly object _lock = new object();

	private static readonly List<LogMessage> _recentEntries = new List<LogMessage>();

	private const int MaxEntries = 300;

	private static readonly string _logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spectre", "debug.log");

	public static event Action<LogMessage>? MessageLogged;

	public static void Log(string message, LogLevel level = LogLevel.Info)
	{
		var entry = new LogMessage
		{
			Timestamp = DateTime.Now.ToString("HH:mm:ss"),
			Message = message,
			Level = level
		};
		lock (_lock)
		{
			_recentEntries.Add(entry);
			if (_recentEntries.Count > MaxEntries)
			{
				_recentEntries.RemoveAt(0);
			}
		}
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_logPath));
			File.AppendAllText(_logPath, entry.Formatted + "\n");
		}
		catch
		{
		}
		try
		{
			AppLogger.MessageLogged?.Invoke(entry);
		}
		catch
		{
		}
		try
		{
			Console.WriteLine(entry.Formatted);
		}
		catch
		{
		}
	}

	public static IReadOnlyList<LogMessage> GetRecentEntries()
	{
		lock (_lock)
		{
			return _recentEntries.AsReadOnly();
		}
	}
}
