using System;
using System.Windows;
using System.Windows.Media.Animation;
using Spectre.Services;
using Spectre.ViewModels;

namespace Spectre;

public partial class App : Application
{
	public new static App Current => (App)Application.Current;

	public IPlaybackService PlaybackService { get; } = new PlaybackService();
	public IQueueService QueueService { get; } = new QueueService();
	public MainViewModel MainViewModel { get; } = new MainViewModel();
	public PlayerBarViewModel PlayerBarViewModel { get; }
	public QueueViewModel QueueViewModel { get; } = new QueueViewModel();
	public SidebarViewModel SidebarViewModel { get; } = new SidebarViewModel();

	public App()
	{
		PlayerBarViewModel = new PlayerBarViewModel(PlaybackService);
		try
		{
			string profileDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spectre", "Cache");
			System.IO.Directory.CreateDirectory(profileDir);
			System.Runtime.ProfileOptimization.SetProfileRoot(profileDir);
			System.Runtime.ProfileOptimization.StartProfile("Startup.Profile");
		}
		catch { }
	}

	protected override void OnStartup(StartupEventArgs e)
	{
		Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata
		{
			DefaultValue = 144
		});
		base.OnStartup(e);
	}
}
