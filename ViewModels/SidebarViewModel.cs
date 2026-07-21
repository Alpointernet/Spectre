using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Spectre.ViewModels;

public partial class SidebarViewModel : ViewModelBase
{
	[ObservableProperty]
	private ObservableCollection<LibraryItemViewModel> _libraryItems = new ObservableCollection<LibraryItemViewModel>();

	[RelayCommand]
	private void Navigate(string pageId)
	{
		WeakReferenceMessenger.Default.Send(new NavigateMessage(pageId));
	}
}
