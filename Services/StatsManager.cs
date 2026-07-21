using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Spectre.Services;

public class StatsManager
{
	public class TopItem
	{
		public string Name { get; set; } = "";

		public string Artist { get; set; } = "";

		public string ThumbUrl { get; set; } = "";

		public int PlayCount { get; set; }
	}

	public class RecentItem
	{
		public string Title { get; set; } = "";

		public string Artist { get; set; } = "";

		public string ThumbUrl { get; set; } = "";

		public long Timestamp { get; set; }
	}

	private static string _dbPath;

	private static string _connectionString;

	public static long TotalScrobbles { get; private set; }

	static StatsManager()
	{
		string spectreDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spectre");
		if (!Directory.Exists(spectreDir))
		{
			Directory.CreateDirectory(spectreDir);
		}
		_dbPath = Path.Combine(spectreDir, "local_stats.db");
		_connectionString = "Data Source=" + _dbPath;
		InitializeDatabase();
	}

	private static void InitializeDatabase()
	{
		using SqliteConnection connection = new SqliteConnection(_connectionString);
		connection.Open();
		using (SqliteCommand pragmaCmd = new SqliteCommand("PRAGMA journal_mode=WAL;", connection))
		{
			pragmaCmd.ExecuteNonQuery();
		}
		using (SqliteCommand command = new SqliteCommand("\r\n                    CREATE TABLE IF NOT EXISTS Scrobbles (\r\n                        Id INTEGER PRIMARY KEY AUTOINCREMENT,\r\n                        Title TEXT NOT NULL,\r\n                        Artist TEXT NOT NULL,\r\n                        Album TEXT,\r\n                        Timestamp INTEGER NOT NULL,\r\n                        DurationMs INTEGER DEFAULT 0,\r\n                        ThumbUrl TEXT DEFAULT ''\r\n                    )", connection))
		{
			command.ExecuteNonQuery();
		}
		try
		{
			using SqliteCommand cmd = new SqliteCommand("ALTER TABLE Scrobbles ADD COLUMN DurationMs INTEGER DEFAULT 0", connection);
			cmd.ExecuteNonQuery();
		}
		catch
		{
		}
		try
		{
			using SqliteCommand cmd2 = new SqliteCommand("ALTER TABLE Scrobbles ADD COLUMN ThumbUrl TEXT DEFAULT ''", connection);
			cmd2.ExecuteNonQuery();
		}
		catch
		{
		}
		using (SqliteCommand command2 = new SqliteCommand("CREATE TABLE IF NOT EXISTS GlobalStats (Id INTEGER PRIMARY KEY, TotalListeningMs INTEGER)", connection))
		{
			command2.ExecuteNonQuery();
		}
		using (SqliteCommand command3 = new SqliteCommand("INSERT OR IGNORE INTO GlobalStats (Id, TotalListeningMs) VALUES (1, 0)", connection))
		{
			command3.ExecuteNonQuery();
		}
		using SqliteCommand command4 = new SqliteCommand("SELECT COUNT(*) FROM Scrobbles", connection);
		object result = command4.ExecuteScalar();
		if (result != null && result != DBNull.Value)
		{
			TotalScrobbles = Convert.ToInt64(result);
		}
	}

	public static async Task RecordPlayAsync(string title, string artist, string album, string thumbUrl, long timestamp, long durationMs)
	{
		await Task.Run(async delegate
		{
			int retries = 3;
			while (retries > 0)
			{
				try
				{
					using (SqliteConnection connection = new SqliteConnection(_connectionString))
					{
						connection.Open();
						using SqliteCommand command = new SqliteCommand("\r\n                                INSERT INTO Scrobbles (Title, Artist, Album, ThumbUrl, Timestamp, DurationMs)\r\n                                VALUES (@title, @artist, @album, @thumbUrl, @timestamp, @durationMs)", connection);
						command.Parameters.AddWithValue("@title", title ?? "Unknown");
						command.Parameters.AddWithValue("@artist", artist ?? "Unknown");
						command.Parameters.AddWithValue("@album", album ?? "");
						command.Parameters.AddWithValue("@thumbUrl", thumbUrl ?? "");
						command.Parameters.AddWithValue("@timestamp", timestamp);
						command.Parameters.AddWithValue("@durationMs", durationMs);
						command.ExecuteNonQuery();
					}
					TotalScrobbles++;
					break;
				}
				catch (SqliteException ex) when (ex.SqliteErrorCode == 5)
				{
					retries--;
					if (retries == 0)
					{
						break;
					}
					await Task.Delay(100);
				}
				catch (Exception)
				{
					break;
				}
			}
		});
	}

