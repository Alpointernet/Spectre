using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Foundation;

namespace Spectre;

public class AudioEngine : IDisposable
{
	// Three players deliberately bound the warm playback set to current, previous,
	// and next.  They are paused when idle, so this avoids an unbounded cache while
	// making both directions of normal queue navigation immediate.
	private readonly MediaPlayer?[] _players = new MediaPlayer?[3];
	private readonly bool[] _hasStarted = new bool[3];
	private readonly string?[] _playerUrls = new string?[3];
	private int _activeIndex;
	private int _previousIndex = -1;
	private string? _previousUrl;
	private int _preparedIndex = -1;
	private string? _preparedUrl;
	private bool _preparedIsReady;
	private int _prepareGeneration;
	private bool _isCrossfading;
	private bool _warmDecodersEnabled;
	private CancellationTokenSource? _fadeCts;
	private int _targetVolume = 100;

	private MediaPlayer? ActivePlayer => _players[_activeIndex];

	public int CrossfadeMs { get; set; }
	public bool WarmDecodersEnabled
	{
		get => _warmDecodersEnabled;
		set
		{
			if (_warmDecodersEnabled == value) return;
			_warmDecodersEnabled = value;
			if (!value) ReleaseWarmPlayers();
		}
	}
	public int Volume
	{
		get => _targetVolume;
		set
		{
			_targetVolume = value;
			if (!_isCrossfading && ActivePlayer != null)
				ActivePlayer.Volume = value / 100.0 * 0.8;
		}
	}

	public long Time
	{
		get => (long)(ActivePlayer?.PlaybackSession.Position.TotalMilliseconds ?? 0);
		set { if (ActivePlayer != null) ActivePlayer.PlaybackSession.Position = TimeSpan.FromMilliseconds(value); }
	}
	public float Position
	{
		get
		{
			if (ActivePlayer == null) return 0;
			double duration = ActivePlayer.PlaybackSession.NaturalDuration.TotalMilliseconds;
			return duration <= 0 ? 0 : (float)(Time / duration);
		}
		set
		{
			if (ActivePlayer != null && ActivePlayer.PlaybackSession.NaturalDuration.TotalMilliseconds > 0)
				Time = (long)(value * ActivePlayer.PlaybackSession.NaturalDuration.TotalMilliseconds);
		}
	}
	public long Length => (long)(ActivePlayer?.PlaybackSession.NaturalDuration.TotalMilliseconds ?? 0);
	public bool IsPlaying => ActivePlayer?.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
	public event EventHandler? EndReached;
	public event EventHandler? Playing;
	public event EventHandler? Paused;

	public AudioEngine(bool loudnessNormalization = false, int networkCacheMs = 250)
	{
		_players[0] = CreatePlayer(0);
	}

	private MediaPlayer CreatePlayer(int index)
	{
		MediaPlayer player = new MediaPlayer { Volume = _targetVolume / 100.0 * 0.8 };
		player.CommandManager.IsEnabled = false;
		player.MediaEnded += (s, e) =>
		{
			if (_activeIndex == index && _hasStarted[index]) EndReached?.Invoke(this, EventArgs.Empty);
		};
		player.MediaFailed += (s, e) =>
		{
			AppLogger.Log($"AudioEngine: MediaPlayer {index + 1} reported a media failure.", LogLevel.Error);
			if (_preparedIndex == index)
			{
				_preparedIsReady = false;
				_preparedUrl = null;
			}
		};
		player.PlaybackSession.PlaybackStateChanged += (s, e) =>
		{
			if (_activeIndex != index) return;
			if (s.PlaybackState == MediaPlaybackState.Playing)
			{
				_hasStarted[index] = true;
				Playing?.Invoke(this, EventArgs.Empty);
			}
			else if (s.PlaybackState == MediaPlaybackState.Paused && _hasStarted[index]) Paused?.Invoke(this, EventArgs.Empty);
		};
		return player;
	}

	public void Play(string url, bool useCrossfade = false, bool isLive = false)
	{
		AppLogger.Log($"AudioEngine: Play called for URL: {url}, isLive: {isLive}", LogLevel.Info);
		_fadeCts?.Cancel();
		_fadeCts = new CancellationTokenSource();
		_prepareGeneration++; // prevents a late MediaOpened from pausing the selected player

		int oldIndex = _activeIndex;
		bool usePrepared = _warmDecodersEnabled && _preparedIsReady && _preparedIndex >= 0 && string.Equals(_preparedUrl, url, StringComparison.Ordinal);
		bool usePrevious = _warmDecodersEnabled && !usePrepared && _previousIndex >= 0 && string.Equals(_previousUrl, url, StringComparison.Ordinal);
		int targetIndex = usePrepared ? _preparedIndex : usePrevious ? _previousIndex : FindAvailablePlayer();
		if (_players[oldIndex] == null) _players[oldIndex] = CreatePlayer(oldIndex);
		if (_players[targetIndex] == null) _players[targetIndex] = CreatePlayer(targetIndex);
		MediaPlayer? oldPlayer = _players[oldIndex];
		MediaPlayer? player = _players[targetIndex];
		bool doCrossfade = useCrossfade && CrossfadeMs > 0 && oldPlayer?.PlaybackSession.PlaybackState == MediaPlaybackState.Playing && targetIndex != oldIndex;

		try
		{
			if (!usePrepared && !usePrevious)
			{
				player?.Pause();
				if (player != null) player.Source = null;
				if (player != null) player.Source = MediaSource.CreateFromUri(new Uri(url));
				_playerUrls[targetIndex] = url;
			}
			_preparedIndex = -1;
			_preparedUrl = null;
			_preparedIsReady = false;
			_activeIndex = targetIndex;
			_hasStarted[targetIndex] = false;
			// A warm player may have played this track before (for example, when the
			// user returns to the previous song). Every navigation starts a track
			// from the beginning; only explicit seeking should preserve a position.
			if (player != null) player.PlaybackSession.Position = TimeSpan.Zero;

			if (doCrossfade)
			{
				player!.Volume = 0;
				player.Play();
				StartCrossfade(oldPlayer!, player, _fadeCts.Token);
			}
			else
			{
				player?.Play();
				if (player != null) player.Volume = _targetVolume / 100.0 * 0.8;
				oldPlayer?.Pause();
			}

			// Keep the directly departed track loaded for a fast Previous action.
			if (oldIndex != targetIndex && _warmDecodersEnabled)
			{
				_previousIndex = oldIndex;
				_previousUrl = _playerUrls[oldIndex];
			}
			else if (oldIndex != targetIndex && !doCrossfade)
			{
				oldPlayer?.Pause();
				if (oldPlayer != null) oldPlayer.Source = null;
				_playerUrls[oldIndex] = null;
				_previousIndex = -1;
				_previousUrl = null;
			}
		}
		catch (Exception ex)
		{
			AppLogger.Log($"AudioEngine: Failed to start playback for '{url}' - {ex}", LogLevel.Error);
		}
	}

