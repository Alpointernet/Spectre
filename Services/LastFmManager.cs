using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Nodes;

namespace Spectre.Services;

public static class LastFmManager
{
	private const string ApiBaseUrl = "http://ws.audioscrobbler.com/2.0/";

	public static bool IsEnabled => !string.IsNullOrEmpty(SessionKey);

	public static string ApiKey { get; set; } = "";

	public static string SharedSecret { get; set; } = "";

	public static string SessionKey { get; set; } = "";

	public static string Username { get; set; } = "";

	private static string GenerateApiSignature(Dictionary<string, string> parameters, string secret)
	{
		IOrderedEnumerable<string> orderedEnumerable = parameters.Keys.OrderBy<string, string>((string k) => k, StringComparer.Ordinal);
		StringBuilder sb = new StringBuilder();
		foreach (string key in orderedEnumerable)
		{
			sb.Append(key);
			sb.Append(parameters[key]);
		}
		sb.Append(secret);
		using MD5 md5 = MD5.Create();
		return BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").ToLower();
	}

	public static async Task<string> GetAuthTokenAsync(string apiKey, string secret)
	{
		try
		{
			string sig = GenerateApiSignature(new Dictionary<string, string>
			{
				{ "api_key", apiKey },
				{ "method", "auth.gettoken" }
			}, secret);
			string url = $"{"http://ws.audioscrobbler.com/2.0/"}?method=auth.gettoken&api_key={apiKey}&api_sig={sig}&format=json";
			using HttpClient client = new HttpClient();
			return JsonNode.Parse(await client.GetStringAsync(url))["token"]?.ToString() ?? "";
		}
		catch (Exception ex)
		{
			AppLogger.Log("Last.fm: GetAuthToken failed - " + ex.Message, LogLevel.Error);
			return "";
		}
	}

	public static async Task<(string SessionKey, string Username)?> GetSessionAsync(string apiKey, string secret, string token)
	{
		try
		{
			string sig = GenerateApiSignature(new Dictionary<string, string>
			{
				{ "api_key", apiKey },
				{ "method", "auth.getsession" },
				{ "token", token }
			}, secret);
			string url = $"{"http://ws.audioscrobbler.com/2.0/"}?method=auth.getsession&api_key={apiKey}&token={token}&api_sig={sig}&format=json";
			using HttpClient client = new HttpClient();
			JsonNode json = JsonNode.Parse(await client.GetStringAsync(url));
			if (json["session"] != null)
			{
				string sk = json["session"]["key"]?.ToString() ?? "";
				string name = json["session"]["name"]?.ToString() ?? "";
				return (sk, name);
			}
		}
		catch
		{
		}
		return null;
	}

	public static async Task UpdateNowPlayingAsync(string track, string artist, string album)
	{
		if (!IsEnabled)
		{
			return;
		}
		try
		{
			Dictionary<string, string> parameters = new Dictionary<string, string>
			{
				{ "method", "track.updatenowplaying" },
				{ "api_key", ApiKey },
				{ "track", track },
				{ "artist", artist },
				{ "sk", SessionKey }
			};
			if (!string.IsNullOrEmpty(album))
			{
				parameters.Add("album", album);
			}
			string sig = GenerateApiSignature(parameters, SharedSecret);
			parameters.Add("api_sig", sig);
			parameters.Add("format", "json");
			using HttpClient client = new HttpClient();
			FormUrlEncodedContent content = new FormUrlEncodedContent(parameters);
			HttpResponseMessage response = await client.PostAsync(ApiBaseUrl, content);
			if (response.IsSuccessStatusCode)
			{
				AppLogger.Log("Last.fm: NowPlaying updated successfully", LogLevel.Success);
			}
			else
			{
				string error = await response.Content.ReadAsStringAsync();
				AppLogger.Log($"Last.fm: NowPlaying failed - {response.StatusCode}: {error}", LogLevel.Error);
			}
		}
		catch (Exception ex)
		{
			AppLogger.Log("Last.fm: NowPlaying failed - " + ex.Message, LogLevel.Error);
		}
	}

