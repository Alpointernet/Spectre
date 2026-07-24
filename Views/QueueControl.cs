using System;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Spectre.Views;

public partial class QueueControl : UserControl
{
	public Border QueueSidebarBorderRef => QueueSidebarBorder;

	public ListBox QueueSidebarPanelRef => QueueSidebarPanel;

	public event MouseWheelEventHandler? QueueScrollViewer_PreviewMouseWheel;

	public QueueControl()
	{
		InitializeComponent();
	}

	private void QueueSidebarPanel_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		this.QueueScrollViewer_PreviewMouseWheel?.Invoke(sender, e);
	}

	private void Image_TargetUpdated(object sender, DataTransferEventArgs e)
	{
		if (sender is System.Windows.Controls.Image img && img.Source is BitmapSource bs)
		{
			if (bs.IsDownloading)
			{
				img.Opacity = 0.0;
				EventHandler? completedHandler = null;
				completedHandler = delegate
				{
					bs.DownloadCompleted -= completedHandler;
					DoubleAnimation anim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(250.0));
					img.BeginAnimation(Image.OpacityProperty, anim);
				};
				bs.DownloadCompleted += completedHandler;

				EventHandler<System.Windows.Media.ExceptionEventArgs>? failedHandler = null;
				failedHandler = delegate
				{
					bs.DownloadFailed -= failedHandler;
					img.Opacity = 1.0;
				};
				bs.DownloadFailed += failedHandler;
			}
			else
			{
				img.Opacity = 1.0;
			}
		}
	}
}

