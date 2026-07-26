using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Spectre.Views;

public partial class SidebarControl : UserControl
{
	public Border SidebarBorderRef => SidebarBorder;

	public StackPanel SidebarTitlePanelRef => SidebarTitlePanel;

	public TextBlock SidebarTitleTextRef => SidebarTitleText;

	public StackPanel HomeNavPanelRef => HomeNavPanel;

	public SidebarTab HomeNavBorderRef => HomeNavBorder;

	public SidebarTab ExploreNavBorderRef => ExploreNavBorder;

	public SidebarTab RadioNavBorderRef => RadioNavBorder;

	public SidebarTab PlaylistsNavBorderRef => PlaylistsNavBorder;

	public SidebarTab AlbumsNavBorderRef => AlbumsNavBorder;

	public SidebarTab LocalNavBorderRef => LocalNavBorder;

	public SidebarTab StatsNavBorderRef => StatsNavBorder;

	public ListBox LibraryPanelRef => LibraryPanel;

	public Grid SidebarCoverContainerRef => SidebarCoverContainer;

	public System.Windows.Shapes.Rectangle SidebarCoverImageRef => SidebarCoverImage;
	
	public System.Windows.Shapes.Ellipse SidebarVinylHoleRef => SidebarVinylHole;
	public System.Windows.Shapes.Ellipse SidebarVinylHoleInnerRef => SidebarVinylHoleInner;
	public System.Windows.Controls.Viewbox SidebarGramophoneTonearmRef => SidebarGramophoneTonearm;
	public System.Windows.Media.RotateTransform SidebarCoverRotationRef => SidebarCoverRotation;
	public System.Windows.Media.RotateTransform SidebarTonearmRotationRef => SidebarTonearmRotation;

	public event MouseButtonEventHandler? Sidebar_MouseLeftButtonDown;
	
	public event MouseButtonEventHandler? SidebarCoverContainer_MouseDown_Event;

	public SidebarControl()
	{
		InitializeComponent();
		SidebarBorder.PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
		{
			this.Sidebar_MouseLeftButtonDown?.Invoke(s, e);
		};
	}
	
	private void SidebarCoverContainer_MouseDown(object sender, MouseButtonEventArgs e)
	{
		this.SidebarCoverContainer_MouseDown_Event?.Invoke(sender, e);
	}

	private bool _isVinylStarted = false;
	private bool _isVinylPaused = false;
	private long _lastRenderTime = 0;

	public void StartVinylRotation()
	{
			if (!_isVinylStarted)
			{
				_isVinylStarted = true;
				_isVinylPaused = false;
				_lastRenderTime = 0;
				System.Windows.Media.CompositionTarget.Rendering += CompositionTarget_Rendering;
			}
			else
			{
				_isVinylPaused = false;
			}
	}

	private void CompositionTarget_Rendering(object? sender, EventArgs e)
	{
		if (!_isVinylStarted || _isVinylPaused)
		{
			_lastRenderTime = 0;
			return;
		}
		
		var renderingArgs = (System.Windows.Media.RenderingEventArgs)e;
		long currentTime = renderingArgs.RenderingTime.Ticks;
		
		if (_lastRenderTime == 0)
		{
			_lastRenderTime = currentTime;
			return;
		}
		
		double deltaTimeSeconds = (currentTime - _lastRenderTime) / 10000000.0;
		_lastRenderTime = currentTime;
		
		double deltaAngle = deltaTimeSeconds * (360.0 / 8.0); // 360 degrees per 8 seconds
		
		SidebarCoverRotation.Angle = (SidebarCoverRotation.Angle + deltaAngle) % 360.0;
	}

	public void PauseVinylRotation()
	{
		_isVinylPaused = true;
	}

	public void ResumeVinylRotation()
	{
		_isVinylPaused = false;
	}

	public void StopVinylRotation()
	{
		_isVinylStarted = false;
		_isVinylPaused = false;
		System.Windows.Media.CompositionTarget.Rendering -= CompositionTarget_Rendering;
		SidebarCoverRotation.Angle = 0;
	}

	public static readonly DependencyProperty SmoothScrollOffsetProperty =
		DependencyProperty.Register("SmoothScrollOffset", typeof(double), typeof(SidebarControl), new PropertyMetadata(0.0, OnSmoothScrollOffsetChanged));

	public double SmoothScrollOffset
	{
		get { return (double)GetValue(SmoothScrollOffsetProperty); }
		set { SetValue(SmoothScrollOffsetProperty, value); }
	}

	private static void OnSmoothScrollOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is SidebarControl control)
		{
			ScrollViewer sv = control.GetScrollViewer(control.LibraryPanel);
			if (sv != null)
			{
				sv.ScrollToVerticalOffset((double)e.NewValue);
			}
		}
	}

	private ScrollViewer? GetScrollViewer(DependencyObject depObj)
	{
		if (depObj is ScrollViewer viewer) return viewer;
		for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
		{
			var child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);
			var result = GetScrollViewer(child);
			if (result != null) return result;
		}
		return null;
	}

	private double _targetScrollOffset = 0;

	private void LibraryPanel_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		ScrollViewer sv = GetScrollViewer(LibraryPanel);
		if (sv != null)
		{
			e.Handled = true;
			// Reset target if it's wildly out of sync (e.g. thumb dragged)
			if (Math.Abs(_targetScrollOffset - sv.VerticalOffset) > 500)
			{
				_targetScrollOffset = sv.VerticalOffset;
			}
			
			_targetScrollOffset -= e.Delta;
			if (_targetScrollOffset < 0) _targetScrollOffset = 0;
			if (_targetScrollOffset > sv.ScrollableHeight) _targetScrollOffset = sv.ScrollableHeight;

			System.Windows.Media.Animation.DoubleAnimation anim = new System.Windows.Media.Animation.DoubleAnimation(_targetScrollOffset, new Duration(TimeSpan.FromMilliseconds(250)))
			{
				EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
			};
			this.BeginAnimation(SmoothScrollOffsetProperty, anim);
		}
	}

	private void LibraryPanel_ScrollChanged(object sender, ScrollChangedEventArgs e)
	{
		double h = LibraryPanel.ActualHeight;
		if (h > 0)
		{
			double fadeRatio = Math.Min(20.0 / h, 0.5);
			TopSolidStop.Offset = fadeRatio;
			BottomSolidStop.Offset = 1.0 - fadeRatio;
		}

		ScrollViewer sv = e.OriginalSource as ScrollViewer;
		if (sv != null)
		{
			bool canScrollUp = sv.VerticalOffset > 0;
			bool canScrollDown = sv.VerticalOffset < sv.ScrollableHeight && sv.ScrollableHeight > 0;

			TopFadeStop.Color = canScrollUp ? System.Windows.Media.Color.FromArgb(0, 255, 255, 255) : System.Windows.Media.Color.FromArgb(255, 255, 255, 255);
			BottomFadeStop.Color = canScrollDown ? System.Windows.Media.Color.FromArgb(0, 255, 255, 255) : System.Windows.Media.Color.FromArgb(255, 255, 255, 255);
		}
	}
}