	public static async Task AddListeningTimeAsync(long msToAdd)
	{
		await Task.Run(delegate
		{
			try
			{
				using SqliteConnection sqliteConnection = new SqliteConnection(_connectionString);
				sqliteConnection.Open();
				using SqliteCommand sqliteCommand = new SqliteCommand("UPDATE GlobalStats SET TotalListeningMs = TotalListeningMs + @ms WHERE Id = 1", sqliteConnection);
				sqliteCommand.Parameters.AddWithValue("@ms", msToAdd);
				sqliteCommand.ExecuteNonQuery();
			}
			catch
			{
			}
		});
	}

	public static async Task<long> GetTotalListeningMsAsync()
	{
		return await Task.Run(delegate
		{
			using (SqliteConnection sqliteConnection = new SqliteConnection(_connectionString))
			{
				sqliteConnection.Open();
				using SqliteCommand sqliteCommand = new SqliteCommand("SELECT TotalListeningMs FROM GlobalStats WHERE Id = 1", sqliteConnection);
				object obj = sqliteCommand.ExecuteScalar();
				if (obj != null && obj != DBNull.Value)
				{
					return Convert.ToInt64(obj);
				}
			}
			return 0L;
		});
	}

	public static async Task<long> GetTotalListeningMinutesAsync()
	{
		return await GetTotalListeningMsAsync() / 60000;
	}

	public static async Task<int> GetUniqueArtistsCountAsync()
	{
		return await Task.Run(delegate
		{
			using (SqliteConnection sqliteConnection = new SqliteConnection(_connectionString))
			{
				sqliteConnection.Open();
				using SqliteCommand sqliteCommand = new SqliteCommand("SELECT COUNT(DISTINCT Artist) FROM Scrobbles", sqliteConnection);
				object obj = sqliteCommand.ExecuteScalar();
				if (obj != null && obj != DBNull.Value)
				{
					return Convert.ToInt32(obj);
				}
			}
			return 0;
		});
	}

	public static async Task<List<TopItem>> GetTopArtistsAsync(int limit = 10)
	{
		return await Task.Run(delegate
		{
			Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			Dictionary<string, string> thumbDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			using (SqliteConnection sqliteConnection = new SqliteConnection(_connectionString))
			{
				sqliteConnection.Open();
				using SqliteCommand sqliteCommand = new SqliteCommand("SELECT Artist, ThumbUrl FROM Scrobbles", sqliteConnection);
				using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
				while (sqliteDataReader.Read())
				{
					string text = sqliteDataReader.GetString(0);
					string value = (sqliteDataReader.IsDBNull(1) ? "" : sqliteDataReader.GetString(1));
					string[] array = text.Split(new string[1] { ", " }, StringSplitOptions.RemoveEmptyEntries);
					for (int i = 0; i < array.Length; i++)
					{
						string text2 = array[i].Trim();
						if (!string.IsNullOrEmpty(text2))
						{
							if (!dictionary.ContainsKey(text2))
							{
								dictionary[text2] = 0;
								thumbDict[text2] = "";
							}
							dictionary[text2]++;
							if (string.IsNullOrEmpty(thumbDict[text2]) && !string.IsNullOrEmpty(value))
							{
								thumbDict[text2] = value;
							}
						}
					}
				}
			}
			return (from kv in dictionary
				select new TopItem
				{
					Name = kv.Key,
					PlayCount = kv.Value,
					ThumbUrl = thumbDict[kv.Key]
				} into x
				orderby x.PlayCount descending
				select x).Take(limit).ToList();
		});
	}

