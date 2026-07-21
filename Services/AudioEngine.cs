using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;

namespace Spectre;

public class AudioEngine : IDisposable
{
	private const string HttpUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

	private LibVLC _libvlc;

	private MediaPlayer _player1;

	private MediaPlayer _player2;

	private Media? _player1Media;

	private Media? _player2Media;

	private bool _usePlayer1 = true;

	private bool _isCrossfading;

	private CancellationTokenSource? _fadeCts;

	private int _targetVolume = 100;

	private int _networkCacheMs;

	private MediaPlayer ActivePlayer
	{
		get
		{
			if (!_usePlayer1)
			{
				return _player2;
			}
			return _player1;
		}
	}

	private MediaPlayer FadingPlayer
	{
		get
		{
			if (!_usePlayer1)
			{
				return _player1;
			}
			return _player2;
		}
	}

	public int CrossfadeMs { get; set; }

	public int Volume
	{
		get
		{
			return _targetVolume;
		}
		set
		{
			_targetVolume = value;
			if (!_isCrossfading)
			{
				ActivePlayer.Volume = (int)((double)value * 0.8);
			}
		}
	}

	public long Time
	{
		get
		{
			return ActivePlayer.Time;
		}
		set
		{
			ActivePlayer.Time = value;
		}
	}

	public float Position
	{
		get
		{
			return ActivePlayer.Position;
		}
		set
		{
			ActivePlayer.Position = value;
		}
	}

	public long Length => ActivePlayer.Length;

	public bool IsPlaying => ActivePlayer.IsPlaying;

	public event EventHandler? EndReached;

	public event EventHandler? Playing;

	public event EventHandler? Paused;

	public AudioEngine(bool loudnessNormalization = false, int networkCacheMs = 250)
	{
		_networkCacheMs = networkCacheMs;
		Core.Initialize();
		string[] options = new string[4]
		{
			$"--network-caching={networkCacheMs}",
			"--no-video",
			"--aout=directsound",
			"--http-user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"
		};
		if (loudnessNormalization)
		{
			options = new string[12]
			{
				$"--network-caching={networkCacheMs}",
				"--no-video",
				"--aout=directsound",
				"--http-user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
				"--audio-filter=compressor",
				"--compressor-rms-peak=0.1",
				"--compressor-attack=15.0",
				"--compressor-release=300.0",
				"--compressor-threshold=-14.0",
				"--compressor-ratio=3.0",
				"--compressor-knee=6.0",
				"--compressor-makeup-gain=2.0"
			};
		}
		_libvlc = new LibVLC(options);
		_player1 = new MediaPlayer(_libvlc);
		_player2 = new MediaPlayer(_libvlc);
		_player1.Volume = (int)((double)_targetVolume * 0.8);
		_player2.Volume = (int)((double)_targetVolume * 0.8);
		_player1.EndReached += delegate
		{
			if (_usePlayer1)
			{
				this.EndReached?.Invoke(this, EventArgs.Empty);
			}
		};
		_player1.Playing += delegate
		{
			if (_usePlayer1)
			{
				this.Playing?.Invoke(this, EventArgs.Empty);
			}
		};
		_player1.Paused += delegate
		{
			if (_usePlayer1)
			{
				this.Paused?.Invoke(this, EventArgs.Empty);
			}
		};
		_player2.EndReached += delegate
		{
			if (!_usePlayer1)
			{
				this.EndReached?.Invoke(this, EventArgs.Empty);
			}
		};
		_player2.Playing += delegate
		{
			if (!_usePlayer1)
			{
				this.Playing?.Invoke(this, EventArgs.Empty);
			}
		};
		_player2.Paused += delegate
		{
			if (!_usePlayer1)
			{
				this.Paused?.Invoke(this, EventArgs.Empty);
			}
		};
	}

	private void SetPlayerMedia(MediaPlayer player, Media? media)
	{
		if (player == _player1)
		{
			_player1Media?.Dispose();
			_player1Media = media;
		}
		else
		{
			_player2Media?.Dispose();
			_player2Media = media;
		}
	}