	private int FindAvailablePlayer()
	{
		for (int i = 0; i < _players.Length; i++)
			if (i != _activeIndex && i != _previousIndex) return i;
		// This only occurs before a previous source exists; replace the oldest idle slot.
		return (_activeIndex + 1) % _players.Length;
	}

	private void StartCrossfade(MediaPlayer oldPlayer, MediaPlayer newPlayer, CancellationToken token)
	{
		_ = Task.Run(async () =>
		{
			_isCrossfading = true;
			double startingVolume = oldPlayer.Volume;
			int steps = Math.Max(1, CrossfadeMs / 50);
			for (int i = 1; i <= steps && !token.IsCancellationRequested; i++)
			{
				float progress = (float)i / steps;
				double max = _targetVolume / 100.0 * 0.8;
				newPlayer.Volume = max * Math.Sqrt(progress);
				oldPlayer.Volume = startingVolume * Math.Sqrt(1 - progress);
				try { await Task.Delay(CrossfadeMs / steps, token); } catch { break; }
			}
			if (!token.IsCancellationRequested)
			{
				oldPlayer.Pause();
				if (!_warmDecodersEnabled) oldPlayer.Source = null;
				newPlayer.Volume = _targetVolume / 100.0 * 0.8;
			}
			_isCrossfading = false;
		}, token);
	}

	public void Preload(string url)
	{
		if (!_warmDecodersEnabled || string.IsNullOrWhiteSpace(url) || _isCrossfading || string.Equals(_previousUrl, url, StringComparison.Ordinal) ||
			(_preparedIndex >= 0 && string.Equals(_preparedUrl, url, StringComparison.Ordinal))) return;

		int index = FindAvailablePlayer();
		if (_players[index] == null) _players[index] = CreatePlayer(index);
		MediaPlayer? player = _players[index];
		if (player == null) return;
		int generation = ++_prepareGeneration;
		_preparedIndex = index;
		_preparedUrl = url;
		_preparedIsReady = false;
		player.Volume = 0;
		TypedEventHandler<MediaPlayer, object>? openedHandler = null;
		openedHandler = (sender, args) =>
		{
			if (generation == _prepareGeneration && _preparedIndex == index && string.Equals(_preparedUrl, url, StringComparison.Ordinal))
			{
				try
				{
					sender.Pause();
					sender.PlaybackSession.Position = TimeSpan.Zero;
					_preparedIsReady = true;
				}
				catch { _preparedIsReady = false; }
			}
			sender.MediaOpened -= openedHandler;
		};
		try
		{
			player.MediaOpened += openedHandler;
			player.Pause();
			player.Source = null;
			player.Source = MediaSource.CreateFromUri(new Uri(url));
			_playerUrls[index] = url;
			player.Play();
		}
		catch
		{
			player.MediaOpened -= openedHandler;
			if (_preparedIndex == index) { _preparedIndex = -1; _preparedUrl = null; _preparedIsReady = false; }
		}
	}

	private void ReleaseWarmPlayers()
	{
		_prepareGeneration++;
		for (int i = 0; i < _players.Length; i++)
		{
			if (i == _activeIndex) continue;
			_players[i]?.Pause();
			if (_players[i] != null) _players[i]!.Source = null;
			_playerUrls[i] = null;
		}
		_previousIndex = _preparedIndex = -1;
		_previousUrl = _preparedUrl = null;
		_preparedIsReady = false;
	}

	public void Pause() { foreach (MediaPlayer? player in _players) player?.Pause(); }
	public void Resume() => ActivePlayer?.Play();
	public void Stop()
	{
		_fadeCts?.Cancel();
		for (int i = 0; i < _players.Length; i++)
		{
			_players[i]?.Pause();
			if (_players[i] != null) _players[i]!.Source = null;
			_playerUrls[i] = null;
		}
		_previousIndex = _preparedIndex = -1;
		_previousUrl = _preparedUrl = null;
		_preparedIsReady = false;
	}
	public void Dispose()
	{
		Stop();
		foreach (MediaPlayer? player in _players) player?.Dispose();
	}
}