	public static async Task ScrobbleAsync(string track, string artist, string album, long timestamp)
	{
		if (!IsEnabled)
		{
			return;
		}
		try
		{
			Dictionary<string, string> parameters = new Dictionary<string, string>
			{
				{ "method", "track.scrobble" },
				{ "api_key", ApiKey },
				{ "track[0]", track },
				{ "artist[0]", artist },
				{
					"timestamp[0]",
					timestamp.ToString()
				},
				{ "sk", SessionKey }
			};
			if (!string.IsNullOrEmpty(album))
			{
				parameters.Add("album[0]", album);
			}
			string sig = GenerateApiSignature(parameters, SharedSecret);
			parameters.Add("api_sig", sig);
			parameters.Add("format", "json");
			using HttpClient client = new HttpClient();
			FormUrlEncodedContent content = new FormUrlEncodedContent(parameters);
			HttpResponseMessage response = await client.PostAsync(ApiBaseUrl, content);
			if (response.IsSuccessStatusCode)
			{
				AppLogger.Log("Last.fm: Scrobble successful", LogLevel.Success);
			}
			else
			{
				string error = await response.Content.ReadAsStringAsync();
				AppLogger.Log($"Last.fm: Scrobble failed - {response.StatusCode}: {error}", LogLevel.Error);
			}
		}
		catch (Exception ex)
		{
			AppLogger.Log("Last.fm: Scrobble failed - " + ex.Message, LogLevel.Error);
		}
	}

	public static async Task<JsonNode?> GetUserInfoAsync()
	{
		if (!IsEnabled || string.IsNullOrEmpty(Username))
		{
			return null;
		}
		try
		{
			string url = $"{"http://ws.audioscrobbler.com/2.0/"}?method=user.getinfo&user={Username}&api_key={ApiKey}&format=json";
			using HttpClient client = new HttpClient();
			return JsonNode.Parse(await client.GetStringAsync(url));
		}
		catch (Exception ex)
		{
			AppLogger.Log("Last.fm: GetUserInfo failed - " + ex.Message, LogLevel.Error);
			return null;
		}
	}

	public static async Task<JsonNode?> GetTopArtistsAsync(int limit = 10, string period = "overall")
	{
		if (!IsEnabled || string.IsNullOrEmpty(Username))
		{
			return null;
		}
		try
		{
			string url = $"{"http://ws.audioscrobbler.com/2.0/"}?method=user.gettopartists&user={Username}&api_key={ApiKey}&limit={limit}&period={period}&format=json";
			using HttpClient client = new HttpClient();
			return JsonNode.Parse(await client.GetStringAsync(url));
		}
		catch (Exception ex)
		{
			AppLogger.Log("Last.fm: GetTopArtists failed - " + ex.Message, LogLevel.Error);
			return null;
		}
	}

	public static async Task<JsonNode?> GetTopTracksAsync(int limit = 10, string period = "overall")
	{
		if (!IsEnabled || string.IsNullOrEmpty(Username))
		{
			return null;
		}
		try
		{
			string url = $"{"http://ws.audioscrobbler.com/2.0/"}?method=user.gettoptracks&user={Username}&api_key={ApiKey}&limit={limit}&period={period}&format=json";
			using HttpClient client = new HttpClient();
			return JsonNode.Parse(await client.GetStringAsync(url));
		}
		catch (Exception ex)
		{
			AppLogger.Log("Last.fm: GetTopTracks failed - " + ex.Message, LogLevel.Error);
			return null;
		}
	}

	public static async Task<JsonNode?> GetRecentTracksAsync(int limit = 10)
	{
		if (!IsEnabled || string.IsNullOrEmpty(Username))
		{
			return null;
		}
		try
		{
			string url = $"{"http://ws.audioscrobbler.com/2.0/"}?method=user.getrecenttracks&user={Username}&api_key={ApiKey}&limit={limit}&format=json";
			using HttpClient client = new HttpClient();
			return JsonNode.Parse(await client.GetStringAsync(url));
		}
		catch (Exception ex)
		{
			AppLogger.Log("Last.fm: GetRecentTracks failed - " + ex.Message, LogLevel.Error);
			return null;
		}
	}

	public static async Task<JsonNode?> GetTopAlbumsAsync(int limit = 10, string period = "overall")
	{
		if (!IsEnabled || string.IsNullOrEmpty(Username))
		{
			return null;
		}
		try
		{
			string url = $"{"http://ws.audioscrobbler.com/2.0/"}?method=user.gettopalbums&user={Username}&api_key={ApiKey}&limit={limit}&period={period}&format=json";
			using HttpClient client = new HttpClient();
			return JsonNode.Parse(await client.GetStringAsync(url));
		}
		catch (Exception ex)
		{
			AppLogger.Log("Last.fm: GetTopAlbums failed - " + ex.Message, LogLevel.Error);
			return null;
		}
	}
}