	private Media? GetCurrentMedia(MediaPlayer player)
	{
		if (player != _player1)
		{
			return _player2Media;
		}
		return _player1Media;
	}

	private void AppendVlcLog(string message)
	{
		try
		{
			File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spectre", "vlc_log.txt"), message);
		}
		catch
		{
		}
	}

	public void Play(string url, bool useCrossfade = false, bool isLive = false)
	{
		AppLogger.Log($"AudioEngine: Play called for URL: {url}, isLive: {isLive}", LogLevel.Info);
		MediaPlayer oldPlayer = ActivePlayer;
		_usePlayer1 = !_usePlayer1;
		MediaPlayer newPlayer = ActivePlayer;
		_fadeCts?.Cancel();
		_fadeCts = new CancellationTokenSource();
		CancellationToken token = _fadeCts.Token;
		Task.Run(async delegate
		{
			bool started = false;
			Media media = null;
			string playUrl = url;
			try
			{
				media = new Media(_libvlc, playUrl, FromType.FromLocation);
				int currentCacheMs = (isLive ? Math.Max(3000, _networkCacheMs) : _networkCacheMs);
				media.AddOption($":network-caching={currentCacheMs}");
				media.AddOption($":file-caching={currentCacheMs}");
				media.AddOption($":live-caching={currentCacheMs}");
				media.AddOption(":clock-jitter=0");
				media.AddOption(":clock-synchro=0");
				media.AddOption(":no-mkv-preload-local-dir");
				media.AddOption(":tcp-nodelay");
				media.AddOption(":ipv4");
				SetPlayerMedia(newPlayer, media);
				started = newPlayer.Play(media);
				if (!started)
				{
					TryDisposeMedia();
				}
			}
			catch (Exception value)
			{
				AppLogger.Log($"AudioEngine: Failed to start playback for '{playUrl}' - {value}", LogLevel.Error);
				TryDisposeMedia();
			}
			if (started)
			{
				if (useCrossfade && CrossfadeMs > 0 && oldPlayer.IsPlaying)
				{
					_isCrossfading = true;
					newPlayer.Volume = 0;
					int startVol = oldPlayer.Volume;
					int steps = Math.Max(1, CrossfadeMs / 50);
					int stepDelay = CrossfadeMs / steps;
					for (int i = 1; i <= steps; i++)
					{
						if (token.IsCancellationRequested)
						{
							break;
						}
						float progress = (float)i / (float)steps;
						float volIn = (float)Math.Sqrt(progress);
						float volOut = (float)Math.Sqrt(1f - progress);
						int currentMaxVol = (int)((double)_targetVolume * 0.8);
						newPlayer.Volume = (int)((float)currentMaxVol * volIn);
						oldPlayer.Volume = (int)((float)startVol * volOut);
						try
						{
							await Task.Delay(stepDelay, token);
						}
						catch
						{
							break;
						}
					}
					oldPlayer.Stop();
					if (!token.IsCancellationRequested)
					{
						newPlayer.Volume = (int)((double)_targetVolume * 0.8);
					}
					_isCrossfading = false;
				}
				else
				{
					oldPlayer.Stop();
					newPlayer.Volume = (int)((double)_targetVolume * 0.8);
				}
			}

			void TryDisposeMedia()
			{
				if (media != null && GetCurrentMedia(newPlayer) == media)
				{
					SetPlayerMedia(newPlayer, null);
				}
				media?.Dispose();
				media = null;
			}
		}, token);
	}

	public void Pause()
	{
		_player1.Pause();
		_player2.Pause();
	}

	public void Resume()
	{
		if (_usePlayer1)
		{
			_player1.Play();
		}
		else
		{
			_player2.Play();
		}
	}

	public void Stop()
	{
		_fadeCts?.Cancel();
		Task.Run(delegate
		{
			_player1.Stop();
			_player2.Stop();
		});
	}

	public void Dispose()
	{
		_fadeCts?.Cancel();
		_player1Media?.Dispose();
		_player2Media?.Dispose();
		_player1?.Dispose();
		_player2?.Dispose();
		_libvlc.Dispose();
	}
}
