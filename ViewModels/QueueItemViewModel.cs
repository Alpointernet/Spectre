using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Spectre.ViewModels;

public partial class QueueItemViewModel : ObservableObject
{
	[ObservableProperty]
	private int _index;

	[ObservableProperty]
	private string _numberText = string.Empty;

	[ObservableProperty]
	private string _title = string.Empty;

	[ObservableProperty]
	private string _artist = string.Empty;

	[ObservableProperty]
	private string _thumbnailUrl = string.Empty;

	[ObservableProperty]
	private string _videoId = string.Empty;

	[ObservableProperty]
	private bool _isPlaying;

	[RelayCommand]
	private void Play()
	{
		WeakReferenceMessenger.Default.Send(new PlayQueueItemMessage
		{
			TargetIndex = Index,
			VideoId = VideoId,
			Title = Title,
			Artist = Artist,
			ThumbnailUrl = ThumbnailUrl
		});
	}
}
