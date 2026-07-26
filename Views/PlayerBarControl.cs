using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using Spectre.ViewModels;

namespace Spectre.Views;

public partial class PlayerBarControl : UserControl
{
	public Grid OldPlayerInfoPanelRef => OldPlayerInfoPanel;

	public System.Windows.Controls.Image OldPlayerThumbnailFallbackRef => OldPlayerThumbnailFallback;

	public Rectangle OldPlayerThumbnailRef => OldPlayerThumbnail;

	public TextBlock OldPlayerTitleRef => OldPlayerTitle;

	public TextBlock OldPlayerArtistPanelRef => OldPlayerArtistPanel;

	public Grid PlayerInfoPanelRef => PlayerInfoPanel;

	public Grid PlayerThumbnailContainerRef => PlayerThumbnailContainer;

	public StackPanel PlayerTextPanelRef => PlayerTextPanel;

	public Border PlayerThumbnailBorderRef => PlayerThumbnailBorder;

	public System.Windows.Controls.Image PlayerThumbnailFallbackRef => PlayerThumbnailFallback;

	public Rectangle PlayerThumbnailOldRef => PlayerThumbnailOld;

	public Rectangle PlayerThumbnailRef => PlayerThumbnail;

	public TextBlock PlayerTitleRef => PlayerTitle;

	public TextBlock PlayerArtistPanelRef => PlayerArtistPanel;

	public Button ShuffleBtnRef => ShuffleBtn;

	public Button PrevBtnRef => PrevBtn;

	public Button PlayPauseBtnRef => PlayPauseBtn;

	public System.Windows.Controls.Image PlayIconRef => PlayIcon;

	public System.Windows.Controls.Image PauseIconRef => PauseIcon;

	public Button NextBtnRef => NextBtn;

	public Button RepeatBtnRef => RepeatBtn;

	public System.Windows.Controls.Image RepeatIconOffRef => RepeatIconOff;

	public System.Windows.Shapes.Rectangle RepeatIconOnRef => RepeatIconOn;

	public Grid TimelineGridRef => TimelineGrid;

	public TextBlock CurrentTimeTextRef => CurrentTimeText;

	public Slider TimelineSliderRef => TimelineSlider;

	public TextBlock TotalTimeTextRef => TotalTimeText;

	public Button QueueBtnRef => QueueBtn;

	public System.Windows.Controls.Image QueueIconOffRef => QueueIconOff;

	public System.Windows.Shapes.Rectangle QueueIconOnRef => QueueIconOn;

	public Button LyricsBtnRef => LyricsBtn;

	public System.Windows.Controls.Image LyricsIconOffRef => LyricsIconOff;

	public System.Windows.Shapes.Rectangle LyricsIconOnRef => LyricsIconOn;

	public System.Windows.Controls.Image ShuffleIconOffRef => ShuffleIconOff;

	public System.Windows.Shapes.Rectangle ShuffleIconOnRef => ShuffleIconOn;

	public System.Windows.Controls.Image VolumeIconRef => VolumeIcon;

	public Slider VolumeSliderRef => VolumeSlider;

	public event MouseButtonEventHandler? PlayerThumbnailBorder_MouseDown_Event;

	public event RoutedEventHandler? ShuffleBtn_Click_Event;

	public event RoutedEventHandler? PrevBtn_Click_Event;

	public event RoutedEventHandler? PlayPauseBtn_Click_Event;

	public event RoutedEventHandler? NextBtn_Click_Event;

	public event RoutedEventHandler? RepeatBtn_Click_Event;

	public event MouseButtonEventHandler? Timeline_MouseLeftButtonDown_Event;

	public event MouseEventHandler? Timeline_MouseMove_Event;

	public event MouseButtonEventHandler? Timeline_MouseLeftButtonUp_Event;

	public event MouseEventHandler? Timeline_MouseLeave_Event;

	public event RoutedEventHandler? QueueBtn_Click_Event;

	public event RoutedEventHandler? LyricsBtn_Click_Event;

	public event RoutedEventHandler? VolumeIcon_Click_Event;

	public event RoutedPropertyChangedEventHandler<double>? VolumeSlider_ValueChanged_Event;

	public event MouseWheelEventHandler? VolumeSlider_PreviewMouseWheel_Event;

	public PlayerBarControl()
	{
		InitializeComponent();
		try
		{
			base.DataContext = App.Current.PlayerBarViewModel;
		}
		catch
		{
		}
		TimelineSlider.ValueChanged += TimelineSlider_ValueChanged;
	}

