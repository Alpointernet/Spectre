using System;
using System.Collections;
using System.Collections.Concurrent;
using ColorConverter = System.Windows.Media.ColorConverter;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Win32;

using System.Text.Json.Nodes;
using System.Text.Json;
using Spectre.Services;
using Spectre.ViewModels;
using Spectre.Views;
using TagLib;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;
using YoutubeExplode;
namespace Spectre; public partial class MainWindow {
	private long _lastRecordedTime = -1L;

	private long _seekTargetTime;

	private DispatcherTimer _playbackTimer;

	private bool _isUserDraggingSlider;

	private DateTime _lastSeekTime = DateTime.MinValue;

	private void PerformSeek(long targetTime, bool resetLyricsScroll = false)
	{
		if (string.IsNullOrEmpty(_currentVideoId))
		{
			return;
		}
		_lastSeekTime = DateTime.Now;
		_seekTargetTime = targetTime;
		try
		{
			if (_player.Length > 0)
			{
				_player.Position = (float)targetTime / (float)_player.Length;
			}
			_player.Time = targetTime;
		}
		catch
		{
		}
		if (resetLyricsScroll)
		{
			_isLyricsUserScrolled = false;
		}
		UpdateLyricsForTime(targetTime, forceScroll: true);
		UpdateDiscordRPC();
		PlaybackTimer_Tick(null, EventArgs.Empty);
	}

	private void Timeline_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (!string.IsNullOrEmpty(_currentVideoId))
		{
			_isUserDraggingSlider = true;
			MainPlayerBarControl.TimelineSliderRef.BeginAnimation(RangeBase.ValueProperty, null);
			Border hitbox = (Border)sender;
			hitbox.CaptureMouse();
			UpdateTimelineFromMouse(e.GetPosition(hitbox).X, hitbox.ActualWidth);
		}
	}

	private void Timeline_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
	{
		if (_isUserDraggingSlider && !string.IsNullOrEmpty(_currentVideoId))
		{
			Border hitbox = (Border)sender;
			UpdateTimelineFromMouse(e.GetPosition(hitbox).X, hitbox.ActualWidth);
		}
	}

	private void Timeline_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (_isUserDraggingSlider)
		{
			_isUserDraggingSlider = false;
			((Border)sender).ReleaseMouseCapture();
			if (!string.IsNullOrEmpty(_currentVideoId))
			{
				PerformSeek((long)MainPlayerBarControl.TimelineSliderRef.Value, resetLyricsScroll: true);
			}
		}
	}

	private void Timeline_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
	{
	}

	private void UpdateTimelineFromMouse(double mouseX, double actualWidth)
	{
		double thumbWidth = 12.0;
		double trackWidth = actualWidth - thumbWidth;
		double adjustedX = mouseX - thumbWidth / 2.0;
		double ratio = ((trackWidth > 0.0) ? (adjustedX / trackWidth) : 0.0);
		ratio = Math.Max(0.0, Math.Min(1.0, ratio));
		long target = (long)(ratio * MainPlayerBarControl.TimelineSliderRef.Maximum);
		MainPlayerBarControl.TimelineSliderRef.BeginAnimation(RangeBase.ValueProperty, null);
		MainPlayerBarControl.TimelineSliderRef.Value = target;
		PlayerBarViewModel vmTime = App.Current.PlayerBarViewModel;
		if (vmTime != null)
		{
			vmTime.CurrentTimeText = TimeSpan.FromMilliseconds(target).ToString("m\\:ss");
		}
	}
}



