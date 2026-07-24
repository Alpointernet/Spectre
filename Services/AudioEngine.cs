using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Windows.Media.Playback;
using Windows.Media.Core;

namespace Spectre;

public class AudioEngine : IDisposable
{
	private MediaPlayer? _player1;
	private MediaPlayer? _player2;
	private bool _usePlayer1 = true;
	private bool _isCrossfading;
	private CancellationTokenSource? _fadeCts;
	private int _targetVolume = 100;
	private int _networkCacheMs;

	private MediaPlayer? ActivePlayer => !_usePlayer1 ? _player2 : _player1;
	private MediaPlayer? FadingPlayer => !_usePlayer1 ? _player1 : _player2;

	private Stopwatch _monotonicStopwatch = Stopwatch.StartNew();
	private long _lastStopwatchMs = 0;
	private long _reportedTime = 0;

	public int CrossfadeMs { get; set; }

	public int Volume
	{
		get => _targetVolume;
		set
		{
			_targetVolume = value;
			if (!_isCrossfading && ActivePlayer != null)
			{
				ActivePlayer.Volume = (value / 100.0) * 0.8;
			}
		}
	}

	public long Time
	{
		get
		{
			long nowMs = _monotonicStopwatch.ElapsedMilliseconds;
			long elapsed = nowMs - _lastStopwatchMs;
			_lastStopwatchMs = nowMs;

			if (ActivePlayer == null) return 0;
			
			long uwpTime = (long)ActivePlayer.PlaybackSession.Position.TotalMilliseconds;
			
			if (ActivePlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
			{
				_reportedTime += elapsed;
				
				long drift = uwpTime - _reportedTime;
				if (Math.Abs(drift) > 500 && Math.Abs(drift) < 5000)
				{
					_reportedTime += (long)(drift * 0.1);
				}
			}
			else
			{
				if (uwpTime > _reportedTime || (_reportedTime - uwpTime) >= 3000)
				{
					_reportedTime = uwpTime;
				}
			}

			long length = this.Length;
			if (length > 0 && _reportedTime > length)
			{
				_reportedTime = length;
			}

			return _reportedTime;
		}
		set
		{
			if (ActivePlayer != null)
			{
				ActivePlayer.PlaybackSession.Position = TimeSpan.FromMilliseconds(value);
				_reportedTime = value;
				_lastStopwatchMs = _monotonicStopwatch.ElapsedMilliseconds;
			}
		}
	}

	public float Position
	{
		get
		{
			if (ActivePlayer == null) return 0;
			var dur = ActivePlayer.PlaybackSession.NaturalDuration.TotalMilliseconds;
			if (dur <= 0) return 0;
			return (float)(this.Time / dur);
		}
		set
		{
			if (ActivePlayer != null)
			{
				var dur = ActivePlayer.PlaybackSession.NaturalDuration.TotalMilliseconds;
				if (dur > 0) this.Time = (long)(value * dur);
			}
		}
	}

	public long Length => (long)(ActivePlayer?.PlaybackSession.NaturalDuration.TotalMilliseconds ?? 0);

	public bool IsPlaying => ActivePlayer?.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

	public event EventHandler? EndReached;
	public event EventHandler? Playing;
	public event EventHandler? Paused;

	public AudioEngine(bool loudnessNormalization = false, int networkCacheMs = 250)
	{
		_networkCacheMs = networkCacheMs;
		
		_player1 = new MediaPlayer();
		_player2 = new MediaPlayer();
		
		_player1.Volume = (_targetVolume / 100.0) * 0.8;
		_player2.Volume = (_targetVolume / 100.0) * 0.8;

		_player1.MediaEnded += (s, e) =>
		{
			if (_usePlayer1) EndReached?.Invoke(this, EventArgs.Empty);
		};
		_player1.PlaybackSession.PlaybackStateChanged += (s, e) =>
		{
			if (_usePlayer1)
			{
				if (s.PlaybackState == MediaPlaybackState.Playing) Playing?.Invoke(this, EventArgs.Empty);
				else if (s.PlaybackState == MediaPlaybackState.Paused) Paused?.Invoke(this, EventArgs.Empty);
			}
		};
		
		_player2.MediaEnded += (s, e) =>
		{
			if (!_usePlayer1) EndReached?.Invoke(this, EventArgs.Empty);
		};
		_player2.PlaybackSession.PlaybackStateChanged += (s, e) =>
		{
			if (!_usePlayer1)
			{
				if (s.PlaybackState == MediaPlaybackState.Playing) Playing?.Invoke(this, EventArgs.Empty);
				else if (s.PlaybackState == MediaPlaybackState.Paused) Paused?.Invoke(this, EventArgs.Empty);
			}
		};
	}

	public void Play(string url, bool useCrossfade = false, bool isLive = false)
	{
		AppLogger.Log($"AudioEngine: Play called for URL: {url}, isLive: {isLive}", LogLevel.Info);
		_usePlayer1 = !_usePlayer1;
		_fadeCts?.Cancel();
		_fadeCts = new CancellationTokenSource();
		CancellationToken token = _fadeCts.Token;
		
		MediaPlayer? oldPlayer = FadingPlayer;
		MediaPlayer? newPlayer = ActivePlayer;
		
		_reportedTime = 0;
		_lastStopwatchMs = _monotonicStopwatch.ElapsedMilliseconds;

		try
		{
			if (newPlayer != null)
			{
				newPlayer.Source = MediaSource.CreateFromUri(new Uri(url));
				newPlayer.Play();
			}
		}
		catch (Exception ex)
		{
			AppLogger.Log($"AudioEngine: Failed to start playback for '{url}' - {ex}", LogLevel.Error);
			return;
		}

		Task.Run(async delegate
		{
			if (useCrossfade && CrossfadeMs > 0 && oldPlayer != null && oldPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
			{
				_isCrossfading = true;
				if (newPlayer != null) newPlayer.Volume = 0;
				double startVol = oldPlayer.Volume;
				int steps = Math.Max(1, CrossfadeMs / 50);
				int stepDelay = CrossfadeMs / steps;
				
				for (int i = 1; i <= steps; i++)
				{
					if (token.IsCancellationRequested) break;
					
					float progress = (float)i / (float)steps;
					float volIn = (float)Math.Sqrt(progress);
					float volOut = (float)Math.Sqrt(1f - progress);
					double currentMaxVol = (_targetVolume / 100.0) * 0.8;
					
					if (newPlayer != null) newPlayer.Volume = currentMaxVol * volIn;
					oldPlayer.Volume = startVol * volOut;
					
					try
					{
						await Task.Delay(stepDelay, token);
					}
					catch
					{
						break;
					}
				}
				
				oldPlayer.Pause();
				oldPlayer.Source = null;
				
				if (!token.IsCancellationRequested && newPlayer != null)
				{
					newPlayer.Volume = (_targetVolume / 100.0) * 0.8;
				}
				_isCrossfading = false;
			}
			else
			{
				if (oldPlayer != null)
				{
					oldPlayer.Pause();
					oldPlayer.Source = null;
				}
				if (newPlayer != null)
				{
					newPlayer.Volume = (_targetVolume / 100.0) * 0.8;
				}
			}
		}, token);
	}

	public void Pause()
	{
		_player1?.Pause();
		_player2?.Pause();
	}

	public void Resume()
	{
		ActivePlayer?.Play();
	}

	public void Stop()
	{
		_fadeCts?.Cancel();
		_player1?.Pause();
		if (_player1 != null) _player1.Source = null;
		
		_player2?.Pause();
		if (_player2 != null) _player2.Source = null;
	}

	public void Dispose()
	{
		_fadeCts?.Cancel();
		if (_player1 != null)
		{
			_player1.Pause();
			_player1.Source = null;
			_player1.Dispose();
		}
		if (_player2 != null)
		{
			_player2.Pause();
			_player2.Source = null;
			_player2.Dispose();
		}
	}
}
