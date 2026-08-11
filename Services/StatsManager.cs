using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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

	private class Scrobble
	{
		public string Title { get; set; } = "";
		public string Artist { get; set; } = "";
		public string Album { get; set; } = "";
		public string ThumbUrl { get; set; } = "";
		public long Timestamp { get; set; }
		public long DurationMs { get; set; }
	}

	private class StatsData
	{
		public long TotalListeningMs { get; set; }
		public List<Scrobble> Scrobbles { get; set; } = new List<Scrobble>();
	}

	private static readonly string _dbPath;
	private static StatsData _data = new StatsData();
	private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
	private static bool _isLoaded = false;

	public static long TotalScrobbles => _data.Scrobbles.Count;

	static StatsManager()
	{
		string spectreDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spectre");
		if (!Directory.Exists(spectreDir))
		{
			Directory.CreateDirectory(spectreDir);
		}
		_dbPath = Path.Combine(spectreDir, "local_stats.json");
	}

	private static async Task EnsureLoadedAsync()
	{
		if (_isLoaded) return;

		await _semaphore.WaitAsync();
		try
		{
			if (_isLoaded) return;

			if (File.Exists(_dbPath))
			{
				try
				{
					string json = await File.ReadAllTextAsync(_dbPath);
					_data = JsonSerializer.Deserialize<StatsData>(json) ?? new StatsData();
				}
				catch
				{
					_data = new StatsData();
				}
			}
			_isLoaded = true;
		}
		finally
		{
			_semaphore.Release();
		}
	}

	private static async Task SaveAsync()
	{
		try
		{
			string json = JsonSerializer.Serialize(_data);
			await File.WriteAllTextAsync(_dbPath, json);
		}
		catch { }
	}

	public static async Task RecordPlayAsync(string title, string artist, string album, string thumbUrl, long timestamp, long durationMs)
	{
		await EnsureLoadedAsync();

		await _semaphore.WaitAsync();
		try
		{
			_data.Scrobbles.Add(new Scrobble
			{
				Title = title ?? "Unknown",
				Artist = artist ?? "Unknown",
				Album = album ?? "",
				ThumbUrl = thumbUrl ?? "",
				Timestamp = timestamp,
				DurationMs = durationMs
			});
			await SaveAsync();
		}
		finally
		{
			_semaphore.Release();
		}
	}

	public static async Task AddListeningTimeAsync(long msToAdd)
	{
		await EnsureLoadedAsync();

		await _semaphore.WaitAsync();
		try
		{
			_data.TotalListeningMs += msToAdd;
			await SaveAsync();
		}
		finally
		{
			_semaphore.Release();
		}
	}

	public static async Task<long> GetTotalListeningMsAsync()
	{
		await EnsureLoadedAsync();
		return _data.TotalListeningMs;
	}

	public static async Task<long> GetTotalListeningMinutesAsync()
	{
		return await GetTotalListeningMsAsync() / 60000;
	}

	public static async Task<int> GetUniqueArtistsCountAsync()
	{
		await EnsureLoadedAsync();
		return _data.Scrobbles.Select(x => x.Artist).Distinct(StringComparer.OrdinalIgnoreCase).Count();
	}

	public static async Task<List<TopItem>> GetTopArtistsAsync(int limit = 10)
	{
		await EnsureLoadedAsync();
		
		var grouped = _data.Scrobbles
			.GroupBy(x => x.Artist, StringComparer.OrdinalIgnoreCase)
			.Select(g => new TopItem
			{
				Name = g.Key,
				Artist = "",
				ThumbUrl = g.OrderByDescending(x => string.IsNullOrEmpty(x.ThumbUrl) ? 0 : 1).FirstOrDefault()?.ThumbUrl ?? "",
				PlayCount = g.Count()
			})
			.OrderByDescending(x => x.PlayCount)
			.Take(limit)
			.ToList();

		return grouped;
	}

	public static async Task<List<TopItem>> GetTopTracksAsync(int limit = 10)
	{
		await EnsureLoadedAsync();

		var grouped = _data.Scrobbles
			.GroupBy(x => new { Title = x.Title.ToLowerInvariant(), Artist = x.Artist.ToLowerInvariant() })
			.Select(g => new TopItem
			{
				Name = g.First().Title,
				Artist = g.First().Artist,
				ThumbUrl = g.OrderByDescending(x => string.IsNullOrEmpty(x.ThumbUrl) ? 0 : 1).FirstOrDefault()?.ThumbUrl ?? "",
				PlayCount = g.Count()
			})
			.OrderByDescending(x => x.PlayCount)
			.Take(limit)
			.ToList();

		return grouped;
	}

	public static async Task<List<TopItem>> GetTopAlbumsAsync(int limit = 10)
	{
		await EnsureLoadedAsync();

		var grouped = _data.Scrobbles
			.Where(x => !string.IsNullOrEmpty(x.Album))
			.GroupBy(x => new { Album = x.Album.ToLowerInvariant(), Artist = x.Artist.ToLowerInvariant() })
			.Select(g => new TopItem
			{
				Name = g.First().Album,
				Artist = g.First().Artist,
				ThumbUrl = g.OrderByDescending(x => string.IsNullOrEmpty(x.ThumbUrl) ? 0 : 1).FirstOrDefault()?.ThumbUrl ?? "",
				PlayCount = g.Count()
			})
			.OrderByDescending(x => x.PlayCount)
			.Take(limit)
			.ToList();

		return grouped;
	}

	public static async Task<List<RecentItem>> GetRecentTracksAsync(int limit = 20)
	{
		await EnsureLoadedAsync();

		return _data.Scrobbles
			.OrderByDescending(x => x.Timestamp)
			.Take(limit)
			.Select(x => new RecentItem
			{
				Title = x.Title,
				Artist = x.Artist,
				Timestamp = x.Timestamp,
				ThumbUrl = x.ThumbUrl
			})
			.ToList();
	}
}