	public static async Task<List<TopItem>> GetTopTracksAsync(int limit = 10)
	{
		return await Task.Run(delegate
		{
			List<TopItem> list = new List<TopItem>();
			using SqliteConnection sqliteConnection = new SqliteConnection(_connectionString);
			sqliteConnection.Open();
			using SqliteCommand sqliteCommand = new SqliteCommand("\r\n                        SELECT Title, Artist, MAX(ThumbUrl) as ThumbUrl, COUNT(*) as PlayCount \r\n                        FROM Scrobbles \r\n                        GROUP BY Title COLLATE NOCASE, Artist COLLATE NOCASE\r\n                        ORDER BY PlayCount DESC \r\n                        LIMIT @limit", sqliteConnection);
			sqliteCommand.Parameters.AddWithValue("@limit", limit);
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				list.Add(new TopItem
				{
					Name = sqliteDataReader.GetString(0),
					Artist = sqliteDataReader.GetString(1),
					ThumbUrl = (sqliteDataReader.IsDBNull(2) ? "" : sqliteDataReader.GetString(2)),
					PlayCount = sqliteDataReader.GetInt32(3)
				});
			}
			return list;
		});
	}

	public static async Task<List<TopItem>> GetTopAlbumsAsync(int limit = 10)
	{
		return await Task.Run(delegate
		{
			List<TopItem> list = new List<TopItem>();
			using SqliteConnection sqliteConnection = new SqliteConnection(_connectionString);
			sqliteConnection.Open();
			using SqliteCommand sqliteCommand = new SqliteCommand("\r\n                        SELECT Album, Artist, MAX(ThumbUrl) as ThumbUrl, COUNT(*) as PlayCount \r\n                        FROM Scrobbles \r\n                        WHERE Album != '' AND Album IS NOT NULL\r\n                        GROUP BY Album COLLATE NOCASE, Artist COLLATE NOCASE\r\n                        ORDER BY PlayCount DESC \r\n                        LIMIT @limit", sqliteConnection);
			sqliteCommand.Parameters.AddWithValue("@limit", limit);
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				list.Add(new TopItem
				{
					Name = sqliteDataReader.GetString(0),
					Artist = sqliteDataReader.GetString(1),
					ThumbUrl = (sqliteDataReader.IsDBNull(2) ? "" : sqliteDataReader.GetString(2)),
					PlayCount = sqliteDataReader.GetInt32(3)
				});
			}
			return list;
		});
	}

	public static async Task<List<RecentItem>> GetRecentTracksAsync(int limit = 20)
	{
		return await Task.Run(delegate
		{
			List<RecentItem> list = new List<RecentItem>();
			using SqliteConnection sqliteConnection = new SqliteConnection(_connectionString);
			sqliteConnection.Open();
			using SqliteCommand sqliteCommand = new SqliteCommand("\r\n                        SELECT Title, Artist, Timestamp, ThumbUrl \r\n                        FROM Scrobbles \r\n                        ORDER BY Timestamp DESC \r\n                        LIMIT @limit", sqliteConnection);
			sqliteCommand.Parameters.AddWithValue("@limit", limit);
			using SqliteDataReader sqliteDataReader = sqliteCommand.ExecuteReader();
			while (sqliteDataReader.Read())
			{
				list.Add(new RecentItem
				{
					Title = sqliteDataReader.GetString(0),
					Artist = sqliteDataReader.GetString(1),
					Timestamp = sqliteDataReader.GetInt64(2),
					ThumbUrl = (sqliteDataReader.IsDBNull(3) ? "" : sqliteDataReader.GetString(3))
				});
			}
			return list;
		});
	}
}
