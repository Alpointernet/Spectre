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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpVectors.Converters;
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
	private async Task ShowCreditsAsync(string videoId)
	{
		MainOverlayControl.CreditsDialogBorderRef.Height = double.NaN;
		MainOverlayControl.CreditsDialogBorderRef.MaxHeight = GetCreditsDialogMaxHeight();
		MainOverlayControl.CreditsDialogBorderRef.UpdateLayout();
		double oldHeight = MainOverlayControl.CreditsDialogBorderRef.ActualHeight;
		MainOverlayControl.CreditsLoadingTextRef.Visibility = Visibility.Visible;
		MainOverlayControl.CreditsLoadingTextRef.Text = "Loading credits...";
		MainOverlayControl.CreditsPanelRef.Children.Clear();
		MainOverlayControl.CreditsPanelRef.Children.Add(MainOverlayControl.CreditsLoadingTextRef);
		MainOverlayControl.CreditsOverlayRef.Visibility = Visibility.Visible;
		DoubleAnimation fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200.0));
		MainOverlayControl.CreditsOverlayRef.BeginAnimation(UIElement.OpacityProperty, fadeIn);
		try
		{
			JObject json = await BackendService.Instance.GetSongCreditsAsync(videoId, CancellationToken.None);
			if (json["error"] != null)
			{
				throw new Exception(((string?)json["error"]) ?? "Error");
			}
			if (!(json["data"] is JObject data) || (data["other_sections"] == null && data["performed_by"] == null && data["written_by"] == null && data["produced_by"] == null && data["music_metadata_provided_by"] == null))
			{
				throw new Exception("No credits data found.");
			}
			MainOverlayControl.CreditsPanelRef.Children.Clear();
			AddSection("performed_by", data);
			AddSection("written_by", data);
			AddSection("produced_by", data);
			if (data["other_sections"] is JArray otherSections)
			{
				foreach (JToken item3 in otherSections)
				{
					if (!(item3 is JObject sectionObj))
					{
						continue;
					}
					string title = ((string?)sectionObj["localized_title"]) ?? "Other";
					if (!(sectionObj["data"] is JArray { Count: >0 } items))
					{
						continue;
					}
					TextBlock headerBlock = new TextBlock
					{
						Text = title,
						Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
						FontSize = 16.0,
						FontWeight = FontWeights.SemiBold,
						Margin = new Thickness(0.0, 0.0, 0.0, 5.0)
					};
					MainOverlayControl.CreditsPanelRef.Children.Add(headerBlock);
					foreach (JToken item in items)
					{
						TextBlock itemBlock = new TextBlock
						{
							Text = (string?)item,
							Foreground = System.Windows.Media.Brushes.LightGray,
							FontSize = 14.0,
							Margin = new Thickness(0.0, 0.0, 0.0, 5.0),
							TextWrapping = TextWrapping.Wrap
						};
						MainOverlayControl.CreditsPanelRef.Children.Add(itemBlock);
					}
					MainOverlayControl.CreditsPanelRef.Children.Add(new Border
					{
						Height = 15.0
					});
				}
			}
			AddSection("music_metadata_provided_by", data);
			if (MainOverlayControl.CreditsPanelRef.Children.Count == 0)
			{
				throw new Exception("No credits data found.");
			}
			double maxAllowedHeight = GetCreditsDialogMaxHeight();
			MainOverlayControl.CreditsDialogBorderRef.MaxHeight = maxAllowedHeight;
			double newHeight = Math.Min(GetCreditsDialogContentHeight(), maxAllowedHeight);
			MainOverlayControl.CreditsDialogBorderRef.Height = oldHeight;
			DoubleAnimation heightAnim = new DoubleAnimation(oldHeight, newHeight, TimeSpan.FromMilliseconds(250.0))
			{
				EasingFunction = new QuadraticEase
				{
					EasingMode = EasingMode.EaseOut
				}
			};
			heightAnim.Completed += delegate
			{
				MainOverlayControl.CreditsDialogBorderRef.Height = newHeight;
			};
			MainOverlayControl.CreditsDialogBorderRef.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
			MainOverlayControl.CreditsPanelRef.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(300.0)));
		}
		catch
		{
			MainOverlayControl.CreditsLoadingTextRef.Visibility = Visibility.Visible;
			MainOverlayControl.CreditsLoadingTextRef.Text = "Credits not available.";
		}
		void AddSection(string key, JObject rootData)
		{
			if (rootData[key] is JObject section)
			{
				string title2 = ((string?)section["localized_title"]) ?? key;
				if (section["data"] is JArray { Count: >0 } items2)
				{
					TextBlock headerBlock2 = new TextBlock
					{
						Text = title2,
						Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
						FontSize = 16.0,
						FontWeight = FontWeights.SemiBold,
						Margin = new Thickness(0.0, 0.0, 0.0, 5.0)
					};
					MainOverlayControl.CreditsPanelRef.Children.Add(headerBlock2);
					foreach (JToken item2 in items2)
					{
						TextBlock itemBlock2 = new TextBlock
						{
							Text = (string?)item2,
							Foreground = System.Windows.Media.Brushes.LightGray,
							FontSize = 14.0,
							Margin = new Thickness(0.0, 0.0, 0.0, 5.0),
							TextWrapping = TextWrapping.Wrap
						};
						MainOverlayControl.CreditsPanelRef.Children.Add(itemBlock2);
					}
					MainOverlayControl.CreditsPanelRef.Children.Add(new Border
					{
						Height = 15.0
					});
				}
			}
		}
	}

	private void CloseCreditsBtn_Click(object sender, RoutedEventArgs e)
	{
		DoubleAnimation fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(200.0));
		fadeOut.Completed += delegate
		{
			MainOverlayControl.CreditsOverlayRef.Visibility = Visibility.Collapsed;
		};
		MainOverlayControl.CreditsOverlayRef.BeginAnimation(UIElement.OpacityProperty, fadeOut);
	}

	private void CreditsOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		CloseCreditsBtn_Click(null, null);
	}

	private void CreditsBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		e.Handled = true;
	}

	private void CreditsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		ScrollViewer scrollViewer = sender as ScrollViewer;
		if (scrollViewer == null)
		{
			return;
		}
		e.Handled = true;


		double baseScrollY = ((!_isCreditsAnimating || !(Math.Abs(_creditsTargetScrollY - _creditsCurrentScrollY) > 1.0) || Math.Sign(_creditsTargetScrollY - _creditsCurrentScrollY) == Math.Sign(-e.Delta)) ? (_isCreditsAnimating ? _creditsTargetScrollY : MainOverlayControl.CreditsScrollViewerRef.VerticalOffset) : MainOverlayControl.CreditsScrollViewerRef.VerticalOffset);
		double targetOffset = Math.Max(0.0, baseScrollY - (double)e.Delta * 0.85);
		
		if (_disableSmoothScrolling)
		{
			MainOverlayControl.CreditsScrollViewerRef.ScrollToVerticalOffset(Math.Max(0.0, Math.Min(MainOverlayControl.CreditsScrollViewerRef.ScrollableHeight, targetOffset)));
			_creditsCurrentScrollY = targetOffset;
			_creditsTargetScrollY = targetOffset;
			return;
		}
		
		double diff = Math.Abs(targetOffset - _creditsCurrentScrollY);
		if (diff > 150.0)
		{
			_creditsUseOldScrollMethod = true;
		}
		else if (!_isCreditsAnimating)
		{
			_creditsUseOldScrollMethod = false;
		}
		
		if (!_isCreditsAnimating)
		{
			_creditsCurrentScrollY = MainOverlayControl.CreditsScrollViewerRef.VerticalOffset;
			_creditsTargetScrollY = _creditsCurrentScrollY;
			_isCreditsAnimating = true;
			_creditsLastScrollTick = 0;
			_creditsScrollVelocity = 0.0;
			CompositionTarget.Rendering += CreditsAnimationLoop;
		}
		_creditsTargetScrollY = Math.Max(0.0, Math.Min(MainOverlayControl.CreditsScrollViewerRef.ScrollableHeight, targetOffset));
	}

	private void CreditsAnimationLoop(object? sender, EventArgs e)
	{
		long now = Stopwatch.GetTimestamp();
		double dt;
		if (_creditsLastScrollTick == 0)
		{
			dt = 0.016;
		}
		else
		{
			dt = (double)(now - _creditsLastScrollTick) / (double)Stopwatch.Frequency;
		}
		_creditsLastScrollTick = now;
		
		if (dt <= 0.0)
		{
			dt = 0.016;
		}
		if (dt > 0.05)
		{
			dt = 0.05;
		}
		double displacement = _creditsCurrentScrollY - _creditsTargetScrollY;
		if (Math.Abs(displacement) < 0.5 && (_creditsUseOldScrollMethod || Math.Abs(_creditsScrollVelocity) < 10.0))
		{
			_creditsCurrentScrollY = _creditsTargetScrollY;
			_creditsScrollVelocity = 0.0;
			_isCreditsAnimating = false;
			CompositionTarget.Rendering -= CreditsAnimationLoop;
			MainOverlayControl.CreditsScrollViewerRef.ScrollToVerticalOffset(_creditsCurrentScrollY);
		}
		else
		{
			if (_creditsUseOldScrollMethod)
			{
				double factor = 1.0 - Math.Exp(-20.0 * dt);
				_creditsCurrentScrollY += (_creditsTargetScrollY - _creditsCurrentScrollY) * factor;
			}
			else
			{
				double w = 25.0;
				double c1 = displacement;
				double c2 = _creditsScrollVelocity + w * displacement;
				double expTerm = Math.Exp(-w * dt);
				
				double newDisplacement = (c1 + c2 * dt) * expTerm;
				_creditsScrollVelocity = (c2 - w * (c1 + c2 * dt)) * expTerm;
				
				_creditsCurrentScrollY = _creditsTargetScrollY + newDisplacement;
			}
			MainOverlayControl.CreditsScrollViewerRef.ScrollToVerticalOffset(_creditsCurrentScrollY);
		}
	}

	private double GetCreditsDialogMaxHeight()
	{
		double windowHeight = System.Windows.Application.Current.MainWindow?.ActualHeight ?? base.ActualHeight;
		return Math.Max(260.0, windowHeight - 120.0);
	}

	private double GetCreditsDialogContentHeight()
	{
		double contentWidth = Math.Max(0.0, MainOverlayControl.CreditsDialogBorderRef.Width - MainOverlayControl.CreditsDialogBorderRef.Padding.Left - MainOverlayControl.CreditsDialogBorderRef.Padding.Right - 10.0);
		MainOverlayControl.CreditsPanelRef.Measure(new System.Windows.Size(contentWidth, double.PositiveInfinity));
		double paddingHeight = MainOverlayControl.CreditsDialogBorderRef.Padding.Top + MainOverlayControl.CreditsDialogBorderRef.Padding.Bottom;
		double headerHeight = 50.0;
		double contentHeight = Math.Ceiling(MainOverlayControl.CreditsPanelRef.DesiredSize.Height);
		return Math.Max(180.0, paddingHeight + headerHeight + contentHeight);
	}
}
