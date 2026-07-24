using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using System.Text.Json.Nodes;
using System.Text.Json;
using TagLib;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace Spectre;

public class BackendService
{
	private sealed class WebmOpusData
	{
		public byte[] OpusHead { get; set; } = Array.Empty<byte>();

		public int TrackNumber { get; set; }

		public List<byte[]> Packets { get; } = new List<byte[]>();
	}

	private readonly struct EbmlElement(ulong id, long dataOffset, long dataSize, long nextOffset)
	{
		public ulong Id { get; } = id;

		public long DataOffset { get; } = dataOffset;

		public long DataSize { get; } = dataSize;

		public long NextOffset { get; } = nextOffset;

		public long EndOffset => DataOffset + DataSize;
	}

	private static BackendService? _instance;

	public static string AuthFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spectre", "headers_auth.json");

	private readonly SemaphoreSlim _searchSemaphore = new SemaphoreSlim(2, 2);

	private readonly SemaphoreSlim _visitorDataLock = new SemaphoreSlim(1, 1);

	private string? _visitorData;

	private readonly HttpClient _client;

	private readonly YoutubeClient _youtubeClient;

	private readonly ConcurrentDictionary<string, Task<MainWindow.PlaybackStreamInfo>> _inflightStreamRequests = new ConcurrentDictionary<string, Task<MainWindow.PlaybackStreamInfo>>();

	private readonly ConcurrentDictionary<string, Task<JsonObject>> _inflightYoutubeiRequests = new ConcurrentDictionary<string, Task<JsonObject>>();

	private readonly ConcurrentDictionary<string, (MainWindow.PlaybackStreamInfo Info, DateTime Expiry)> _streamUrlCache = new ConcurrentDictionary<string, (MainWindow.PlaybackStreamInfo, DateTime)>();

	private string? _cookieString;

	private string? _sapisid;

	private string? _userAgent;

	private string _authUser = "0";

	private DateTime _authLoadedAt = DateTime.MinValue;

	private static readonly HttpClient _lrcClient = new HttpClient(new SocketsHttpHandler
	{
		UseProxy = false,
		PooledConnectionLifetime = TimeSpan.FromMinutes(15.0)
	})
	{
		Timeout = TimeSpan.FromSeconds(6.0)
	};

	private static string _spotifyToken = null;

	private static DateTime _spotifyTokenExpiry = DateTime.MinValue;

	private static int _cachedSignatureTimestamp = 0;

	private static DateTime _cachedSignatureTimestampExpiry = DateTime.MinValue;

	private static SemaphoreSlim _stsLock = new SemaphoreSlim(1, 1);

	public static BackendService Instance => _instance ?? (_instance = new BackendService());

	public bool EnableStreamCache { get; set; } = true;

	public bool ExcludePlainVideoResults { get; set; } = true;