	private void PlayerThumbnailBorder_MouseDown(object sender, MouseButtonEventArgs e)
	{
		this.PlayerThumbnailBorder_MouseDown_Event?.Invoke(sender, e);
	}

	private void ShuffleBtn_Click(object sender, RoutedEventArgs e)
	{
		this.ShuffleBtn_Click_Event?.Invoke(sender, e);
	}

	private void PrevBtn_Click(object sender, RoutedEventArgs e)
	{
		this.PrevBtn_Click_Event?.Invoke(sender, e);
	}

	private void PlayPauseBtn_Click(object sender, RoutedEventArgs e)
	{
		this.PlayPauseBtn_Click_Event?.Invoke(sender, e);
	}

	private void NextBtn_Click(object sender, RoutedEventArgs e)
	{
		this.NextBtn_Click_Event?.Invoke(sender, e);
	}

	private void RepeatBtn_Click(object sender, RoutedEventArgs e)
	{
		this.RepeatBtn_Click_Event?.Invoke(sender, e);
	}

	private void Timeline_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		this.Timeline_MouseLeftButtonDown_Event?.Invoke(sender, e);
	}

	private void Timeline_MouseMove(object sender, MouseEventArgs e)
	{
		this.Timeline_MouseMove_Event?.Invoke(sender, e);
	}

	private void Timeline_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		this.Timeline_MouseLeftButtonUp_Event?.Invoke(sender, e);
	}

	private void Timeline_MouseLeave(object sender, MouseEventArgs e)
	{
		this.Timeline_MouseLeave_Event?.Invoke(sender, e);
	}

	private void QueueBtn_Click(object sender, RoutedEventArgs e)
	{
		this.QueueBtn_Click_Event?.Invoke(sender, e);
	}

	private void LyricsBtn_Click(object sender, RoutedEventArgs e)
	{
		this.LyricsBtn_Click_Event?.Invoke(sender, e);
	}

	private void VolumeIcon_Click(object sender, RoutedEventArgs e)
	{
		this.VolumeIcon_Click_Event?.Invoke(sender, e);
	}

	private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		this.VolumeSlider_ValueChanged_Event?.Invoke(sender, e);
	}

	private void TimelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		DrawWigglyPath();
	}

	private void VolumeSlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		this.VolumeSlider_PreviewMouseWheel_Event?.Invoke(sender, e);
	}

	private bool _isUserDraggingVolume = false;

	private void Volume_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		_isUserDraggingVolume = true;
		VolumeSlider.BeginAnimation(Slider.ValueProperty, null);
		Border hitbox = (Border)sender;
		hitbox.CaptureMouse();
		UpdateVolumeFromMouse(e.GetPosition(hitbox).X, hitbox.ActualWidth);
	}

	private void Volume_MouseMove(object sender, MouseEventArgs e)
	{
		if (_isUserDraggingVolume)
		{
			Border hitbox = (Border)sender;
			UpdateVolumeFromMouse(e.GetPosition(hitbox).X, hitbox.ActualWidth);
		}
	}

	private void Volume_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (_isUserDraggingVolume)
		{
			_isUserDraggingVolume = false;
			((Border)sender).ReleaseMouseCapture();
		}
	}

	private void Volume_MouseLeave(object sender, MouseEventArgs e)
	{
	}

	private void UpdateVolumeFromMouse(double mouseX, double actualWidth)
	{
		double thumbWidth = 12.0;
		double trackWidth = actualWidth - thumbWidth;
		double adjustedX = mouseX - thumbWidth / 2.0;
		double ratio = ((trackWidth > 0.0) ? (adjustedX / trackWidth) : 0.0);
		ratio = Math.Max(0.0, Math.Min(1.0, ratio));
		double target = ratio * VolumeSlider.Maximum;
		VolumeSlider.BeginAnimation(Slider.ValueProperty, null);
		VolumeSlider.Value = target;
	}

	private bool _isVinylStarted = false;
	private bool _isVinylPaused = false;
	private long _lastRenderTime = 0;

	public void UpdateVinylMode(bool enabled, bool isPaused = false)
	{
		if (enabled)
		{
			PlayerThumbnailBorder.CornerRadius = new CornerRadius(24);
			OldPlayerThumbnailBorder.CornerRadius = new CornerRadius(24);
			PlayerThumbnail.RadiusX = 24;
			PlayerThumbnail.RadiusY = 24;
			PlayerThumbnailOld.RadiusX = 24;
			PlayerThumbnailOld.RadiusY = 24;
			OldPlayerThumbnail.RadiusX = 24;
			OldPlayerThumbnail.RadiusY = 24;
			VinylHole.Visibility = Visibility.Visible;
			VinylHoleInner.Visibility = Visibility.Visible;
			OldVinylHole.Visibility = Visibility.Visible;
			OldVinylHoleInner.Visibility = Visibility.Visible;
			GramophoneTonearm.Visibility = Visibility.Visible;

			System.Windows.Media.Animation.DoubleAnimation armAnim = new System.Windows.Media.Animation.DoubleAnimation(20, new Duration(TimeSpan.FromMilliseconds(500)))
			{
				EasingFunction = new System.Windows.Media.Animation.QuadraticEase() { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
			};
			TonearmRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, armAnim);

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

			if (isPaused)
			{
				PauseVinylRotation();
			}
		}
		else
		{
			PlayerThumbnailBorder.CornerRadius = new CornerRadius(4);
			OldPlayerThumbnailBorder.CornerRadius = new CornerRadius(4);
			PlayerThumbnail.RadiusX = 4;
			PlayerThumbnail.RadiusY = 4;
			PlayerThumbnailOld.RadiusX = 4;
			PlayerThumbnailOld.RadiusY = 4;
			OldPlayerThumbnail.RadiusX = 4;
			OldPlayerThumbnail.RadiusY = 4;
			VinylHole.Visibility = Visibility.Collapsed;
			VinylHoleInner.Visibility = Visibility.Collapsed;
			OldVinylHole.Visibility = Visibility.Collapsed;
			OldVinylHoleInner.Visibility = Visibility.Collapsed;
			GramophoneTonearm.Visibility = Visibility.Collapsed;
			TonearmRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
			TonearmRotation.Angle = 0;
			_isVinylStarted = false;
			_isVinylPaused = false;
			System.Windows.Media.CompositionTarget.Rendering -= CompositionTarget_Rendering;
			PlayerThumbnailRotation.Angle = 0;
			OldPlayerThumbnailRotation.Angle = 0;
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
		
		PlayerThumbnailRotation.Angle = (PlayerThumbnailRotation.Angle + deltaAngle) % 360.0;
		OldPlayerThumbnailRotation.Angle = (OldPlayerThumbnailRotation.Angle + deltaAngle) % 360.0;
	}

	public void PauseVinylRotation()
	{
		_isVinylPaused = true;
		if (GramophoneTonearm.Visibility == Visibility.Visible)
		{
			System.Windows.Media.Animation.DoubleAnimation armAnim = new System.Windows.Media.Animation.DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(400)))
			{
				EasingFunction = new System.Windows.Media.Animation.QuadraticEase() { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
			};
			TonearmRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, armAnim);
		}
	}

	public void ResumeVinylRotation()
	{
		_isVinylPaused = false;
		if (GramophoneTonearm.Visibility == Visibility.Visible)
		{
			System.Windows.Media.Animation.DoubleAnimation armAnim = new System.Windows.Media.Animation.DoubleAnimation(20, new Duration(TimeSpan.FromMilliseconds(400)))
			{
				EasingFunction = new System.Windows.Media.Animation.QuadraticEase() { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
			};
			TonearmRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, armAnim);
		}
	}

	public void AnimateTonearmSkip()
	{
		if (GramophoneTonearm.Visibility == Visibility.Visible)
		{
			System.Windows.Media.Animation.Storyboard sb = new System.Windows.Media.Animation.Storyboard();
			System.Windows.Media.Animation.DoubleAnimation armOff = new System.Windows.Media.Animation.DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(200)))
			{
				EasingFunction = new System.Windows.Media.Animation.QuadraticEase() { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
			};
			
			System.Windows.Media.Animation.DoubleAnimation armOn = new System.Windows.Media.Animation.DoubleAnimation(20, new Duration(TimeSpan.FromMilliseconds(300)))
			{
				BeginTime = TimeSpan.FromMilliseconds(300),
				EasingFunction = new System.Windows.Media.Animation.QuadraticEase() { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
			};
			
			System.Windows.Media.Animation.Storyboard.SetTarget(armOff, TonearmRotation);
			System.Windows.Media.Animation.Storyboard.SetTargetProperty(armOff, new PropertyPath(System.Windows.Media.RotateTransform.AngleProperty));
			System.Windows.Media.Animation.Storyboard.SetTarget(armOn, TonearmRotation);
			System.Windows.Media.Animation.Storyboard.SetTargetProperty(armOn, new PropertyPath(System.Windows.Media.RotateTransform.AngleProperty));
			
			sb.Children.Add(armOff);
			sb.Children.Add(armOn);
			sb.Begin();
		}
	}

	private System.Windows.Threading.DispatcherTimer _wigglyTimer;
	private double _wigglyPhase = 0;
	private bool _isWigglyPaused = false;
	private double _wigglyCurrentAmplitude = 0;

	public void UpdateWigglyProgress(bool enabled)
	{
		if (enabled)
		{
			TimelineSlider.Foreground = System.Windows.Media.Brushes.Transparent;
			TimelineSlider.Background = System.Windows.Media.Brushes.Transparent;
			WigglyBackgroundCanvas.Visibility = Visibility.Visible;
			WigglyCanvas.Visibility = Visibility.Visible;
			if (_wigglyTimer == null)
			{
				_wigglyTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
				_wigglyTimer.Tick += (s, e) =>
				{
					_wigglyPhase -= 0.1; // Slower wave speed
					
					double targetAmp = _isWigglyPaused ? 0 : 4.5;
					_wigglyCurrentAmplitude += (targetAmp - _wigglyCurrentAmplitude) * 0.35;
					if (_isWigglyPaused && Math.Abs(_wigglyCurrentAmplitude) < 0.05)
					{
						_wigglyCurrentAmplitude = 0;
						_wigglyTimer.Stop();
					}

					DrawWigglyPath();
				};
			}
			_isWigglyPaused = false;
			_wigglyTimer.Start();
		}
		else
		{
			TimelineSlider.ClearValue(Slider.ForegroundProperty);
			TimelineSlider.ClearValue(Slider.BackgroundProperty);
			WigglyBackgroundCanvas.Visibility = Visibility.Collapsed;
			WigglyCanvas.Visibility = Visibility.Collapsed;
			_isWigglyPaused = true;
			_wigglyCurrentAmplitude = 0;
			_wigglyTimer?.Stop();
		}
	}

	private void DrawWigglyPath()
	{
		if (WigglyCanvas.Visibility != Visibility.Visible || TimelineSlider.Maximum <= 0) return;
		
		double width = TimelineSlider.ActualWidth;
		if (width <= 0) return;

		double percent = TimelineSlider.Value / TimelineSlider.Maximum;
		double fillWidth = width * percent;

		WigglyClip.Rect = new Rect(-10, 0, fillWidth + 10, WigglyCanvas.Height);
		WigglyBackgroundLine.X1 = fillWidth;

		double amplitude = _wigglyCurrentAmplitude;
		double frequency = 0.08;

		System.Windows.Media.StreamGeometry geom = new System.Windows.Media.StreamGeometry();
		using (System.Windows.Media.StreamGeometryContext ctx = geom.Open())
		{
			ctx.BeginFigure(new Point(0, 10), false, false);
			
			for (double x = 0; x < fillWidth; x += 3)
			{
				double env1 = 1.0;
				double flatLeft = 0;
				double transLeft = 30;
				if (x <= flatLeft) env1 = 0;
				else if (x < flatLeft + transLeft)
				{
					double t = (x - flatLeft) / transLeft;
					env1 = (1.0 - Math.Cos(t * Math.PI)) / 2.0;
				}

				double env2 = 1.0;
				double flatRight = 8; // 2px flat + 6px under the thumb
				double transRight = 16;
				double distFromEnd = fillWidth - x;
				if (distFromEnd <= flatRight) env2 = 0;
				else if (distFromEnd < flatRight + transRight)
				{
					double t = (distFromEnd - flatRight) / transRight;
					env2 = (1.0 - Math.Cos(t * Math.PI)) / 2.0;
				}

				double envelope = env1 * env2;

				double y = 10 + Math.Sin(x * frequency + _wigglyPhase) * amplitude * envelope;
				ctx.LineTo(new Point(x, y), true, false);
			}
			ctx.LineTo(new Point(fillWidth, 10), true, false);
		}
		geom.Freeze();
		WigglyPath.Data = geom;
	}

	public void PauseWiggly()
	{
		_isWigglyPaused = true;
		// Allow timer to continue until it flattens
	}

	public void ResumeWiggly()
	{
		_isWigglyPaused = false;
		if (TimelineSlider.Foreground == System.Windows.Media.Brushes.Transparent)
		{
			if (_wigglyTimer != null && !_wigglyTimer.IsEnabled)
				_wigglyTimer.Start();
		}
	}
}



