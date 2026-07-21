using System;

namespace Spectre.Services;

public interface IPlaybackService
{
	int Volume { get; set; }

	long Time { get; set; }

	float Position { get; set; }

	long Length { get; }

	bool IsPlaying { get; }

	event EventHandler? EndReached;

	event EventHandler? Playing;

	event EventHandler? Paused;

	void Play(string url, bool useCrossfade = false, bool isLive = false);

	void Pause();

	void Resume();

	void Stop();
}
