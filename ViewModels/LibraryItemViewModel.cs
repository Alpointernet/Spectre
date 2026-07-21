using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Spectre.ViewModels;

public partial class LibraryItemViewModel : ObservableObject
{
	[ObservableProperty]
	private string _title = string.Empty;

	[ObservableProperty]
	private string _playlistId = string.Empty;

	[ObservableProperty]
	private string _albumId = string.Empty;

	[ObservableProperty]
	private string _iconUrl = string.Empty;

	[ObservableProperty]
	private bool _isPlaylist;

	[RelayCommand]
	private void Open()
	{
		if (IsPlaylist)
		{
			WeakReferenceMessenger.Default.Send(new NavigateMessage("playlists_page"));
		}
		else
		{
			WeakReferenceMessenger.Default.Send(new NavigateMessage("albums_page"));
		}
	}
}
