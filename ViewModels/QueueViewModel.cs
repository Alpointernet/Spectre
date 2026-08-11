using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Spectre.ViewModels;

public partial class QueueViewModel : ViewModelBase
{
	[ObservableProperty]
	private ObservableCollection<QueueItemViewModel> _items = new ObservableCollection<QueueItemViewModel>();
}
