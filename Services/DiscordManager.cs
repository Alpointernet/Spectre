using System;
using System.Linq;
using DiscordRPC;
using DiscordRPC.Message;


namespace Spectre.Services;

public class DiscordManager : IDisposable
{
	private DiscordRpcClient? _discordClient;
	private readonly string _clientId;
	private readonly string _iconUrl;
	private bool _isEnabled;

	public DiscordManager(string clientId, string iconUrl, bool isEnabled = false)
	{
		_clientId = clientId;
		_iconUrl = iconUrl;
		_isEnabled = isEnabled;
	}

	public void Initialize()
	{
		if (!_isEnabled) return;
		try
		{
			if (_discordClient == null && !string.IsNullOrEmpty(_clientId))
			{
				_discordClient = new DiscordRpcClient(_clientId);
				_discordClient.OnReady += delegate(object sender, ReadyMessage e)
				{
					AppLogger.Log("Discord: RPC connected as " + e.User.Username, LogLevel.Success);
				};
				_discordClient.OnError += delegate(object sender, ErrorMessage e)
				{
					AppLogger.Log($"Discord: RPC error [{e.Code}] - {e.Message}", LogLevel.Error);
				};
				_discordClient.OnClose += delegate(object sender, CloseMessage e)
				{
					AppLogger.Log($"Discord: RPC closed [{e.Code}] - {e.Reason}", LogLevel.Warning);
				};
				_discordClient.Initialize();
			}
		}
		catch (Exception value)
		{
			AppLogger.Log($"Discord: Initialization failed - {value}", LogLevel.Error);
		}
	}

	private string? LimitStr(string? str, int maxLength)
	{
		if (string.IsNullOrEmpty(str)) return null;
		return str.Length > maxLength ? str.Substring(0, maxLength) : str;
	}

	public void UpdatePresence(string title, string artist, string album, string thumbUrl, bool isPlaying, double positionMs, double durationMs)
	{
		if (!_isEnabled || _discordClient == null)
		{
			return;
		}
		try
		{
			if (!isPlaying)
			{
				_discordClient.ClearPresence();
				return;
			}
			
			string stateText = artist;
			RichPresence presence = new RichPresence
			{
				Type = ActivityType.Listening,
				StatusDisplay = StatusDisplayType.State,
				Details = LimitStr(title, 128),
				State = LimitStr(stateText, 128),
				Assets = new Assets
				{
					// DiscordRPC 1.6.1.70 allows up to 256 chars for System.Windows.Controls.Image keys (which can be external URLs).
					LargeImageKey = (!string.IsNullOrEmpty(thumbUrl) && thumbUrl.Length <= 256) 
						? thumbUrl 
						: (!string.IsNullOrEmpty(_iconUrl) && _iconUrl.Length <= 256 ? _iconUrl : null),
					LargeImageText = LimitStr(album, 128)
				}
			};
			if (durationMs > 0.0 && isPlaying)
			{
				DateTime startTime = DateTime.UtcNow.Subtract(TimeSpan.FromMilliseconds(positionMs));
				DateTime endTime = startTime.Add(TimeSpan.FromMilliseconds(durationMs));
				presence.Timestamps = new Timestamps
				{
					Start = startTime,
					End = endTime
				};
			}
			_discordClient.SetPresence(presence);
		}
		catch (Exception ex)
		{
			AppLogger.Log("Discord: Update failed - " + ex.Message, LogLevel.Error);
		}
	}

	public void SetEnabled(bool enabled)
	{
		_isEnabled = enabled;
		if (_isEnabled)
		{
			Initialize();
		}
		else
		{
			Dispose();
		}
	}

	public void Dispose()
	{
		try
		{
			if (_discordClient != null)
			{
				_discordClient.ClearPresence();
				_discordClient.Dispose();
				_discordClient = null;
			}
		}
		catch
		{
		}
	}
}

