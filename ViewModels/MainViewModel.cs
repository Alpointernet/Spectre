using CommunityToolkit.Mvvm.ComponentModel;

namespace Spectre.ViewModels;

public partial class MainViewModel : ViewModelBase
{
	[ObservableProperty]
	private string _windowTitle = "Spectre";
}