	private BackendService()
	{
		SocketsHttpHandler handler = new SocketsHttpHandler
		{
			UseCookies = false,
			AutomaticDecompression = (DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli),
			PooledConnectionLifetime = TimeSpan.FromMinutes(15.0),
			PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5.0),
			MaxConnectionsPerServer = 100,
			EnableMultipleHttp2Connections = true,
			UseProxy = false
		};
		_client = new HttpClient(handler)
		{
			Timeout = TimeSpan.FromSeconds(15.0),
			DefaultRequestVersion = HttpVersion.Version30,
			DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
		};
		_client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:88.0) Gecko/20100101 Firefox/88.0");
		_client.DefaultRequestHeaders.Add("X-Origin", "https://music.youtube.com");
		_youtubeClient = new YoutubeClient(_client);
		Task.Run(async delegate
		{
			try
			{
				await _youtubeClient.Videos.Streams.GetManifestAsync("dQw4w9WgXcQ");
			}
			catch
			{
			}
		});
		AppLogger.Log("Backend: Service initialized successfully", LogLevel.Success);
	}

	private async Task LoadAuthAsync()
	{
		if (((DateTime.UtcNow - _authLoadedAt).TotalSeconds < 30.0 && !string.IsNullOrEmpty(_cookieString)) || !System.IO.File.Exists(AuthFilePath))
		{
			return;
		}
		try
		{
			byte[] fileBytes = await System.IO.File.ReadAllBytesAsync(AuthFilePath);
			string jsonStr = Encoding.UTF8.GetString(fileBytes);
			jsonStr = jsonStr.Trim('\ufeff', ' ', '\r', '\n', '\t');
			if (jsonStr.StartsWith("{"))
			{
				JsonObject json = System.Text.Json.Nodes.JsonNode.Parse(jsonStr)!.AsObject();
				if (json["cookie"] != null)
				{
					_cookieString = json["cookie"]?.ToString();
					_sapisid = json["sapisid"]?.ToString();
					_userAgent = json["userAgent"]?.ToString();
					_authUser = json["authUser"]?.ToString() ?? "0";
					_authLoadedAt = DateTime.UtcNow;
				}
			}
		}
		catch
		{
		}
	}

	public static void SaveAuthData(string json)
	{
		string text = AuthFilePath + ".tmp";
		System.IO.File.WriteAllText(text, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		System.IO.File.Move(text, AuthFilePath, overwrite: true);
		_instance?.ForceReloadAuth();
	}

	public void ForceReloadAuth()
	{
		_authLoadedAt = DateTime.MinValue;
	}

	public void ClearAuth()
	{
		_cookieString = null;
		_sapisid = null;
		_userAgent = null;
		_authUser = "0";
		_authLoadedAt = DateTime.MinValue;
		try
		{
			if (System.IO.File.Exists(AuthFilePath))
			{
				System.IO.File.Delete(AuthFilePath);
			}
		}
		catch
		{
		}
	}

	private Task<JsonObject> SendYoutubeiRequestAsync(string endpoint, JsonObject payload, CancellationToken token)
	{
		string cacheKey = endpoint + "|" + payload.ToJsonString(new System.Text.Json.JsonSerializerOptions());
		return _inflightYoutubeiRequests.GetOrAdd(cacheKey, async delegate(string k)
		{
			try
			{
				return await SendYoutubeiRequestInternalAsync(endpoint, payload, token);
			}
			finally
			{
				_inflightYoutubeiRequests.TryRemove(k, out Task<JsonObject> _);
			}
		});
	}

	private async Task<JsonObject> SendYoutubeiRequestInternalAsync(string endpoint, JsonObject payload, CancellationToken token, bool isRetry = false)
	{
		await LoadAuthAsync();
		string url = "https://music.youtube.com/youtubei/v1/" + endpoint + "?alt=json&key=AIzaSyC9XL3ZjWddXya6X74dJoCTL-WEYFDNX30";
		HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
		string cName = ((string?)payload["context"]?["client"]?["clientName"]) ?? "WEB_REMIX";
		request.Headers.Remove("User-Agent");
		switch (cName)
		{
		case "IOS":
			request.Headers.TryAddWithoutValidation("User-Agent", "com.google.ios.youtube/19.29.1 (iPhone16,2; U; CPU iOS 17_5_1 like Mac OS X)");
			break;
		case "ANDROID_MUSIC":
			request.Headers.TryAddWithoutValidation("User-Agent", "com.google.android.apps.youtube.music/7.09.51 (Linux; U; Android 14)");
			break;
		case "ANDROID_VR":
			request.Headers.TryAddWithoutValidation("User-Agent", "com.google.android.apps.youtube.vr.oculus/1.61.48 (Linux; U; Android 11; en_US; Quest 2 Build/SQ3A.220605.009.A1)");
			break;
		default:
			request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
			break;
		}
		string vData = (string?)payload["context"]?["client"]?["visitorData"];
		if (!string.IsNullOrEmpty(vData))
		{
			request.Headers.TryAddWithoutValidation("X-Goog-Visitor-Id", vData);
		}
		if (cName == "WEB_REMIX" && !string.IsNullOrEmpty(_cookieString) && !string.IsNullOrEmpty(_sapisid))
		{
			request.Headers.TryAddWithoutValidation("Cookie", _cookieString);
			long time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			using SHA1 sha1 = SHA1.Create();
			string hashHex = BitConverter.ToString(sha1.ComputeHash(Encoding.UTF8.GetBytes($"{time} {_sapisid} https://music.youtube.com"))).Replace("-", "").ToLowerInvariant();
			request.Headers.TryAddWithoutValidation("Authorization", $"SAPISIDHASH {time}_{hashHex}");
			request.Headers.TryAddWithoutValidation("X-Origin", "https://music.youtube.com");
			request.Headers.TryAddWithoutValidation("X-Goog-AuthUser", _authUser);
			if (!string.IsNullOrEmpty(_userAgent))
			{
				request.Headers.Remove("User-Agent");
				request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
			}
		}
		if (payload["context"] == null)
		{
			payload["context"] = new JsonObject { ["client"] = new JsonObject
			{
				["clientName"] = "WEB_REMIX",
				["clientVersion"] = "1.20230508.01.00",
				["hl"] = "en"
			} };
		}
		request.Content = new StringContent(payload.ToJsonString(new System.Text.Json.JsonSerializerOptions()), Encoding.UTF8, "application/json");
		HttpResponseMessage response = await _client.SendAsync(request, token).ConfigureAwait(continueOnCapturedContext: false);
		string resStr = await response.Content.ReadAsStringAsync(token).ConfigureAwait(continueOnCapturedContext: false);
		if (!response.IsSuccessStatusCode)
		{
			if (!isRetry && (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden || resStr.Contains("\"UNAUTHENTICATED\"")))
			{
				if (MainWindow.Instance != null)
				{
					bool refreshed = await MainWindow.Instance.RefreshSessionSilentlyAsync();
					if (refreshed)
					{
						request.Dispose();
						response.Dispose();
						return await SendYoutubeiRequestInternalAsync(endpoint, payload, token, isRetry: true);
					}
					else
					{
						ClearAuth();
						request.Dispose();
						response.Dispose();
						return await SendYoutubeiRequestInternalAsync(endpoint, payload, token, isRetry: true);
					}
				}
			}
			throw new Exception($"YouTube API returned {response.StatusCode}: {resStr}");
		}
		return await Task.Run(() => System.Text.Json.Nodes.JsonNode.Parse(resStr)!.AsObject());
	}

	public async Task<byte[]> DownloadImageAsync(string url, CancellationToken token)
	{
		return await _client.GetByteArrayAsync(url, token).ConfigureAwait(continueOnCapturedContext: false);
	}

	private JsonArray GetThumbnails(JsonNode? renderer)
	{
		return ((renderer?.SelectTokens("..thumbnail.musicThumbnailRenderer.thumbnail.thumbnails").FirstOrDefault() ?? renderer?.SelectTokens("..thumbnailRenderer.musicThumbnailRenderer.thumbnail.thumbnails").FirstOrDefault() ?? renderer?.SelectTokens("..thumbnails[0].thumbnails").FirstOrDefault() ?? renderer?.SelectTokens("..thumbnails").FirstOrDefault())?.DeepClone() as System.Text.Json.Nodes.JsonArray) ?? new System.Text.Json.Nodes.JsonArray();
	}

	private static bool HasVersionTag(string text)
	{
		return Regex.IsMatch(text ?? "", "(?i)(album version|album edit|radio edit|single edit|extended|remaster(?:ed)?|live|acoustic|clean|explicit|mix|version)");
	}

	private static IEnumerable<string> VersionTags(string text)
	{
		return from Match m in Regex.Matches(text ?? "", "(?i)(album version|album edit|radio edit|single edit|extended|remaster(?:ed)?|live|acoustic|clean|explicit|mix|version)")
			select m.Value;
	}

	private static bool IsPlainVideoResult(JsonObject track)
	{
		string type = track["resultType"]?.ToString();
		if (!string.Equals(type, "video", StringComparison.OrdinalIgnoreCase))
		{
			return string.Equals(type, "musicVideo", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private JsonObject ParseTrack(JsonNode item)
	{
		JsonNode renderer = item["musicResponsiveListItemRenderer"] ?? item["musicTwoRowItemRenderer"] ?? item["playlistPanelVideoRenderer"];
		if (renderer == null)
		{
			return new JsonObject();
		}
		string videoId = renderer.SelectToken("playlistItemData.videoId")?.ToString() ?? renderer.SelectToken("videoId")?.ToString() ?? renderer.SelectTokens("..playNavigationEndpoint.watchEndpoint.videoId").FirstOrDefault()?.ToString() ?? renderer.SelectTokens("..navigationEndpoint.watchEndpoint.videoId").FirstOrDefault()?.ToString();
		string title = (renderer.SelectToken("title.runs[0].text") ?? renderer.SelectToken("flexColumns[0].musicResponsiveListItemFlexColumnRenderer.text.runs[0].text") ?? renderer.SelectTokens("..title.runs[0].text").FirstOrDefault())?.ToString() ?? "";
		JsonArray artists = new JsonArray();
		string album = "";
		string albumId = "";
		string year = "";
		JsonNode subtitleRuns = renderer.SelectTokens("..subtitle.runs").FirstOrDefault() ?? renderer.SelectTokens("..flexColumns[1]..runs").FirstOrDefault() ?? renderer.SelectTokens("..longBylineText.runs").FirstOrDefault();
		string subtitleText = "";
		if (subtitleRuns != null)
		{
			foreach (JsonNode item2 in (IEnumerable<JsonNode>)subtitleRuns)
			{
				string text = item2["text"]?.ToString() ?? "";
				if (!string.IsNullOrWhiteSpace(text))
				{
					subtitleText = subtitleText + " " + text;
				}
				string nav = item2.SelectToken("navigationEndpoint.browseEndpoint.browseId")?.ToString() ?? "";
				if (nav.StartsWith("UC") && !string.IsNullOrWhiteSpace(text))
				{
					artists.Add(new JsonObject
					{
						["name"] = text,
						["id"] = nav
					});
				}
				else if (nav.StartsWith("MPREb") && !string.IsNullOrWhiteSpace(text))
				{
					album = text;
					albumId = nav;
				}
				else if (Regex.IsMatch(text, "^\\d{4}$"))
				{
					year = text;
				}
			}
		}
		string subtitleLower = subtitleText.ToLowerInvariant();
		string resultType = "";
		if (subtitleLower.Contains("music video"))
		{
			resultType = "musicVideo";
		}
		else if (Regex.IsMatch(subtitleLower, "\\bsong\\b"))
		{
			resultType = "song";
		}
		else if (Regex.IsMatch(subtitleLower, "\\bvideo\\b"))
		{
			resultType = "video";
		}
		string releaseType = "";
		if (Regex.IsMatch(subtitleLower, "\\bep\\b"))
		{
			releaseType = "EP";
		}
		else if (Regex.IsMatch(subtitleLower, "\\bsingle\\b"))
		{
			releaseType = "Single";
		}
		string setVideoId = renderer.SelectToken("playlistItemData.playlistSetVideoId")?.ToString();
		string duration = "";
		if (renderer.SelectToken("fixedColumns") is JsonArray { Count: >0 } fixedColumns)
		{
			IEnumerable<JsonNode> runs = fixedColumns[0].SelectTokens("musicResponsiveListItemFixedColumnRenderer.text.runs[*].text");
			duration = ((runs == null || !runs.Any()) ? (fixedColumns[0].SelectToken("musicResponsiveListItemFixedColumnRenderer.text.simpleText")?.ToString() ?? "") : string.Join("", runs.Select((JsonNode r) => r.ToString())));
		}
		string plays = "";
		if (renderer.SelectToken("flexColumns") is JsonArray flexColumns)
		{
			for (int i = 1; i < flexColumns.Count; i++)
			{
				string text2 = "";
				IEnumerable<JsonNode> runs2 = flexColumns[i].SelectTokens("musicResponsiveListItemFlexColumnRenderer.text.runs[*].text");
				text2 = ((runs2 == null || !runs2.Any()) ? (flexColumns[i].SelectToken("musicResponsiveListItemFlexColumnRenderer.text.simpleText")?.ToString() ?? "") : string.Join("", runs2.Select((JsonNode r) => r.ToString())));
				if (string.IsNullOrWhiteSpace(text2))
				{
					continue;
				}
				if (Regex.IsMatch(text2, "^[\\d,\\.]+[KMBkmb]?$") || text2.Contains("plays") || text2.Contains("views"))
				{
					plays = text2;
				}
				else if (string.IsNullOrEmpty(album) && i >= 2)
				{
					album = text2;
					string nav2 = flexColumns[i].SelectToken("musicResponsiveListItemFlexColumnRenderer.text.runs[0].navigationEndpoint.browseEndpoint.browseId")?.ToString() ?? "";
					if (nav2.StartsWith("MPREb"))
					{
						albumId = nav2;
					}
				}
				else if (!string.IsNullOrEmpty(album) && string.IsNullOrEmpty(albumId) && i >= 2 && text2 == album)
				{
					string nav3 = flexColumns[i].SelectToken("musicResponsiveListItemFlexColumnRenderer.text.runs[0].navigationEndpoint.browseEndpoint.browseId")?.ToString() ?? "";
					if (nav3.StartsWith("MPREb"))
					{
						albumId = nav3;
					}
				}
			}
		}
		if (!string.IsNullOrEmpty(videoId))
		{
			if (string.IsNullOrWhiteSpace(title))
			{
				return new JsonObject();
			}
		}
		else if (string.IsNullOrWhiteSpace(title))
		{
			return new JsonObject();
		}
		JsonObject track = new JsonObject
		{
			["videoId"] = videoId,
			["title"] = title,
			["artists"] = artists,
			["album"] = new JsonObject
			{
				["name"] = album,
				["id"] = albumId
			},
			["thumbnails"] = GetThumbnails(renderer)
		};
		if (!string.IsNullOrEmpty(resultType))
		{
			track["resultType"] = resultType;
		}
		if (!string.IsNullOrEmpty(releaseType))
		{
			track["releaseType"] = releaseType;
		}
		if (!string.IsNullOrEmpty(year))
		{
			track["year"] = year;
		}
		if (!string.IsNullOrEmpty(setVideoId))
		{
			track["setVideoId"] = setVideoId;
		}
		if (!string.IsNullOrEmpty(duration))
		{
			track["duration"] = duration;
		}
		if (!string.IsNullOrEmpty(plays))
		{
			track["plays"] = plays;
		}
		return track;
	}

	public async Task<JsonObject> GetHomeFeedAsync(CancellationToken token, int limit = 10)
	{
		JsonObject payload = new JsonObject { ["browseId"] = "FEmusic_home" };
		JsonObject res = await SendYoutubeiRequestAsync("browse", payload, token).ConfigureAwait(continueOnCapturedContext: false);
		JsonArray shelves = new JsonArray();
		int shelvesFetched = 0;
		ProcessShelves(res);
		JsonNode currentRes = res;
		while (shelvesFetched < limit)
		{
			string continuation = currentRes.SelectTokens("..nextContinuationData.continuation").FirstOrDefault()?.ToString() ?? currentRes.SelectTokens("..continuationCommand.token").FirstOrDefault()?.ToString();
			if (string.IsNullOrEmpty(continuation))
			{
				break;
			}
			JsonObject cPayload = new JsonObject { ["continuation"] = continuation };
			currentRes = await SendYoutubeiRequestAsync("browse", cPayload, token).ConfigureAwait(continueOnCapturedContext: false);
			int prevCount = shelvesFetched;
			ProcessShelves(currentRes);
			if (shelvesFetched == prevCount)
			{
				break;
			}
		}
		return new JsonObject { ["data"] = shelves };
		void ProcessShelves(JsonNode root)
		{
			foreach (JsonNode shelf in root.SelectTokens("..musicCarouselShelfRenderer"))
			{
				string title = shelf.SelectTokens("..header..title.runs[0].text").FirstOrDefault()?.ToString() ?? "";
				if (!string.IsNullOrEmpty(title))
				{
					JsonArray items = new JsonArray();
					foreach (JsonNode item in shelf.SelectTokens("..musicTwoRowItemRenderer"))
					{
						JsonObject track = ParseTrack(new JsonObject { ["musicTwoRowItemRenderer"] = item?.DeepClone() });
						if (track.Count != 0)
						{
							string playlistId = item.SelectTokens("..navigationEndpoint.browseEndpoint.browseId").FirstOrDefault()?.ToString() ?? item.SelectTokens("..playNavigationEndpoint.watchEndpoint.playlistId").FirstOrDefault()?.ToString();
							if (!string.IsNullOrEmpty(playlistId))
							{
								track["playlistId"] = playlistId;
							}
							items.Add(track);
						}
					}
					foreach (JsonNode item2 in shelf.SelectTokens("..musicResponsiveListItemRenderer"))
					{
						JsonObject track2 = ParseTrack(new JsonObject { ["musicResponsiveListItemRenderer"] = item2?.DeepClone() });
						if (track2.Count != 0)
						{
							string playlistId2 = item2.SelectTokens("..navigationEndpoint.browseEndpoint.browseId").FirstOrDefault()?.ToString() ?? item2.SelectTokens("..playNavigationEndpoint.watchEndpoint.playlistId").FirstOrDefault()?.ToString();
							if (!string.IsNullOrEmpty(playlistId2))
							{
								track2["playlistId"] = playlistId2;
							}
							items.Add(track2);
						}
					}
					if (items.Count > 0)
					{
						shelves.Add(new JsonObject
						{
							["title"] = title,
							["contents"] = items
						});
						shelvesFetched++;
					}
				}
			}
		}
	}

	public async IAsyncEnumerable<JsonArray> GetHomeFeedStreamAsync([EnumeratorCancellation] CancellationToken token, int limit = 10)
	{
		int num = -1;
		BackendService backendService = this;
		await LoadAuthAsync();
		int shelvesFetched = 0;
		if (string.IsNullOrEmpty(_cookieString))
		{
			string[] guestFeeds = new string[3] { "FEmusic_explore", "FEmusic_charts", "FEmusic_new_releases" };
			string[] array = guestFeeds;
			foreach (string feedId in array)
			{
				JsonObject payload = new JsonObject { ["browseId"] = feedId };
				JsonArray shelves = ProcessShelves(await SendYoutubeiRequestAsync("browse", payload, token).ConfigureAwait(continueOnCapturedContext: false));
				if (shelves.Count > 0)
				{
					yield return shelves;
				}
			}
			yield break;
		}
		JsonObject homePayload = new JsonObject { ["browseId"] = "FEmusic_home" };
		JsonObject homeRes = await SendYoutubeiRequestAsync("browse", homePayload, token).ConfigureAwait(continueOnCapturedContext: false);
		JsonArray initialShelves = ProcessShelves(homeRes);
		JsonNode currentRes = homeRes;
		for (int prefetchCount = 0; prefetchCount < 2; prefetchCount++)
		{
			if (initialShelves.Any((JsonNode? JsonNode) => JsonNode?["title"]?.ToString().ToLower().Contains("quick picks") ?? false))
			{
				break;
			}
			string continuation = currentRes.SelectTokens("..nextContinuationData.continuation").FirstOrDefault()?.ToString() ?? currentRes.SelectTokens("..continuationCommand.token").FirstOrDefault()?.ToString();
			if (string.IsNullOrEmpty(continuation))
			{
				break;
			}
			JsonObject cPayload = new JsonObject { ["continuation"] = continuation };
			currentRes = await SendYoutubeiRequestAsync("browse", cPayload, token).ConfigureAwait(continueOnCapturedContext: false);
			List<JsonNode>.Enumerator enumerator = ProcessShelves(currentRes).ToList().GetEnumerator();
			try
			{
				while (enumerator.MoveNext())
				{
					JsonNode s = enumerator.Current;
					initialShelves.Add(s?.DeepClone());
				}
			}
			finally
			{
				if (num == -1)
				{
					((IDisposable)enumerator/*cast due to constrained. prefix*/).Dispose();
				}
			}
		}
		if (initialShelves.Count > 0)
		{
			yield return initialShelves;
		}
		while (shelvesFetched < limit)
		{
			string continuation2 = currentRes.SelectTokens("..nextContinuationData.continuation").FirstOrDefault()?.ToString() ?? currentRes.SelectTokens("..continuationCommand.token").FirstOrDefault()?.ToString();
			if (!string.IsNullOrEmpty(continuation2))
			{
				JsonObject cPayload2 = new JsonObject { ["continuation"] = continuation2 };
				currentRes = await SendYoutubeiRequestAsync("browse", cPayload2, token).ConfigureAwait(continueOnCapturedContext: false);
				int i = shelvesFetched;
				JsonArray contShelves = ProcessShelves(currentRes);
				if (contShelves.Count > 0)
				{
					yield return contShelves;
				}
				if (shelvesFetched == i)
				{
					break;
				}
				continue;
			}
			break;
		}
		JsonArray ProcessShelves(JsonNode root)
		{
			JsonArray shelves2 = new JsonArray();
			foreach (JsonNode shelf in root.SelectTokens("..musicCarouselShelfRenderer"))
			{
				string title = shelf.SelectTokens("..header..title.runs[0].text").FirstOrDefault()?.ToString() ?? "";
				if (!string.IsNullOrEmpty(title) && !title.ToLower().Contains("music video") && !title.ToLower().Contains("music videos") && !title.ToLower().Contains("shows") && !title.ToLower().Contains("podcasts") && !title.ToLower().Contains("episodes"))
				{
					JsonArray items = new JsonArray();
					foreach (JsonNode item in shelf.SelectTokens("..musicTwoRowItemRenderer"))
					{
						JsonObject track = ParseTrack(new JsonObject { ["musicTwoRowItemRenderer"] = item?.DeepClone() });
						if (track.Count != 0)
						{
							track["isCard"] = true;
							string playlistId = item.SelectTokens("..navigationEndpoint.browseEndpoint.browseId").FirstOrDefault()?.ToString() ?? item.SelectTokens("..playNavigationEndpoint.watchEndpoint.playlistId").FirstOrDefault()?.ToString();
							if (!string.IsNullOrEmpty(playlistId))
							{
								track["playlistId"] = playlistId;
							}
							items.Add(track);
						}
					}
					foreach (JsonNode item2 in shelf.SelectTokens("..musicResponsiveListItemRenderer"))
					{
						JsonObject track2 = ParseTrack(new JsonObject { ["musicResponsiveListItemRenderer"] = item2?.DeepClone() });
						if (track2.Count != 0)
						{
							track2["isCard"] = false;
							string playlistId2 = item2.SelectTokens("..navigationEndpoint.browseEndpoint.browseId").FirstOrDefault()?.ToString() ?? item2.SelectTokens("..playNavigationEndpoint.watchEndpoint.playlistId").FirstOrDefault()?.ToString();
							if (!string.IsNullOrEmpty(playlistId2))
							{
								track2["playlistId"] = playlistId2;
							}
							items.Add(track2);
						}
					}
					if (items.Count > 0)
					{
						shelves2.Add(new JsonObject
						{
							["title"] = title,
							["contents"] = items
						});
						shelvesFetched++;
					}
				}
			}
			return shelves2;
		}
	}

	public async IAsyncEnumerable<JsonArray> GetExploreFeedStreamAsync([EnumeratorCancellation] CancellationToken token)
	{
		await LoadAuthAsync();
		string[] exploreFeeds = new string[4] { "FEmusic_explore", "FEmusic_charts", "FEmusic_new_releases", "FEmusic_moods_and_genres" };
		string[] array = exploreFeeds;
		foreach (string feedId in array)
		{
			JsonObject payload = new JsonObject { ["browseId"] = feedId };
			JsonArray shelves = ProcessShelves(await SendYoutubeiRequestAsync("browse", payload, token).ConfigureAwait(continueOnCapturedContext: false));
			if (shelves.Count > 0)
			{
				yield return shelves;
			}
		}
		JsonArray ProcessShelves(JsonNode root)
		{
			JsonArray shelves2 = new JsonArray();
			foreach (JsonNode shelf in root.SelectTokens("..musicCarouselShelfRenderer"))
			{
				string title = shelf.SelectTokens("..header..title.runs[0].text").FirstOrDefault()?.ToString() ?? "";
				if (!string.IsNullOrEmpty(title) && !title.ToLower().Contains("music video") && !title.ToLower().Contains("music videos") && !title.ToLower().Contains("shows") && !title.ToLower().Contains("podcasts") && !title.ToLower().Contains("episodes"))
				{
					JsonArray items = new JsonArray();
					foreach (JsonNode item in shelf.SelectTokens("..musicTwoRowItemRenderer"))
					{
						JsonObject track = ParseTrack(new JsonObject { ["musicTwoRowItemRenderer"] = item?.DeepClone() });
						if (track.Count != 0)
						{
							track["isCard"] = true;
							string playlistId = item.SelectTokens("..navigationEndpoint.browseEndpoint.browseId").FirstOrDefault()?.ToString() ?? item.SelectTokens("..playNavigationEndpoint.watchEndpoint.playlistId").FirstOrDefault()?.ToString();
							if (!string.IsNullOrEmpty(playlistId))
							{
								track["playlistId"] = playlistId;
							}
							items.Add(track);
						}
					}
					foreach (JsonNode item2 in shelf.SelectTokens("..musicResponsiveListItemRenderer"))
					{
						JsonObject track2 = ParseTrack(new JsonObject { ["musicResponsiveListItemRenderer"] = item2?.DeepClone() });
						if (track2.Count != 0)
						{
							track2["isCard"] = false;
							string playlistId2 = item2.SelectTokens("..navigationEndpoint.browseEndpoint.browseId").FirstOrDefault()?.ToString() ?? item2.SelectTokens("..playNavigationEndpoint.watchEndpoint.playlistId").FirstOrDefault()?.ToString();
							if (!string.IsNullOrEmpty(playlistId2))
							{
								track2["playlistId"] = playlistId2;
							}
							items.Add(track2);
						}
					}
					if (items.Count > 0)
					{
						shelves2.Add(new JsonObject
						{
							["title"] = title,
							["contents"] = items
						});
					}
				}
			}
			return shelves2;
		}
	}

	public async Task<JsonObject> FetchAutoplayNextAsync(string videoId, CancellationToken token)
	{
		JsonObject payload = new JsonObject
		{
			["enablePersistentPlaylistPanel"] = true,
			["isAudioOnly"] = true,
			["tunerSettingValue"] = "AUTOMIX_SETTING_NORMAL",
			["videoId"] = videoId,
			["playlistId"] = "RDAMVM" + videoId
		};
		JsonObject res = await SendYoutubeiRequestAsync("next", payload, token).ConfigureAwait(continueOnCapturedContext: false);
		JsonArray tracks = new JsonArray();
		foreach (JsonNode item in res.SelectTokens("..playlistPanelVideoRenderer"))
		{
			JsonObject track = ParseTrack(new JsonObject { ["playlistPanelVideoRenderer"] = item?.DeepClone() });
			if (track.Count != 0 && !string.IsNullOrEmpty(track["videoId"]?.ToString()) && (!ExcludePlainVideoResults || !IsPlainVideoResult(track)))
			{
				tracks.Add(track);
			}
		}
		if (tracks.Count == 0)
		{
			foreach (JsonNode item2 in res.SelectTokens("..musicResponsiveListItemRenderer"))
			{
				JsonObject track2 = ParseTrack(new JsonObject { ["musicResponsiveListItemRenderer"] = item2?.DeepClone() });
				if (track2.Count != 0 && !string.IsNullOrEmpty(track2["videoId"]?.ToString()) && (!ExcludePlainVideoResults || !IsPlainVideoResult(track2)))
				{
					tracks.Add(track2);
				}
			}
		}
		return new JsonObject { ["data"] = new JsonObject { ["tracks"] = tracks } };
	}

	public async Task<JsonObject> GetMixTracksAsync(string playlistId, CancellationToken token)
	{
		JsonObject payload = new JsonObject
		{
			["enablePersistentPlaylistPanel"] = true,
			["isAudioOnly"] = true,
			["tunerSettingValue"] = "AUTOMIX_SETTING_NORMAL",
			["playlistId"] = playlistId
		};
		JsonObject res = await SendYoutubeiRequestAsync("next", payload, token).ConfigureAwait(continueOnCapturedContext: false);
		JsonArray tracks = new JsonArray();
		foreach (JsonNode item in res.SelectTokens("..playlistPanelVideoRenderer"))
		{
			JsonObject track = ParseTrack(new JsonObject { ["playlistPanelVideoRenderer"] = item?.DeepClone() });
			if (track.Count != 0 && !string.IsNullOrEmpty(track["videoId"]?.ToString()) && (!ExcludePlainVideoResults || !IsPlainVideoResult(track)))
			{
				tracks.Add(track);
			}
		}
		if (tracks.Count == 0)
		{
			foreach (JsonNode item2 in res.SelectTokens("..musicResponsiveListItemRenderer"))
			{
				JsonObject track2 = ParseTrack(new JsonObject { ["musicResponsiveListItemRenderer"] = item2?.DeepClone() });
				if (track2.Count != 0 && !string.IsNullOrEmpty(track2["videoId"]?.ToString()) && (!ExcludePlainVideoResults || !IsPlainVideoResult(track2)))
				{
					tracks.Add(track2);
				}
			}
		}
		return new JsonObject { ["data"] = new JsonObject
		{
			["tracks"] = tracks,
			["thumbnails"] = new JsonArray()
		} };
	}

	public async Task<JsonObject> GetPlaylistTracksAsync(string playlistId, CancellationToken token)
	{
		JsonObject payload = new JsonObject { ["browseId"] = playlistId };
		JsonObject res = await SendYoutubeiRequestAsync("browse", payload, token).ConfigureAwait(continueOnCapturedContext: false);
		JsonArray tracks = new JsonArray();
		foreach (JsonNode item in res.SelectTokens("..musicResponsiveListItemRenderer"))
		{
			JsonObject track = ParseTrack(new JsonObject { ["musicResponsiveListItemRenderer"] = item?.DeepClone() });
			if (track.Count != 0 && !string.IsNullOrEmpty(track["videoId"]?.ToString()) && (!ExcludePlainVideoResults || !IsPlainVideoResult(track)))
			{
				tracks.Add(track);
			}
		}
		JsonNode header = res.SelectTokens("..musicDetailHeaderRenderer").FirstOrDefault() ?? res.SelectTokens("..musicEditablePlaylistDetailHeaderRenderer").FirstOrDefault() ?? res.SelectTokens("..musicResponsiveHeaderRenderer").FirstOrDefault();
		string headerTitle = header?.SelectToken("title.runs[0].text")?.ToString() ?? header?.SelectToken("title.text")?.ToString() ?? "";
		string description = string.Join("", (from t in header?.SelectTokens("..description.runs[*].text")
			select t.ToString()) ?? Enumerable.Empty<string>()).Trim();
		if (string.IsNullOrWhiteSpace(description))
		{
			description = string.Join("", from t in res.SelectTokens("..musicDescriptionShelfRenderer..description.runs[*].text")
				select t.ToString()).Trim();
		}
		JsonArray artists = new JsonArray();
		string year = "";
		JsonArray allRuns = new JsonArray();
		if (header?.SelectTokens("..subtitle.runs").FirstOrDefault() is JsonArray sub1)
		{
			foreach (JsonNode r in sub1)
			{
				allRuns.Add(r?.DeepClone());
			}
		}
		if (header?.SelectTokens("..secondSubtitle.runs").FirstOrDefault() is JsonArray sub2)
		{
			foreach (JsonNode r2 in sub2)
			{
				allRuns.Add(r2?.DeepClone());
			}
		}
		if (header?.SelectTokens("..straplineTextOne.runs").FirstOrDefault() is JsonArray strap1)
		{
			foreach (JsonNode r3 in strap1)
			{
				allRuns.Add(r3?.DeepClone());
			}
		}
		if (allRuns.Count > 0)
		{
			foreach (JsonNode item2 in allRuns)
			{
				string text = item2["text"]?.ToString() ?? "";
				string nav = item2.SelectToken("navigationEndpoint.browseEndpoint.browseId")?.ToString() ?? "";
				if (nav.StartsWith("UC") && !string.IsNullOrWhiteSpace(text))
				{
					artists.Add(new JsonObject
					{
						["name"] = text,
						["id"] = nav
					});
				}
				else if (Regex.IsMatch(text, "^\\d{4}$"))
				{
					year = text;
				}
				else if (string.IsNullOrEmpty(nav) && !string.IsNullOrWhiteSpace(text) && text.Trim() != "•" && text.Trim() != "Album" && text.Trim() != "EP" && text.Trim() != "Single")
				{
					string lowerText = text.ToLowerInvariant();
					if (artists.Count == 0 && !lowerText.Contains(" views") && !lowerText.Contains(" plays") && !lowerText.Contains(" song") && !lowerText.Contains(" minute") && !lowerText.Contains(" hour") && !Regex.IsMatch(text, "^\\d+$"))
					{
						artists.Add(new JsonObject
						{
							["name"] = text.Trim(),
							["id"] = ""
						});
					}
				}
			}
		}
		JsonObject data = new JsonObject
		{
			["tracks"] = tracks,
			["thumbnails"] = GetThumbnails(header)
		};
		if (!string.IsNullOrWhiteSpace(headerTitle))
		{
			data["title"] = headerTitle;
		}
		if (!string.IsNullOrWhiteSpace(description))
		{
			data["description"] = description;
		}
		if (artists.Count > 0)
		{
			data["artists"] = artists;
		}
		if (!string.IsNullOrWhiteSpace(year))
		{
			data["year"] = year;
		}
		return new JsonObject { ["data"] = data };
	}

	public async Task<JsonObject> GetLikedSongsAsync(CancellationToken token)
	{
		JsonObject payload = new JsonObject { ["browseId"] = "FEmusic_liked_videos" };
		JsonObject obj = await SendYoutubeiRequestAsync("browse", payload, token).ConfigureAwait(continueOnCapturedContext: false);
		JsonArray tracks = new JsonArray();
		foreach (JsonNode item in obj.SelectTokens("..musicResponsiveListItemRenderer"))
		{
			JsonObject track = ParseTrack(new JsonObject { ["musicResponsiveListItemRenderer"] = item?.DeepClone() });
			if (track.Count != 0 && !string.IsNullOrEmpty(track["videoId"]?.ToString()))
			{
				tracks.Add(track);
			}
		}
		return new JsonObject { ["data"] = new JsonObject { ["tracks"] = tracks } };
	}

	public async Task<JsonObject> GetHistoryAsync(CancellationToken token)
	{
		JsonObject payload = new JsonObject { ["browseId"] = "FEmusic_history" };
		JsonObject obj = await SendYoutubeiRequestAsync("browse", payload, token).ConfigureAwait(continueOnCapturedContext: false);
		JsonArray tracks = new JsonArray();
		foreach (JsonNode item in obj.SelectTokens("..musicResponsiveListItemRenderer"))
		{
			JsonObject track = ParseTrack(new JsonObject { ["musicResponsiveListItemRenderer"] = item?.DeepClone() });
			if (track.Count != 0 && !string.IsNullOrEmpty(track["videoId"]?.ToString()))
			{
				tracks.Add(track);
			}
		}
		return new JsonObject { ["data"] = tracks };
	}

	public async Task<JsonObject> GetLibraryPlaylistsAsync(CancellationToken token)
	{
		JsonObject payload = new JsonObject { ["browseId"] = "FEmusic_liked_playlists" };
		JsonObject obj = await SendYoutubeiRequestAsync("browse", payload, token).ConfigureAwait(continueOnCapturedContext: false);
		JsonArray playlists = new JsonArray();
		foreach (JsonNode item in obj.SelectTokens("..musicTwoRowItemRenderer"))
		{
			string browseId = item.SelectTokens("..navigationEndpoint.browseEndpoint.browseId").FirstOrDefault()?.ToString() ?? "";
			string title = item.SelectTokens("..title.runs[0].text").FirstOrDefault()?.ToString() ?? "";
			if (!string.IsNullOrEmpty(browseId))
			{
				playlists.Add(new JsonObject
				{
					["playlistId"] = browseId,
					["title"] = title,
					["thumbnails"] = GetThumbnails(item)
				});
			}
		}
		JsonArray albums = new JsonArray();
		JsonObject payload2 = new JsonObject { ["browseId"] = "FEmusic_liked_albums" };
		try
		{
			foreach (JsonNode item2 in (await SendYoutubeiRequestAsync("browse", payload2, token).ConfigureAwait(continueOnCapturedContext: false)).SelectTokens("..musicTwoRowItemRenderer"))
			{
				string browseId2 = item2.SelectTokens("..navigationEndpoint.browseEndpoint.browseId").FirstOrDefault()?.ToString() ?? "";
				string title2 = item2.SelectTokens("..title.runs[0].text").FirstOrDefault()?.ToString() ?? "";
				if (string.IsNullOrEmpty(browseId2))
				{
					continue;
				}
				JsonObject track = ParseTrack(new JsonObject { ["musicTwoRowItemRenderer"] = item2?.DeepClone() });
				if (track.Count != 0)
				{
					JsonObject albObj = new JsonObject
					{
						["browseId"] = browseId2,
						["title"] = title2,
						["thumbnails"] = track["thumbnails"]?.DeepClone(),
						["artists"] = track["artists"]?.DeepClone()
					};
					if (track["year"] != null)
					{
						albObj["year"] = track["year"]?.DeepClone();
					}
					albums.Add(albObj);
				}
			}
		}
		catch
		{
		}
		return new JsonObject
		{
			["data"] = playlists,
			["albums"] = albums
		};
	}

	public async Task<JsonObject> SearchAsync(string query, CancellationToken token)
	{
		JsonObject payload = new JsonObject { ["query"] = query };
		JsonObject obj = await SendYoutubeiRequestAsync("search", payload, token).ConfigureAwait(continueOnCapturedContext: false);
		JsonArray songs = new JsonArray();
		JsonArray artists = new JsonArray();
		JsonArray albums = new JsonArray();
		JsonNode topResult = obj.SelectTokens("..musicCardShelfRenderer").FirstOrDefault();
		if (topResult != null)
		{
			string title = topResult.SelectTokens("..title.runs[0].text").FirstOrDefault()?.ToString() ?? "";
			string text = string.Join(" ", from t in topResult.SelectTokens("..subtitle.runs[*].text")
				select t.ToString()).ToLower();
			string browseId = topResult.SelectTokens("..browseEndpoint.browseId").FirstOrDefault()?.ToString() ?? "";
			if (text.Contains("artist") && !string.IsNullOrEmpty(browseId))
			{
				artists.Add(new JsonObject { ["browseId"] = browseId, ["artist"] = title, ["thumbnails"] = GetThumbnails(topResult)?.DeepClone() });
			}
		}
		foreach (JsonNode item in obj.SelectTokens("..musicResponsiveListItemRenderer"))
		{
			JsonObject track = ParseTrack(new JsonObject { ["musicResponsiveListItemRenderer"] = item?.DeepClone() });
			if (track.Count == 0)
			{
				continue;
			}
			string subtitle = string.Join(" ", from t in item.SelectTokens("..flexColumns[1]..text.runs[*].text")
				select t.ToString()).ToLower();
			if (subtitle.Contains("song") || subtitle.Contains("video"))
			{
				if (!string.IsNullOrEmpty(track["videoId"]?.ToString()) && (!ExcludePlainVideoResults || !IsPlainVideoResult(track)))
				{
					songs.Add(track);
				}
			}
			else if (subtitle.Contains("artist") || subtitle.Contains("profile"))
			{
				string browseId2 = item.SelectTokens("..navigationEndpoint.browseEndpoint.browseId").FirstOrDefault()?.ToString() ?? "";
				if (!string.IsNullOrEmpty(browseId2))
				{
					artists.Add(new JsonObject
					{
						["browseId"] = browseId2,
						["artist"] = track["title"]?.DeepClone(),
						["thumbnails"] = track["thumbnails"]?.DeepClone()
					});
				}
			}
			else
			{
				if ((!subtitle.Contains("album") && !subtitle.Contains("ep")) || subtitle.Contains("episode") || subtitle.Contains("podcast"))
				{
					continue;
				}
				List<string> browseIds = (from t in item.SelectTokens("..navigationEndpoint.browseEndpoint.browseId")
					select t.ToString()).ToList();
				string browseId3 = browseIds.FirstOrDefault((string b) => b.StartsWith("MPRE")) ?? browseIds.FirstOrDefault((string b) => !b.StartsWith("UC")) ?? "";
				if (!string.IsNullOrEmpty(browseId3))
				{
					JsonObject albObj = new JsonObject
					{
						["browseId"] = browseId3,
						["title"] = track["title"]?.DeepClone(),
						["thumbnails"] = track["thumbnails"]?.DeepClone(),
						["artists"] = track["artists"]?.DeepClone()
					};
					if (track["year"] != null)
					{
						albObj["year"] = track["year"]?.DeepClone();
					}
					albums.Add(albObj);
				}
			}
		}
		if (songs.Count > 1)
		{
			songs = new JsonArray(songs.OrderByDescending(RankSong).Select(x => x?.DeepClone()).ToArray());
		}
		return new JsonObject { ["data"] = new JsonObject
		{
			["songs"] = songs,
			["artists"] = artists,
			["albums"] = albums
		} };
		static string NormalizeForRank(string s)
		{
			if (string.IsNullOrWhiteSpace(s))
			{
				return "";
			}
			StringBuilder sb = new StringBuilder();
			foreach (char c in s)
			{
				if (char.IsLetterOrDigit(c))
				{
					sb.Append(char.ToLowerInvariant(c));
				}
			}
			return sb.ToString();
		}
		int RankSong(JsonNode? song)
		{
			if (song == null) return 0;
			string title2 = song["title"]?.ToString() ?? "";
			string s = string.Join(" ", from x in (song["artists"] as JsonArray ?? new JsonArray()) select x["name"]?.ToString());
			string normQuery = NormalizeForRank(query);
			string normTitle = NormalizeForRank(title2);
			string normArtists = NormalizeForRank(s);
			string resultType = song["resultType"]?.ToString() ?? "";
			bool queryHasVersion = HasVersionTag(query);
			bool titleHasVersion = HasVersionTag(title2);
			int score = 0;
			if (!string.IsNullOrEmpty(normTitle) && !string.IsNullOrEmpty(normQuery))
			{
				if (normQuery.Contains(normTitle))
				{
					score += 25;
				}
				if (normTitle.Contains(normQuery))
				{
					score += 60;
				}
			}
			if (string.Equals(resultType, "song", StringComparison.OrdinalIgnoreCase))
			{
				score += 35;
			}
			else if (string.Equals(resultType, "musicVideo", StringComparison.OrdinalIgnoreCase))
			{
				score += 15;
			}
			else if (string.Equals(resultType, "video", StringComparison.OrdinalIgnoreCase))
			{
				score -= 120;
			}
			if (!queryHasVersion && titleHasVersion)
			{
				score -= 110;
			}
			else if (queryHasVersion && !titleHasVersion)
			{
				score -= 70;
			}
			foreach (string item2 in VersionTags(query))
			{
				string versionText = NormalizeForRank(item2);
				if (!string.IsNullOrEmpty(versionText))
				{
					score += (normTitle.Contains(versionText) ? 90 : (-90));
				}
			}
			foreach (string word in from Match m in Regex.Matches(query, "[\\p{L}\\p{Nd}]+")
				select NormalizeForRank(m.Value) into w
				where w.Length > 2
				select w)
			{
				if (normTitle.Contains(word))
				{
					score += 8;
				}
				else if (normArtists.Contains(word))
				{
					score += 3;
				}
			}
			return score;
		}
	}

	public async Task<JsonObject> GetArtistInfoAsync(string channelId, CancellationToken token)
	{
		JsonObject payload = new JsonObject { ["browseId"] = channelId };
		JsonObject res = await SendYoutubeiRequestAsync("browse", payload, token).ConfigureAwait(continueOnCapturedContext: false);
		string name = res.SelectTokens("..musicImmersiveHeaderRenderer..title.runs[0].text").FirstOrDefault()?.ToString() ?? res.SelectTokens("..musicVisualHeaderRenderer..title.runs[0].text").FirstOrDefault()?.ToString() ?? "Artist";
		JsonArray thumbnails = GetThumbnails(res.SelectTokens("..musicImmersiveHeaderRenderer").FirstOrDefault() ?? res.SelectTokens("..musicVisualHeaderRenderer").FirstOrDefault());
		string desc = res.SelectTokens("..musicImmersiveHeaderRenderer.description.runs[0].text").FirstOrDefault()?.ToString() ?? "";
		string subs = res.SelectTokens("..subscriberCountText.runs[0].text").FirstOrDefault()?.ToString() ?? res.SelectTokens("..subscriberCountText.simpleText").FirstOrDefault()?.ToString() ?? "";
		JsonArray albumsArr = new JsonArray();
		JsonArray singlesArr = new JsonArray();
		JsonArray similarArtistsArr = new JsonArray();
		List<JsonNode> shelvesEnum = res.SelectTokens("..musicCarouselShelfRenderer").ToList();
		List<Task<(string, JsonArray)>> fetchTasks = new List<Task<(string, JsonArray)>>();
		foreach (JsonNode shelf in shelvesEnum)
		{
			string shelfTitle = shelf.SelectTokens("..header..title.runs[0].text").FirstOrDefault()?.ToString().ToLower() ?? "";
			if (!shelfTitle.Contains("album") && !shelfTitle.Contains("single") && !shelfTitle.Contains("ep"))
			{
				continue;
			}
			string category = (shelfTitle.Contains("album") ? "album" : "single");
			string seeAllBrowseId = shelf.SelectTokens("..header..title.runs[0].navigationEndpoint.browseEndpoint.browseId").FirstOrDefault()?.ToString();
			string seeAllParams = shelf.SelectTokens("..header..title.runs[0].navigationEndpoint.browseEndpoint.params").FirstOrDefault()?.ToString();
			if (!string.IsNullOrEmpty(seeAllBrowseId))
			{
				fetchTasks.Add(Task.Run(async delegate
				{
					JsonObject p = new JsonObject { ["browseId"] = seeAllBrowseId };
					if (!string.IsNullOrEmpty(seeAllParams))
					{
						p["params"] = seeAllParams;
					}
					JsonObject obj2 = await SendYoutubeiRequestAsync("browse", p, token).ConfigureAwait(continueOnCapturedContext: false);
					JsonArray items2 = new JsonArray();
					foreach (JsonNode item5 in obj2.SelectTokens("..musicTwoRowItemRenderer"))
					{
						JsonObject track3 = ParseTrack(new JsonObject { ["musicTwoRowItemRenderer"] = item5?.DeepClone() });
						if (track3.Count != 0)
						{
							string browseId3 = item5.SelectTokens("..navigationEndpoint.browseEndpoint.browseId").FirstOrDefault()?.ToString() ?? "";
							if (!string.IsNullOrEmpty(browseId3))
							{
								JsonObject albObj2 = new JsonObject
								{
									["browseId"] = browseId3,
									["title"] = track3["title"]?.DeepClone(),
									["thumbnails"] = track3["thumbnails"]?.DeepClone(),
									["artists"] = track3["artists"]?.DeepClone()
								};
								if (track3["year"] != null)
								{
									albObj2["year"] = track3["year"]?.DeepClone();
								}
								if (track3["releaseType"] != null)
								{
									albObj2["releaseType"] = track3["releaseType"]?.DeepClone();
								}
								items2.Add(albObj2);
							}
						}
					}
					return (category: category, items: items2);
				}));
				continue;
			}
			JsonArray items = new JsonArray();
			foreach (JsonNode item in shelf.SelectTokens("..musicTwoRowItemRenderer"))
			{
				JsonObject track = ParseTrack(new JsonObject { ["musicTwoRowItemRenderer"] = item?.DeepClone() });
				if (track.Count == 0)
				{
					continue;
				}
				string browseId = item.SelectTokens("..navigationEndpoint.browseEndpoint.browseId").FirstOrDefault()?.ToString() ?? "";
				if (!string.IsNullOrEmpty(browseId))
				{
					JsonObject albObj = new JsonObject
					{
						["browseId"] = browseId,
						["title"] = track["title"]?.DeepClone(),
						["thumbnails"] = track["thumbnails"]?.DeepClone(),
						["artists"] = track["artists"]?.DeepClone()
					};
					if (track["year"] != null)
					{
						albObj["year"] = track["year"]?.DeepClone();
					}
					if (track["releaseType"] != null)
					{
						albObj["releaseType"] = track["releaseType"]?.DeepClone();
					}
					items.Add(albObj);
				}
			}
			fetchTasks.Add(Task.FromResult((category, items)));
		}
		(string, JsonArray)[] obj = await Task.WhenAll(fetchTasks);
		List<JsonObject> flatAlbums = new List<JsonObject>();
		List<JsonObject> flatSingles = new List<JsonObject>();
		(string, JsonArray)[] array = obj;
		for (int num = 0; num < array.Length; num++)
		{
			(string, JsonArray) resTuple = array[num];
			if (resTuple.Item1 == "album")
			{
				foreach (JsonNode item2 in resTuple.Item2)
				{
					flatAlbums.Add((JsonObject)item2);
				}
				continue;
			}
			foreach (JsonNode item3 in resTuple.Item2)
			{
				flatSingles.Add((JsonObject)item3);
			}
		}
		flatAlbums = flatAlbums.OrderByDescending((JsonObject JsonObject) => (JsonObject["year"] != null && int.TryParse(JsonObject["year"].ToString(), out var result)) ? result : 0).ToList();
		foreach (JsonObject a in flatAlbums)
		{
			albumsArr.Add(a?.DeepClone());
		}
		flatSingles = flatSingles.OrderByDescending((JsonObject JsonObject) => (JsonObject["year"] != null && int.TryParse(JsonObject["year"].ToString(), out var result)) ? result : 0).ToList();
		foreach (JsonObject a2 in flatSingles)
		{
			singlesArr.Add(a2?.DeepClone());
		}
		foreach (JsonNode shelf2 in shelvesEnum)
		{
			string shelfTitle2 = shelf2.SelectTokens("..header..title.runs[0].text").FirstOrDefault()?.ToString().ToLower() ?? "";
			if (!shelfTitle2.Contains("fans might also like") && !shelfTitle2.Contains("similar artists"))
			{
				continue;
			}
			foreach (JsonNode item4 in shelf2.SelectTokens("..musicTwoRowItemRenderer"))
			{
				JsonObject track2 = ParseTrack(new JsonObject { ["musicTwoRowItemRenderer"] = item4?.DeepClone() });
				if (track2.Count != 0)
				{
					string browseId2 = item4.SelectTokens("..navigationEndpoint.browseEndpoint.browseId").FirstOrDefault()?.ToString() ?? "";
					if (!string.IsNullOrEmpty(browseId2))
					{
						similarArtistsArr.Add(new JsonObject
						{
							["browseId"] = browseId2,
							["title"] = track2["title"]?.DeepClone(),
							["thumbnails"] = track2["thumbnails"]?.DeepClone()
						});
					}
				}
			}
		}
		JsonObject dataObj = new JsonObject
		{
			["name"] = name,
			["thumbnails"] = thumbnails,
			["description"] = desc,
			["subscribers"] = subs,
			["albums"] = new JsonObject { ["results"] = albumsArr },
			["singles"] = new JsonObject { ["results"] = singlesArr },
			["similarArtists"] = similarArtistsArr
		};
		return new JsonObject { ["data"] = dataObj };
	}

	public async Task<JsonObject> GetArtistSongsAsync(string channelId, string artistName, CancellationToken token, int limit = 500)
	{
		JsonObject payload = new JsonObject { ["browseId"] = channelId };
		JsonObject obj = await SendYoutubeiRequestAsync("browse", payload, token).ConfigureAwait(continueOnCapturedContext: false);
		JsonArray tracks = new JsonArray();
		string songsBrowseId = "";
		List<JsonNode> shelvesEnum = obj.SelectTokens("..musicShelfRenderer").ToList();
		JsonNode songsShelf = null;
		foreach (JsonNode shelf in shelvesEnum)
		{
			string title = shelf.SelectTokens("..title..text").FirstOrDefault()?.ToString().ToLower() ?? "";
			if (title.Contains("song") || title.Contains("track") || title.Contains("popular") || title.Contains("top"))
			{
				songsShelf = shelf;
				break;
			}
		}
		if (songsShelf == null && shelvesEnum.Count > 0)
		{
			songsShelf = shelvesEnum[0];
		}
		if (songsShelf != null)
		{
			songsBrowseId = songsShelf.SelectTokens("..bottomEndpoint.browseEndpoint.browseId").FirstOrDefault()?.ToString() ?? "";
			if (string.IsNullOrEmpty(songsBrowseId))
			{
				foreach (JsonNode item in songsShelf.SelectTokens("..musicResponsiveListItemRenderer"))
				{
					JsonObject track = ParseTrack(new JsonObject { ["musicResponsiveListItemRenderer"] = item?.DeepClone() });
					if (track.Count != 0 && !string.IsNullOrEmpty(track["videoId"]?.ToString()))
					{
						tracks.Add(track);
					}
				}
			}
		}
		if (!string.IsNullOrEmpty(songsBrowseId))
		{
			foreach (JsonNode item2 in (await SendYoutubeiRequestAsync("browse", new JsonObject { ["browseId"] = songsBrowseId }, token).ConfigureAwait(continueOnCapturedContext: false)).SelectTokens("..musicResponsiveListItemRenderer"))
			{
				JsonObject track2 = ParseTrack(new JsonObject { ["musicResponsiveListItemRenderer"] = item2?.DeepClone() });
				if (track2.Count != 0 && !string.IsNullOrEmpty(track2["videoId"]?.ToString()))
				{
					tracks.Add(track2);
				}
			}
		}
		return new JsonObject { ["tracks"] = tracks };
	}

	private async Task<string?> GetSpotifyTokenAsync(CancellationToken token)
	{
		if (_spotifyToken != null && DateTime.UtcNow < _spotifyTokenExpiry)
		{
			return _spotifyToken;
		}
		try
		{
			HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, "https://open.spotify.com/get_access_token?reason=transport&productType=web_player");
			req.Headers.Add("User-Agent", "Mozilla/5.0");
			HttpResponseMessage res = await _lrcClient.SendAsync(req, token);
			if (res.IsSuccessStatusCode)
			{
				JsonObject JsonObject = System.Text.Json.Nodes.JsonNode.Parse(await res.Content.ReadAsStringAsync())!.AsObject();
				_spotifyToken = JsonObject["accessToken"]?.ToString();
				long expiresTimestampMs = JsonObject["accessTokenExpirationTimestampMs"]?.Deserialize<long>() ?? 0;
				if (expiresTimestampMs > 0)
				{
					_spotifyTokenExpiry = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(expiresTimestampMs).AddMinutes(-5.0);
				}
				else
				{
					_spotifyTokenExpiry = DateTime.UtcNow.AddMinutes(55.0);
				}
				return _spotifyToken;
			}
		}
		catch
		{
		}
		return null;
	}

	private async Task<string?> GetSpotifyTrackIdAsync(string title, string artist, long durationMs, CancellationToken token)
	{
		try
		{
			string spotifyToken = await GetSpotifyTokenAsync(token);
			if (string.IsNullOrEmpty(spotifyToken))
			{
				return null;
			}
			string query = Uri.EscapeDataString(title + " " + artist);
			HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/search?q=" + query + "&type=track&limit=10");
			req.Headers.Add("Authorization", "Bearer " + spotifyToken);
			HttpResponseMessage res = await _lrcClient.SendAsync(req, token);
			if (res.IsSuccessStatusCode)
			{
				if (!(JsonObject.Parse(await res.Content.ReadAsStringAsync()).SelectToken("tracks.items") is JsonArray { Count: not 0 } items))
				{
					return null;
				}
				return (from item in items
					select new
					{
						Item = item,
						Score = Score(item)
					} into x
					where x.Score > 0
					orderby x.Score descending
					select x).FirstOrDefault()?.Item["id"]?.ToString();
			}
		}
		catch
		{
		}
		return null;
		bool DurationClose(JsonNode? JsonNode)
		{
			if (durationMs <= 0 || JsonNode == null)
			{
				return true;
			}
			long itemDuration = ((long?)JsonNode).GetValueOrDefault();
			if (itemDuration <= 0)
			{
				return true;
			}
			long tolerance = Math.Max(8000L, Math.Min(12000L, durationMs / 25));
			return Math.Abs(itemDuration - durationMs) <= tolerance;
		}
		static string Normalize(string s)
		{
			if (string.IsNullOrWhiteSpace(s))
			{
				return "";
			}
			s = Regex.Replace(s, "(?i)\\s+(ft\\.|feat\\.|with).*$", "");
			StringBuilder sb = new StringBuilder();
			string text = s;
			foreach (char c in text)
			{
				if (char.IsLetterOrDigit(c) || c == ' ')
				{
					sb.Append(char.ToLowerInvariant(c));
				}
				else
				{
					sb.Append(' ');
				}
			}
			return Regex.Replace(sb.ToString(), "\\s+", " ").Trim();
		}
		int Score(JsonNode item)
		{
			string itemTitle = item["name"]?.ToString() ?? "";
			string s = string.Join(" ", from x in (item["artists"] as JsonArray ?? new JsonArray()) select x["name"]?.ToString());
			string normTitle = Normalize(title);
			string normArtist = Normalize(artist);
			string normItemTitle = Normalize(itemTitle);
			string normItemArtists = Normalize(s);
			if (string.IsNullOrEmpty(normTitle) || string.IsNullOrEmpty(normArtist) || string.IsNullOrEmpty(normItemTitle) || string.IsNullOrEmpty(normItemArtists))
			{
				return -1000;
			}
			int score = 0;
			if (normItemTitle == normTitle)
			{
				score += 90;
			}
			else if (normItemTitle.Contains(normTitle) || normTitle.Contains(normItemTitle))
			{
				score += 45;
			}
			string baseTitle = Normalize(StripVersionTags(title));
			string baseItemTitle = Normalize(StripVersionTags(itemTitle));
			if (!string.IsNullOrEmpty(baseTitle) && baseTitle == baseItemTitle)
			{
				score += 20;
			}
			string[] stopWords = new string[6] { "the", "and", "feat", "ft", "with", "&" };
			List<string> queryWords = (from w in normArtist.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				where !Enumerable.Contains(stopWords, w)
				select w).ToList();
			List<string> resWords = (from w in normItemArtists.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				where !Enumerable.Contains(stopWords, w)
				select w).ToList();
			score = ((!(normItemArtists == normArtist) && !normItemArtists.Contains(normArtist) && !normArtist.Contains(normItemArtists) && (queryWords.Count <= 0 || resWords.Count <= 0 || !queryWords.Intersect(resWords).Any())) ? (score - 150) : (score + 45));
			score = ((!DurationClose(item["duration_ms"])) ? (score - 60) : (score + 25));
			foreach (Match match in Regex.Matches(title, "(?i)(album edit|radio edit|single edit|extended|remaster(?:ed)?|live|acoustic|clean|explicit|mix|version)"))
			{
				string versionText = Normalize(match.Value);
				if (!string.IsNullOrEmpty(versionText))
				{
					score += (normItemTitle.Contains(versionText) ? 35 : (-35));
				}
			}
			return score;
		}
		static string StripVersionTags(string s)
		{
			if (string.IsNullOrWhiteSpace(s))
			{
				return "";
			}
			s = Regex.Replace(s, "(?i)\\s*[\\(\\[].*?(edit|version|remaster|remastered|mix|radio|album|single|extended|deluxe|live|acoustic|explicit|clean).*?[\\)\\]]", "");
			return s.Trim();
		}
	}

	private async Task<JsonObject?> GetCommunityLyricsAsync(string trackId, long durationMs, CancellationToken token)
	{
		try
		{
			string spotifyToken = await GetSpotifyTokenAsync(token);
			if (!string.IsNullOrEmpty(spotifyToken))
			{
				HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, "https://beautiful-lyrics.socalifornian.live/lyrics/" + trackId);
				req.Headers.Add("Authorization", "Bearer " + spotifyToken);
				HttpResponseMessage res = await _lrcClient.SendAsync(req, token);
				if (res.IsSuccessStatusCode)
				{
					JsonObject parsed = ParseCommunityLyrics(await res.Content.ReadAsStringAsync(), durationMs);
					if (parsed != null && ((JsonArray)parsed["lines"]).Count > 0)
					{
						parsed["source"] = "BeautifulLyrics";
						return parsed;
					}
				}
			}
		}
		catch
		{
		}
		try
		{
			string spotifyToken2 = await GetSpotifyTokenAsync(token);
			if (!string.IsNullOrEmpty(spotifyToken2))
			{
				HttpRequestMessage req2 = new HttpRequestMessage(HttpMethod.Post, "https://api.spicylyrics.org/query");
				req2.Headers.Add("SpicyLyrics-WebAuth", "Bearer " + spotifyToken2);
				req2.Headers.Add("SpicyLyrics-Version", "1.0.0");
				JsonObject body = new JsonObject
				{
					["queries"] = new JsonArray
					{
						new JsonObject
						{
							["operation"] = "lyrics",
							["variables"] = new JsonObject
							{
								["id"] = trackId,
								["auth"] = "SpicyLyrics-WebAuth"
							},
							["operationId"] = "0"
						}
					},
					["client"] = new JsonObject { ["version"] = "1.0.0" }
				};
				req2.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
				HttpResponseMessage res2 = await _lrcClient.SendAsync(req2, token);
				if (res2.IsSuccessStatusCode)
				{
					JsonNode lyricsData = JsonObject.Parse(await res2.Content.ReadAsStringAsync()).SelectToken("queries[0].result.data");
					if (lyricsData != null)
					{
						JsonObject parsed2 = ParseCommunityLyrics(lyricsData.ToString(), durationMs);
						if (parsed2 != null && ((JsonArray)parsed2["lines"]).Count > 0)
						{
							parsed2["source"] = "SpicyLyrics";
							return parsed2;
						}
					}
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private JsonObject? ParseCommunityLyrics(string jsonStr, long durationMs)
	{
		JsonObject JsonObject = System.Text.Json.Nodes.JsonNode.Parse(jsonStr)!.AsObject();
		string type = JsonObject["Type"]?.ToString();
		if (!(JsonObject["Content"] is JsonArray contentArr))
		{
			return null;
		}
		JsonArray lines = new JsonArray();
		if (type == "Line")
		{
			foreach (JsonNode item in contentArr)
			{
				if (item["Type"]?.ToString() == "Vocal")
				{
					string text = item["Text"]?.ToString();
					double startTime = 0.0;
					if (item["StartTime"] != null)
					{
						double.TryParse(item["StartTime"].ToString(), out startTime);
					}
					if (!string.IsNullOrEmpty(text))
					{
						lines.Add(new JsonObject
						{
							["timeMs"] = (long)(startTime * 1000.0),
							["text"] = text
						});
					}
				}
			}
		}
		else if (type == "Syllable")
		{
			foreach (JsonNode item2 in contentArr)
			{
				if (!(item2["Type"]?.ToString() == "Vocal"))
				{
					continue;
				}
				JsonNode lead = item2["Lead"];
				if (lead == null)
				{
					continue;
				}
				double startTime2 = 0.0;
				if (lead["StartTime"] != null)
				{
					double.TryParse(lead["StartTime"].ToString(), out startTime2);
				}
				if (!(lead["Syllables"] is JsonArray syllablesToken))
				{
					continue;
				}
				StringBuilder textBuilder = new StringBuilder();
				JsonArray syllablesArr = new JsonArray();
				foreach (JsonNode syl in syllablesToken)
				{
					bool isPart = syl["IsPartOfWord"]?.Deserialize<bool>() ?? false;
					string txt = syl["Text"]?.ToString() ?? "";
					double sylStartTime = 0.0;
					if (syl["StartTime"] != null)
					{
						double.TryParse(syl["StartTime"].ToString(), out sylStartTime);
					}
					double sylEndTime = 0.0;
					if (syl["EndTime"] != null)
					{
						double.TryParse(syl["EndTime"].ToString(), out sylEndTime);
					}
					long sylDurMs = (long)((sylEndTime - sylStartTime) * 1000.0);
					if (sylDurMs < 0)
					{
						sylDurMs = 0L;
					}
					if (textBuilder.Length > 0 && !isPart)
					{
						textBuilder.Append(" ");
						txt = " " + txt;
					}
					textBuilder.Append(syl["Text"]?.ToString() ?? "");
					syllablesArr.Add(new JsonObject
					{
						["timeMs"] = (long)(sylStartTime * 1000.0),
						["durationMs"] = sylDurMs,
						["text"] = txt
					});
				}
				lines.Add(new JsonObject
				{
					["timeMs"] = (long)(startTime2 * 1000.0),
					["text"] = textBuilder.ToString().Trim(),
					["syllables"] = syllablesArr
				});
			}
		}
		JsonArray sortedLines = new JsonArray(lines.OrderBy((JsonNode? l) => (long)(l?["timeMs"] ?? 0)).Select(x => x?.DeepClone()).ToArray());
		bool isSynced = IsPlausibleSyncedLyrics(sortedLines, durationMs);
		return new JsonObject
		{
			["lines"] = sortedLines,
			["synced"] = isSynced
		};
	}

	public async Task<JsonObject> GetLyricsAsync(string videoId, string title, string artist, long durationMs, CancellationToken token)
	{
		AppLogger.Log($"Backend: Fetching lyrics for '{title}' by '{artist}' (https://music.youtube.com/watch?v={videoId})", LogLevel.Info);
		string cleanTitle = CleanSearchText(title, stripVersionTags: false);
		string baseTitle = CleanSearchText(title, stripVersionTags: true);
		string cleanArtist = CleanSearchText(artist, stripVersionTags: false);
		if (!string.IsNullOrEmpty(cleanTitle))
		{
			JsonObject appleLyrics = await TryFetchAppleMusicSyllablesAsync(cleanTitle, cleanArtist, durationMs, token);
			if (appleLyrics != null && ((JsonArray)appleLyrics["lines"]).Count > 0)
			{
				return appleLyrics;
			}
		}
		try
		{
			HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, "https://lyrics.paxsenix.org/youtube/lyrics?id=" + videoId);
			req.Headers.UserAgent.ParseAdd("Spectre/1.0");
			HttpResponseMessage res = await _client.SendAsync(req, token).ConfigureAwait(continueOnCapturedContext: false);
			if (res.IsSuccessStatusCode)
			{
				string lrcText = await res.Content.ReadAsStringAsync(token).ConfigureAwait(continueOnCapturedContext: false);
				if (!string.IsNullOrWhiteSpace(lrcText) && lrcText.Contains("["))
				{
					JsonArray lines = ParseLrc(lrcText);
					if (lines.Count > 0 && IsPlausibleSyncedLyrics(lines, durationMs))
					{
						return new JsonObject
						{
							["lines"] = lines,
							["synced"] = true,
							["source"] = "YouTube (Synced)"
						};
					}
				}
			}
		}
		catch (Exception ex)
		{
			AppLogger.Log("Backend: Paxsenix YouTube Lyrics failed - " + ex.Message, LogLevel.Warning);
		}
		string spotifyTrackId = await GetSpotifyTrackIdAsync(cleanTitle, cleanArtist, durationMs, token);
		if (!string.IsNullOrEmpty(spotifyTrackId))
		{
			JsonObject communityLyrics = await GetCommunityLyricsAsync(spotifyTrackId, durationMs, token);
			if (communityLyrics != null && ((JsonArray)communityLyrics["lines"]).Count > 0)
			{
				return communityLyrics;
			}
		}
		try
		{
			_lrcClient.DefaultRequestHeaders.UserAgent.ParseAdd("Spectre/1.0");
			JsonNode bestItem = null;
			if (durationMs > 0)
			{
				try
				{
					int durSec = (int)(durationMs / 1000);
					string url = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(cleanTitle)}&artist_name={Uri.EscapeDataString(cleanArtist)}&duration={durSec}";
					JsonObject obj = System.Text.Json.Nodes.JsonNode.Parse(await _lrcClient.GetStringAsync(url, token))!.AsObject();
					if (!string.IsNullOrEmpty(obj["syncedLyrics"]?.ToString()) && IsMetadataMatch(cleanTitle, cleanArtist, obj["trackName"]?.ToString(), obj["artistName"]?.ToString()) && IsDurationMatch(durationMs, obj["duration"]))
					{
						bestItem = obj;
					}
				}
				catch
				{
				}
			}
			if (bestItem == null)
			{
				string searchUrl = "https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(cleanTitle) + "&artist_name=" + Uri.EscapeDataString(cleanArtist);
				string resStr = await _lrcClient.GetStringAsync(searchUrl, token);
				if (resStr.TrimStart().StartsWith("["))
				{
					List<JsonNode> candidates = (from x in System.Text.Json.Nodes.JsonNode.Parse(resStr)!.AsArray()
						where !string.IsNullOrEmpty(x["syncedLyrics"]?.ToString())
						where IsMetadataMatch(cleanTitle, cleanArtist, x["trackName"]?.ToString(), x["artistName"]?.ToString())
						select x).ToList();
					if (durationMs > 0)
					{
						List<JsonNode> durCandidates = candidates.Where((JsonNode x) => IsDurationMatch(durationMs, x["duration"])).ToList();
						if (durCandidates.Count > 0)
						{
							bestItem = durCandidates.OrderBy((JsonNode x) => GetDurationDiff(durationMs, x["duration"])).First();
						}
						else if (candidates.Count > 0 && !HasVersionTag(cleanTitle))
						{
							bestItem = candidates.OrderBy((JsonNode x) => GetDurationDiff(durationMs, x["duration"])).First();
						}
					}
					else if (candidates.Count > 0)
					{
						bestItem = candidates.First();
					}
				}
			}
			if (bestItem != null)
			{
				JsonArray lines2 = ParseLrc(bestItem["syncedLyrics"]?.ToString());
				if (lines2.Count > 0 && IsPlausibleSyncedLyrics(lines2, durationMs))
				{
					return new JsonObject
					{
						["lines"] = lines2,
						["synced"] = true,
						["source"] = "LRCLIB"
					};
				}
				bestItem = null;
			}
			if (bestItem == null && !string.Equals(baseTitle, cleanTitle, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(baseTitle))
			{
				string searchUrl2 = "https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(baseTitle) + "&artist_name=" + Uri.EscapeDataString(cleanArtist);
				string resStr2 = await _lrcClient.GetStringAsync(searchUrl2, token);
				if (resStr2.TrimStart().StartsWith("["))
				{
					JsonArray lines3 = ParseLrc((from x in System.Text.Json.Nodes.JsonNode.Parse(resStr2)!.AsArray()
						where !string.IsNullOrEmpty(x["syncedLyrics"]?.ToString())
						where IsMetadataMatch(baseTitle, cleanArtist, x["trackName"]?.ToString(), x["artistName"]?.ToString())
						where IsDurationMatch(durationMs, x["duration"])
						orderby GetDurationDiff(durationMs, x["duration"])
						select x).ToList().FirstOrDefault()?["syncedLyrics"]?.ToString());
					if (lines3.Count > 0 && IsPlausibleSyncedLyrics(lines3, durationMs))
					{
						return new JsonObject
						{
							["lines"] = lines3,
							["synced"] = true,
							["source"] = "LRCLIB"
						};
					}
				}
			}
		}
		catch
		{
		}
		try
		{
			JsonObject watchPayload = new JsonObject
			{
				["enablePersistentPlaylistPanel"] = true,
				["isAudioOnly"] = true,
				["videoId"] = videoId,
				["playlistId"] = "RDAMVM" + videoId
			};
			JsonObject watchRes = await SendYoutubeiRequestAsync("next", watchPayload, token).ConfigureAwait(continueOnCapturedContext: false);
			string lyricsBrowseId = watchRes.SelectTokens("..musicLyricsTabContentRenderer..browseEndpoint.browseId").FirstOrDefault()?.ToString() ?? watchRes.SelectTokens("..tab..tabRenderer..browseEndpoint.browseId").FirstOrDefault()?.ToString();
			if (string.IsNullOrEmpty(lyricsBrowseId))
			{
				foreach (JsonNode tab in watchRes.SelectTokens("..tabs[*].tabRenderer"))
				{
					if ((tab.SelectToken("title")?.ToString()?.ToLower() ?? "").Contains("lyric"))
					{
						lyricsBrowseId = tab.SelectTokens("..browseEndpoint.browseId").FirstOrDefault()?.ToString();
						break;
					}
				}
			}
			if (!string.IsNullOrEmpty(lyricsBrowseId))
			{
				string lyricText = (await SendYoutubeiRequestAsync("browse", new JsonObject { ["browseId"] = lyricsBrowseId }, token).ConfigureAwait(continueOnCapturedContext: false)).SelectTokens("..musicDescriptionShelfRenderer..description.runs[0].text").FirstOrDefault()?.ToString() ?? "";
				if (!string.IsNullOrEmpty(lyricText))
				{
					JsonArray lines4 = SplitUnsyncedLyrics(lyricText);
					return new JsonObject
					{
						["lines"] = lines4,
						["synced"] = false,
						["source"] = "YouTube Music"
					};
				}
			}
		}
		catch
		{
		}
		return new JsonObject
		{
			["lines"] = new JsonArray(),
			["synced"] = false,
			["source"] = ""
		};
		static string CleanSearchText(string t, bool stripVersionTags)
		{
			if (string.IsNullOrEmpty(t))
			{
				return "";
			}
			t = Regex.Replace(t, "(?i)\\s+(ft\\.|feat\\.|with).*$", "");
			if (stripVersionTags)
			{
				t = Regex.Replace(t, "(?i)\\s*[\\(\\[].*?(edit|version|remaster|remastered|mix|radio|album|single|extended|deluxe|live|acoustic|explicit|clean).*?[\\)\\]]", "");
				t = Regex.Replace(t, "\\(.*?\\)", "");
				t = Regex.Replace(t, "\\[.*?\\]", "");
			}
			return t.Trim();
		}
		static long GetDurationDiff(long targetDurMs, JsonNode? durToken)
		{
			return Math.Abs(GetDurMs(durToken) - targetDurMs);
		}
		static long GetDurMs(JsonNode? durToken)
		{
			if (durToken == null)
			{
				return 0L;
			}
			if (durToken.GetValueKind() == System.Text.Json.JsonValueKind.Number || durToken.GetValueKind() == System.Text.Json.JsonValueKind.Number)
			{
				return (long)((double)durToken * 1000.0);
			}
			if (long.TryParse(durToken.ToString(), out var parsedDur))
			{
				return parsedDur * 1000;
			}
			return 0L;
		}
		static bool HasVersionTag(string s)
		{
			return Regex.IsMatch(s ?? "", "(?i)(album edit|radio edit|single edit|extended|remaster(?:ed)?|live|acoustic|clean|explicit|mix|version)");
		}
		static bool IsDurationMatch(long targetDurMs, JsonNode? durToken)
		{
			if (targetDurMs <= 0)
			{
				return true;
			}
			long itemDurMs = GetDurMs(durToken);
			if (itemDurMs <= 0)
			{
				return true;
			}
			long tolerance = Math.Max(8000L, Math.Min(12000L, targetDurMs / 25));
			return Math.Abs(itemDurMs - targetDurMs) <= tolerance;
		}
		static bool IsMetadataMatch(string queryTitle, string queryArtist, string? resTitle, string? resArtist)
		{
			if (string.IsNullOrEmpty(resTitle) || string.IsNullOrEmpty(resArtist))
			{
				return false;
			}
			string normQueryTitle = NormalizeString(queryTitle);
			string normQueryArtist = NormalizeString(queryArtist);
			string normResTitle = NormalizeString(resTitle);
			string normResArtist = NormalizeString(resArtist);
			if (string.IsNullOrEmpty(normQueryTitle) || string.IsNullOrEmpty(normQueryArtist))
			{
				return false;
			}
			bool titleMatch = normResTitle == normQueryTitle || (normResTitle.Contains(normQueryTitle) && normQueryTitle.Length > 3) || (normQueryTitle.Contains(normResTitle) && normResTitle.Length > 3);
			string[] stopWords = new string[6] { "the", "and", "feat", "ft", "with", "&" };
			List<string> queryWords = (from w in normQueryArtist.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				where !Enumerable.Contains(stopWords, w)
				select w).ToList();
			List<string> resWords = (from w in normResArtist.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				where !Enumerable.Contains(stopWords, w)
				select w).ToList();
			bool artistMatch = normResArtist == normQueryArtist || normResArtist.Contains(normQueryArtist) || normQueryArtist.Contains(normResArtist) || (queryWords.Count > 0 && resWords.Count > 0 && queryWords.Intersect(resWords).Any());
			if (titleMatch)
			{
				Regex regex = new Regex("(?i)(album edit|radio edit|single edit|extended|remaster(?:ed)?|live|acoustic|clean|explicit|mix|version)");
				List<string> queryTags = (from Match m in regex.Matches(queryTitle ?? "")
					select NormalizeString(m.Value) into x
					where !string.IsNullOrEmpty(x)
					select x).ToList();
				List<string> resTags = (from Match m in regex.Matches(resTitle ?? "")
					select NormalizeString(m.Value) into x
					where !string.IsNullOrEmpty(x)
					select x).ToList();
				foreach (string tag in resTags)
				{
					if (!queryTags.Contains(tag))
					{
						return false;
					}
				}
				foreach (string tag2 in queryTags)
				{
					if (!resTags.Contains(tag2) && !normResTitle.Contains(tag2))
					{
						return false;
					}
				}
			}
			return titleMatch && artistMatch;
		}
		static string NormalizeString(string s)
		{
			if (string.IsNullOrEmpty(s))
			{
				return "";
			}
			StringBuilder sb = new StringBuilder();
			foreach (char c in s)
			{
				if (char.IsLetterOrDigit(c) || c == ' ')
				{
					sb.Append(char.ToLowerInvariant(c));
				}
				else
				{
					sb.Append(' ');
				}
			}
			return Regex.Replace(sb.ToString(), "\\s+", " ").Trim();
		}
	}

	private JsonArray ParseLrc(string? text)
	{
		JsonArray lines = new JsonArray();
		if (string.IsNullOrEmpty(text))
		{
			return lines;
		}
		string[] array = text.Split('\n');
		foreach (string input in array)
		{
			MatchCollection matches = Regex.Matches(input, "\\[(\\d+):(\\d+(?:\\.\\d+)?)\\]");
			string lyricText = Regex.Replace(input, "\\[\\d+:\\d+(?:\\.\\d+)?\\]", "").Trim();
			if (matches.Count == 0)
			{
				continue;
			}
			if (string.IsNullOrEmpty(lyricText))
			{
				lyricText = "•••";
			}
			foreach (Match item in matches)
			{
				int minutes = int.Parse(item.Groups[1].Value);
				double seconds = double.Parse(item.Groups[2].Value, CultureInfo.InvariantCulture);
				long timeMs = (long)(((double)(minutes * 60) + seconds) * 1000.0);
				lines.Add(new JsonObject
				{
					["timeMs"] = timeMs,
					["text"] = lyricText
				});
			}
		}
		return new JsonArray(lines.OrderBy((JsonNode? l) => (long)(l?["timeMs"] ?? 0)).Select(x => x?.DeepClone()).ToArray());
	}

	private bool IsPlausibleSyncedLyrics(JsonArray lines, long durationMs)
	{
		if (durationMs <= 0 || lines.Count < 2)
		{
			return true;
		}
		long valueOrDefault = ((long?)lines.AsArray().FirstOrDefault()?["timeMs"]).GetValueOrDefault();
		long last = ((long?)lines.AsArray().LastOrDefault()?["timeMs"]).GetValueOrDefault();
		if (valueOrDefault > Math.Min(45000L, durationMs / 3))
		{
			return false;
		}
		if (last > durationMs + 15000)
		{
			return false;
		}
		if (durationMs > 90000 && (double)last < (double)durationMs * 0.45)
		{
			return false;
		}
		return true;
	}

	private JsonArray SplitUnsyncedLyrics(string text)
	{
		List<string> lyricLines = (from l in text.Split('\n')
			where !string.IsNullOrWhiteSpace(l)
			select l.Trim()).ToList();
		if (lyricLines.Count == 0)
		{
			return new JsonArray();
		}
		JsonArray arr = new JsonArray();
		foreach (string line in lyricLines)
		{
			arr.Add(new JsonObject
			{
				["timeMs"] = 0L,
				["text"] = line
			});
		}
		return arr;
	}

	private async Task<JsonObject?> TryFetchAppleMusicSyllablesAsync(string title, string artist, long durationMs, CancellationToken token)
	{
		_ = 2;
		try
		{
			string query = (title + " " + artist).Trim();
			string searchUrl = "https://itunes.apple.com/search?term=" + Uri.EscapeDataString(query) + "&entity=song&limit=5&country=US";
			if (!(JsonObject.Parse(await _client.GetStringAsync(searchUrl, token).ConfigureAwait(continueOnCapturedContext: false))["results"] is JsonArray { Count: not 0 } results))
			{
				return null;
			}
			JsonNode bestTrack = null;
			foreach (JsonNode track in results)
			{
				string resTitle = track["trackName"]?.ToString() ?? "";
				string s = track["artistName"]?.ToString() ?? "";
				string normQueryTitle = Normalize(title);
				string normQueryArtist = Normalize(artist);
				string normResTitle = Normalize(resTitle);
				string normResArtist = Normalize(s);
				bool titleMatch = normResTitle == normQueryTitle || (normResTitle.Contains(normQueryTitle) && normQueryTitle.Length > 3) || (normQueryTitle.Contains(normResTitle) && normResTitle.Length > 3);
				string[] stopWords = new string[6] { "the", "and", "feat", "ft", "with", "&" };
				List<string> queryWords = (from w in normQueryArtist.Split(' ', StringSplitOptions.RemoveEmptyEntries)
					where !Enumerable.Contains(stopWords, w)
					select w).ToList();
				List<string> resWords = (from w in normResArtist.Split(' ', StringSplitOptions.RemoveEmptyEntries)
					where !Enumerable.Contains(stopWords, w)
					select w).ToList();
				bool artistMatch = normResArtist == normQueryArtist || normResArtist.Contains(normQueryArtist) || normQueryArtist.Contains(normResArtist) || (queryWords.Count > 0 && resWords.Count > 0 && queryWords.Intersect(resWords).Any());
				if (titleMatch && artistMatch)
				{
					long trackTimeMs = ((long?)track["trackTimeMillis"]).GetValueOrDefault();
					if (durationMs <= 0 || trackTimeMs <= 0)
					{
						bestTrack = track;
						break;
					}
					if (Math.Abs(trackTimeMs - durationMs) <= 12000)
					{
						bestTrack = track;
						break;
					}
				}
			}
			if (bestTrack == null)
			{
				return null;
			}
			long trackId = (long)bestTrack["trackId"];
			HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, $"https://lyrics.paxsenix.org/apple-music/lyrics?id={trackId}");
			req.Headers.UserAgent.ParseAdd("Spectre/1.0");
			HttpResponseMessage res = await _client.SendAsync(req, token).ConfigureAwait(continueOnCapturedContext: false);
			if (!res.IsSuccessStatusCode)
			{
				return null;
			}
			JsonObject obj = System.Text.Json.Nodes.JsonNode.Parse(await res.Content.ReadAsStringAsync(token).ConfigureAwait(continueOnCapturedContext: false))!.AsObject();
			if ((bool?)obj["error"] == true)
			{
				return null;
			}
			if (!(obj["content"] is JsonArray { Count: not 0 } content))
			{
				return null;
			}
			JsonArray finalLines = new JsonArray();
			foreach (JsonNode section in content)
			{
				if (!(section["text"] is JsonArray { Count: not 0 } textArr))
				{
					continue;
				}
				JsonObject lineObj = new JsonObject();
				lineObj["timeMs"] = ((long?)section["timestamp"]).GetValueOrDefault();
				JsonArray syllables = new JsonArray();
				StringBuilder sb = new StringBuilder();
				foreach (JsonNode syl in textArr)
				{
					string sText = ((string?)syl["text"]) ?? "";
					bool isPart = (bool?)syl["part"] == true;
					JsonObject sylObj = new JsonObject();
					sylObj["timeMs"] = ((long?)syl["timestamp"]).GetValueOrDefault();
					sylObj["durationMs"] = ((long?)syl["duration"]).GetValueOrDefault();
					sylObj["text"] = sText + (isPart ? "" : " ");
					syllables.Add(sylObj);
					sb.Append(sylObj["text"]);
				}
				lineObj["text"] = sb.ToString().Trim();
				lineObj["syllables"] = syllables;
				finalLines.Add(lineObj);
			}
			if (finalLines.Count > 0)
			{
				bool isSynced = IsPlausibleSyncedLyrics(finalLines, durationMs);
				return new JsonObject
				{
					["lines"] = finalLines,
					["synced"] = isSynced,
					["source"] = "Apple Music"
				};
			}
		}
		catch (Exception ex)
		{
			AppLogger.Log("Backend: Apple Music Syllables failed - " + ex.Message, LogLevel.Warning);
		}
		return null;
		static string Normalize(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return "";
			}
			StringBuilder sb2 = new StringBuilder();
			foreach (char c in text)
			{
				if (char.IsLetterOrDigit(c) || c == ' ')
				{
					sb2.Append(char.ToLowerInvariant(c));
				}
				else
				{
					sb2.Append(' ');
				}
			}
			return Regex.Replace(sb2.ToString(), "\\s+", " ").Trim();
		}
	}

	public async Task<JsonObject> GetSongCreditsAsync(string videoId, CancellationToken token)
	{
		string browseId = "MPTC" + videoId;
		JsonObject payload = new JsonObject { ["browseId"] = browseId };
		JsonObject obj = await SendYoutubeiRequestAsync("browse", payload, token).ConfigureAwait(continueOnCapturedContext: false);
		JsonObject credits = new JsonObject { ["other_sections"] = new JsonArray() };
		JsonArray obj2 = (obj.SelectToken("onResponseReceivedActions[0].openPopupAction.popup.dismissableDialogRenderer.sections") as JsonArray) ?? throw new Exception("Credits not available.");
		Dictionary<string, string> sectionMap = new Dictionary<string, string>
		{
			["Performed by"] = "performed_by",
			["Written by"] = "written_by",
			["Produced by"] = "produced_by",
			["Music metadata provided by"] = "music_metadata_provided_by"
		};
		foreach (JsonNode item in obj2)
		{
			JsonNode content = item["dismissableDialogContentSectionRenderer"];
			if (content == null)
			{
				continue;
			}
			string sectionTitle = content.SelectToken("title.runs[0].text")?.ToString() ?? "";
			JsonArray subtitleRuns = content.SelectToken("subtitle.runs") as JsonArray;
			JsonArray data = new JsonArray();
			if (subtitleRuns != null)
			{
				for (int i = 0; i < subtitleRuns.Count; i += 2)
				{
					data.Add(subtitleRuns[i]["text"]?.ToString() ?? "");
				}
			}
			JsonObject sectionData = new JsonObject
			{
				["localized_title"] = sectionTitle,
				["data"] = data
			};
			if (sectionMap.TryGetValue(sectionTitle, out var key))
			{
				credits[key] = sectionData;
			}
			else
			{
				((JsonArray)credits["other_sections"]).Add(sectionData);
			}
		}
		return new JsonObject { ["data"] = credits };
	}

	public async Task<JsonObject> GetAccountInfoAsync(CancellationToken token)
	{
		JsonObject obj = await SendYoutubeiRequestAsync("account/account_menu", new JsonObject(), token).ConfigureAwait(continueOnCapturedContext: false);
		string name = obj.SelectTokens("..accountName.runs[0].text").FirstOrDefault()?.ToString() ?? "User";
		string email = obj.SelectTokens("..accountEmail.runs[0].text").FirstOrDefault()?.ToString() ?? "";
		string avatarUrl = obj.SelectTokens("..accountPhoto.thumbnails[0].url").FirstOrDefault()?.ToString() ?? "";
		return new JsonObject { ["data"] = new JsonObject
		{
			["accountName"] = name,
			["accountEmail"] = email,
			["accountPhoto"] = avatarUrl
		} };
	}

	public async Task<JsonObject> RateSongAsync(string videoId, string rating, CancellationToken token)
	{
		JsonObject payload = new JsonObject { ["target"] = new JsonObject { ["videoId"] = videoId } };
		string ep = ((rating == "LIKE") ? "like/like" : "like/removelike");
		await SendYoutubeiRequestAsync(ep, payload, token).ConfigureAwait(continueOnCapturedContext: false);
		return new JsonObject { ["success"] = true };
	}

	public async Task<JsonObject> RatePlaylistAsync(string playlistId, string rating, CancellationToken token)
	{
		JsonObject payload = new JsonObject { ["target"] = new JsonObject { ["playlistId"] = playlistId } };
		string ep = ((rating == "LIKE") ? "like/like" : "like/removelike");
		await SendYoutubeiRequestAsync(ep, payload, token).ConfigureAwait(continueOnCapturedContext: false);
		return new JsonObject { ["success"] = true };
	}

	public async Task<JsonObject> CreatePlaylistAsync(string title, string privacy, CancellationToken token)
	{
		JsonObject payload = new JsonObject
		{
			["title"] = title,
			["description"] = "",
			["privacyStatus"] = privacy
		};
		return await SendYoutubeiRequestAsync("playlist/create", payload, token).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<JsonObject> DeletePlaylistAsync(string playlistId, CancellationToken token)
	{
		if (playlistId.StartsWith("VL"))
		{
			playlistId = playlistId.Substring(2);
		}
		JsonObject payload = new JsonObject { ["playlistId"] = playlistId };
		await SendYoutubeiRequestAsync("playlist/delete", payload, token).ConfigureAwait(continueOnCapturedContext: false);
		return new JsonObject { ["success"] = true };
	}

	public async Task<JsonObject> RenamePlaylistAsync(string playlistId, string title, CancellationToken token)
	{
		if (playlistId.StartsWith("VL"))
		{
			playlistId = playlistId.Substring(2);
		}
		JsonObject payload = new JsonObject
		{
			["playlistId"] = playlistId,
			["actions"] = new JsonArray
			{
				new JsonObject
				{
					["action"] = "ACTION_SET_PLAYLIST_NAME",
					["playlistName"] = title
				}
			}
		};
		await SendYoutubeiRequestAsync("browse/edit_playlist", payload, token).ConfigureAwait(continueOnCapturedContext: false);
		return new JsonObject { ["success"] = true };
	}

	public async Task<JsonObject> AddPlaylistItemAsync(string playlistId, string videoId, CancellationToken token)
	{
		if (playlistId.StartsWith("VL"))
		{
			playlistId = playlistId.Substring(2);
		}
		JsonObject payload = new JsonObject
		{
			["playlistId"] = playlistId,
			["actions"] = new JsonArray
			{
				new JsonObject
				{
					["action"] = "ACTION_ADD_VIDEO",
					["addedVideoId"] = videoId
				}
			}
		};
		for (int i = 0; i < 4; i++)
		{
			try
			{
				await SendYoutubeiRequestAsync("browse/edit_playlist", payload, token).ConfigureAwait(continueOnCapturedContext: false);
				return new JsonObject { ["success"] = true };
			}
			catch (Exception)
			{
				if (i == 3)
				{
					throw;
				}
				await Task.Delay(1500, token);
			}
		}
		return new JsonObject { ["success"] = true };
	}

	public async Task<JsonObject> RemovePlaylistItemAsync(string playlistId, string videoId, CancellationToken token, string setVideoId = "")
	{
		if (string.IsNullOrEmpty(setVideoId))
		{
			return new JsonObject { ["success"] = false };
		}
		if (playlistId.StartsWith("VL"))
		{
			playlistId = playlistId.Substring(2);
		}
		JsonObject payload = new JsonObject
		{
			["playlistId"] = playlistId,
			["actions"] = new JsonArray
			{
				new JsonObject
				{
					["action"] = "ACTION_REMOVE_VIDEO_BY_SET_VIDEO_ID",
					["setVideoId"] = setVideoId
				}
			}
		};
		await SendYoutubeiRequestAsync("browse/edit_playlist", payload, token).ConfigureAwait(continueOnCapturedContext: false);
		return new JsonObject { ["success"] = true };
	}

	private async Task<int> GetSignatureTimestampAsync(CancellationToken token)
	{
		if (_cachedSignatureTimestamp > 0 && DateTime.UtcNow < _cachedSignatureTimestampExpiry)
		{
			return _cachedSignatureTimestamp;
		}
		await _stsLock.WaitAsync(token);
		try
		{
			if (_cachedSignatureTimestamp > 0 && DateTime.UtcNow < _cachedSignatureTimestampExpiry)
			{
				return _cachedSignatureTimestamp;
			}
			HttpClient client = new HttpClient
			{
				DefaultRequestHeaders = { { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36" } }
			};
			Match baseJsMatch = Regex.Match(await client.GetStringAsync("https://music.youtube.com", token), "\\\\/\\\\/s\\.ytimg\\.com\\\\/yts\\\\/jsbin\\\\/.*?-vfl.*?\\\\/base\\.js|\\\\/\\\\/s\\.ytimg\\.com\\\\/yts\\\\/jsbin\\\\/.*?-vfl.*?\\\\/player_ias\\.js|\\\\/s\\\\/player\\\\/[a-zA-Z0-9_-]+\\\\/player_ias\\.vflset\\\\/[a-zA-Z0-9_-]+\\\\/base\\.js");
			if (baseJsMatch.Success)
			{
				string jsUrl = baseJsMatch.Value.Replace("\\/", "/");
				if (jsUrl.StartsWith("//"))
				{
					jsUrl = "https:" + jsUrl;
				}
				else if (jsUrl.StartsWith("/"))
				{
					jsUrl = "https://music.youtube.com" + jsUrl;
				}
				Match stsMatch = Regex.Match(await client.GetStringAsync(jsUrl, token), "signatureTimestamp:(\\d+)");
				if (stsMatch.Success && int.TryParse(stsMatch.Groups[1].Value, out var sts))
				{
					_cachedSignatureTimestamp = sts;
					_cachedSignatureTimestampExpiry = DateTime.UtcNow.AddHours(24.0);
					return sts;
				}
			}
		}
		catch (Exception ex)
		{
			AppLogger.Log("Backend: Failed to fetch signatureTimestamp - " + ex.Message, LogLevel.Error);
		}
		finally
		{
			_stsLock.Release();
		}
		return (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalDays - 1;
	}

	public async Task<JsonObject> AddHistoryItemAsync(string videoId, CancellationToken token)
	{
		if (string.IsNullOrEmpty(videoId))
		{
			return new JsonObject { ["success"] = false };
		}
		try
		{
			await LoadAuthAsync();
			int sts = await GetSignatureTimestampAsync(token);
			JsonObject payload = new JsonObject
			{
				["context"] = new JsonObject { ["client"] = new JsonObject
				{
					["clientName"] = "WEB_REMIX",
					["clientVersion"] = "1.20230524.01.00"
				} },
				["videoId"] = videoId,
				["playbackContext"] = new JsonObject { ["contentPlaybackContext"] = new JsonObject { ["signatureTimestamp"] = sts } }
			};
			string trackingUrl = (await SendYoutubeiRequestAsync("player", payload, token).ConfigureAwait(continueOnCapturedContext: false)).SelectToken("..playbackTracking.videostatsPlaybackUrl.baseUrl")?.ToString() ?? "";
			if (string.IsNullOrEmpty(trackingUrl))
			{
				return new JsonObject { ["success"] = false };
			}
			string CPNA = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";
			Random random = new Random();
			string cpn = new string((from _ in Enumerable.Range(0, 16)
				select CPNA[random.Next(CPNA.Length)]).ToArray());
			string url = trackingUrl;
			url = url + (url.Contains("?") ? "&" : "?") + "ver=2&c=WEB_REMIX&cpn=" + cpn;
			HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url);
			if (!string.IsNullOrEmpty(_cookieString) && !string.IsNullOrEmpty(_sapisid))
			{
				req.Headers.TryAddWithoutValidation("Cookie", _cookieString);
				long time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
				using SHA1 sha1 = SHA1.Create();
				string hashHex = BitConverter.ToString(sha1.ComputeHash(Encoding.UTF8.GetBytes($"{time} {_sapisid} https://music.youtube.com"))).Replace("-", "").ToLowerInvariant();
				req.Headers.TryAddWithoutValidation("Authorization", $"SAPISIDHASH {time}_{hashHex}");
				req.Headers.TryAddWithoutValidation("X-Origin", "https://music.youtube.com");
			}
			await _client.SendAsync(req, token).ConfigureAwait(continueOnCapturedContext: false);
			return new JsonObject { ["success"] = true };
		}
		catch
		{
			return new JsonObject { ["success"] = false };
		}
	}

	public Task<MainWindow.PlaybackStreamInfo> FetchStreamUrlAsync(string videoId, CancellationToken token)
	{
		if (EnableStreamCache && _streamUrlCache.TryGetValue(videoId, out (MainWindow.PlaybackStreamInfo, DateTime) cached) && DateTime.UtcNow < cached.Item2)
		{
			return Task.FromResult(cached.Item1);
		}
		return _inflightStreamRequests.GetOrAdd(videoId, async delegate(string id)
		{
			try
			{
				MainWindow.PlaybackStreamInfo info = await FetchStreamUrlInternalAsync(id, token);
				if (EnableStreamCache)
				{
					_streamUrlCache[id] = (info, DateTime.UtcNow.AddHours(4.0));
				}
				return info;
			}
			finally
			{
				_inflightStreamRequests.TryRemove(id, out Task<MainWindow.PlaybackStreamInfo> _);
			}
		});
	}

	private async Task<string?> GetVisitorDataAsync(CancellationToken token)
	{
		if (!string.IsNullOrEmpty(_visitorData))
		{
			return _visitorData;
		}
		await _visitorDataLock.WaitAsync(token);
		try
		{
			if (!string.IsNullOrEmpty(_visitorData))
			{
				return _visitorData;
			}
			JsonObject payload = new JsonObject { ["context"] = new JsonObject { ["client"] = new JsonObject
			{
				["clientName"] = "ANDROID_VR",
				["clientVersion"] = "1.61.48"
			} } };
			_visitorData = (await SendYoutubeiRequestAsync("visitor_id", payload, token)).SelectToken("..visitorData")?.ToString();
			return _visitorData;
		}
		catch
		{
			return null;
		}
		finally
		{
			_visitorDataLock.Release();
		}
	}

	private async Task<MainWindow.PlaybackStreamInfo?> TryFetchNativeAsync(string videoId, string clientName, CancellationToken token)
	{
		_ = 2;
		try
		{
			JsonObject clientObj = clientName switch
			{
				"ANDROID" => new JsonObject
				{
					["clientName"] = "ANDROID",
					["clientVersion"] = "19.30.36",
					["hl"] = "en",
					["osName"] = "Android",
					["osVersion"] = "14"
				}, 
				"ANDROID_MUSIC" => new JsonObject
				{
					["clientName"] = "ANDROID_MUSIC",
					["clientVersion"] = "7.09.51",
					["hl"] = "en",
					["osName"] = "Android",
					["osVersion"] = "14"
				}, 
				"TVHTML5_SIMPLY_EMBEDDED_PLAYER" => new JsonObject
				{
					["clientName"] = "TVHTML5_SIMPLY_EMBEDDED_PLAYER",
					["clientVersion"] = "2.0"
				}, 
				"WEB_REMIX" => new JsonObject
				{
					["clientName"] = "WEB_REMIX",
					["clientVersion"] = "1.20230524.01.00"
				}, 
				"ANDROID_VR" => new JsonObject
				{
					["clientName"] = "ANDROID_VR",
					["clientVersion"] = "1.61.48",
					["osName"] = "Android",
					["osVersion"] = "11",
					["androidSdkVersion"] = 30,
					["hl"] = "en",
					["gl"] = "US"
				}, 
				"ANDROID_VR_1_43" => new JsonObject
				{
					["clientName"] = "ANDROID_VR",
					["clientVersion"] = "1.43.32",
					["osName"] = "Android",
					["osVersion"] = "12",
					["androidSdkVersion"] = 32,
					["hl"] = "en",
					["gl"] = "US"
				}, 
				_ => new JsonObject
				{
					["clientName"] = "IOS",
					["clientVersion"] = "19.29.1",
					["hl"] = "en",
					["deviceMake"] = "Apple",
					["deviceModel"] = "iPhone16,2",
					["osName"] = "iOS",
					["osVersion"] = "17.5.1"
				}, 
			};
			int sts = await GetSignatureTimestampAsync(token);
			JsonObject payload = new JsonObject
			{
				["context"] = new JsonObject { ["client"] = clientObj },
				["videoId"] = videoId,
				["playbackContext"] = new JsonObject { ["contentPlaybackContext"] = new JsonObject { ["signatureTimestamp"] = sts } }
			};
			string visitorData = await GetVisitorDataAsync(token);
			if (!string.IsNullOrEmpty(visitorData))
			{
				clientObj["visitorData"] = visitorData;
			}
			JsonObject res = await SendYoutubeiRequestAsync("player", payload, token).ConfigureAwait(continueOnCapturedContext: false);
			var bestFormat = (from f in res.SelectTokens("..streamingData.adaptiveFormats[*]")
				select new
				{
					Url = f["url"]?.ToString(),
					MimeType = (f["mimeType"]?.ToString() ?? ""),
					Bitrate = ((long?)f["bitrate"]).GetValueOrDefault()
				} into f
				where !string.IsNullOrEmpty(f.Url) && f.MimeType.Contains("audio/")
				orderby f.MimeType.Contains("opus") ? 1 : 0 descending, f.Bitrate descending
				select f).ToList().FirstOrDefault();
			string trackingUrl = res.SelectToken("..playbackTracking.videostatsPlaybackUrl.baseUrl")?.ToString() ?? "";
			if (bestFormat != null)
			{
				string codec = (bestFormat.MimeType.Contains("opus") ? "opus" : "m4a");
				long durationMs = 0L;
				string lenSecStr = res.SelectToken("videoDetails.lengthSeconds")?.ToString();
				if (!string.IsNullOrEmpty(lenSecStr) && long.TryParse(lenSecStr, out var lenSec))
				{
					durationMs = lenSec * 1000;
				}
				return new MainWindow.PlaybackStreamInfo
				{
					Url = bestFormat.Url,
					QualityLabel = $"{Math.Round((double)bestFormat.Bitrate / 1000.0)} kbps {codec}",
					Provider = "Native API (" + clientName + ")",
					TrackingUrl = trackingUrl,
					DurationMs = durationMs
				};
			}
		}
		catch (Exception ex)
		{
			AppLogger.Log($"Backend: Native API {clientName} failed for {videoId} - {ex.Message}", LogLevel.Error);
		}
		return null;
	}

	public async Task<string> TestPlayerResponseAsync(string videoId)
	{
		_ = 2;
		try
		{
			JsonObject clientObj = new JsonObject
			{
				["clientName"] = "WEB_REMIX",
				["clientVersion"] = "1.20230524.01.00"
			};
			int sts = await GetSignatureTimestampAsync(CancellationToken.None);
			JsonObject payload = new JsonObject
			{
				["context"] = new JsonObject { ["client"] = clientObj },
				["videoId"] = videoId,
				["playbackContext"] = new JsonObject { ["contentPlaybackContext"] = new JsonObject { ["signatureTimestamp"] = sts } }
			};
			string visitorData = await GetVisitorDataAsync(CancellationToken.None);
			if (!string.IsNullOrEmpty(visitorData))
			{
				clientObj["visitorData"] = visitorData;
			}
			return (await SendYoutubeiRequestAsync("player", payload, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false)).ToString();
		}
		catch (Exception ex)
		{
			return ex.ToString();
		}
	}

	private async Task<MainWindow.PlaybackStreamInfo> FetchStreamUrlInternalAsync(string videoId, CancellationToken token)
	{
		HashSet<string> clientsToTry = new HashSet<string> { "ANDROID_VR" };
		TaskCompletionSource<MainWindow.PlaybackStreamInfo?> tcs = new TaskCompletionSource<MainWindow.PlaybackStreamInfo>();
		int pending = clientsToTry.Count;
		foreach (string client in clientsToTry)
		{
			_ = _ = _ = _ = Task.Run(async delegate
			{
				try
				{
					MainWindow.PlaybackStreamInfo info = await TryFetchNativeAsync(videoId, client, token);
					if (info != null)
					{
						tcs.TrySetResult(info);
						return;
					}
				}
				catch
				{
				}
				if (Interlocked.Decrement(ref pending) == 0)
				{
					tcs.TrySetResult(null);
				}
			}, token);
		}
		MainWindow.PlaybackStreamInfo fastestInfo = await tcs.Task;
		if (fastestInfo != null)
		{
			return fastestInfo;
		}
		try
		{
			StreamManifest obj = await _youtubeClient.Videos.Streams.GetManifestAsync(videoId, token);
			long durationMs = 0L;
			AudioOnlyStreamInfo streamInfo = (from s in obj.GetAudioOnlyStreams()
				orderby s.AudioCodec.Contains("opus") ? 1 : 0 descending, s.Bitrate descending
				select s).FirstOrDefault();
			if (streamInfo != null)
			{
				return new MainWindow.PlaybackStreamInfo
				{
					Url = streamInfo.Url,
					QualityLabel = $"{Math.Round(streamInfo.Bitrate.KiloBitsPerSecond)} kbps {streamInfo.AudioCodec}",
					Provider = "YoutubeExplode",
					DurationMs = durationMs
				};
			}
			AppLogger.Log("Backend: YoutubeExplode returned no audio streams", LogLevel.Error);
		}
		catch (Exception ex)
		{
			AppLogger.Log("Backend: YoutubeExplode extraction failed - " + ex.Message, LogLevel.Error);
		}
		throw new Exception("No audio stream found.");
	}

	public async Task<JsonObject> DownloadSongAsync(string videoId, string outputDir, string format, CancellationToken token, string title = "", string artist = "", string thumbUrl = "")
	{
		if (string.IsNullOrWhiteSpace(videoId))
		{
			throw new ArgumentException("Missing video id.", "videoId");
		}
		outputDir = (string.IsNullOrEmpty(outputDir) ? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) : outputDir);
		Directory.CreateDirectory(outputDir);
		Video video = await _youtubeClient.Videos.GetAsync(videoId, token).ConfigureAwait(continueOnCapturedContext: false);
		List<AudioOnlyStreamInfo> streams = (await _youtubeClient.Videos.Streams.GetManifestAsync(videoId, token).ConfigureAwait(continueOnCapturedContext: false)).GetAudioOnlyStreams().ToList();
		if (streams.Count == 0)
		{
			throw new Exception("No audio stream found.");
		}
		string normalizedFormat = (format ?? "").Trim().ToLowerInvariant();
		IAudioStreamInfo streamInfo;
		switch (normalizedFormat)
		{
		case "m4a":
		case "mp4":
			streamInfo = (from s in streams
				where s.Container.Name.Equals("mp4", StringComparison.OrdinalIgnoreCase) || s.AudioCodec.Contains("mp4a", StringComparison.OrdinalIgnoreCase)
				orderby s.Bitrate descending
				select s).FirstOrDefault();
			break;
		case "webm":
			streamInfo = (from s in streams
				where s.Container.Name.Equals("webm", StringComparison.OrdinalIgnoreCase)
				orderby s.Bitrate descending
				select s).FirstOrDefault();
			break;
		default:
			streamInfo = (from s in streams
				orderby s.AudioCodec.Contains("opus", StringComparison.OrdinalIgnoreCase) ? 1 : 0 descending, s.Bitrate descending
				select s).FirstOrDefault();
			break;
		}
		if (streamInfo == null)
		{
			streamInfo = streams.OrderByDescending((AudioOnlyStreamInfo s) => s.Bitrate).First();
		}
		string resolvedTitle = (string.IsNullOrWhiteSpace(title) ? video.Title : title.Trim());
		string resolvedArtist = (string.IsNullOrWhiteSpace(artist) ? video.Author.ChannelTitle : artist.Trim());
		string extension = ((normalizedFormat == "opus") ? "opus" : (streamInfo.Container.Name.Equals("mp4", StringComparison.OrdinalIgnoreCase) ? "m4a" : streamInfo.Container.Name));
		if (string.IsNullOrWhiteSpace(extension))
		{
			extension = ((normalizedFormat == "webm") ? "webm" : "m4a");
		}
		byte[] artwork = await GetDownloadArtworkAsync(thumbUrl, video.Thumbnails.Select((Thumbnail t) => t.Url), token).ConfigureAwait(continueOnCapturedContext: false);
		string safeName = MakeSafeFileName(resolvedArtist + " - " + resolvedTitle);
		string outputPath = GetAvailablePath(Path.Combine(outputDir, safeName + "." + extension));
		if (normalizedFormat == "opus" && streamInfo.Container.Name.Equals("webm", StringComparison.OrdinalIgnoreCase))
		{
			string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.webm");
			try
			{
				await _youtubeClient.Videos.Streams.DownloadAsync(streamInfo, tempPath, null, token).ConfigureAwait(continueOnCapturedContext: false);
				RemuxWebmOpusToOggOpus(tempPath, outputPath, resolvedTitle, resolvedArtist, videoId);
				TryWriteAudioTags(outputPath, resolvedTitle, resolvedArtist, videoId, artwork);
			}
			finally
			{
				try
				{
					if (System.IO.File.Exists(tempPath))
					{
						System.IO.File.Delete(tempPath);
					}
				}
				catch
				{
				}
			}
		}
		else
		{
			await _youtubeClient.Videos.Streams.DownloadAsync(streamInfo, outputPath, null, token).ConfigureAwait(continueOnCapturedContext: false);
			TryWriteAudioTags(outputPath, resolvedTitle, resolvedArtist, videoId, artwork);
		}
		return new JsonObject
		{
			["success"] = true,
			["path"] = outputPath,
			["format"] = extension,
			["codec"] = streamInfo.AudioCodec,
			["bitrate"] = Math.Round(streamInfo.Bitrate.KiloBitsPerSecond)
		};
	}

	private static string MakeSafeFileName(string name)
	{
		char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
		foreach (char c in invalidFileNameChars)
		{
			name = name.Replace(c, '_');
		}
		name = Regex.Replace(name, "\\s+", " ").Trim();
		if (!string.IsNullOrWhiteSpace(name))
		{
			return name;
		}
		return "download";
	}

	private static string GetAvailablePath(string path)
	{
		if (!System.IO.File.Exists(path))
		{
			return path;
		}
		string dir = Path.GetDirectoryName(path) ?? "";
		string name = Path.GetFileNameWithoutExtension(path);
		string ext = Path.GetExtension(path);
		int i = 2;
		string candidate;
		while (true)
		{
			candidate = Path.Combine(dir, $"{name} ({i}){ext}");
			if (!System.IO.File.Exists(candidate))
			{
				break;
			}
			i++;
		}
		return candidate;
	}

	private async Task<byte[]?> GetDownloadArtworkAsync(string preferredUrl, IEnumerable<string> fallbackUrls, CancellationToken token)
	{
		List<string> urls = new List<string>();
		if (!string.IsNullOrWhiteSpace(preferredUrl))
		{
			urls.Add(preferredUrl);
		}
		urls.AddRange(fallbackUrls.Where((string u) => !string.IsNullOrWhiteSpace(u)));
		foreach (string item in urls.Distinct())
		{
			string url = item;
			if (url.Contains("googleusercontent.com") || url.Contains("ggpht.com"))
			{
				int eqIndex = url.LastIndexOf("=");
				url = ((eqIndex > 0) ? (url.Substring(0, eqIndex) + "=w800-h800-p-l90-rj") : (url + "=w800-h800-p-l90-rj"));
			}
			try
			{
				return await _client.GetByteArrayAsync(url, token).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch
			{
			}
		}
		return null;
	}

	private static void TryWriteAudioTags(string path, string title, string artist, string videoId, byte[]? artwork)
	{
		try
		{
			using TagLib.File file = TagLib.File.Create(path);
			file.Tag.Title = title;
			file.Tag.Performers = SplitArtists(artist);
			file.Tag.AlbumArtists = SplitArtists(artist);
			file.Tag.Comment = "Downloaded from YouTube Music: https://music.youtube.com/watch?v=" + videoId;
			if (artwork != null && artwork.Length != 0)
			{
				string mimeType = GetImageMimeType(artwork);
				file.Tag.Pictures = new IPicture[1]
				{
					new Picture(new ByteVector(artwork))
					{
						Type = PictureType.FrontCover,
						Description = "Cover",
						MimeType = mimeType
					}
				};
			}
			file.Save();
		}
		catch (Exception ex)
		{
			AppLogger.Log($"Backend: Failed to write download metadata for '{path}' - {ex.Message}", LogLevel.Error);
		}
	}

	private static void RemuxWebmOpusToOggOpus(string webmPath, string opusPath, string title, string artist, string videoId)
	{
		WebmOpusData demuxed = DemuxWebmOpus(System.IO.File.ReadAllBytes(webmPath));
		if (demuxed.Packets.Count == 0)
		{
			throw new Exception("No Opus packets found in WebM stream.");
		}
		using FileStream output = System.IO.File.Create(opusPath);
		int serial = Random.Shared.Next(1, int.MaxValue);
		int sequence = 0;
		long granule = 0L;
		byte[] opusHead = ((demuxed.OpusHead.Length != 0) ? demuxed.OpusHead : CreateDefaultOpusHead());
		WriteOggPage(output, opusHead, serial, ref sequence, 0L, 2);
		byte[] tags = CreateOpusTags(title, artist, videoId);
		WriteOggPage(output, tags, serial, ref sequence, 0L, 0);
		for (int i = 0; i < demuxed.Packets.Count; i++)
		{
			byte[] packet = demuxed.Packets[i];
			granule += GetOpusPacketSampleCount(packet);
			byte headerType = (byte)((i == demuxed.Packets.Count - 1) ? 4 : 0);
			WriteOggPage(output, packet, serial, ref sequence, granule, headerType);
		}
	}

	private static WebmOpusData DemuxWebmOpus(byte[] data)
	{
		WebmOpusData result = new WebmOpusData();
		long segmentStart = 0L;
		long segmentEnd = data.Length;
		foreach (EbmlElement element in ReadEbmlElements(data, 0L, data.Length))
		{
			if (element.Id == 408125543)
			{
				segmentStart = element.DataOffset;
				segmentEnd = Math.Min(element.EndOffset, data.Length);
				break;
			}
		}
		foreach (EbmlElement element2 in ReadEbmlElements(data, segmentStart, segmentEnd))
		{
			if (element2.Id == 374648427)
			{
				ReadWebmTracks(data, element2.DataOffset, element2.EndOffset, result);
			}
		}
		if (result.TrackNumber <= 0)
		{
			throw new Exception("No Opus audio track found.");
		}
		foreach (EbmlElement element3 in ReadEbmlElements(data, segmentStart, segmentEnd))
		{
			if (element3.Id == 524531317)
			{
				ReadWebmCluster(data, element3.DataOffset, element3.EndOffset, result);
			}
		}
		return result;
	}

	private static void ReadWebmTracks(byte[] data, long start, long end, WebmOpusData result)
	{
		foreach (EbmlElement trackEntry in from e in ReadEbmlElements(data, start, end)
			where e.Id == 174
			select e)
		{
			int trackNumber = 0;
			int trackType = 0;
			string codecId = "";
			byte[] codecPrivate = Array.Empty<byte>();
			foreach (EbmlElement child in ReadEbmlElements(data, trackEntry.DataOffset, trackEntry.EndOffset))
			{
				if (child.Id == 215)
				{
					trackNumber = (int)ReadUnsignedInt(data, child.DataOffset, child.DataSize);
				}
				else if (child.Id == 131)
				{
					trackType = (int)ReadUnsignedInt(data, child.DataOffset, child.DataSize);
				}
				else if (child.Id == 134)
				{
					codecId = Encoding.ASCII.GetString(data, (int)child.DataOffset, (int)child.DataSize);
				}
				else if (child.Id == 25506)
				{
					codecPrivate = data.AsSpan((int)child.DataOffset, (int)child.DataSize).ToArray();
				}
			}
			if (trackType == 2 && codecId.Equals("A_OPUS", StringComparison.OrdinalIgnoreCase))
			{
				result.TrackNumber = trackNumber;
				result.OpusHead = codecPrivate;
				break;
			}
		}
	}

	private static void ReadWebmCluster(byte[] data, long start, long end, WebmOpusData result)
	{
		foreach (EbmlElement element in ReadEbmlElements(data, start, end))
		{
			if (element.Id == 163)
			{
				ReadWebmBlock(data, element.DataOffset, element.EndOffset, result);
			}
			else
			{
				if (element.Id != 160)
				{
					continue;
				}
				foreach (EbmlElement blockGroupChild in ReadEbmlElements(data, element.DataOffset, element.EndOffset))
				{
					if (blockGroupChild.Id == 161)
					{
						ReadWebmBlock(data, blockGroupChild.DataOffset, blockGroupChild.EndOffset, result);
					}
				}
			}
		}
	}

	private static void ReadWebmBlock(byte[] data, long start, long end, WebmOpusData result)
	{
		int pos = (int)start;
		int limit = (int)end;
		if ((int)ReadVint(data, ref pos, limit, out var _) != result.TrackNumber || pos + 3 > limit)
		{
			return;
		}
		pos += 2;
		int lacing = (data[pos++] & 6) >> 1;
		foreach (byte[] frame in ReadBlockFrames(data, pos, limit, lacing))
		{
			if (frame.Length != 0)
			{
				result.Packets.Add(frame);
			}
		}
	}

	private static IEnumerable<byte[]> ReadBlockFrames(byte[] data, int pos, int limit, int lacing)
	{
		if (lacing == 0)
		{
			yield return data.AsSpan(pos, limit - pos).ToArray();
		}
		else
		{
			if (pos >= limit)
			{
				yield break;
			}
			int length = pos++;
			int frameCount = data[length] + 1;
			if (frameCount <= 0)
			{
				yield break;
			}
			List<int> sizes = new List<int>();
			switch (lacing)
			{
			case 1:
			{
				for (int j = 0; j < frameCount - 1; j++)
				{
					int size = 0;
					byte b;
					do
					{
						if (pos < limit)
						{
							b = data[pos++];
							size += b;
							continue;
						}
						yield break;
					}
					while (b == byte.MaxValue);
					sizes.Add(size);
				}
				break;
			}
			case 2:
			{
				int fixedSize = (limit - pos) / frameCount;
				for (int k = 0; k < frameCount - 1; k++)
				{
					sizes.Add(fixedSize);
				}
				break;
			}
			case 3:
			{
				int firstLen;
				ulong first = ReadVint(data, ref pos, limit, out firstLen);
				sizes.Add((int)first);
				int previous = (int)first;
				for (int i = 1; i < frameCount - 1; i++)
				{
					long delta = ReadSignedVint(data, ref pos, limit, out length);
					previous += (int)delta;
					sizes.Add(previous);
				}
				break;
			}
			}
			int known = sizes.Sum();
			sizes.Add(Math.Max(0, limit - pos - known));
			foreach (int size2 in sizes)
			{
				if (size2 >= 0 && pos + size2 <= limit)
				{
					yield return data.AsSpan(pos, size2).ToArray();
					pos += size2;
					continue;
				}
				yield break;
			}
		}
	}

	private static IEnumerable<EbmlElement> ReadEbmlElements(byte[] data, long start, long end)
	{
		long pos = start;
		end = Math.Min(end, data.Length);
		while (pos < end)
		{
			int intPos = (int)pos;
			if (!TryReadEbmlId(data, ref intPos, (int)end, out var id) || !TryReadEbmlSize(data, ref intPos, (int)end, out var size))
			{
				break;
			}
			long dataOffset = intPos;
			long dataEnd = ((size < 0) ? end : Math.Min(dataOffset + size, end));
			yield return new EbmlElement(id, dataOffset, dataEnd - dataOffset, dataEnd);
			pos = dataEnd;
		}
	}

	private static bool TryReadEbmlId(byte[] data, ref int pos, int limit, out ulong id)
	{
		id = 0uL;
		if (pos >= limit)
		{
			return false;
		}
		int length = GetVintLength(data[pos]);
		if (length <= 0 || pos + length > limit)
		{
			return false;
		}
		for (int i = 0; i < length; i++)
		{
			id = (id << 8) | data[pos++];
		}
		return true;
	}

	private static bool TryReadEbmlSize(byte[] data, ref int pos, int limit, out long size)
	{
		size = 0L;
		if (pos >= limit)
		{
			return false;
		}
		byte first = data[pos++];
		int length = GetVintLength(first);
		if (length <= 0 || pos + length - 1 > limit)
		{
			return false;
		}
		ulong value = (ulong)(first & (255 >> length));
		ulong unknown = (ulong)((1L << 7 * length) - 1);
		for (int i = 1; i < length; i++)
		{
			value = (value << 8) | data[pos++];
		}
		size = ((value == unknown) ? (-1L) : ((long)value));
		return true;
	}

	private static int GetVintLength(byte first)
	{
		for (int length = 1; length <= 8; length++)
		{
			if ((first & (128 >> length - 1)) != 0)
			{
				return length;
			}
		}
		return 0;
	}

	private static ulong ReadVint(byte[] data, ref int pos, int limit, out int length)
	{
		if (pos >= limit)
		{
			throw new InvalidDataException("Unexpected end of EBML data.");
		}
		byte first = data[pos++];
		length = GetVintLength(first);
		if (length <= 0 || pos + length - 1 > limit)
		{
			throw new InvalidDataException("Invalid EBML variable integer.");
		}
		ulong value = (ulong)(first & (255 >> length));
		for (int i = 1; i < length; i++)
		{
			value = (value << 8) | data[pos++];
		}
		return value;
	}

	private static long ReadSignedVint(byte[] data, ref int pos, int limit, out int length)
	{
		ulong num = ReadVint(data, ref pos, limit, out length);
		long bias = (1L << 7 * length - 1) - 1;
		return (long)num - bias;
	}

	private static ulong ReadUnsignedInt(byte[] data, long start, long size)
	{
		ulong value = 0uL;
		for (int i = 0; i < size; i++)
		{
			value = (value << 8) | data[(int)start + i];
		}
		return value;
	}

	private static byte[] CreateDefaultOpusHead()
	{
		byte[] head = new byte[19];
		Encoding.ASCII.GetBytes("OpusHead").CopyTo(head, 0);
		head[8] = 1;
		head[9] = 2;
		BitConverter.GetBytes((ushort)312).CopyTo(head, 10);
		BitConverter.GetBytes(48000u).CopyTo(head, 12);
		return head;
	}

	private static byte[] CreateOpusTags(string title, string artist, string videoId)
	{
		using MemoryStream ms = new MemoryStream();
		ms.Write(Encoding.ASCII.GetBytes("OpusTags"));
		WriteLittleEndian(ms, "Spectre".Length);
		ms.Write(Encoding.UTF8.GetBytes("Spectre"));
		List<string> comments = new List<string>();
		if (!string.IsNullOrWhiteSpace(title))
		{
			comments.Add("TITLE=" + title);
		}
		if (!string.IsNullOrWhiteSpace(artist))
		{
			comments.Add("ARTIST=" + artist);
		}
		comments.Add("COMMENT=Downloaded from YouTube Music: https://music.youtube.com/watch?v=" + videoId);
		WriteLittleEndian(ms, comments.Count);
		foreach (string comment in comments)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(comment);
			WriteLittleEndian(ms, bytes.Length);
			ms.Write(bytes);
		}
		return ms.ToArray();
	}

	private static void WriteOggPage(Stream output, byte[] packet, int serial, ref int sequence, long granulePosition, byte headerType)
	{
		int offset = 0;
		bool firstSegment = true;
		while (offset < packet.Length || firstSegment)
		{
			int chunkSize = Math.Min(packet.Length - offset, 65025);
			List<byte> laces = new List<byte>();
			int remaining;
			for (remaining = chunkSize; remaining >= 255; remaining -= 255)
			{
				laces.Add(byte.MaxValue);
			}
			laces.Add((byte)remaining);
			using MemoryStream page = new MemoryStream();
			page.Write(Encoding.ASCII.GetBytes("OggS"));
			page.WriteByte(0);
			page.WriteByte((byte)((!firstSegment) ? 1 : headerType));
			page.Write(BitConverter.GetBytes(granulePosition));
			page.Write(BitConverter.GetBytes(serial));
			page.Write(BitConverter.GetBytes(sequence++));
			page.Write(new byte[4]);
			page.WriteByte((byte)laces.Count);
			page.Write(laces.ToArray());
			page.Write(packet, offset, chunkSize);
			byte[] pageBytes = page.ToArray();
			BitConverter.GetBytes(ComputeOggCrc(pageBytes)).CopyTo(pageBytes, 22);
			output.Write(pageBytes, 0, pageBytes.Length);
			offset += chunkSize;
			firstSegment = false;
		}
	}

	private static uint ComputeOggCrc(byte[] data)
	{
		uint crc = 0u;
		foreach (byte b in data)
		{
			crc ^= (uint)(b << 24);
			for (int j = 0; j < 8; j++)
			{
				crc = (((crc & 0x80000000u) != 0) ? ((crc << 1) ^ 0x4C11DB7) : (crc << 1));
			}
		}
		return crc;
	}

	private static int GetOpusPacketSampleCount(byte[] packet)
	{
		if (packet.Length == 0)
		{
			return 960;
		}
		byte num = packet[0];
		int config = num >> 3;
		int num2;
		switch (num & 3)
		{
		case 0:
			num2 = 1;
			break;
		case 1:
			num2 = 2;
			break;
		case 2:
			num2 = 2;
			break;
		case 3:
			if (packet.Length > 1)
			{
				num2 = packet[1] & 0x3F;
				break;
			}
			goto default;
		default:
			num2 = 1;
			break;
		}
		int frames = num2;
		int samplesPerFrame = ((config < 12) ? ((config & 3) switch
		{
			0 => 480, 
			1 => 960, 
			2 => 1920, 
			_ => 2880, 
		}) : ((config >= 16) ? (120 << (config & 3)) : (((config & 1) == 0) ? 480 : 960)));
		return Math.Max(1, frames) * samplesPerFrame;
	}

	private static void WriteLittleEndian(Stream stream, int value)
	{
		stream.Write(BitConverter.GetBytes(value));
	}

	private static void WriteBigEndian(Stream stream, int value)
	{
		stream.WriteByte((byte)((value >> 24) & 0xFF));
		stream.WriteByte((byte)((value >> 16) & 0xFF));
		stream.WriteByte((byte)((value >> 8) & 0xFF));
		stream.WriteByte((byte)(value & 0xFF));
	}

	private static string GetImageMimeType(byte[] bytes)
	{
		if (bytes.Length >= 3 && bytes[0] == byte.MaxValue && bytes[1] == 216 && bytes[2] == byte.MaxValue)
		{
			return "image/jpeg";
		}
		if (bytes.Length >= 8 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71 && bytes[4] == 13 && bytes[5] == 10 && bytes[6] == 26 && bytes[7] == 10)
		{
			return "image/png";
		}
		if (bytes.Length >= 12 && bytes[0] == 82 && bytes[1] == 73 && bytes[2] == 70 && bytes[3] == 70 && bytes[8] == 87 && bytes[9] == 69 && bytes[10] == 66 && bytes[11] == 80)
		{
			return "image/webp";
		}
		return "image/jpeg";
	}

	private static string[] SplitArtists(string artist)
	{
		if (string.IsNullOrWhiteSpace(artist))
		{
			return Array.Empty<string>();
		}
		return (from a in artist.Split(new char[3] { ',', ';', '&' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			where !string.IsNullOrWhiteSpace(a)
			select a).ToArray();
	}

	public void Close()
	{
		_client?.Dispose();
	}
}













