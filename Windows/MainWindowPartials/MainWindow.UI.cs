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
	private void UpdateNavButtons()
	{
		MainTopbarControl.BackBtnRef.Opacity = ((_backHistory.Count > 0) ? 1.0 : 0.3);
		MainTopbarControl.BackBtnRef.IsEnabled = _backHistory.Count > 0;
		MainTopbarControl.ForwardBtnRef.Opacity = ((_forwardHistory.Count > 0) ? 1.0 : 0.3);
		MainTopbarControl.ForwardBtnRef.IsEnabled = _forwardHistory.Count > 0;
	}

	private async Task MonitorLoadingTimeAsync(int transitionId)
	{
		await Task.Delay(3000);
		if (transitionId == _transitionId && _isLoadingContent)
		{
			MainOverlayControl.TopLoadingBarRef.Visibility = Visibility.Visible;
			DoubleAnimation anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(300.0));
			MainOverlayControl.TopLoadingBarRef.BeginAnimation(UIElement.OpacityProperty, anim);
		}
	}

	private async Task<int> FadeOutContentAsync()
	{
		CancelScrollAnimation();
		if (!string.IsNullOrEmpty(_currentPageId))
		{
			_pageVirtualizationCache[_currentPageId] = (new List<Border>(_lazyVirtualizationElements), new List<Func<UIElement>>(_lazyVirtualizationActions));
		}
		_lazyVirtualizationElements.Clear();
		_lazyVirtualizationActions.Clear();
		int currentId = ++_transitionId;
		_isLoadingContent = true;
		_ = _ = _ = _ = MonitorLoadingTimeAsync(currentId);
		if (ContentPanel.Children.Count > 0 && ContentPanel.Opacity > 0.0)
		{
			if (_reduceAnimations)
			{
				ContentPanel.BeginAnimation(UIElement.OpacityProperty, null);
				ContentPanel.Opacity = 0.0;
			}
			else
			{
				TaskCompletionSource<bool> tcsOut = new TaskCompletionSource<bool>();
				DoubleAnimation animOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150.0));
				animOut.Completed += delegate
				{
					tcsOut.TrySetResult(result: true);
				};
				ContentPanel.BeginAnimation(UIElement.OpacityProperty, animOut);
				await tcsOut.Task;
			}
		}
		if (currentId == _transitionId)
		{
			ContentPanel.Children.Clear();
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
		}
		return currentId;
	}

	private async Task FadeInContentAsync(int transitionId, Action updateUI)
	{
		if (transitionId != _transitionId)
		{
			return;
		}
		MainOverlayControl.GlobalErrorOverlayRef.Visibility = Visibility.Collapsed;
		_isLoadingContent = false;
		if (MainOverlayControl.TopLoadingBarRef.Visibility == Visibility.Visible)
		{
			DoubleAnimation fadeOutBar = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(300.0));
			fadeOutBar.Completed += delegate
			{
				MainOverlayControl.TopLoadingBarRef.Visibility = Visibility.Collapsed;
			};
			MainOverlayControl.TopLoadingBarRef.BeginAnimation(UIElement.OpacityProperty, fadeOutBar);
		}
		updateUI();
		await base.Dispatcher.InvokeAsync(delegate
		{
		}, DispatcherPriority.Loaded);
		if (transitionId == _transitionId)
		{
			CheckVisibilityOfLazyElements();
			if (!string.IsNullOrEmpty(_currentVideoId))
			{
				HighlightNowPlaying(_currentVideoId);
			}
			if (_reduceAnimations)
			{
				ContentPanel.BeginAnimation(UIElement.OpacityProperty, null);
				ContentPanel.Opacity = 1.0;
			}
			else
			{
				DoubleAnimation animIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200.0));
				ContentPanel.BeginAnimation(UIElement.OpacityProperty, animIn);
			}
		}
	}

	private void ShowToast(string message, int durationSeconds = 3)
	{
		base.Dispatcher.Invoke(delegate
		{
			MainOverlayControl.ToastTextRef.Text = message;
			MainOverlayControl.ToastBorderRef.Visibility = Visibility.Visible;
			if (_toastStoryboard != null)
			{
				_toastStoryboard.Stop();
			}
			_toastStoryboard = new Storyboard();
			DoubleAnimation doubleAnimation = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200.0));
			DoubleAnimation doubleAnimation2 = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(300.0))
			{
				BeginTime = TimeSpan.FromSeconds(durationSeconds)
			};
			DoubleAnimation doubleAnimation3 = new DoubleAnimation(20.0, 0.0, TimeSpan.FromMilliseconds(200.0))
			{
				EasingFunction = new QuadraticEase
				{
					EasingMode = EasingMode.EaseOut
				}
			};
			Storyboard.SetTarget(doubleAnimation, MainOverlayControl.ToastBorderRef);
			Storyboard.SetTargetProperty(doubleAnimation, new PropertyPath(UIElement.OpacityProperty));
			Storyboard.SetTarget(doubleAnimation2, MainOverlayControl.ToastBorderRef);
			Storyboard.SetTargetProperty(doubleAnimation2, new PropertyPath(UIElement.OpacityProperty));
			Storyboard.SetTarget(doubleAnimation3, MainOverlayControl.ToastTransformRef);
			Storyboard.SetTargetProperty(doubleAnimation3, new PropertyPath(TranslateTransform.YProperty));
			_toastStoryboard.Children.Add(doubleAnimation);
			_toastStoryboard.Children.Add(doubleAnimation2);
			_toastStoryboard.Children.Add(doubleAnimation3);
			_toastStoryboard.Completed += delegate
			{
				MainOverlayControl.ToastBorderRef.Visibility = Visibility.Collapsed;
			};
			MainOverlayControl.ToastBorderRef.BeginAnimation(UIElement.OpacityProperty, null);
			MainOverlayControl.ToastTransformRef.BeginAnimation(TranslateTransform.YProperty, null);
			_toastStoryboard.Begin();
		});
	}

	private void ShowGlobalError(string action, string details, Action onRetry)
	{
		details = details ?? "";
		bool isNetwork = details.ToLower().Contains("connection") || details.ToLower().Contains("host") || details.ToLower().Contains("socket") || details.ToLower().Contains("network") || details.ToLower().Contains("internet");
		MainOverlayControl.GlobalErrorTitleRef.Text = (isNetwork ? "No Internet Connection" : action);
		MainOverlayControl.GlobalErrorDetailsRef.Text = details;
		_globalErrorRetryAction = onRetry;
		MainOverlayControl.GlobalErrorRetryBtnRef.IsEnabled = true;
		if (MainOverlayControl.GlobalErrorRetryBtnRef.Child is TextBlock tb)
		{
			tb.Text = "Retry";
		}
		if (MainOverlayControl.GlobalErrorOverlayRef.Visibility != Visibility.Visible || MainOverlayControl.GlobalErrorOverlayRef.Opacity == 0.0)
		{
			MainOverlayControl.GlobalErrorOverlayRef.Visibility = Visibility.Visible;
			DoubleAnimation anim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(250.0))
			{
				EasingFunction = new CubicEase
				{
					EasingMode = EasingMode.EaseOut
				}
			};
			MainOverlayControl.GlobalErrorOverlayRef.BeginAnimation(UIElement.OpacityProperty, anim);
		}
	}

	private void GlobalErrorRetryBtn_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
	{
		FadeBorderBackgroundToColor(MainOverlayControl.GlobalErrorRetryBtnRef, System.Windows.Media.Color.FromArgb(60, byte.MaxValue, byte.MaxValue, byte.MaxValue));
	}

	private void GlobalErrorRetryBtn_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
	{
		FadeBorderBackgroundToColor(MainOverlayControl.GlobalErrorRetryBtnRef, System.Windows.Media.Color.FromArgb(40, byte.MaxValue, byte.MaxValue, byte.MaxValue));
	}

	private void GlobalErrorRetryBtn_Click(object sender, MouseButtonEventArgs e)
	{
		if (MainOverlayControl.GlobalErrorRetryBtnRef.IsEnabled)
		{
			MainOverlayControl.GlobalErrorRetryBtnRef.IsEnabled = false;
			if (MainOverlayControl.GlobalErrorRetryBtnRef.Child is TextBlock tb)
			{
				tb.Text = "Retrying...";
			}
			_globalErrorRetryAction?.Invoke();
		}
	}

	private void ApplyImageOverlay(Border imgBorder)
	{
		UIElement child = imgBorder.Child;
		if (child != null)
		{
			imgBorder.Child = null;
			Geometry geom = imgBorder.Clip;
			if (geom == null && imgBorder.Width > 0.0 && imgBorder.Height > 0.0)
			{
				geom = new RectangleGeometry
				{
					Rect = new Rect(0.0, 0.0, imgBorder.Width, imgBorder.Height),
					RadiusX = imgBorder.CornerRadius.TopLeft,
					RadiusY = imgBorder.CornerRadius.TopLeft
				};
			}
			Grid grid = new Grid();
			Border clippedContainer = new Border
			{
				Clip = geom,
				Child = child,
				Background = imgBorder.Background
			};
			grid.Children.Add(clippedContainer);
			if (geom != null)
			{
				System.Windows.Shapes.Path glassEdge = new System.Windows.Shapes.Path
				{
					Data = geom,
					Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(25, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
					StrokeThickness = 1.5,
					IsHitTestVisible = false
				};
				grid.Children.Add(glassEdge);
			}
			imgBorder.Clip = null;
			imgBorder.Background = System.Windows.Media.Brushes.Transparent;
			imgBorder.Child = grid;
		}
	}

	private void AnimateStatValue(TextBlock tb, string newValue)
	{
		if (!(tb.Text == newValue))
		{
			ScaleTransform scaleTransform = (ScaleTransform)(tb.RenderTransform = new ScaleTransform(1.0, 1.0));
			tb.RenderTransformOrigin = new System.Windows.Point(0.0, 0.5);
			tb.Text = newValue;
			DoubleAnimation grow = new DoubleAnimation(1.15, TimeSpan.FromMilliseconds(120.0))
			{
				EasingFunction = new QuadraticEase
				{
					EasingMode = EasingMode.EaseOut
				}
			};
			DoubleAnimation shrink = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180.0))
			{
				BeginTime = TimeSpan.FromMilliseconds(120.0),
				EasingFunction = new QuadraticEase
				{
					EasingMode = EasingMode.EaseIn
				}
			};
			ColorAnimation colorAnim = new ColorAnimation(System.Windows.Media.Color.FromRgb(120, 200, byte.MaxValue), TimeSpan.FromMilliseconds(150.0));
			ColorAnimation colorBack = new ColorAnimation(Colors.White, TimeSpan.FromMilliseconds(250.0))
			{
				BeginTime = TimeSpan.FromMilliseconds(150.0)
			};
			scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
			scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
			scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
			scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
			SolidColorBrush brush = (SolidColorBrush)(tb.Foreground = new SolidColorBrush(Colors.White));
			brush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
			brush.BeginAnimation(SolidColorBrush.ColorProperty, colorBack);
		}
	}

	private double MeasureStatsTextWidth(TextBlock source, string text)
	{
		TextBlock textBlock = new TextBlock();
		textBlock.Text = text;
		textBlock.FontFamily = source.FontFamily;
		textBlock.FontSize = source.FontSize;
		textBlock.FontStretch = source.FontStretch;
		textBlock.FontStyle = source.FontStyle;
		textBlock.FontWeight = source.FontWeight;
		textBlock.Margin = source.Margin;
		textBlock.Padding = source.Padding;
		textBlock.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
		return Math.Ceiling(textBlock.DesiredSize.Width) + 1.0;
	}

	private void SetStatsMinutesValue(string primaryText, string unitText, string secondsText, bool showSeconds, bool animate)
	{
		if (_statsMinutesValueText == null || _statsSecondsRevealBorder == null || _statsSecondsValueText == null || _statsMinutesUnitRevealBorder == null || _statsMinutesUnitText == null)
		{
			return;
		}
		int animationToken = ++_statsMinutesAnimationToken;
		_statsSecondsRevealBorder.BeginAnimation(FrameworkElement.WidthProperty, null);
		_statsSecondsRevealBorder.BeginAnimation(UIElement.OpacityProperty, null);
		_statsMinutesUnitRevealBorder.BeginAnimation(FrameworkElement.WidthProperty, null);
		_statsMinutesUnitRevealBorder.BeginAnimation(UIElement.OpacityProperty, null);
		_statsMinutesValueText.Text = primaryText;
		_statsMinutesUnitText.Text = unitText;
		if (showSeconds || !animate)
		{
			_statsSecondsValueText.Text = secondsText;
		}
		_statsSecondsRevealBorder.ClipToBounds = true;
		_statsMinutesUnitRevealBorder.ClipToBounds = true;
		double secondsTargetWidth = (showSeconds ? MeasureStatsTextWidth(_statsSecondsValueText, secondsText) : 0.0);
		double unitTargetWidth = (showSeconds ? 0.0 : MeasureStatsTextWidth(_statsMinutesUnitText, _statsMinutesUnitText.Text));
		if (!animate)
		{
			_statsSecondsRevealBorder.Width = secondsTargetWidth;
			_statsSecondsRevealBorder.Opacity = (showSeconds ? 1 : 0);
			_statsMinutesUnitRevealBorder.Width = unitTargetWidth;
			_statsMinutesUnitRevealBorder.Opacity = ((!showSeconds) ? 1 : 0);
			return;
		}
		double secondsStartWidth = ((_statsSecondsRevealBorder.ActualWidth > 0.0) ? _statsSecondsRevealBorder.ActualWidth : _statsSecondsRevealBorder.Width);
		if (double.IsNaN(secondsStartWidth) || double.IsInfinity(secondsStartWidth))
		{
			secondsStartWidth = (showSeconds ? 0.0 : secondsTargetWidth);
		}
		double unitStartWidth = ((_statsMinutesUnitRevealBorder.ActualWidth > 0.0) ? _statsMinutesUnitRevealBorder.ActualWidth : _statsMinutesUnitRevealBorder.Width);
		if (double.IsNaN(unitStartWidth) || double.IsInfinity(unitStartWidth))
		{
			unitStartWidth = (showSeconds ? unitTargetWidth : 0.0);
		}
		_statsSecondsRevealBorder.Width = secondsStartWidth;
		_statsMinutesUnitRevealBorder.Width = unitStartWidth;
		DoubleAnimation secondsWidthAnimation = new DoubleAnimation(secondsTargetWidth, TimeSpan.FromMilliseconds(240.0))
		{
			EasingFunction = new QuarticEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		secondsWidthAnimation.Completed += delegate
		{
			if (animationToken == _statsMinutesAnimationToken)
			{
				if (!showSeconds)
				{
					_statsSecondsValueText.Text = "";
				}
				_statsSecondsRevealBorder.Width = secondsTargetWidth;
				_statsSecondsRevealBorder.Opacity = (showSeconds ? 1 : 0);
			}
		};
		DoubleAnimation secondsOpacityAnimation = new DoubleAnimation(showSeconds ? 1 : 0, TimeSpan.FromMilliseconds(180.0))
		{
			EasingFunction = new QuadraticEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		DoubleAnimation unitWidthAnimation = new DoubleAnimation(unitTargetWidth, TimeSpan.FromMilliseconds(200.0))
		{
			EasingFunction = new QuarticEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		unitWidthAnimation.Completed += delegate
		{
			if (animationToken == _statsMinutesAnimationToken)
			{
				_statsMinutesUnitRevealBorder.Width = unitTargetWidth;
				_statsMinutesUnitRevealBorder.Opacity = ((!showSeconds) ? 1 : 0);
			}
		};
		DoubleAnimation unitOpacityAnimation = new DoubleAnimation((!showSeconds) ? 1 : 0, TimeSpan.FromMilliseconds(140.0))
		{
			EasingFunction = new QuadraticEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		_statsSecondsRevealBorder.BeginAnimation(FrameworkElement.WidthProperty, secondsWidthAnimation);
		_statsSecondsRevealBorder.BeginAnimation(UIElement.OpacityProperty, secondsOpacityAnimation);
		_statsMinutesUnitRevealBorder.BeginAnimation(FrameworkElement.WidthProperty, unitWidthAnimation);
		_statsMinutesUnitRevealBorder.BeginAnimation(UIElement.OpacityProperty, unitOpacityAnimation);
	}

	private void ShowAddRadioPopup(RadioStation? existingRadio = null)
	{
		if (_addRadioPopup != null)
		{
			RootGrid.Children.Remove(_addRadioPopup);
		}
		_addRadioPopup = new Grid
		{
			Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(178, 0, 0, 0))
		};
		Grid.SetRowSpan(_addRadioPopup, 3);
		Grid.SetColumnSpan(_addRadioPopup, 4);
		System.Windows.Controls.Panel.SetZIndex(_addRadioPopup, 1500);
		Border dialogCard = new Border
		{
			Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 36)),
			BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(48, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(12.0),
			Padding = new Thickness(25.0),
			Width = 450.0,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		StackPanel sp = new StackPanel();
		sp.Children.Add(new TextBlock
		{
			Text = ((existingRadio != null) ? "Edit Custom Radio" : "Add Custom Radio"),
			Foreground = System.Windows.Media.Brushes.White,
			FontSize = 22.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 0.0, 0.0, 20.0)
		});
		ControlTemplate textBoxTemplate = (ControlTemplate)XamlReader.Parse("\r\n                <ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TextBox'>\r\n                    <Border Background='{TemplateBinding Background}' CornerRadius='8' Padding='{TemplateBinding Padding}'>\r\n                        <ScrollViewer x:Name='PART_ContentHost'/>\r\n                    </Border>\r\n                </ControlTemplate>");
		sp.Children.Add(new TextBlock
		{
			Text = "Station Name",
			Foreground = System.Windows.Media.Brushes.Gray,
			FontSize = 12.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 5.0),
			HorizontalAlignment = System.Windows.HorizontalAlignment.Left
		});
		System.Windows.Controls.TextBox nameBox = new System.Windows.Controls.TextBox
		{
			Text = (existingRadio?.Name ?? ""),
			Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			Foreground = System.Windows.Media.Brushes.White,
			CaretBrush = System.Windows.Media.Brushes.White,
			Padding = new Thickness(10.0, 6.0, 10.0, 6.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 15.0),
			BorderThickness = new Thickness(0.0),
			Template = textBoxTemplate
		};
		sp.Children.Add(nameBox);
		sp.Children.Add(new TextBlock
		{
			Text = "Description (Optional)",
			Foreground = System.Windows.Media.Brushes.Gray,
			FontSize = 12.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 5.0),
			HorizontalAlignment = System.Windows.HorizontalAlignment.Left
		});
		System.Windows.Controls.TextBox descBox = new System.Windows.Controls.TextBox
		{
			Text = (existingRadio?.Description ?? ""),
			Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			Foreground = System.Windows.Media.Brushes.White,
			CaretBrush = System.Windows.Media.Brushes.White,
			Padding = new Thickness(10.0, 6.0, 10.0, 6.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 15.0),
			BorderThickness = new Thickness(0.0),
			Template = textBoxTemplate
		};
		sp.Children.Add(descBox);
		sp.Children.Add(new TextBlock
		{
			Text = "Stream URL",
			Foreground = System.Windows.Media.Brushes.Gray,
			FontSize = 12.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 5.0),
			HorizontalAlignment = System.Windows.HorizontalAlignment.Left
		});
		string existingUrl = ((existingRadio?.Streams != null && existingRadio.Streams.Count > 0) ? existingRadio.Streams[0].Url : "");
		System.Windows.Controls.TextBox urlBox = new System.Windows.Controls.TextBox
		{
			Text = existingUrl,
			Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			Foreground = System.Windows.Media.Brushes.White,
			CaretBrush = System.Windows.Media.Brushes.White,
			Padding = new Thickness(10.0, 6.0, 10.0, 6.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 15.0),
			BorderThickness = new Thickness(0.0),
			Template = textBoxTemplate
		};
		sp.Children.Add(urlBox);
		sp.Children.Add(new TextBlock
		{
			Text = "Thumbnail URL (Optional)",
			Foreground = System.Windows.Media.Brushes.Gray,
			FontSize = 12.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 5.0),
			HorizontalAlignment = System.Windows.HorizontalAlignment.Left
		});
		System.Windows.Controls.TextBox thumbBox = new System.Windows.Controls.TextBox
		{
			Text = (existingRadio?.ThumbnailUrl ?? ""),
			Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			Foreground = System.Windows.Media.Brushes.White,
			CaretBrush = System.Windows.Media.Brushes.White,
			Padding = new Thickness(10.0, 6.0, 10.0, 6.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 25.0),
			BorderThickness = new Thickness(0.0),
			Template = textBoxTemplate
		};
		sp.Children.Add(thumbBox);
		StackPanel btnPanel = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Right
		};
		System.Windows.Controls.Button cancelBtn = new System.Windows.Controls.Button
		{
			Content = "Cancel",
			Margin = new Thickness(0.0, 0.0, 10.0, 0.0),
			Foreground = System.Windows.Media.Brushes.White,
			Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 50, 60)),
			BorderThickness = new Thickness(0.0),
			Padding = new Thickness(15.0, 10.0, 15.0, 10.0),
			Cursor = System.Windows.Input.Cursors.Hand
		};
		cancelBtn.Template = (ControlTemplate)XamlReader.Parse("\r\n                <ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>\r\n                    <Grid>\r\n                        <Border x:Name='NormalBorder' Background='{TemplateBinding Background}' CornerRadius='18' />\r\n                        <Border x:Name='HoverBorder' Background='#20FFFFFF' CornerRadius='18' Opacity='0' />\r\n                        <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' Margin='{TemplateBinding Padding}'/>\r\n                    </Grid>\r\n                    <ControlTemplate.Triggers>\r\n                        <EventTrigger RoutedEvent='MouseEnter'>\r\n                            <BeginStoryboard>\r\n                                <Storyboard>\r\n                                    <DoubleAnimation Storyboard.TargetName='HoverBorder' Storyboard.TargetProperty='Opacity' To='1' Duration='0:0:0.1' />\r\n                                </Storyboard>\r\n                            </BeginStoryboard>\r\n                        </EventTrigger>\r\n                        <EventTrigger RoutedEvent='MouseLeave'>\r\n                            <BeginStoryboard>\r\n                                <Storyboard>\r\n                                    <DoubleAnimation Storyboard.TargetName='HoverBorder' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.1' />\r\n                                </Storyboard>\r\n                            </BeginStoryboard>\r\n                        </EventTrigger>\r\n                    </ControlTemplate.Triggers>\r\n                </ControlTemplate>");
		cancelBtn.Click += delegate
		{
			DoubleAnimation doubleAnimation = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150.0))
			{
				EasingFunction = new QuadraticEase
				{
					EasingMode = EasingMode.EaseIn
				}
			};
			doubleAnimation.Completed += delegate
			{
				RootGrid.Children.Remove(_addRadioPopup);
			};
			_addRadioPopup.BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
		};
		System.Windows.Controls.Button saveBtn = new System.Windows.Controls.Button
		{
			Content = "Save",
			Style = (Style)System.Windows.Application.Current.MainWindow.Resources["SettingsAccentButtonStyle"],
			FontWeight = FontWeights.Bold
		};
		saveBtn.Click += delegate
		{
			if (!string.IsNullOrWhiteSpace(nameBox.Text) && !string.IsNullOrWhiteSpace(urlBox.Text))
			{
				if (existingRadio != null)
				{
					existingRadio.Name = nameBox.Text.Trim();
					existingRadio.Description = descBox.Text.Trim();
					existingRadio.ThumbnailUrl = thumbBox.Text.Trim();
					if (existingRadio.Streams != null && existingRadio.Streams.Count > 0)
					{
						existingRadio.Streams[0].Url = urlBox.Text.Trim();
					}
					else
					{
						existingRadio.Streams = new List<RadioStream>
						{
							new RadioStream
							{
								Name = "Stream",
								Url = urlBox.Text.Trim(),
								Icon = "▶"
							}
						};
					}
				}
				else
				{
					_savedRadios.Add(new RadioStation
					{
						Name = nameBox.Text.Trim(),
						Description = descBox.Text.Trim(),
						ThumbnailUrl = thumbBox.Text.Trim(),
						Streams = new List<RadioStream>
						{
							new RadioStream
							{
								Name = "Stream",
								Url = urlBox.Text.Trim(),
								Icon = "▶"
							}
						}
					});
				}
				SaveSession();
				DoubleAnimation doubleAnimation = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150.0))
				{
					EasingFunction = new QuadraticEase
					{
						EasingMode = EasingMode.EaseIn
					}
				};
				doubleAnimation.Completed += delegate
				{
					RootGrid.Children.Remove(_addRadioPopup);
				};
				_addRadioPopup.BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
				if (_currentPageId == "radio_page")
				{
					_ = _ = _ = _ = LoadRadioFeedAsync(forceReload: true);
				}
			}
		};
		btnPanel.Children.Add(cancelBtn);
		btnPanel.Children.Add(saveBtn);
		sp.Children.Add(btnPanel);
		dialogCard.Child = sp;
		_addRadioPopup.Children.Add(dialogCard);
		_addRadioPopup.Opacity = 0.0;
		dialogCard.RenderTransform = new TranslateTransform(0.0, 20.0);
		RootGrid.Children.Add(_addRadioPopup);
		DoubleAnimation animOpacity = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200.0))
		{
			EasingFunction = new QuadraticEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		DoubleAnimation animMove = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(300.0))
		{
			EasingFunction = new QuarticEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		_addRadioPopup.BeginAnimation(UIElement.OpacityProperty, animOpacity);
		dialogCard.RenderTransform.BeginAnimation(TranslateTransform.YProperty, animMove);
	}

	private void PopulateArtistLinks(TextBlock tb, string artistString, int fontSize = 12, JsonArray? artistsData = null)
	{
		tb.Inlines.Clear();
		string[] names = (artistString ?? "").Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < names.Length; i++)
		{
			string aName = names[i].Trim();
			if (string.IsNullOrEmpty(aName))
			{
				continue;
			}
			string exactArtistId = null;
			if (artistsData != null)
			{
				foreach (JsonNode a in artistsData)
				{
					string jName = (string?)a?["name"];
					if (jName != null && aName.Equals(jName.Trim(), StringComparison.OrdinalIgnoreCase))
					{
						exactArtistId = ((string?)a?["id"]) ?? ((string?)a?["browseId"]);
						break;
					}
				}
			}
			Run run = new Run(aName)
			{
				FontSize = fontSize
			};
			Hyperlink hl = new Hyperlink(run);
			hl.TextDecorations = null;
			hl.Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"];
			hl.Cursor = System.Windows.Input.Cursors.Hand;
			hl.MouseEnter += delegate
			{
				FadeTextElementForegroundToColor(hl, ((SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"]).Color);
			};
			hl.MouseLeave += delegate
			{
				FadeTextElementForegroundToColor(hl, ((SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"]).Color);
			};
			hl.Click += async delegate(object s, RoutedEventArgs e)
			{
				e.Handled = true;
				if (!string.IsNullOrEmpty(exactArtistId))
				{
					int earlyTId = await FadeOutContentAsync();
					try
					{
						MainTopbarControl.StatusLabelRef.Text = "";
						OpenArtistPage(exactArtistId, aName, "", earlyTId);
						return;
					}
					catch
					{
						MainTopbarControl.StatusLabelRef.Text = "Artist resolution failed.";
						await FadeInContentAsync(earlyTId, delegate
						{
						});
						return;
					}
				}
				try
				{
					if ((await BackendService.Instance.SearchAsync(aName, CancellationToken.None))["data"] is JsonObject data && data["artists"] is JsonArray { Count: >0 } artists)
					{
						JsonNode JsonNode = artists[0];
						string id = ((string?)JsonNode["browseId"]) ?? "";
						string n = ((string?)JsonNode["artist"]) ?? "";
						string thumb = "";
						if (JsonNode["thumbnails"] is JsonArray { Count: >0 } thumbs)
						{
							thumb = ((string?)thumbs[thumbs.Count - 1]["url"]) ?? "";
						}
						OpenArtistPage(id, n, thumb);
					}
				}
				catch
				{
				}
			};
			tb.Inlines.Add(hl);
			if (i < names.Length - 1)
			{
				tb.Inlines.Add(new Run(", ")
				{
					Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"],
					FontSize = fontSize
				});
			}
		}
	}

	private bool IsLikedSongsPage(string playlistId, string title)
	{
		string normalizedTitle = title.Trim().ToLowerInvariant();
		if (!playlistId.Equals("LM", StringComparison.OrdinalIgnoreCase) && !(normalizedTitle == "liked songs"))
		{
			return normalizedTitle == "liked music";
		}
		return true;
	}

	private void UpdateLikedMenuLabel(MenuItem likeMenu, string videoId)
	{
		likeMenu.Header = (_likedVideoIds.Contains(videoId) ? "Remove from Liked songs" : "Save to Liked songs");
	}

	private string GetProcessedThumbUrl(string thumbUrl)
	{
		if (string.IsNullOrEmpty(thumbUrl))
		{
			return thumbUrl;
		}
		if (thumbUrl.Contains("googleusercontent.com") || thumbUrl.Contains("ggpht.com"))
		{
			int eqIndex = thumbUrl.LastIndexOf("=");
			thumbUrl = ((eqIndex <= 0) ? (thumbUrl + "=w544-h544-p-l90-rj") : (thumbUrl.Substring(0, eqIndex) + "=w544-h544-p-l90-rj"));
		}
		if (thumbUrl.Contains("i.ytimg.com"))
		{
			if (thumbUrl.Contains("sqp="))
			{
				thumbUrl = Regex.Replace(thumbUrl, "/[^/]+\\?sqp=", "/sddefault.jpg?sqp=");
			}
			else
			{
				int qIndex = thumbUrl.IndexOf("?");
				if (qIndex > 0)
				{
					thumbUrl = thumbUrl.Substring(0, qIndex);
				}
				int slashIndex = thumbUrl.LastIndexOf("/");
				if (slashIndex > 0)
				{
					thumbUrl = thumbUrl.Substring(0, slashIndex + 1) + "mqdefault.jpg";
				}
			}
		}
		return thumbUrl;
	}

	private void PrefetchThumbnail(JsonNode item)
	{
		try
		{
			string url = "";
			JsonArray thumbs = (item["thumbnails"] as JsonArray) ?? (item["thumbnail"] as JsonArray);
			if (thumbs == null)
			{
				JsonNode thumbObj = item["thumbnail"];
				if (thumbObj != null && thumbObj is JsonObject)
				{
					thumbs = thumbObj["thumbnails"] as JsonArray;
				}
			}
			if (thumbs != null && thumbs.Count > 0)
			{
				url = ((string?)thumbs[thumbs.Count - 1]["url"]) ?? "";
			}
			url = GetProcessedThumbUrl(url);
			if (string.IsNullOrEmpty(url) || _imageCache.ContainsKey(url))
			{
				return;
			}
			_inflightThumbnails[url] = true;
			Task.Run(async delegate
			{
				await _thumbnailSemaphore.WaitAsync();
				try
				{
					byte[] buffer = await BackendService.Instance.DownloadImageAsync(url, CancellationToken.None);
					BitmapImage bmp = new BitmapImage();
					using (MemoryStream ms = new MemoryStream(buffer))
					{
						bmp.BeginInit();
						bmp.CacheOption = BitmapCacheOption.OnLoad;
						bmp.CreateOptions = BitmapCreateOptions.None;
						bmp.DecodePixelWidth = 120;
						bmp.StreamSource = ms;
						bmp.EndInit();
					}
					bmp.Freeze();
					if (_imageCache.Count > 100)
					{
						_imageCache.Clear();
					}
					_imageCache[url] = bmp;
				}
				catch
				{
				}
				finally
				{
					_thumbnailSemaphore.Release();
					_inflightThumbnails.TryRemove(url, out var _);
				}
			});
		}
		catch
		{
		}
	}

	private void MainScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
	{
		UpdateMainScrollVisuals(MainScrollViewer.VerticalOffset);
		if (!isAnimating)
		{
			currentScrollY = MainScrollViewer.VerticalOffset;
			targetScrollY = currentScrollY;
			_mainBaseScrollY = currentScrollY;
			ScheduleScrollWork();
		}
	}

	private void UpdateMainScrollVisuals(double verticalOffset)
	{
		bool isScrolledNow = verticalOffset > 5.0;
		if (isScrolledNow != _lastMainScrollIsScrolled)
		{
			_lastMainScrollIsScrolled = isScrolledNow;
			IsScrolled = isScrolledNow;
		}
		double topOpacity = Math.Min(1.0, verticalOffset / 100.0);
		if (Math.Abs(topOpacity - _lastTopbarOpacity) >= 0.01)
		{
			_lastTopbarOpacity = topOpacity;
			MainTopbarControl.TopbarBackgroundRef.Opacity = topOpacity;
		}
		if (_isLyricsViewOpen && !_lyricsAreSynced)
		{
			long now = Stopwatch.GetTimestamp();
			if (((_lastUnsyncedLyricsOpacityTick == 0L) ? double.MaxValue : ((double)(now - _lastUnsyncedLyricsOpacityTick) * 1000.0 / (double)Stopwatch.Frequency)) >= 33.0)
			{
				_lastUnsyncedLyricsOpacityTick = now;
				UpdateUnsyncedLyricsOpacity();
			}
		}
	}

	private void ScheduleScrollWork()
	{
		if (!_isVirtualizationQueued)
		{
			_isVirtualizationQueued = true;
		}
		_scrollWorkTimer.Stop();
		_scrollWorkTimer.Start();
	}

	private void VirtualizeOffscreenElements()
	{
	}

	private void CheckVisibilityOfLazyElements()
	{
		if (_lazyVirtualizationElements.Count > 0)
		{
			Border firstBorder = _lazyVirtualizationElements[0];
			if (System.Windows.PresentationSource.FromVisual(firstBorder) == null)
			{
				return;
			}
			if (!firstBorder.IsLoaded)
			{
				base.Dispatcher.InvokeAsync(CheckVisibilityOfLazyElements, DispatcherPriority.Loaded);
				return;
			}
			double scrollTop = MainScrollViewer.VerticalOffset;
			double scrollBottom = scrollTop + MainScrollViewer.ViewportHeight;
			scrollBottom += 2500.0;
			scrollTop -= 2500.0;
			bool anyRendered = false;
			GeneralTransform transform = null;
			try
			{
				transform = firstBorder.TransformToAncestor(ContentPanel);
			}
			catch
			{
			}
			if (transform == null)
			{
				if (System.Windows.PresentationSource.FromVisual(firstBorder) != null)
				{
					base.Dispatcher.InvokeAsync(CheckVisibilityOfLazyElements, DispatcherPriority.Loaded);
				}
				return;
			}
			double currentY = transform.Transform(new System.Windows.Point(0.0, 0.0)).Y;
			for (int i = 0; i < _lazyVirtualizationElements.Count; i++)
			{
				Border border = _lazyVirtualizationElements[i];
				double itemHeight = ((border.Height > 0.0) ? border.Height : ((border.ActualHeight > 0.0) ? border.ActualHeight : 70.0));
				bool inView = currentY < scrollBottom && currentY + itemHeight > scrollTop;
				if (inView && border.Child == null)
				{
					if (border.Tag is UIElement cachedElement)
					{
						border.Child = cachedElement;
						anyRendered = true;
					}
					else if (!(border.Tag is string t && t == "loading"))
					{
						Func<UIElement> action = _lazyVirtualizationActions[i];
						if (action != null)
						{
							UIElement newEl = action();
							border.Tag = newEl;
							border.Child = newEl;
							anyRendered = true;
						}
					}
				}
				currentY += itemHeight;
			}
			if (anyRendered && !string.IsNullOrEmpty(_currentVideoId))
			{
				HighlightNowPlaying(_currentVideoId);
			}
		}
		if (_lazyRenderElements.Count == 0)
		{
			return;
		}
		double scrollTopOld = MainScrollViewer.VerticalOffset;
		double scrollBottomOld = scrollTopOld + MainScrollViewer.ViewportHeight;
		scrollBottomOld += 800.0;
		scrollTopOld -= 800.0;
		bool anyRenderedOld = false;
		for (int i2 = _lazyRenderElements.Count - 1; i2 >= 0; i2--)
		{
			FrameworkElement el = _lazyRenderElements[i2];
			if (el.IsLoaded)
			{
				try
				{
					System.Windows.Point topLeft = el.TransformToAncestor(ContentPanel).Transform(new System.Windows.Point(0.0, 0.0));
					bool inView2 = topLeft.Y < scrollBottomOld && topLeft.Y + el.ActualHeight > scrollTopOld;
					if (inView2 && !_lazyRenderStates[i2])
					{
						_lazyRenderStates[i2] = true;
						Action<bool> action2 = _lazyRenderActions[i2];
						base.Dispatcher.InvokeAsync(delegate
						{
							action2?.Invoke(obj: true);
						}, DispatcherPriority.Background);
						anyRenderedOld = true;
					}
					else if (!inView2 && _lazyRenderStates[i2])
					{
						_lazyRenderStates[i2] = false;
						Action<bool> action3 = _lazyRenderActions[i2];
						base.Dispatcher.InvokeAsync(delegate
						{
							action3?.Invoke(obj: false);
						}, DispatcherPriority.Background);
					}
				}
				catch
				{
				}
			}
		}
		if (anyRenderedOld && !string.IsNullOrEmpty(_currentVideoId))
		{
			HighlightNowPlaying(_currentVideoId);
		}
	}	private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		e.Handled = true;
		if (_isLyricsViewOpen)
		{
			_isLyricsUserScrolled = true;
		}
		double baseScrollY = ((!isAnimating || !(Math.Abs(targetScrollY - currentScrollY) > 1.0) || Math.Sign(targetScrollY - currentScrollY) == Math.Sign(-e.Delta)) ? (isAnimating ? targetScrollY : currentScrollY) : currentScrollY);
		SmoothScrollTo(baseScrollY - (double)e.Delta * 0.85);
	}

	private void SmoothScrollTo(double targetOffset)
	{
		if (_disableSmoothScrolling)
		{
			MainScrollViewer.ScrollToVerticalOffset(Math.Max(0.0, targetOffset));
			currentScrollY = targetOffset;
			targetScrollY = targetOffset;
			ScheduleScrollWork();
			return;
		}
		
		double diff = Math.Abs(targetOffset - currentScrollY);
		if (diff > 150.0)
		{
			_mainUseOldScrollMethod = true;
		}
		else if (!isAnimating)
		{
			_mainUseOldScrollMethod = false;
		}
		
		if (!isAnimating)
		{
			currentScrollY = MainScrollViewer.VerticalOffset;
			_mainBaseScrollY = currentScrollY;
			targetScrollY = currentScrollY;
			isAnimating = true;
			_lastScrollTick = 0;
			_mainScrollVelocity = 0.0;
			CompositionTarget.Rendering += AnimationLoop;
		}
		targetScrollY = Math.Max(0.0, Math.Min(MainScrollViewer.ScrollableHeight, targetOffset));
	}

	private void AnimationLoop(object? sender, EventArgs e)
	{
		long now = Stopwatch.GetTimestamp();
		double dt;
		if (_lastScrollTick == 0)
		{
			dt = 0.016;
		}
		else
		{
			dt = (double)(now - _lastScrollTick) / (double)Stopwatch.Frequency;
		}
		_lastScrollTick = now;
		
		if (dt <= 0.0)
		{
			dt = 0.016;
		}
		if (dt > 0.05)
		{
			dt = 0.05;
		}
		
		if (Math.Abs(MainScrollViewer.VerticalOffset - _mainBaseScrollY) > 5.0)
		{
			currentScrollY = MainScrollViewer.VerticalOffset;
			targetScrollY = currentScrollY;
			isAnimating = false;
			CompositionTarget.Rendering -= AnimationLoop;
			ScheduleScrollWork();
			return;
		}
		
		double clampedTarget = Math.Min(MainScrollViewer.ScrollableHeight, targetScrollY);
		double displacement = currentScrollY - clampedTarget;
		
		if (Math.Abs(displacement) < 0.5 && (_mainUseOldScrollMethod || Math.Abs(_mainScrollVelocity) < 10.0))
		{
			currentScrollY = clampedTarget;
			_mainScrollVelocity = 0.0;
			MainScrollViewer.ScrollToVerticalOffset(currentScrollY);
			_mainBaseScrollY = currentScrollY;
			CompositionTarget.Rendering -= AnimationLoop;
			isAnimating = false;
			ScheduleScrollWork();
		}
		else
		{
			if (_mainUseOldScrollMethod)
			{
				double factor = 1.0 - Math.Exp(-20.0 * dt);
				currentScrollY += (clampedTarget - currentScrollY) * factor;
			}
			else
			{
				double w = 25.0;
				double c1 = displacement;
				double c2 = _mainScrollVelocity + w * displacement;
				double expTerm = Math.Exp(-w * dt);
				
				double newDisplacement = (c1 + c2 * dt) * expTerm;
				_mainScrollVelocity = (c2 - w * (c1 + c2 * dt)) * expTerm;
				
				currentScrollY = clampedTarget + newDisplacement;
			}
			MainScrollViewer.ScrollToVerticalOffset(currentScrollY);
			_mainBaseScrollY = currentScrollY;
		}
	}

	private void CancelScrollAnimation()
	{
		if (isAnimating)
		{
			CompositionTarget.Rendering -= AnimationLoop;
			isAnimating = false;
		}
		_mainScrollVelocity = 0.0;
		_mainUseOldScrollMethod = false;
		_lastScrollTick = 0;
		currentScrollY = MainScrollViewer.VerticalOffset;
		targetScrollY = currentScrollY;
		_mainBaseScrollY = currentScrollY;
	}

	private void ResetScroll()
	{
		isAnimating = false;
		CompositionTarget.Rendering -= AnimationLoop;
		_mainScrollVelocity = 0.0;
		_mainUseOldScrollMethod = false;
		_lastScrollTick = 0;
		targetScrollY = 0.0;
		currentScrollY = 0.0;
		_mainBaseScrollY = 0.0;
		MainScrollViewer.ScrollToTop();
	}

	private void FadeBorderBackgroundToColor(Border border, System.Windows.Media.Color targetColor)
	{
		if (border.IsMouseOver)
		{
			FadeBorderBrushToColor(border, _hoverBorderColor);
		}
		else
		{
			FadeBorderBrushToColor(border, Colors.Transparent);
		}
		System.Windows.Media.Color startColor = Colors.Transparent;
		if (border.Background is SolidColorBrush sb)
		{
			startColor = sb.Color;
		}
		else if (border.Background != null && border.ReadLocalValue(Border.BackgroundProperty) is DynamicResourceExtension)
		{
			startColor = ((SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["CardHoverBrush"]).Color;
		}
		if (startColor == targetColor)
		{
			border.Background = new SolidColorBrush(targetColor);
			return;
		}
		SolidColorBrush newBrush = new SolidColorBrush(startColor);
		border.Background = newBrush;
		ColorAnimation anim = new ColorAnimation(targetColor, TimeSpan.FromMilliseconds(80.0));
		anim.Completed += delegate
		{
			if (border.Background == newBrush)
			{
				border.Background = new SolidColorBrush(targetColor);
			}
		};
		newBrush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
	}

	private void FadeBorderBackgroundToResource(Border border, string resourceKey)
	{
		if (border.IsMouseOver)
		{
			{
			}
			FadeBorderBrushToColor(border, _hoverBorderColor);
		}
		else
		{
			FadeBorderBrushToColor(border, Colors.Transparent);
		}
		if (!(System.Windows.Application.Current.MainWindow.Resources[resourceKey] is SolidColorBrush { Color: var targetColor }))
		{
			return;
		}
		System.Windows.Media.Color startColor = Colors.Transparent;
		if (border.Background is SolidColorBrush sb)
		{
			startColor = sb.Color;
		}
		else if (border.Background != null && border.ReadLocalValue(Border.BackgroundProperty) is DynamicResourceExtension)
		{
			startColor = ((SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["CardHoverBrush"]).Color;
		}
		if (startColor == targetColor)
		{
			border.SetResourceReference(Border.BackgroundProperty, resourceKey);
			return;
		}
		SolidColorBrush newBrush = new SolidColorBrush(startColor);
		border.Background = newBrush;
		ColorAnimation anim = new ColorAnimation(targetColor, TimeSpan.FromMilliseconds(80.0));
		anim.Completed += delegate
		{
			if (border.Background == newBrush)
			{
				border.SetResourceReference(Border.BackgroundProperty, resourceKey);
			}
		};
		newBrush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
	}

	private void FadeBorderBrushToColor(Border border, System.Windows.Media.Color targetColor)
	{
		if (!_enableHoverBorders && targetColor == _hoverBorderColor)
		{
			return;
		}
		System.Windows.Media.Color startColor = ((border.BorderBrush is SolidColorBrush currentBrush) ? currentBrush.Color : Colors.Transparent);
		if (startColor == targetColor)
		{
			border.BorderBrush = new SolidColorBrush(targetColor);
			return;
		}
		SolidColorBrush newBrush = new SolidColorBrush(startColor);
		border.BorderBrush = newBrush;
		ColorAnimation anim = new ColorAnimation(targetColor, TimeSpan.FromMilliseconds(80.0));
		anim.Completed += delegate
		{
			if (border.BorderBrush == newBrush)
			{
				border.BorderBrush = new SolidColorBrush(targetColor);
			}
		};
		newBrush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
	}

	private void FadeTextForegroundToColor(TextBlock txt, System.Windows.Media.Color targetColor)
	{
		System.Windows.Media.Color startColor = ((txt.GetValue(TextBlock.ForegroundProperty) is SolidColorBrush currentBrush) ? currentBrush.Color : Colors.Transparent);
		if (startColor == targetColor)
		{
			txt.Foreground = new SolidColorBrush(targetColor);
			return;
		}
		SolidColorBrush newBrush = new SolidColorBrush(startColor);
		txt.Foreground = newBrush;
		ColorAnimation anim = new ColorAnimation(targetColor, TimeSpan.FromMilliseconds(80.0));
		anim.Completed += delegate
		{
			if (txt.Foreground == newBrush)
			{
				txt.Foreground = new SolidColorBrush(targetColor);
			}
		};
		newBrush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
	}

	private void FadeTextElementForegroundToColor(TextElement txt, System.Windows.Media.Color targetColor)
	{
		System.Windows.Media.Color startColor = ((txt.GetValue(TextElement.ForegroundProperty) is SolidColorBrush currentBrush) ? currentBrush.Color : Colors.Transparent);
		if (startColor == targetColor)
		{
			txt.Foreground = new SolidColorBrush(targetColor);
			return;
		}
		SolidColorBrush newBrush = new SolidColorBrush(startColor);
		txt.Foreground = newBrush;
		ColorAnimation anim = new ColorAnimation(targetColor, TimeSpan.FromMilliseconds(80.0));
		anim.Completed += delegate
		{
			if (txt.Foreground == newBrush)
			{
				txt.Foreground = new SolidColorBrush(targetColor);
			}
		};
		newBrush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
	}

	public void TriggerConfetti(System.Windows.Point position)
	{
		if (MainOverlayControl.ConfettiCanvasRef == null)
		{
			return;
		}
		Random rnd = new Random();
		int numParticles = 35;
		System.Windows.Media.Color[] colors = new System.Windows.Media.Color[7]
		{
			Colors.Red,
			Colors.Yellow,
			Colors.Cyan,
			Colors.Lime,
			Colors.Magenta,
			Colors.Orange,
			Colors.White
		};
		for (int i = 0; i < numParticles; i++)
		{
			System.Windows.Shapes.Rectangle p = new System.Windows.Shapes.Rectangle
			{
				Width = rnd.Next(5, 12),
				Height = rnd.Next(5, 12),
				Fill = new SolidColorBrush(colors[rnd.Next(colors.Length)]),
				RadiusX = 2.0,
				RadiusY = 2.0,
				RenderTransformOrigin = new System.Windows.Point(0.5, 0.5)
			};
			Canvas.SetLeft(p, position.X - p.Width / 2.0);
			Canvas.SetTop(p, position.Y - p.Height / 2.0);
			MainOverlayControl.ConfettiCanvasRef.Children.Add(p);
			TransformGroup group = new TransformGroup();
			ScaleTransform scale = new ScaleTransform(1.0, 1.0);
			RotateTransform rotate = new RotateTransform(rnd.Next(0, 360));
			TranslateTransform translateVelocity = new TranslateTransform(0.0, 0.0);
			TranslateTransform translateGravity = new TranslateTransform(0.0, 0.0);
			group.Children.Add(scale);
			group.Children.Add(rotate);
			group.Children.Add(translateVelocity);
			group.Children.Add(translateGravity);
			p.RenderTransform = group;
			double num = rnd.NextDouble() * (Math.PI * 4.0 / 5.0) + 3.455751918948773;
			double velocity = rnd.NextDouble() * 150.0 + 100.0;
			double tx = Math.Cos(num) * velocity;
			double ty = Math.Sin(num) * velocity;
			TimeSpan duration = TimeSpan.FromMilliseconds(rnd.Next(900, 1400));
			DoubleAnimation animX = new DoubleAnimation(0.0, tx, duration)
			{
				EasingFunction = new CircleEase
				{
					EasingMode = EasingMode.EaseOut
				}
			};
			DoubleAnimation animVelocityY = new DoubleAnimation(0.0, ty, duration)
			{
				EasingFunction = new CircleEase
				{
					EasingMode = EasingMode.EaseOut
				}
			};
			DoubleAnimation animGravityY = new DoubleAnimation(0.0, 350.0, duration)
			{
				EasingFunction = new QuadraticEase
				{
					EasingMode = EasingMode.EaseIn
				}
			};
			DoubleAnimation animRot = new DoubleAnimation(0.0, rnd.Next(180, 540) * ((rnd.Next(2) == 0) ? 1 : (-1)), duration);
			TimeSpan fadeDuration = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.4);
			DoubleAnimation animOp = new DoubleAnimation(1.0, 0.0, fadeDuration)
			{
				BeginTime = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.6)
			};
			animOp.Completed += delegate
			{
				MainOverlayControl.ConfettiCanvasRef.Children.Remove(p);
			};
			translateVelocity.BeginAnimation(TranslateTransform.XProperty, animX);
			translateVelocity.BeginAnimation(TranslateTransform.YProperty, animVelocityY);
			translateGravity.BeginAnimation(TranslateTransform.YProperty, animGravityY);
			rotate.BeginAnimation(RotateTransform.AngleProperty, animRot);
			p.BeginAnimation(UIElement.OpacityProperty, animOp);
		}
	}

	private void AnimateCollapse(FrameworkElement element, bool collapse, bool instant = false)
	{
		Transform layoutTransform = element.LayoutTransform;
		ScaleTransform st = layoutTransform as ScaleTransform;
		if (st == null)
		{
			st = new ScaleTransform(1.0, 1.0);
			element.LayoutTransform = st;
		}
		if (instant)
		{
			if (collapse)
			{
				element.Visibility = Visibility.Collapsed;
				element.Opacity = 0.0;
				st.ScaleY = 0.0;
			}
			else
			{
				element.Visibility = Visibility.Visible;
				element.Opacity = 1.0;
				st.ScaleY = 1.0;
			}
			return;
		}
		if (collapse)
		{
			if (element.Visibility == Visibility.Collapsed || (element.Opacity == 0.0 && st.ScaleY == 0.0))
			{
				return;
			}
			DoubleAnimation animY = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(250.0))
			{
				EasingFunction = new QuarticEase
				{
					EasingMode = EasingMode.EaseInOut
				}
			};
			DoubleAnimation animO = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150.0));
			animY.Completed += delegate
			{
				if (element.LayoutTransform == st && st.ScaleY == 0.0)
				{
					element.Visibility = Visibility.Collapsed;
				}
			};
			element.BeginAnimation(UIElement.OpacityProperty, animO);
			st.BeginAnimation(ScaleTransform.ScaleYProperty, animY);
		}
		else if (element.Visibility != Visibility.Visible || element.Opacity != 1.0 || st.ScaleY != 1.0)
		{
			element.Visibility = Visibility.Visible;
			DoubleAnimation animY2 = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(250.0))
			{
				EasingFunction = new QuarticEase
				{
					EasingMode = EasingMode.EaseInOut
				}
			};
			DoubleAnimation animO2 = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200.0))
			{
				BeginTime = TimeSpan.FromMilliseconds(50.0)
			};
			element.BeginAnimation(UIElement.OpacityProperty, animO2);
			st.BeginAnimation(ScaleTransform.ScaleYProperty, animY2);
		}
	}

	private Border CreateGridCard(string id, string title, string subtitle, string tUrl, string type)
	{
		Border border = new Border
		{
			Width = 180.0,
			Margin = new Thickness(0.0, 0.0, 20.0, 30.0),
			Background = System.Windows.Media.Brushes.Transparent,
			Cursor = System.Windows.Input.Cursors.Hand,
			Tag = id
		};
		StackPanel sp = new StackPanel();
		Border imgBorder = new Border
		{
			Width = 180.0,
			Height = 180.0,
			CornerRadius = new CornerRadius(8.0),
			Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 25, 35)),
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		};
		Geometry clipRect = new RectangleGeometry
		{
			Rect = new Rect(0.0, 0.0, 180.0, 180.0),
			RadiusX = 8.0,
			RadiusY = 8.0
		};
		imgBorder.Clip = clipRect;
		imgBorder.Child = CreateImage(tUrl, 180, 180);
		imgBorder.CacheMode = new BitmapCache();
		ApplyImageOverlay(imgBorder);
		sp.Children.Add(imgBorder);
		TextBlock titleTxt = new TextBlock
		{
			Text = title,
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			FontSize = 14.0,
			FontWeight = FontWeights.SemiBold,
			TextTrimming = TextTrimming.CharacterEllipsis,
			Margin = new Thickness(2.0, 0.0, 2.0, 4.0)
		};
		sp.Children.Add(titleTxt);
		TextBlock subTxt = new TextBlock
		{
			Text = subtitle,
			Foreground = System.Windows.Media.Brushes.Gray,
			FontSize = 12.0,
			TextTrimming = TextTrimming.CharacterEllipsis,
			Margin = new Thickness(2.0, 0.0, 2.0, 0.0)
		};
		sp.Children.Add(subTxt);
		border.Child = sp;
		border.MouseEnter += delegate
		{
			FadeBorderBackgroundToResource(border, "CardHoverBrush");
		};
		border.MouseLeave += delegate
		{
			FadeBorderBackgroundToColor(border, Colors.Transparent);
		};
		if (type == "Playlist" || type == "Album")
		{
			border.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
			{
				e.Handled = true;
				OpenPlaylistPage(id, title, subtitle, tUrl, type);
			};
			border.ContextMenu = CreateCollectionContextMenu(id, title, type, border, titleTxt);
		}
		if (!string.IsNullOrEmpty(_currentVideoId) && id == _currentVideoId)
		{
			HighlightInPanel(border, _currentVideoId);
		}
		return border;
	}

	private UIElement CreateImage(string url, int width, int height, Stretch stretch = Stretch.UniformToFill)
	{
		System.Windows.Shapes.Rectangle rect = new System.Windows.Shapes.Rectangle();
		if (width > 0)
		{
			rect.Width = width;
		}
		if (height > 0)
		{
			rect.Height = height;
		}
		if (string.IsNullOrEmpty(url))
		{
			return rect;
		}
		int decodeW = ((width > 0) ? width : 0);
		if (decodeW > 0 && url.Contains("googleusercontent.com"))
		{
			int eqIndex = url.LastIndexOf("=");
			if (eqIndex > 0)
			{
				url = url.Substring(0, eqIndex) + $"=w{decodeW}-h{decodeW}-p-l90-rj";
			}
			else
			{
				url += $"=w{decodeW}-h{decodeW}-p-l90-rj";
			}
			decodeW = 0;
		}
		string cacheKey = ((decodeW > 0) ? $"{url}|{decodeW}" : url);
		ImageBrush brush = new ImageBrush
		{
			Stretch = stretch,
			AlignmentX = AlignmentX.Center,
			AlignmentY = AlignmentY.Center
		};
		rect.Fill = brush;
		if (_imageCache.TryGetValue(cacheKey, out BitmapImage cachedBmp))
		{
			brush.ImageSource = cachedBmp;
			rect.Opacity = 0.0;
			DoubleAnimation anim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(250.0));
			rect.BeginAnimation(UIElement.OpacityProperty, anim);
		}
		else
		{
			rect.Opacity = 0.0;
			Task.Run(async delegate
			{
				_ = 1;
				try
				{
					byte[] bytes = ((!url.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)) ? (await BackendService.Instance.DownloadImageAsync(url, CancellationToken.None)) : System.IO.File.ReadAllBytes(new Uri(url).LocalPath));
					BitmapImage bmp = new BitmapImage();
					using (MemoryStream ms = new MemoryStream(bytes))
					{
						bmp.BeginInit();
						bmp.CacheOption = BitmapCacheOption.OnLoad;
						if (decodeW > 0)
						{
							bmp.DecodePixelWidth = decodeW;
						}
						bmp.StreamSource = ms;
						bmp.EndInit();
					}
					bmp.Freeze();
					await base.Dispatcher.InvokeAsync(delegate
					{
						try
						{
							if (_imageCache.Count > 100)
							{
								_imageCache.Clear();
							}
							_imageCache[cacheKey] = bmp;
							brush.ImageSource = bmp;
							DoubleAnimation animation = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(250.0));
							rect.BeginAnimation(UIElement.OpacityProperty, animation);
						}
						catch
						{
						}
					});
				}
				catch
				{
				}
			});
		}
		rect.Tag = url;
		return rect;
	}

	private Border CreateLoadingWaveOverlay(double cornerRadius)
	{
		Border obj = new Border
		{
			CornerRadius = new CornerRadius(cornerRadius),
			Background = System.Windows.Media.Brushes.Transparent,
			Opacity = 0.0,
			Visibility = Visibility.Collapsed,
			IsHitTestVisible = false
		};
		LinearGradientBrush brush = new LinearGradientBrush
		{
			StartPoint = new System.Windows.Point(0.0, 0.0),
			EndPoint = new System.Windows.Point(1.0, 0.0),
			GradientStops = new GradientStopCollection
			{
				new GradientStop(System.Windows.Media.Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.0),
				new GradientStop(System.Windows.Media.Color.FromArgb(10, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.3),
				new GradientStop(System.Windows.Media.Color.FromArgb(30, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.5),
				new GradientStop(System.Windows.Media.Color.FromArgb(10, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.7),
				new GradientStop(System.Windows.Media.Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), 1.0)
			}
		};
		TranslateTransform translate = new TranslateTransform(-1.0, 0.0);
		brush.RelativeTransform = translate;
		System.Windows.Shapes.Rectangle rect = new System.Windows.Shapes.Rectangle
		{
			Fill = brush,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch
		};
		obj.Child = rect;
		return obj;
	}

	private UIElement CreateTrackCard(string videoId, string title, string artist, string thumbUrl, string type = "Song", JsonArray? artistsData = null, JsonObject? albumData = null)
	{
		Border border = new Border
		{
			Background = (System.Windows.Media.Brush)System.Windows.Application.Current.MainWindow.Resources["CardBackground"],
			BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(15, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(10.0),
			Width = 150.0,
			Height = 196.0,
			Margin = new Thickness(10.0),
			Cursor = System.Windows.Input.Cursors.Hand,
			Tag = videoId
		};
		Grid grid = new Grid();
		StackPanel stack = new StackPanel
		{
			Margin = new Thickness(10.0)
		};
		bool isArtist = type == "Artist";
		Geometry clipRect = ((!isArtist) ? new RectangleGeometry
		{
			Rect = new Rect(0.0, 0.0, 128.0, 128.0),
			RadiusX = 8.0,
			RadiusY = 8.0
		} : new RectangleGeometry
		{
			Rect = new Rect(0.0, 0.0, 128.0, 128.0),
			RadiusX = 64.0,
			RadiusY = 64.0
		});
		Border imgBorder = new Border
		{
			Width = 128.0,
			Height = 128.0,
			Clip = clipRect,
			Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 25, 35))
		};
		imgBorder.Child = CreateImage(thumbUrl, 128, 128);
		imgBorder.CacheMode = new BitmapCache();
		ApplyImageOverlay(imgBorder);
		stack.Children.Add(imgBorder);
		stack.Children.Add(new TextBlock
		{
			Text = title,
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			FontSize = 13.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 10.0, 0.0, 0.0),
			TextTrimming = TextTrimming.CharacterEllipsis,
			TextAlignment = (isArtist ? TextAlignment.Center : TextAlignment.Left)
		});
		if (!isArtist)
		{
			if (!string.IsNullOrWhiteSpace(artist))
			{
				TextBlock artistWrap = new TextBlock
				{
					TextTrimming = TextTrimming.CharacterEllipsis,
					MaxHeight = 18.0,
					Margin = new Thickness(0.0, 0.0, 0.0, 0.0),
					Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"]
				};
				PopulateArtistLinks(artistWrap, artist, 11, artistsData);
				if (artistWrap.Inlines.Count > 0)
				{
					stack.Children.Add(artistWrap);
				}
			}
		}
		else
		{
			stack.Children.Add(new TextBlock
			{
				Text = "Artist",
				Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(150, 150, 150)),
				FontSize = 11.0,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
				Margin = new Thickness(0.0, 2.0, 0.0, 0.0)
			});
		}
		grid.Children.Add(stack);
		if (type == "Mix")
		{
			Border badge = new Border
			{
				Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 0, 0, 0)),
				CornerRadius = new CornerRadius(4.0),
				Padding = new Thickness(6.0, 2.0, 6.0, 2.0),
				HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Top,
				Margin = new Thickness(0.0, 15.0, 15.0, 0.0)
			};
			badge.Child = new TextBlock
			{
				Text = type,
				Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
				FontSize = 10.0,
				FontWeight = FontWeights.Bold
			};
			grid.Children.Add(badge);
		}
		border.Child = grid;
		border.MouseEnter += delegate
		{
			if ((string)border.Tag != _currentVideoId)
			{
				FadeBorderBackgroundToResource(border, "CardHoverBrush");
				FadeBorderBrushToColor(border, _hoverBorderColor);
			}
		};
		border.MouseLeave += delegate
		{
			if ((string)border.Tag != _currentVideoId)
			{
				FadeBorderBackgroundToResource(border, "CardBackground");
				FadeBorderBrushToColor(border, System.Windows.Media.Color.FromArgb(15, byte.MaxValue, byte.MaxValue, byte.MaxValue));
			}
		};
		border.MouseLeftButtonDown += async delegate(object s, MouseButtonEventArgs e)
		{
			e.Handled = true;
			if (type == "Artist")
			{
				OpenArtistPage(videoId, title, thumbUrl);
			}
			else if (type == "Album" || type == "Playlist" || type == "Mix" || type == "Single")
			{
				OpenPlaylistPage(videoId, title, artist, thumbUrl, type);
			}
			else
			{
				_currentQueue = null;
				Border loadingOverlay = CreateLoadingWaveOverlay(10.0);
				grid.Children.Insert(0, loadingOverlay);
				ShowLoadingOverlay(loadingOverlay);
				try
				{
					await PlayTrack(videoId, title, artist, thumbUrl, addToHistory: true, startPaused: false, useCrossfade: false, 0, artistsData, albumData);
				}
				finally
				{
					HideLoadingOverlay(loadingOverlay);
					await Task.Delay(250);
					grid.Children.Remove(loadingOverlay);
				}
			}
		};
		if (type == "Song" || string.IsNullOrEmpty(type))
		{
			string albumId = albumData?["id"]?.ToString() ?? "";
			string albumName = albumData?["name"]?.ToString() ?? "";
			border.ContextMenu = CreateSongContextMenu(videoId, "", "", null, hideLikedToggle: false, title, artist, thumbUrl, albumId, albumName);
		}
		else if (type == "Album" || type == "Playlist" || type == "Mix" || type == "Single")
		{
			border.ContextMenu = CreateCollectionContextMenu(videoId, title, type, border);
		}
		if (!string.IsNullOrEmpty(_currentVideoId) && videoId == _currentVideoId)
		{
			HighlightInPanel(border, _currentVideoId);
		}
		return new Viewbox
		{
			Stretch = Stretch.Uniform,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Child = border
		};
	}

	private UIElement CreateHeroHeader(List<(string videoId, string title, string artist, string thumbUrl, JsonArray? artistsData)> items)
	{
		if (items == null || items.Count == 0)
		{
			return new Grid();
		}
		Grid containerGrid = new Grid
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 25.0),
			Tag = "hero_header"
		};
		ContextMenu cm = new ContextMenu();
		MenuItem hideMi = new MenuItem
		{
			Header = "Hide Hero Banner"
		};
		hideMi.Click += delegate
		{
			if (!_blockedCategories.Contains("Hero Header"))
			{
				_blockedCategories.Add("Hero Header");
				SaveSession();
				containerGrid.Visibility = Visibility.Collapsed;
				containerGrid.Height = 0.0;
			}
		};
		cm.Items.Add(hideMi);
		containerGrid.ContextMenu = cm;
		containerGrid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(2.0, GridUnitType.Star)
		});
		if (items.Count > 1)
		{
			containerGrid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(1.0, GridUnitType.Star)
			});
		}
		Border leftHero = CreateHeroCard(items[0], isLarge: true);
		Grid.SetColumn(leftHero, 0);
		containerGrid.Children.Add(leftHero);
		if (items.Count > 1)
		{
			Grid rightGrid = new Grid
			{
				Margin = new Thickness(15.0, 0.0, 0.0, 0.0)
			};
			rightGrid.RowDefinitions.Add(new RowDefinition
			{
				Height = new GridLength(1.0, GridUnitType.Star)
			});
			if (items.Count > 2)
			{
				rightGrid.RowDefinitions.Add(new RowDefinition
				{
					Height = new GridLength(1.0, GridUnitType.Star)
				});
			}
			Border rightTop = CreateHeroCard(items[1], isLarge: false);
			Grid.SetRow(rightTop, 0);
			if (items.Count > 2)
			{
				rightTop.Margin = new Thickness(0.0, 0.0, 0.0, 7.5);
			}
			rightGrid.Children.Add(rightTop);
			if (items.Count > 2)
			{
				Border rightBottom = CreateHeroCard(items[2], isLarge: false);
				Grid.SetRow(rightBottom, 1);
				rightBottom.Margin = new Thickness(0.0, 7.5, 0.0, 0.0);
				rightGrid.Children.Add(rightBottom);
			}
			Grid.SetColumn(rightGrid, 1);
			containerGrid.Children.Add(rightGrid);
		}
		return containerGrid;
	}

	private Border CreateHeroCard((string videoId, string title, string artist, string thumbUrl, JsonArray? artistsData) data, bool isLarge)
	{
		Border border = new Border
		{
			CornerRadius = new CornerRadius(16.0),
			Height = (isLarge ? 320.0 : double.NaN),
			Cursor = System.Windows.Input.Cursors.Hand,
			Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 15, 20)),
			BorderThickness = new Thickness(1.0),
			BorderBrush = System.Windows.Media.Brushes.Transparent
		};
		Grid grid = new Grid();
		Border imgBorder = new Border
		{
			CornerRadius = new CornerRadius(15.0),
			Margin = new Thickness(1.0),
			CacheMode = new BitmapCache()
		};
		if (CreateImage(data.thumbUrl, isLarge ? 960 : 480, 0) is System.Windows.Shapes.Rectangle bgImg)
		{
			bgImg.Width = double.NaN;
			bgImg.RadiusX = 15.0;
			bgImg.RadiusY = 15.0;
			imgBorder.Child = bgImg;
			ApplyImageOverlay(imgBorder);
		}
		grid.Children.Add(imgBorder);
		Border overlay = new Border
		{
			CornerRadius = new CornerRadius(16.0)
		};
		LinearGradientBrush gradient = new LinearGradientBrush
		{
			StartPoint = new System.Windows.Point(0.0, 0.0),
			EndPoint = new System.Windows.Point(1.0, 0.0)
		};
		if (isLarge)
		{
			gradient.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(byte.MaxValue, 15, 15, 20), 0.0));
			gradient.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(200, 15, 15, 20), 0.4));
			gradient.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(80, 15, 15, 20), 1.0));
		}
		else
		{
			gradient = new LinearGradientBrush
			{
				StartPoint = new System.Windows.Point(0.0, 1.0),
				EndPoint = new System.Windows.Point(0.0, 0.0)
			};
			gradient.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(byte.MaxValue, 15, 15, 20), 0.0));
			gradient.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(80, 15, 15, 20), 1.0));
		}
		overlay.Background = gradient;
		grid.Children.Add(overlay);
		Border hoverOverlay = new Border
		{
			CornerRadius = new CornerRadius(16.0),
			Background = System.Windows.Media.Brushes.White,
			Opacity = 0.0,
			IsHitTestVisible = false
		};
		grid.Children.Add(hoverOverlay);
		StackPanel contentStack = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Bottom,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
			Margin = (isLarge ? new Thickness(40.0) : new Thickness(20.0))
		};
		TextBlock titleBlock = new TextBlock
		{
			Text = data.title,
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			FontSize = (isLarge ? 48 : 24),
			FontWeight = FontWeights.Black,
			Margin = new Thickness(0.0, 0.0, 0.0, 4.0),
			TextTrimming = TextTrimming.CharacterEllipsis,
			MaxWidth = (isLarge ? 800 : 350)
		};
		contentStack.Children.Add(titleBlock);
		TextBlock artistWrap = new TextBlock
		{
			TextTrimming = TextTrimming.CharacterEllipsis,
			Margin = (isLarge ? new Thickness(0.0, 0.0, 0.0, 24.0) : new Thickness(0.0, 0.0, 0.0, 10.0))
		};
		PopulateArtistLinks(artistWrap, data.artist, isLarge ? 18 : 14, data.artistsData);
		contentStack.Children.Add(artistWrap);
		if (isLarge)
		{
			System.Windows.Controls.Button playBtn = new System.Windows.Controls.Button
			{
				Content = "Play Now",
				Width = 140.0,
				Height = 44.0,
				FontSize = 16.0,
				FontWeight = FontWeights.Bold,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
				Cursor = System.Windows.Input.Cursors.Hand,
				Style = (Style)FindResource("AccentButtonStyle")
			};
			playBtn.Click += async delegate(object s, RoutedEventArgs e)
			{
				e.Handled = true;
				_currentQueue = null;
				await PlayTrack(data.videoId, data.title, data.artist, data.thumbUrl, addToHistory: true, startPaused: false, useCrossfade: false, 0, data.artistsData);
			};
			contentStack.Children.Add(playBtn);
		}
		border.MouseLeftButtonDown += async delegate(object s, MouseButtonEventArgs e)
		{
			e.Handled = true;
			_currentQueue = null;
			await PlayTrack(data.videoId, data.title, data.artist, data.thumbUrl, addToHistory: true, startPaused: false, useCrossfade: false, 0, data.artistsData);
		};
		grid.Children.Add(contentStack);
		border.Child = grid;
		border.MouseEnter += delegate
		{
			{
			}
			FadeBorderBrushToColor(border, _hoverBorderColor);
			hoverOverlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.03, TimeSpan.FromMilliseconds(250.0))
			{
				EasingFunction = new QuarticEase
				{
					EasingMode = EasingMode.EaseOut
				}
			});
		};
		border.MouseLeave += delegate
		{
			FadeBorderBrushToColor(border, Colors.Transparent);
			hoverOverlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(250.0))
			{
				EasingFunction = new QuarticEase
				{
					EasingMode = EasingMode.EaseOut
				}
			});
		};
		if (!string.IsNullOrEmpty(_currentVideoId) && data.videoId == _currentVideoId)
		{
			HighlightInPanel(border, _currentVideoId);
		}
		return border;
	}

	private Border CreateTrackTile(string id, string title, string artist, string thumbUrl, string type = "Song", JsonArray? artistsData = null)
	{
		Border border = new Border
		{
			Background = (System.Windows.Media.Brush)System.Windows.Application.Current.MainWindow.Resources["CardBackground"],
			BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(15, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(10.0),
			Height = 70.0,
			Margin = new Thickness(10.0),
			HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
			Cursor = System.Windows.Input.Cursors.Hand,
			Tag = id
		};
		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(70.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		bool isArtist = type == "Artist";
		RectangleGeometry clipRect = new RectangleGeometry
		{
			Rect = new Rect(0.0, 0.0, 50.0, 50.0),
			RadiusX = (isArtist ? 25 : 5),
			RadiusY = (isArtist ? 25 : 5)
		};
		Border imgBorder = new Border
		{
			Width = 50.0,
			Height = 50.0,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Clip = clipRect,
			Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 25, 35))
		};
		imgBorder.Child = CreateImage(thumbUrl, 50, 50);
		imgBorder.CacheMode = new BitmapCache();
		ApplyImageOverlay(imgBorder);
		Grid.SetColumn(imgBorder, 0);
		grid.Children.Add(imgBorder);
		StackPanel textStack = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 10.0, 0.0)
		};
		textStack.Children.Add(new TextBlock
		{
			Text = title,
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			FontSize = 14.0,
			FontWeight = FontWeights.Bold,
			TextTrimming = TextTrimming.CharacterEllipsis
		});
		if (isArtist)
		{
			textStack.Children.Add(new TextBlock
			{
				Text = "Artist",
				Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(150, 150, 150)),
				FontSize = 12.0
			});
		}
		else if (!string.IsNullOrWhiteSpace(artist))
		{
			TextBlock artistWrap = new TextBlock
			{
				TextTrimming = TextTrimming.CharacterEllipsis,
				MaxHeight = 18.0,
				Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"]
			};
			PopulateArtistLinks(artistWrap, artist, 12, artistsData);
			if (artistWrap.Inlines.Count > 0)
			{
				textStack.Children.Add(artistWrap);
			}
		}
		Grid.SetColumn(textStack, 1);
		grid.Children.Add(textStack);
		if (type == "Playlist" || type == "Mix")
		{
			Border badge = new Border
			{
				Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(50, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
				CornerRadius = new CornerRadius(4.0),
				Padding = new Thickness(6.0, 2.0, 6.0, 2.0),
				HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0.0, 0.0, 15.0, 0.0)
			};
			badge.Child = new TextBlock
			{
				Text = type,
				Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
				FontSize = 10.0,
				FontWeight = FontWeights.Bold
			};
			Grid.SetColumn(badge, 1);
			grid.Children.Add(badge);
		}
		border.Child = grid;
		border.MouseEnter += delegate
		{
			if ((string)border.Tag != _currentVideoId)
			{
				FadeBorderBackgroundToResource(border, "CardHoverBrush");
				FadeBorderBrushToColor(border, _hoverBorderColor);
			}
		};
		border.MouseLeave += delegate
		{
			if ((string)border.Tag != _currentVideoId)
			{
				FadeBorderBackgroundToResource(border, "CardBackground");
				FadeBorderBrushToColor(border, System.Windows.Media.Color.FromArgb(15, byte.MaxValue, byte.MaxValue, byte.MaxValue));
			}
		};
		border.MouseLeftButtonDown += async delegate(object s, MouseButtonEventArgs e)
		{
			e.Handled = true;
			if (type == "Artist")
			{
				OpenArtistPage(id, title, thumbUrl);
			}
			else if (type == "Album" || type == "Playlist" || type == "Mix")
			{
				OpenPlaylistPage(id, title, artist, thumbUrl, type);
			}
			else
			{
				_currentQueue = null;
				Border loadingOverlay = CreateLoadingWaveOverlay(10.0);
				Grid.SetColumnSpan(loadingOverlay, 2);
				grid.Children.Insert(0, loadingOverlay);
				ShowLoadingOverlay(loadingOverlay);
				try
				{
					await PlayTrack(id, title, artist, thumbUrl, addToHistory: true, startPaused: false, useCrossfade: false, 0, artistsData);
				}
				finally
				{
					HideLoadingOverlay(loadingOverlay);
					await Task.Delay(250);
					grid.Children.Remove(loadingOverlay);
				}
			}
		};
		border.MouseRightButtonUp += delegate(object s, MouseButtonEventArgs e)
		{
			e.Handled = true;
			if (border.ContextMenu == null)
			{
				if (type == "Song" || string.IsNullOrEmpty(type))
				{
					border.ContextMenu = CreateSongContextMenu(id, "", "", null, hideLikedToggle: false, title, artist, thumbUrl);
				}
				else if (type == "Album" || type == "Playlist" || type == "Mix")
				{
					border.ContextMenu = CreateCollectionContextMenu(id, title, type, border);
				}
			}
			if (border.ContextMenu != null)
			{
				border.ContextMenu.IsOpen = true;
			}
		};
		if (!string.IsNullOrEmpty(_currentVideoId) && id == _currentVideoId)
		{
			HighlightInPanel(border, _currentVideoId);
		}
		return border;
	}

	private Border CreateTrackRow(string videoId, string title, string artist, string thumbUrl, string trackNumber = "", string album = "", string albumId = "", string duration = "", string contextPlaylistId = "", JsonArray? queueContext = null, int queueIndex = -1, string setVideoId = "", bool hideLikedToggle = false, Action onPlay = null, JsonArray? artistsData = null, string displayAlbum = null)
	{
		Border border = new Border
		{
			Background = (System.Windows.Media.Brush)System.Windows.Application.Current.MainWindow.Resources["CardBackground"],
			BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(15, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(10.0),
			Height = 60.0,
			Margin = new Thickness(0.0, 5.0, 0.0, 5.0),
			HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
			Cursor = System.Windows.Input.Cursors.Hand,
			Tag = videoId,
			Opacity = 0.0
		};
		border.Loaded += delegate
		{
			DoubleAnimation anim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200.0))
			{
				EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
			};
			border.BeginAnimation(UIElement.OpacityProperty, anim);
		};
		Grid grid = new Grid();
		int currentColumn = 0;
		if (!string.IsNullOrEmpty(trackNumber))
		{
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(48.0)
			});
			TextBlock numTxt = new TextBlock
			{
				Text = trackNumber,
				Foreground = System.Windows.Media.Brushes.Gray,
				FontSize = 14.0,
				FontWeight = FontWeights.Bold,
				VerticalAlignment = VerticalAlignment.Center,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
				TextAlignment = TextAlignment.Center
			};
			Grid.SetColumn(numTxt, currentColumn);
			grid.Children.Add(numTxt);
			currentColumn++;
		}
		int imgCol = currentColumn;
		if (!string.IsNullOrEmpty(trackNumber))
		{
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(52.0)
			});
		}
		else
		{
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(60.0)
			});
		}
		currentColumn++;
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(4.0, GridUnitType.Star)
		});
		int titleCol = currentColumn;
		currentColumn++;
		int albumCol = -1;
		if (!string.IsNullOrEmpty(album))
		{
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(3.0, GridUnitType.Star)
			});
			albumCol = currentColumn;
			currentColumn++;
		}
		int durCol = -1;
		if (!string.IsNullOrEmpty(duration))
		{
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(60.0)
			});
			durCol = currentColumn;
			currentColumn++;
		}
		RectangleGeometry clipRect = new RectangleGeometry
		{
			Rect = new Rect(0.0, 0.0, 44.0, 44.0),
			RadiusX = 4.0,
			RadiusY = 4.0
		};
		Border imgBorder = new Border
		{
			Width = 44.0,
			Height = 44.0,
			HorizontalAlignment = (string.IsNullOrEmpty(trackNumber) ? System.Windows.HorizontalAlignment.Center : System.Windows.HorizontalAlignment.Left),
			VerticalAlignment = VerticalAlignment.Center,
			Clip = clipRect,
			Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 25, 35))
		};
		imgBorder.Child = CreateImage(thumbUrl, 44, 44);
		imgBorder.CacheMode = new BitmapCache();
		ApplyImageOverlay(imgBorder);
		Grid.SetColumn(imgBorder, imgCol);
		grid.Children.Add(imgBorder);
		StackPanel textStack = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(10.0, 0.0, 0.0, 0.0)
		};
		textStack.Children.Add(new TextBlock
		{
			Text = title,
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			FontSize = 14.0,
			FontWeight = FontWeights.Bold,
			TextTrimming = TextTrimming.CharacterEllipsis
		});
		TextBlock artistWrap = new TextBlock
		{
			TextTrimming = TextTrimming.CharacterEllipsis,
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"]
		};
		PopulateArtistLinks(artistWrap, artist, 12, artistsData);
		textStack.Children.Add(artistWrap);
		Grid.SetColumn(textStack, titleCol);
		grid.Children.Add(textStack);
		if (albumCol != -1)
		{
			TextBlock albumText = new TextBlock
			{
				Text = (displayAlbum ?? album),
				Foreground = System.Windows.Media.Brushes.Gray,
				FontSize = 13.0,
				VerticalAlignment = VerticalAlignment.Center,
				TextTrimming = TextTrimming.CharacterEllipsis,
				Margin = new Thickness(10.0, 0.0, 10.0, 0.0)
			};
			if (string.IsNullOrEmpty(displayAlbum) && !string.IsNullOrEmpty(albumId))
			{
				albumText.Cursor = System.Windows.Input.Cursors.Hand;
				albumText.MouseEnter += delegate
				{
					FadeTextForegroundToColor(albumText, Colors.LightGray);
				};
				albumText.MouseLeave += delegate
				{
					FadeTextForegroundToColor(albumText, Colors.Gray);
				};
				albumText.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
				{
					e.Handled = true;
					OpenPlaylistPage(albumId, album, "", "", "Album");
				};
			}
			Grid.SetColumn(albumText, albumCol);
			grid.Children.Add(albumText);
		}
		if (durCol != -1)
		{
			TextBlock durText = new TextBlock
			{
				Text = duration,
				Foreground = System.Windows.Media.Brushes.Gray,
				FontSize = 13.0,
				VerticalAlignment = VerticalAlignment.Center,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
				Margin = new Thickness(0.0, 0.0, 10.0, 0.0)
			};
			Grid.SetColumn(durText, durCol);
			grid.Children.Add(durText);
		}
		border.MouseEnter += delegate
		{
			if ((string)border.Tag != _currentVideoId)
			{
				FadeBorderBackgroundToResource(border, "CardHoverBrush");
				FadeBorderBrushToColor(border, _hoverBorderColor);
			}
		};
		border.MouseLeave += delegate
		{
			if ((string)border.Tag != _currentVideoId)
			{
				FadeBorderBackgroundToResource(border, "CardBackground");
				FadeBorderBrushToColor(border, System.Windows.Media.Color.FromArgb(15, byte.MaxValue, byte.MaxValue, byte.MaxValue));
			}
		};
		border.MouseLeftButtonDown += async delegate(object s, MouseButtonEventArgs e)
		{
			e.Handled = true;
			if (queueContext != null && queueIndex >= 0)
			{
				InitQueueAndShuffle((System.Text.Json.Nodes.JsonArray)queueContext.DeepClone(), queueIndex);
			}
			onPlay?.Invoke();
			Border loadingOverlay = CreateLoadingWaveOverlay(10.0);
			Grid.SetColumnSpan(loadingOverlay, (grid.ColumnDefinitions.Count <= 0) ? 1 : grid.ColumnDefinitions.Count);
			grid.Children.Insert(0, loadingOverlay);
			ShowLoadingOverlay(loadingOverlay);
			JsonObject albumData = new JsonObject
			{
				["name"] = album,
				["id"] = albumId
			};
			try
			{
				await PlayTrack(videoId, title, artist, thumbUrl, addToHistory: true, startPaused: false, useCrossfade: false, 0, artistsData, albumData);
			}
			finally
			{
				HideLoadingOverlay(loadingOverlay);
				await Task.Delay(250);
				grid.Children.Remove(loadingOverlay);
			}
		};
		border.Child = grid;
		border.MouseRightButtonUp += delegate(object s, MouseButtonEventArgs e)
		{
			e.Handled = true;
			if (border.ContextMenu == null)
			{
				border.ContextMenu = CreateSongContextMenu(videoId, contextPlaylistId, setVideoId, border, hideLikedToggle, title, artist, thumbUrl, albumId, album);
			}
			border.ContextMenu.IsOpen = true;
		};
		if (!string.IsNullOrEmpty(_currentVideoId) && videoId == _currentVideoId)
		{
			HighlightInPanel(border, _currentVideoId);
		}
		return border;
	}

	private UIElement CreateListHeader(bool hasNumber, bool hasAlbum, bool hasDuration, string albumHeaderText = "Album")
	{
		Grid grid = new Grid
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 15.0)
		};
		int currentColumn = 0;
		if (hasNumber)
		{
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(48.0)
			});
			TextBlock numTxt = new TextBlock
			{
				Text = "#",
				Foreground = System.Windows.Media.Brushes.Gray,
				FontSize = 13.0,
				FontWeight = FontWeights.Bold,
				VerticalAlignment = VerticalAlignment.Center,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
				TextAlignment = TextAlignment.Center
			};
			Grid.SetColumn(numTxt, currentColumn);
			grid.Children.Add(numTxt);
			currentColumn++;
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(52.0)
			});
		}
		else
		{
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(60.0)
			});
		}
		currentColumn++;
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(4.0, GridUnitType.Star)
		});
		TextBlock titleTxt = new TextBlock
		{
			Text = "Title",
			Foreground = System.Windows.Media.Brushes.Gray,
			FontSize = 13.0,
			FontWeight = FontWeights.SemiBold,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(10.0, 0.0, 0.0, 0.0)
		};
		Grid.SetColumn(titleTxt, currentColumn);
		grid.Children.Add(titleTxt);
		currentColumn++;
		if (hasAlbum)
		{
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(3.0, GridUnitType.Star)
			});
			TextBlock albumTxt = new TextBlock
			{
				Text = albumHeaderText,
				Foreground = System.Windows.Media.Brushes.Gray,
				FontSize = 13.0,
				FontWeight = FontWeights.SemiBold,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(10.0, 0.0, 10.0, 0.0)
			};
			Grid.SetColumn(albumTxt, currentColumn);
			grid.Children.Add(albumTxt);
			currentColumn++;
		}
		if (hasDuration)
		{
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(60.0)
			});
			System.Windows.Controls.Image durIcon = new System.Windows.Controls.Image
			{
				Source = (System.Windows.Media.DrawingImage)System.Windows.Application.Current.FindResource("clockIcon"),
				Width = 14.0,
				Height = 14.0,
				VerticalAlignment = VerticalAlignment.Center,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
				Margin = new Thickness(0.0, 0.0, 10.0, 0.0),
				Opacity = 0.5
			};
			Grid.SetColumn(durIcon, currentColumn);
			grid.Children.Add(durIcon);
			currentColumn++;
		}
		return new Border
		{
			BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			BorderThickness = new Thickness(0.0, 0.0, 0.0, 1.0),
			Padding = new Thickness(0.0, 0.0, 0.0, 10.0),
			Child = grid
		};
	}

	private ContextMenu CreateSongContextMenu(string videoId, string contextPlaylistId = "", string setVideoId = "", Border? rowBorder = null, bool hideLikedToggle = false, string title = "", string artist = "", string thumbUrl = "", string albumId = "", string albumName = "")
	{
		ContextMenu ctx = new ContextMenu();
		if (System.Windows.Application.Current.MainWindow.TryFindResource(typeof(ContextMenu)) is Style style)
		{
			ctx.Style = style;
		}
		if (System.Windows.Application.Current.MainWindow.TryFindResource(typeof(MenuItem)) is Style itemStyle)
		{
			ctx.ItemContainerStyle = itemStyle;
		}
		if (rowBorder != null)
		{
			ctx.PlacementTarget = rowBorder;
		}
		if (!string.IsNullOrEmpty(title))
		{
			MenuItem playNextMenu = new MenuItem
			{
				Header = "Play next"
			};
			playNextMenu.Click += delegate
			{
				if (_currentQueue == null || _currentQueue.Count == 0)
				{
					_currentQueue = new JsonArray();
				}
				JsonObject item = new JsonObject
				{
					["videoId"] = videoId,
					["title"] = title,
					["artist"] = artist,
					["thumbUrl"] = thumbUrl
				};
				if (_currentQueueIndex >= 0 && _currentQueueIndex < _currentQueue.Count)
				{
					_currentQueue.Insert(_currentQueueIndex + 1, item);
				}
				else
				{
					_currentQueue.Add(item);
				}
				if (_isQueueOpen)
				{
					RenderQueueSidebar();
				}
				ShowToast("Will play next");
			};
			ctx.Items.Add(playNextMenu);
			MenuItem addToQueueMenu = new MenuItem
			{
				Header = "Add to queue"
			};
			addToQueueMenu.Click += delegate
			{
				if (_currentQueue == null || _currentQueue.Count == 0)
				{
					_currentQueue = new JsonArray();
				}
				JsonObject item = new JsonObject
				{
					["videoId"] = videoId,
					["title"] = title,
					["artist"] = artist,
					["thumbUrl"] = thumbUrl
				};
				_currentQueue.Add(item);
				if (_isQueueOpen)
				{
					RenderQueueSidebar();
				}
				ShowToast("Added to queue");
			};
			ctx.Items.Add(addToQueueMenu);
		}
		if (!hideLikedToggle)
		{
			MenuItem likeMenu = new MenuItem();
			ctx.Opened += async delegate
			{
				if (!_likedSongsLoaded)
				{
					await LoadLikedSongsAsync();
				}
				UpdateLikedMenuLabel(likeMenu, videoId);
			};
			likeMenu.Click += async delegate
			{
				try
				{
					bool isLiked = _likedVideoIds.Contains(videoId);
					await BackendService.Instance.RateSongAsync(videoId, isLiked ? "INDIFFERENT" : "LIKE", CancellationToken.None);
					if (isLiked)
					{
						_likedVideoIds.Remove(videoId);
						MainTopbarControl.StatusLabelRef.Text = "Removed from liked songs";
					}
					else
					{
						_likedVideoIds.Add(videoId);
						MainTopbarControl.StatusLabelRef.Text = "Saved to liked songs";
					}
					_likedSongsLoaded = true;
					UpdateLikedMenuLabel(likeMenu, videoId);
				}
				catch
				{
					MainTopbarControl.StatusLabelRef.Text = "Failed to like song";
				}
			};
			UpdateLikedMenuLabel(likeMenu, videoId);
			ctx.Items.Add(likeMenu);
		}
		MenuItem downloadMenu = new MenuItem
		{
			Header = "Download Song"
		};
		ctx.Opened += delegate
		{
			downloadMenu.Visibility = ((!_enableDownloads) ? Visibility.Collapsed : Visibility.Visible);
		};
		MenuItem opusMenu = new MenuItem
		{
			Header = "Opus (Best Quality)"
		};
		MenuItem m4aMenu = new MenuItem
		{
			Header = "M4A (Most Compatible)"
		};
		MenuItem webmMenu = new MenuItem
		{
			Header = "WebM (Original)"
		};
		RoutedEventHandler downloadHandler = async delegate(object sender, RoutedEventArgs e)
		{
			MenuItem menuItem = sender as MenuItem;
			string format = "opus";
			if (menuItem == m4aMenu)
			{
				format = "m4a";
			}
			else if (menuItem == webmMenu)
			{
				format = "webm";
			}
			if (string.IsNullOrEmpty(_downloadsPath))
			{
				ShowToast("Please select a download folder in settings first.");
				return;
			}
			try
			{
				ShowToast("Downloading song (" + format.ToUpper() + ")...", 5);
				await BackendService.Instance.DownloadSongAsync(videoId, _downloadsPath, format, CancellationToken.None, title, artist, thumbUrl);
				ShowToast("Download complete!");
			}
			catch (Exception ex)
			{
				ShowToast("Download failed: " + ex.Message);
			}
		};
		opusMenu.Click += downloadHandler;
		m4aMenu.Click += downloadHandler;
		webmMenu.Click += downloadHandler;
		downloadMenu.Items.Add(opusMenu);
		downloadMenu.Items.Add(m4aMenu);
		downloadMenu.Items.Add(webmMenu);
		ctx.Items.Add(downloadMenu);
		MenuItem addMenu = new MenuItem
		{
			Header = "Add to playlist"
		};
		addMenu.SubmenuOpened += delegate
		{
			PopulatePlaylistSubmenu(addMenu, videoId);
		};
		PopulatePlaylistSubmenu(addMenu, videoId);
		ctx.Items.Add(addMenu);
		if (!string.IsNullOrEmpty(contextPlaylistId))
		{
			MenuItem removeMenu = new MenuItem
			{
				Header = "Remove from playlist"
			};
			removeMenu.Click += async delegate
			{
				try
				{
					await BackendService.Instance.RemovePlaylistItemAsync(contextPlaylistId, videoId, CancellationToken.None, setVideoId);
					MainTopbarControl.StatusLabelRef.Text = "Removed from playlist";
					if (rowBorder != null)
					{
						rowBorder.Visibility = Visibility.Collapsed;
					}
				}
				catch
				{
					MainTopbarControl.StatusLabelRef.Text = "Failed to remove from playlist";
				}
			};
			ctx.Items.Add(removeMenu);
		}
		MenuItem creditsMenu = new MenuItem
		{
			Header = "View song credits"
		};
		creditsMenu.Click += delegate
		{
			_ = _ = _ = _ = ShowCreditsAsync(videoId);
		};
		ctx.Items.Add(creditsMenu);
		return ctx;
	}

	private ContextMenu CreateCollectionContextMenu(string collectionId, string title, string type, Border ownerBorder, TextBlock? titleText = null)
	{
		ContextMenu ctx = new ContextMenu();
		if (System.Windows.Application.Current.MainWindow.TryFindResource(typeof(ContextMenu)) is Style style)
		{
			ctx.Style = style;
		}
		if (System.Windows.Application.Current.MainWindow.TryFindResource(typeof(MenuItem)) is Style itemStyle)
		{
			ctx.ItemContainerStyle = itemStyle;
		}
		if (ownerBorder != null)
		{
			ctx.PlacementTarget = ownerBorder;
		}
		MenuItem actionMenu = new MenuItem();
		ctx.Opened += delegate
		{
			if (type == "Album")
			{
				bool flag = _savedAlbumIds.Contains(collectionId);
				actionMenu.Header = (flag ? "Unsave album" : "Save album");
			}
			else
			{
				actionMenu.Header = "Delete playlist";
			}
		};
		actionMenu.Click += async delegate
		{
			_ = 3;
			try
			{
				bool isSaved = !(type == "Album") || _savedAlbumIds.Contains(collectionId);
				if (type == "Album" && !isSaved)
				{
					await BackendService.Instance.RatePlaylistAsync(collectionId, "LIKE", CancellationToken.None);
					MainTopbarControl.StatusLabelRef.Text = "Album saved";
					_savedAlbumIds.Add(collectionId);
				}
				else
				{
					if (type == "Album")
					{
						await BackendService.Instance.RatePlaylistAsync(collectionId, "INDIFFERENT", CancellationToken.None);
						MainTopbarControl.StatusLabelRef.Text = "Album unsaved";
						_savedAlbumIds.Remove(collectionId);
					}
					else
					{
						await BackendService.Instance.DeletePlaylistAsync(collectionId, CancellationToken.None);
						MainTopbarControl.StatusLabelRef.Text = "Playlist deleted";
					}
					if (MainSidebar.LibraryPanelRef.Items.Contains(ownerBorder))
					{
						Storyboard storyboard = new Storyboard();
						DoubleAnimation fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(300.0));
						Storyboard.SetTarget(fadeOut, ownerBorder);
						Storyboard.SetTargetProperty(fadeOut, new PropertyPath(UIElement.OpacityProperty));
						storyboard.Children.Add(fadeOut);
						storyboard.Completed += delegate
						{
							if (MainSidebar.LibraryPanelRef.Items.Contains(ownerBorder))
							{
								MainSidebar.LibraryPanelRef.Items.Remove(ownerBorder);
							}
						};
						storyboard.Begin();
					}
				}
				_pageCache.Remove(collectionId);
				await Task.Delay(1500);
				_ = _ = _ = _ = LoadLibraryAsync();
			}
			catch
			{
				bool isSaved2 = !(type == "Album") || _savedAlbumIds.Contains(collectionId);
				MainTopbarControl.StatusLabelRef.Text = ((!(type == "Album")) ? "Failed to remove playlist" : (isSaved2 ? "Failed to unsave album" : "Failed to save album"));
			}
		};
		ctx.Items.Add(actionMenu);
		MenuItem hideMenu = new MenuItem
		{
			Header = "Hide"
		};
		hideMenu.Click += delegate
		{
			if (!_hiddenLibraryItems.ContainsKey(collectionId))
			{
				_hiddenLibraryItems[collectionId] = title;
				SaveSession();
			}
			Storyboard storyboard = new Storyboard();
			DoubleAnimation doubleAnimation = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(300.0));
			Storyboard.SetTarget(doubleAnimation, ownerBorder);
			Storyboard.SetTargetProperty(doubleAnimation, new PropertyPath(UIElement.OpacityProperty));
			storyboard.Children.Add(doubleAnimation);
			if (MainSidebar.LibraryPanelRef.Items.Contains(ownerBorder))
			{
				DoubleAnimation doubleAnimation2 = new DoubleAnimation(ownerBorder.ActualHeight, 0.0, TimeSpan.FromMilliseconds(300.0))
				{
					EasingFunction = new QuadraticEase
					{
						EasingMode = EasingMode.EaseInOut
					}
				};
				Storyboard.SetTarget(doubleAnimation2, ownerBorder);
				Storyboard.SetTargetProperty(doubleAnimation2, new PropertyPath(FrameworkElement.HeightProperty));
				storyboard.Children.Add(doubleAnimation2);
			}
			storyboard.Completed += delegate
			{
				if (MainSidebar.LibraryPanelRef.Items.Contains(ownerBorder))
				{
					MainSidebar.LibraryPanelRef.Items.Remove(ownerBorder);
				}
				else if (ownerBorder.Parent is System.Windows.Controls.Panel panel)
				{
					panel.Children.Remove(ownerBorder);
				}
			};
			storyboard.Begin();
		};
		ctx.Items.Add(hideMenu);
		if (type == "Playlist")
		{
			MenuItem renameMenu = new MenuItem
			{
				Header = "Rename playlist"
			};
			renameMenu.Click += delegate
			{
				StackPanel parentPanel;
				System.Windows.Controls.TextBox textBox;
				bool completed;
				if (titleText != null)
				{
					DependencyObject parent = titleText.Parent;
					parentPanel = parent as StackPanel;
					if (parentPanel != null)
					{
						textBox = new System.Windows.Controls.TextBox
						{
							Text = title,
							Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
							Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
							BorderThickness = new Thickness(0.0),
							Padding = new Thickness(4.0, 2.0, 4.0, 2.0),
							Margin = new Thickness(0.0, 0.0, 0.0, 0.0),
							FontSize = titleText.FontSize,
							FontWeight = titleText.FontWeight,
							VerticalAlignment = VerticalAlignment.Center,
							CaretBrush = System.Windows.Media.Brushes.White
						};
						textBox.Style = null;
						int num = parentPanel.Children.IndexOf(titleText);
						if (num != -1)
						{
							parentPanel.Children.Insert(num, textBox);
							titleText.Visibility = Visibility.Collapsed;
							textBox.SelectAll();
							textBox.Focus();
							textBox.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs ev)
							{
								ev.Handled = true;
							};
							textBox.PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs ev)
							{
								ev.Handled = true;
							};
							completed = false;
							textBox.KeyDown += delegate(object s, System.Windows.Input.KeyEventArgs ev)
							{
								if (ev.Key == Key.Return)
								{
									ev.Handled = true;
									FinishRename();
								}
								else if (ev.Key == Key.Escape)
								{
									ev.Handled = true;
									parentPanel.Children.Remove(textBox);
									titleText.Visibility = Visibility.Visible;
									completed = true;
								}
							};
							textBox.LostFocus += delegate
							{
								FinishRename();
							};
						}
					}
				}
				async void FinishRename()
				{
					if (!completed)
					{
						completed = true;
						string newTitle = textBox.Text.Trim();
						parentPanel.Children.Remove(textBox);
						titleText.Visibility = Visibility.Visible;
						if (!string.IsNullOrWhiteSpace(newTitle) && !(newTitle == title))
						{
							try
							{
								titleText.Text = newTitle;
								await BackendService.Instance.RenamePlaylistAsync(collectionId, newTitle, CancellationToken.None);
								MainTopbarControl.StatusLabelRef.Text = "Playlist renamed";
								_pageCache.Remove(collectionId);
								_ = _ = _ = _ = LoadLibraryAsync();
							}
							catch
							{
								MainTopbarControl.StatusLabelRef.Text = "Failed to rename playlist";
								titleText.Text = title;
							}
						}
					}
				}
			};
			ctx.Items.Add(renameMenu);
		}
		return ctx;
	}

	private UIElement CreateExpandableAboutSection(string description)
	{
		StackPanel obj = new StackPanel
		{
			Margin = new Thickness(0.0, 25.0, 0.0, 30.0),
			Children = { (UIElement)new TextBlock
			{
				Text = "About",
				Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
				FontSize = 24.0,
				FontWeight = FontWeights.Bold,
				Margin = new Thickness(0.0, 0.0, 0.0, 15.0)
			} }
		};
		TextBlock descBlock = new TextBlock
		{
			Text = description,
			Foreground = System.Windows.Media.Brushes.LightGray,
			FontSize = 14.0,
			TextWrapping = TextWrapping.Wrap
		};
		Border innerWrapper = new Border
		{
			Child = descBlock,
			ClipToBounds = true
		};
		GradientStop endStop = new GradientStop(System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 1.0);
		LinearGradientBrush fadeMask = new LinearGradientBrush
		{
			StartPoint = new System.Windows.Point(0.0, 0.0),
			EndPoint = new System.Windows.Point(0.0, 1.0)
		};
		fadeMask.GradientStops.Add(new GradientStop(Colors.Black, 0.0));
		fadeMask.GradientStops.Add(new GradientStop(Colors.Black, 0.6));
		fadeMask.GradientStops.Add(endStop);
		Border descBorder = new Border
		{
			Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(15, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			CornerRadius = new CornerRadius(10.0),
			Padding = new Thickness(15.0),
			Cursor = System.Windows.Input.Cursors.Arrow,
			Child = innerWrapper,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
		};
		bool isExpanded = false;
		bool isAnimating = false;
		bool canExpand = false;
		double expandedHeight = 0.0;
		descBorder.Loaded += delegate
		{
			UpdateAboutSizing();
		};
		descBorder.SizeChanged += delegate
		{
			UpdateAboutSizing();
		};
		descBorder.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
		{
			e.Handled = true;
			if (!isAnimating && canExpand)
			{
				UpdateAboutSizing();
				isExpanded = !isExpanded;
				isAnimating = true;
				ColorAnimation animation = new ColorAnimation
				{
					To = (isExpanded ? Colors.Black : System.Windows.Media.Color.FromArgb(0, 0, 0, 0)),
					Duration = TimeSpan.FromMilliseconds(500.0),
					EasingFunction = new QuarticEase
					{
						EasingMode = EasingMode.EaseInOut
					}
				};
				endStop.BeginAnimation(GradientStop.ColorProperty, animation);
				double actualHeight = innerWrapper.ActualHeight;
				DoubleAnimation doubleAnimation = new DoubleAnimation
				{
					From = actualHeight,
					To = (isExpanded ? expandedHeight : 100.0),
					Duration = TimeSpan.FromMilliseconds(500.0),
					EasingFunction = new QuarticEase
					{
						EasingMode = EasingMode.EaseInOut
					}
				};
				doubleAnimation.Completed += delegate
				{
					innerWrapper.BeginAnimation(FrameworkElement.HeightProperty, null);
					innerWrapper.Height = (isExpanded ? double.NaN : 100.0);
					isAnimating = false;
				};
				innerWrapper.BeginAnimation(FrameworkElement.HeightProperty, doubleAnimation);
			}
		};
		obj.Children.Add(descBorder);
		return obj;
		void UpdateAboutSizing()
		{
			if (!(isExpanded || isAnimating))
			{
				double textWidth = Math.Max(0.0, descBorder.ActualWidth - descBorder.Padding.Left - descBorder.Padding.Right);
				if (!(textWidth <= 0.0))
				{
					descBlock.Measure(new System.Windows.Size(textWidth, double.PositiveInfinity));
					expandedHeight = Math.Ceiling(descBlock.DesiredSize.Height);
					canExpand = expandedHeight > 101.0;
					innerWrapper.BeginAnimation(FrameworkElement.HeightProperty, null);
					innerWrapper.Height = (canExpand ? 100.0 : double.NaN);
					innerWrapper.OpacityMask = (canExpand ? fadeMask : null);
					descBorder.Cursor = (canExpand ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow);
				}
			}
		}
	}

	private void ShowLoadingOverlay(UIElement overlay)
	{
		overlay.Opacity = 0.0;
		overlay.Visibility = Visibility.Visible;
		DoubleAnimation anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(250.0));
		overlay.BeginAnimation(UIElement.OpacityProperty, anim);
		if (overlay is Border { Child: System.Windows.Shapes.Rectangle { Fill: LinearGradientBrush { RelativeTransform: TranslateTransform tt } } })
		{
			DoubleAnimation waveAnim = new DoubleAnimation
			{
				From = -1.0,
				To = 1.0,
				Duration = TimeSpan.FromSeconds(1.5),
				RepeatBehavior = RepeatBehavior.Forever
			};
			tt.BeginAnimation(TranslateTransform.XProperty, waveAnim);
		}
	}

	private void HideLoadingOverlay(UIElement overlay)
	{
		DoubleAnimation anim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(250.0));
		anim.Completed += delegate
		{
			overlay.Visibility = Visibility.Collapsed;
			if (overlay is Border { Child: System.Windows.Shapes.Rectangle { Fill: LinearGradientBrush { RelativeTransform: TranslateTransform relativeTransform } } })
			{
				relativeTransform.BeginAnimation(TranslateTransform.XProperty, null);
			}
		};
		overlay.BeginAnimation(UIElement.OpacityProperty, anim);
	}

	private Geometry CreateLeafGeometry(double width, double height, double largeRadius, double smallRadius)
	{
		StreamGeometry geom = new StreamGeometry();
		using (StreamGeometryContext ctx = geom.Open())
		{
			ctx.BeginFigure(new System.Windows.Point(largeRadius, 0.0), isFilled: true, isClosed: true);
			ctx.LineTo(new System.Windows.Point(width - smallRadius, 0.0), isStroked: true, isSmoothJoin: true);
			ctx.ArcTo(new System.Windows.Point(width, smallRadius), new System.Windows.Size(smallRadius, smallRadius), 0.0, isLargeArc: false, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);
			ctx.LineTo(new System.Windows.Point(width, height - largeRadius), isStroked: true, isSmoothJoin: true);
			ctx.ArcTo(new System.Windows.Point(width - largeRadius, height), new System.Windows.Size(largeRadius, largeRadius), 0.0, isLargeArc: false, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);
			ctx.LineTo(new System.Windows.Point(smallRadius, height), isStroked: true, isSmoothJoin: true);
			ctx.ArcTo(new System.Windows.Point(0.0, height - smallRadius), new System.Windows.Size(smallRadius, smallRadius), 0.0, isLargeArc: false, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);
			ctx.LineTo(new System.Windows.Point(0.0, largeRadius), isStroked: true, isSmoothJoin: true);
			ctx.ArcTo(new System.Windows.Point(largeRadius, 0.0), new System.Windows.Size(largeRadius, largeRadius), 0.0, isLargeArc: false, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);
		}
		geom.Freeze();
		return geom;
	}

	private void MinBtn_Click(object sender, RoutedEventArgs e)
	{
		SendSystemWindowCommand(61472);
	}

	private void MaxBtn_Click(object sender, RoutedEventArgs e)
	{
		if (base.WindowState == WindowState.Maximized)
		{
			SendSystemWindowCommand(61728);
		}
		else
		{
			SendSystemWindowCommand(61488);
		}
	}

	private void CloseBtn_Click(object sender, RoutedEventArgs e)
	{
		SendSystemWindowCommand(61536);
	}

	private void SendSystemWindowCommand(int command)
	{
		nint hwnd = new WindowInteropHelper(this).Handle;
		if (hwnd != IntPtr.Zero)
		{
			SendMessage(hwnd, 274, new IntPtr(command), IntPtr.Zero);
		}
	}

	[DllImport("gdi32.dll")]
	private static extern nint CreateSolidBrush(int crColor);

	private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
	{
		switch (msg)
		{
		case 20:
			handled = true;
			return new IntPtr(1);
		case 132:
		{
			try
			{
				int lParamInt = unchecked((int)lParam.ToInt64());
				int x = (short)(lParamInt & 0xFFFF);
				int y = (short)((lParamInt >> 16) & 0xFFFF);
				System.Windows.Point screenPoint = new System.Windows.Point(x, y);
				System.Windows.Point clientPoint = PointFromScreen(screenPoint);

				if (clientPoint.Y <= 60 && clientPoint.Y >= 6 && clientPoint.X >= 6 && clientPoint.X <= ActualWidth - 6)
				{
					IInputElement hit = InputHitTest(clientPoint);
					if (hit != null && hit is DependencyObject hitObject)
					{
						DependencyObject parent = hitObject;
						while (parent != null)
						{
							if ((bool)parent.GetValue(System.Windows.Shell.WindowChrome.IsHitTestVisibleInChromeProperty))
							{
								handled = true;
								return new IntPtr(1); // HTCLIENT
							}
							parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
						}
					}
					
					// If we didn't hit an interactive element, allow dragging
					handled = true;
					return new IntPtr(2); // HTCAPTION
				}
			}
			catch { }
			break;
		}
		}
		if (msg == 532 && !_isWindowResizing)
		{
			_isWindowResizing = true;
			MainScrollViewer.Width = MainScrollViewer.ActualWidth;
			DoubleAnimation fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120.0));
			MainScrollViewer.BeginAnimation(UIElement.OpacityProperty, fadeOut);
		}
		else if (msg == 562)
		{
			if (_isWindowResizing)
			{
				_isWindowResizing = false;
				MainScrollViewer.Width = double.NaN;
				DoubleAnimation fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200.0));
				MainScrollViewer.BeginAnimation(UIElement.OpacityProperty, fadeIn);
			}
		}
		if (msg == 793 && base.IsActive)
		{
			switch ((int)(((long)lParam >> 16) & 0xFFF))
			{
			case 14:
				PlayPauseBtn_Click(null, null);
				handled = true;
				break;
			case 11:
				NextBtn_Click(null, null);
				handled = true;
				break;
			case 12:
				PrevBtn_Click(null, null);
				handled = true;
				break;
			}
		}
		return IntPtr.Zero;
	}

	private void UpdateSidebarTextOpacity(double opacity)
	{
		MainSidebar.SidebarTitleTextRef.BeginAnimation(UIElement.OpacityProperty, null);
		MainSidebar.SidebarTitleTextRef.Opacity = opacity;
		foreach (object child2 in MainSidebar.HomeNavPanelRef.Children)
		{
			if (child2 is SidebarTab tab)
			{
				tab.SetTextOpacity(opacity);
			}
		}
		foreach (object child in (IEnumerable)MainSidebar.LibraryPanelRef.Items)
		{
			if (child is Grid headerGrid && headerGrid.Children.Count == 2 && headerGrid.Children[1] is StackPanel headerSp && headerGrid.Children[0] is Border headerSep)
			{
				headerSp.BeginAnimation(UIElement.OpacityProperty, null);
				headerSp.Opacity = opacity;
				headerSep.Opacity = 1.0 - opacity;
			}
			else if (child is Grid oldHeaderGrid && oldHeaderGrid.Children.Count == 2 && oldHeaderGrid.Children[1] is TextBlock oldHeaderTb && oldHeaderGrid.Children[0] is Border oldHeaderSep)
			{
				oldHeaderTb.BeginAnimation(UIElement.OpacityProperty, null);
				oldHeaderTb.Opacity = opacity;
				oldHeaderSep.Opacity = 1.0 - opacity;
			}
			else if (child is TextBlock fallbackHeaderTb)
			{
				fallbackHeaderTb.BeginAnimation(UIElement.OpacityProperty, null);
				fallbackHeaderTb.Opacity = opacity;
			}
			else
			{
				if (!(child is Border { Child: StackPanel sp }))
				{
					continue;
				}
				foreach (object child3 in sp.Children)
				{
					if (child3 is TextBlock tb)
					{
						tb.BeginAnimation(UIElement.OpacityProperty, null);
						tb.Opacity = opacity;
					}
				}
			}
		}
	}

	private void UpdateTabVisibility(bool instant = false)
	{
		bool isLoggedIn = System.IO.File.Exists(BackendService.AuthFilePath);
		MainSidebar.HomeNavBorderRef.Visibility = ((!isLoggedIn) ? Visibility.Collapsed : Visibility.Visible);
		AnimateCollapse(MainSidebar.ExploreNavBorderRef, _blockedCategories.Contains("Tab: Explore"), instant);
		if (_groupLibraryTabs)
		{
			AnimateCollapse(MainSidebar.PlaylistsNavBorderRef, _blockedCategories.Contains("Tab: Playlists"), instant);
			AnimateCollapse(MainSidebar.AlbumsNavBorderRef, _blockedCategories.Contains("Tab: Albums"), instant);
			AnimateCollapse(MainSidebar.RadioNavBorderRef, _blockedCategories.Contains("Tab: Radio"), instant);
			AnimateCollapse(MainSidebar.LocalNavBorderRef, !_enableLocalMusic || _blockedCategories.Contains("Tab: Local"), instant);
			AnimateCollapse(MainSidebar.StatsNavBorderRef, _blockedCategories.Contains("Tab: Stats"), instant);
		}
		else
		{
			AnimateCollapse(MainSidebar.PlaylistsNavBorderRef, collapse: true, instant);
			AnimateCollapse(MainSidebar.AlbumsNavBorderRef, collapse: true, instant);
			AnimateCollapse(MainSidebar.RadioNavBorderRef, _blockedCategories.Contains("Tab: Radio"), instant);
			AnimateCollapse(MainSidebar.LocalNavBorderRef, !_enableLocalMusic || _blockedCategories.Contains("Tab: Local"), instant);
			AnimateCollapse(MainSidebar.StatsNavBorderRef, _blockedCategories.Contains("Tab: Stats"), instant);
		}
	}

	private void UpdateSidebarHighlight()
	{
		if (_currentPageId != "search")
		{
			MainTopbarControl.MainSearchControlRef.SearchBox.Text = "";
		}
		foreach (object child in MainSidebar.HomeNavPanelRef.Children)
		{
			if (child is SidebarTab tab)
			{
				tab.UpdateHighlight(_currentPageId, FadeBorderBackgroundToColor, FadeTextForegroundToColor);
			}
		}
		foreach (object item in (IEnumerable)MainSidebar.LibraryPanelRef.Items)
		{
			if (!(item is Border { Tag: string id } rowBorder))
			{
				continue;
			}
			if (id == _currentPageId)
			{
				rowBorder.SetResourceReference(Border.BackgroundProperty, "CardHoverBrush");
				if (!(rowBorder.Child is StackPanel sp))
				{
					continue;
				}
				foreach (object child2 in sp.Children)
				{
					if (child2 is TextBlock tb)
					{
						tb.Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"];
					}
				}
				continue;
			}
			FadeBorderBackgroundToColor(rowBorder, Colors.Transparent);
			if (!(rowBorder.Child is StackPanel sp2))
			{
				continue;
			}
			foreach (object child3 in sp2.Children)
			{
				if (child3 is TextBlock tb2)
				{
					FadeTextForegroundToColor(tb2, ((SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["SidebarTextBrush"]).Color);
				}
			}
		}
		if (_currentPageId != "lyrics")
		{
			SetLyricsOffsetPanelVisibility(visible: false);
		}
		else
		{
			UpdateLyricsOffsetUI();
		}
		UpdateLyricsIcon();
	}

	private (UIElement, System.Windows.Controls.Panel) CreateExpandableSection(string title, int itemCount = 999, Action onExpand = null)
	{
		StackPanel container = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Vertical,
			Margin = new Thickness(0.0, 15.0, 0.0, 10.0)
		};
		Grid headerGrid = new Grid
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		};
		headerGrid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		headerGrid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		TextBlock titleBlock = new TextBlock
		{
			Text = title,
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			FontSize = 24.0,
			FontWeight = FontWeights.Bold,
			VerticalAlignment = VerticalAlignment.Center
		};
		ContextMenu ctx = new ContextMenu();
		if (System.Windows.Application.Current.MainWindow.TryFindResource(typeof(ContextMenu)) is Style style)
		{
			ctx.Style = style;
		}
		if (System.Windows.Application.Current.MainWindow.TryFindResource(typeof(MenuItem)) is Style menuItemStyle)
		{
			ctx.ItemContainerStyle = menuItemStyle;
		}
		MenuItem blockItem = new MenuItem
		{
			Header = "Hide this category",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"]
		};
		blockItem.Click += delegate
		{
			if (!_blockedCategories.Contains(title))
			{
				_blockedCategories.Add(title);
				SaveSession();
				container.Visibility = Visibility.Collapsed;
				container.Height = 0.0;
			}
		};
		ctx.Items.Add(blockItem);
		titleBlock.ContextMenu = ctx;
		Grid.SetColumn(titleBlock, 0);
		headerGrid.Children.Add(titleBlock);
		Border wpContainer = new Border
		{
			ClipToBounds = true,
			VerticalAlignment = VerticalAlignment.Top
		};
		StackPanel innerStack = new StackPanel();
		UniformGrid wp = new UniformGrid
		{
			VerticalAlignment = VerticalAlignment.Top
		};
		innerStack.Children.Add(wp);
		wpContainer.Child = innerStack;
		Grid seeAllContainer = new Grid
		{
			Background = System.Windows.Media.Brushes.Transparent,
			Cursor = System.Windows.Input.Cursors.Hand,
			Visibility = Visibility.Collapsed,
			VerticalAlignment = VerticalAlignment.Center
		};
		TextBlock seeAllBtn = new TextBlock
		{
			Text = "See all",
			Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			FontSize = 14.0,
			FontWeight = FontWeights.SemiBold,
			VerticalAlignment = VerticalAlignment.Center,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
			Margin = new Thickness(10.0, 5.0, 28.0, 5.0)
		};
		TranslateTransform textTransform = new TranslateTransform(0.0, 0.0);
		seeAllBtn.RenderTransform = textTransform;
		System.Windows.Controls.Image arrowIcon = new System.Windows.Controls.Image
		{
			Source = (System.Windows.Media.DrawingImage)System.Windows.Application.Current.FindResource("rightarrowIcon"),
			Width = 14.0,
			Height = 14.0,
			VerticalAlignment = VerticalAlignment.Center,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
			Margin = new Thickness(0.0, 0.0, 10.0, 0.0),
			IsHitTestVisible = false
		};
		seeAllContainer.Children.Add(seeAllBtn);
		seeAllContainer.Children.Add(arrowIcon);
		bool isLoaded = false;
		bool isExpanded = false;
		wp.SizeChanged += delegate(object s, SizeChangedEventArgs e)
		{
			if (e.NewSize.Width != 0.0)
			{
				int num = Math.Max(1, (int)(e.NewSize.Width / 180.0));
				if (wp.Columns != num)
				{
					wp.Columns = num;
				}
				seeAllContainer.Visibility = ((itemCount <= num) ? Visibility.Collapsed : Visibility.Visible);
				if (!isExpanded)
				{
					double num2 = e.NewSize.Width / (double)num;
					wpContainer.MaxHeight = num2 * 1.2705882352941176;
				}
			}
		};
		seeAllContainer.MouseEnter += delegate
		{
			seeAllBtn.Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"];
			arrowIcon.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(250.0))
			{
				EasingFunction = new QuarticEase
				{
					EasingMode = EasingMode.EaseOut
				}
			});
			textTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(18.0, TimeSpan.FromMilliseconds(250.0))
			{
				EasingFunction = new QuarticEase
				{
					EasingMode = EasingMode.EaseOut
				}
			});
		};
		seeAllContainer.MouseLeave += delegate
		{
			seeAllBtn.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, byte.MaxValue, byte.MaxValue, byte.MaxValue));
			arrowIcon.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(250.0))
			{
				EasingFunction = new QuarticEase
				{
					EasingMode = EasingMode.EaseOut
				}
			});
			textTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(250.0))
			{
				EasingFunction = new QuarticEase
				{
					EasingMode = EasingMode.EaseOut
				}
			});
		};
		seeAllContainer.MouseLeftButtonDown += delegate
		{
			if (!isExpanded)
			{
				if (!isLoaded && onExpand != null)
				{
					onExpand();
					isLoaded = true;
				}
				isExpanded = true;
				double actualHeight = wpContainer.ActualHeight;
				innerStack.Measure(new System.Windows.Size(wpContainer.ActualWidth, double.PositiveInfinity));
				double height = innerStack.DesiredSize.Height;
				DoubleAnimation doubleAnimation = new DoubleAnimation(actualHeight, height, TimeSpan.FromMilliseconds(450.0))
				{
					EasingFunction = new QuarticEase
					{
						EasingMode = EasingMode.EaseInOut
					}
				};
				doubleAnimation.Completed += delegate
				{
					wpContainer.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
					wpContainer.MaxHeight = double.PositiveInfinity;
				};
				wpContainer.BeginAnimation(FrameworkElement.MaxHeightProperty, doubleAnimation);
				DoubleAnimation doubleAnimation2 = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120.0))
				{
					EasingFunction = new QuadraticEase
					{
						EasingMode = EasingMode.EaseOut
					}
				};
				doubleAnimation2.Completed += delegate
				{
					seeAllBtn.Text = "Show less";
					seeAllBtn.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120.0))
					{
						EasingFunction = new QuadraticEase
						{
							EasingMode = EasingMode.EaseIn
						}
					});
				};
				seeAllBtn.BeginAnimation(UIElement.OpacityProperty, doubleAnimation2);
			}
			else
			{
				isExpanded = false;
				double toValue = wp.ActualWidth / (double)wp.Columns * 1.2705882352941176;
				DoubleAnimation animation = new DoubleAnimation(wpContainer.ActualHeight, toValue, TimeSpan.FromMilliseconds(450.0))
				{
					EasingFunction = new QuarticEase
					{
						EasingMode = EasingMode.EaseInOut
					}
				};
				wpContainer.BeginAnimation(FrameworkElement.MaxHeightProperty, animation);
				DoubleAnimation doubleAnimation3 = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120.0))
				{
					EasingFunction = new QuadraticEase
					{
						EasingMode = EasingMode.EaseOut
					}
				};
				doubleAnimation3.Completed += delegate
				{
					seeAllBtn.Text = "See all";
					seeAllBtn.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120.0))
					{
						EasingFunction = new QuadraticEase
						{
							EasingMode = EasingMode.EaseIn
						}
					});
				};
				seeAllBtn.BeginAnimation(UIElement.OpacityProperty, doubleAnimation3);
			}
		};
		Grid.SetColumn(seeAllContainer, 1);
		headerGrid.Children.Add(seeAllContainer);
		container.Children.Add(headerGrid);
		container.Children.Add(wpContainer);
		return (container, wp);
	}

	private (UIElement, System.Windows.Controls.Panel) CreateExpandableGridSection(string title, double collapsedHeight, int itemCount = 999, Action onExpand = null)
	{
		StackPanel container = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Vertical,
			Margin = new Thickness(0.0, 15.0, 0.0, 10.0)
		};
		Grid headerGrid = new Grid
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		};
		headerGrid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		headerGrid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		TextBlock titleBlock = new TextBlock
		{
			Text = title,
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			FontSize = 24.0,
			FontWeight = FontWeights.Bold,
			VerticalAlignment = VerticalAlignment.Center
		};
		ContextMenu ctx = new ContextMenu();
		if (System.Windows.Application.Current.MainWindow.TryFindResource(typeof(ContextMenu)) is Style style)
		{
			ctx.Style = style;
		}
		if (System.Windows.Application.Current.MainWindow.TryFindResource(typeof(MenuItem)) is Style menuItemStyle)
		{
			ctx.ItemContainerStyle = menuItemStyle;
		}
		MenuItem blockItem = new MenuItem
		{
			Header = "Hide this category",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"]
		};
		blockItem.Click += delegate
		{
			if (!_blockedCategories.Contains(title))
			{
				_blockedCategories.Add(title);
				SaveSession();
				container.Visibility = Visibility.Collapsed;
				container.Height = 0.0;
			}
		};
		ctx.Items.Add(blockItem);
		titleBlock.ContextMenu = ctx;
		Grid.SetColumn(titleBlock, 0);
		headerGrid.Children.Add(titleBlock);
		Border border = new Border
		{
			MaxHeight = collapsedHeight,
			ClipToBounds = true
		};
		StackPanel sp = new StackPanel();
		border.Child = sp;
		UniformGrid grid = new UniformGrid();
		sp.Children.Add(grid);
		Grid seeAllContainer = new Grid
		{
			Background = System.Windows.Media.Brushes.Transparent,
			Cursor = System.Windows.Input.Cursors.Hand,
			Visibility = Visibility.Collapsed,
			VerticalAlignment = VerticalAlignment.Center
		};
		TextBlock seeAllBtn = new TextBlock
		{
			Text = "See all",
			Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			FontSize = 14.0,
			FontWeight = FontWeights.SemiBold,
			VerticalAlignment = VerticalAlignment.Center,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
			Margin = new Thickness(10.0, 5.0, 28.0, 5.0)
		};
		TranslateTransform textTransform = new TranslateTransform(0.0, 0.0);
		seeAllBtn.RenderTransform = textTransform;
		System.Windows.Controls.Image arrowIcon = new System.Windows.Controls.Image
		{
			Source = (System.Windows.Media.DrawingImage)System.Windows.Application.Current.FindResource("rightarrowIcon"),
			Width = 14.0,
			Height = 14.0,
			VerticalAlignment = VerticalAlignment.Center,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
			Margin = new Thickness(0.0, 0.0, 10.0, 0.0),
			IsHitTestVisible = false
		};
		seeAllContainer.Children.Add(seeAllBtn);
		seeAllContainer.Children.Add(arrowIcon);
		grid.SizeChanged += delegate(object s, SizeChangedEventArgs e)
		{
			int num = Math.Max(1, (int)(e.NewSize.Width / 260.0));
			int num2 = Math.Max(1, (int)(collapsedHeight / 90.0));
			int num3 = num * num2;
			seeAllContainer.Visibility = ((itemCount <= num3) ? Visibility.Collapsed : Visibility.Visible);
		};
		seeAllContainer.MouseEnter += delegate
		{
			seeAllBtn.Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"];
			arrowIcon.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(250.0))
			{
				EasingFunction = new QuarticEase
				{
					EasingMode = EasingMode.EaseOut
				}
			});
			textTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(18.0, TimeSpan.FromMilliseconds(250.0))
			{
				EasingFunction = new QuarticEase
				{
					EasingMode = EasingMode.EaseOut
				}
			});
		};
		seeAllContainer.MouseLeave += delegate
		{
			seeAllBtn.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, byte.MaxValue, byte.MaxValue, byte.MaxValue));
			arrowIcon.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(250.0))
			{
				EasingFunction = new QuarticEase
				{
					EasingMode = EasingMode.EaseOut
				}
			});
			textTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(250.0))
			{
				EasingFunction = new QuarticEase
				{
					EasingMode = EasingMode.EaseOut
				}
			});
		};
		bool isLoaded = false;
		bool isExpanded = false;
		seeAllContainer.MouseLeftButtonDown += delegate
		{
			if (!isExpanded)
			{
				if (!isLoaded && onExpand != null)
				{
					onExpand();
					isLoaded = true;
				}
				isExpanded = true;
				double actualHeight = border.ActualHeight;
				sp.Measure(new System.Windows.Size(border.ActualWidth, double.PositiveInfinity));
				double height = sp.DesiredSize.Height;
				DoubleAnimation doubleAnimation = new DoubleAnimation(actualHeight, height, TimeSpan.FromMilliseconds(450.0))
				{
					EasingFunction = new QuarticEase
					{
						EasingMode = EasingMode.EaseInOut
					}
				};
				doubleAnimation.Completed += delegate
				{
					border.MaxHeight = double.PositiveInfinity;
				};
				border.BeginAnimation(FrameworkElement.MaxHeightProperty, doubleAnimation);
				DoubleAnimation doubleAnimation2 = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120.0))
				{
					EasingFunction = new QuadraticEase
					{
						EasingMode = EasingMode.EaseOut
					}
				};
				doubleAnimation2.Completed += delegate
				{
					seeAllBtn.Text = "Show less";
					seeAllBtn.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120.0))
					{
						EasingFunction = new QuadraticEase
						{
							EasingMode = EasingMode.EaseIn
						}
					});
				};
				seeAllBtn.BeginAnimation(UIElement.OpacityProperty, doubleAnimation2);
			}
			else
			{
				isExpanded = false;
				double toValue = collapsedHeight;
				DoubleAnimation animation = new DoubleAnimation(border.ActualHeight, toValue, TimeSpan.FromMilliseconds(450.0))
				{
					EasingFunction = new QuarticEase
					{
						EasingMode = EasingMode.EaseInOut
					}
				};
				border.BeginAnimation(FrameworkElement.MaxHeightProperty, animation);
				DoubleAnimation doubleAnimation3 = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120.0))
				{
					EasingFunction = new QuadraticEase
					{
						EasingMode = EasingMode.EaseOut
					}
				};
				doubleAnimation3.Completed += delegate
				{
					seeAllBtn.Text = "See all";
					seeAllBtn.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120.0))
					{
						EasingFunction = new QuadraticEase
						{
							EasingMode = EasingMode.EaseIn
						}
					});
				};
				seeAllBtn.BeginAnimation(UIElement.OpacityProperty, doubleAnimation3);
			}
		};
		Grid.SetColumn(seeAllContainer, 1);
		headerGrid.Children.Add(seeAllContainer);
		container.Children.Add(headerGrid);
		container.Children.Add(border);
		return (container, grid);
	}
}








