using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Spectre.Services;

namespace Spectre.ViewModels;

public partial class PlayerBarViewModel : ViewModelBase
{
	private readonly IPlaybackService _playbackService;

	[ObservableProperty]
	private string _title = "No Track Selected";

	[ObservableProperty]
	private string _artist = "";

	[ObservableProperty]
	private string _thumbnailUrl = "";

	[ObservableProperty]
	private string _currentTimeText = "0:00";

	[ObservableProperty]
	private string _totalTimeText = "0:00";

	[ObservableProperty]
	private double _progress;

	[ObservableProperty]
	private double _volume = 100.0;

	[ObservableProperty]
	private bool _isPlaying;

	public PlayerBarViewModel(IPlaybackService playbackService)
	{
		_playbackService = playbackService;
		_playbackService.Playing += delegate
		{
			IsPlaying = true;
		};
		_playbackService.Paused += delegate
		{
			IsPlaying = false;
		};
		_playbackService.EndReached += delegate
		{
			IsPlaying = false;
		};
	}

	[RelayCommand]
	private void PlayPause()
	{
		WeakReferenceMessenger.Default.Send(new PlayPauseMessage());
	}

	[RelayCommand]
	private void Next()
	{
		WeakReferenceMessenger.Default.Send(new NextTrackMessage());
	}

	[RelayCommand]
	private void Prev()
	{
		WeakReferenceMessenger.Default.Send(new PrevTrackMessage());
	}

	[RelayCommand]
	private void ToggleShuffle()
	{
		WeakReferenceMessenger.Default.Send(new ShuffleMessage());
	}

	[RelayCommand]
	private void ToggleRepeat()
	{
		WeakReferenceMessenger.Default.Send(new RepeatMessage());
	}

	[RelayCommand]
	private void ToggleQueue()
	{
		WeakReferenceMessenger.Default.Send(new ToggleQueueMessage());
	}

	[RelayCommand]
	private void ToggleLyrics()
	{
		WeakReferenceMessenger.Default.Send(new ToggleLyricsMessage());
	}
}
