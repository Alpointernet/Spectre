using System;
using System.Windows;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Services;
using Spectre.ViewModels;

namespace Spectre;

public partial class App : Application
{
	public new static App Current => (App)Application.Current;

	public IServiceProvider Services { get; }

	public App()
	{
		try
		{
			string profileDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spectre", "Cache");
			System.IO.Directory.CreateDirectory(profileDir);
			System.Runtime.ProfileOptimization.SetProfileRoot(profileDir);
			System.Runtime.ProfileOptimization.StartProfile("Startup.Profile");
		}
		catch { }

		Services = ConfigureServices();
	}

	private static IServiceProvider ConfigureServices()
	{
		ServiceCollection services = new ServiceCollection();
		services.AddSingleton<IPlaybackService, PlaybackService>();
		services.AddSingleton<IQueueService, QueueService>();
		services.AddSingleton<MainViewModel>();
		services.AddSingleton<PlayerBarViewModel>();
		services.AddSingleton<QueueViewModel>();
		services.AddSingleton<SidebarViewModel>();
		return services.BuildServiceProvider();
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
