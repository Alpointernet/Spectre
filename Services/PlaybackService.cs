using System;

namespace Spectre.Services;

public class PlaybackService : IPlaybackService, IDisposable
{
	public AudioEngine _engine;

	public int Volume
	{
		get
		{
			return _engine.Volume;
		}
		set
		{
			_engine.Volume = value;
		}
	}

	public long Time
	{
		get
		{
			return _engine.Time;
		}
		set
		{
			_engine.Time = value;
		}
	}

	public float Position
	{
		get
		{
			return _engine.Position;
		}
		set
		{
			_engine.Position = value;
		}
	}

	public long Length => _engine.Length;

	public bool IsPlaying => _engine.IsPlaying;

	public event EventHandler? EndReached;

	public event EventHandler? Playing;

	public event EventHandler? Paused;

	public PlaybackService()
	{
		_engine = new AudioEngine();
		_engine.EndReached += delegate
		{
			this.EndReached?.Invoke(this, EventArgs.Empty);
		};
		_engine.Playing += delegate
		{
			this.Playing?.Invoke(this, EventArgs.Empty);
		};
		_engine.Paused += delegate
		{
			this.Paused?.Invoke(this, EventArgs.Empty);
		};
	}

	public void Play(string url, bool useCrossfade = false, bool isLive = false)
	{
		_engine.Play(url, useCrossfade, isLive);
	}

	public void Pause()
	{
		_engine.Pause();
	}

	public void Resume()
	{
		_engine.Resume();
	}

	public void Stop()
	{
		_engine.Stop();
	}

	public void Dispose()
	{
		_engine?.Dispose();
	}
}
