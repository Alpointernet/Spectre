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
using System.Text.Json;
using System.Text.Json.Nodes;
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
	private async void SettingsBtn_Click(object sender, RoutedEventArgs e)
	{
		if (_currentPageId == "settings")
		{
			return;
		}
		_currentPageId = "settings";
		UpdateSidebarHighlight();
		int tId = await FadeOutContentAsync();
		StackPanel pagePanel = new StackPanel
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 80.0)
		};
		pagePanel.Children.Add(new TextBlock
		{
			Text = "Settings",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			FontSize = 36.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 20.0, 0.0, 30.0)
		});
		Style toggleStyle = (Style)FindResource("ModernToggleSwitch");
		Style radioStyle = (Style)FindResource("ModernRadioButton");
		Style btnStyle = (Style)FindResource("SettingsButtonStyle");
		System.Windows.Controls.CheckBox prefetchToggle = CreateSettingToggle("Enable Pre-fetching", "Loads upcoming songs in background before they play.", _prefetchEnabled, new Thickness(0.0, 0.0, 0.0, 20.0));
		prefetchToggle.Checked += delegate
		{
			_prefetchEnabled = true;
			_ = _ = _ = _ = MaintainPreloadBufferAsync();
			SaveSession();
		};
		prefetchToggle.Unchecked += delegate
		{
			_prefetchEnabled = false;
			_preloadTasks.Clear();
			SaveSession();
		};
		System.Windows.Controls.CheckBox loudnessToggle = CreateSettingToggle("Loudness Normalization", "Balances audio levels across different tracks.", _loudnessNormalization, new Thickness(0.0, 0.0, 0.0, 20.0));
		loudnessToggle.Checked += delegate
		{
			_loudnessNormalization = true;
			SaveSession();
		};
		loudnessToggle.Unchecked += delegate
		{
			_loudnessNormalization = false;
			SaveSession();
		};
		TextBlock crossfadeLabel = new TextBlock
		{
			Text = "Crossfade Duration:",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"],
			FontSize = 14.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		};
		TextBlock crossfadeValueText = new TextBlock
		{
			Text = ((_crossfadeMs == 0) ? "Off" : $"{_crossfadeMs / 1000}s"),
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			FontSize = 14.0,
			VerticalAlignment = VerticalAlignment.Center
		};
		Slider crossfadeSlider = new Slider
		{
			Style = (Style)FindResource("ModernSlider"),
			Minimum = 0.0,
			Maximum = 12.0,
			Value = _crossfadeMs / 1000,
			TickFrequency = 1.0,
			IsSnapToTickEnabled = true,
			IsMoveToPointEnabled = true,
			Width = 250.0,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 15.0, 0.0)
		};
		crossfadeSlider.ValueChanged += delegate
		{
			int num = (int)crossfadeSlider.Value;
			_crossfadeMs = num * 1000;
			_player.CrossfadeMs = _crossfadeMs;
			crossfadeValueText.Text = ((num == 0) ? "Off" : $"{num}s");
			SaveSession();
		};
		StackPanel crossfadePanel = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			Margin = new Thickness(0.0, 0.0, 0.0, 0.0)
		};
		crossfadePanel.Children.Add(crossfadeSlider);
		crossfadePanel.Children.Add(crossfadeValueText);
		pagePanel.Children.Add(CreateCard("Playback", new UIElement[4] { prefetchToggle, loudnessToggle, crossfadeLabel, crossfadePanel }));
		System.Windows.Controls.CheckBox sourceFilterToggle = CreateSettingToggle("Only Play Songs", "Skips YouTube Video results and Music Videos.", _excludePlainVideoResults, new Thickness(0.0, 0.0, 0.0, 0.0));
		sourceFilterToggle.Checked += delegate
		{
			_excludePlainVideoResults = true;
			BackendService.Instance.ExcludePlainVideoResults = true;
			SaveSession();
		};
		sourceFilterToggle.Unchecked += delegate
		{
			_excludePlainVideoResults = false;
			BackendService.Instance.ExcludePlainVideoResults = false;
			SaveSession();
		};
		pagePanel.Children.Add(CreateCard("Playback Sources", new UIElement[1] { sourceFilterToggle }));
		System.Windows.Controls.CheckBox localToggle = CreateSettingToggle("Enable Local Music", "Include local music directories in your library.", _enableLocalMusic, new Thickness(0.0, 0.0, 0.0, 15.0));
		StackPanel localPathPanel = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal
		};
		System.Windows.Controls.Button localPathBtn = new System.Windows.Controls.Button
		{
			Content = "Select Folder",
			Style = btnStyle,
			Padding = new Thickness(15.0, 8.0, 15.0, 8.0),
			Cursor = System.Windows.Input.Cursors.Hand
		};
		TextBlock localPathTxt = new TextBlock
		{
			Text = (string.IsNullOrEmpty(_LocalMusicPath) ? "No folder selected" : _LocalMusicPath),
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"],
			FontSize = 13.0,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(15.0, 0.0, 0.0, 0.0)
		};
		localPathPanel.Children.Add(localPathBtn);
		localPathPanel.Children.Add(localPathTxt);
		localToggle.Checked += delegate
		{
			_enableLocalMusic = true;
			SaveSession();
			UpdateTabVisibility();
		};
		localToggle.Unchecked += delegate
		{
			_enableLocalMusic = false;
			SaveSession();
			UpdateTabVisibility();
		};
		localPathBtn.Click += delegate
		{
			FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
			try
			{
				if (folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
				{
					_LocalMusicPath = folderBrowserDialog.SelectedPath;
					localPathTxt.Text = _LocalMusicPath;
					SaveSession();
					if (_enableLocalMusic)
					{
						_ = _ = _ = _ = LoadLibraryAsync();
					}
				}
			}
			finally
			{
				((IDisposable)(object)folderBrowserDialog)?.Dispose();
			}
		};
		pagePanel.Children.Add(CreateCard("Local Music", new UIElement[2] { localToggle, localPathPanel }));
		System.Windows.Controls.CheckBox downloadToggle = CreateSettingToggle("Enable Downloads", "Add a download option to right click context menus.", _enableDownloads, new Thickness(0.0, 0.0, 0.0, 15.0));
		StackPanel downloadPathPanel = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal
		};
		System.Windows.Controls.Button downloadPathBtn = new System.Windows.Controls.Button
		{
			Content = "Select Folder",
			Style = btnStyle,
			Padding = new Thickness(15.0, 8.0, 15.0, 8.0),
			Cursor = System.Windows.Input.Cursors.Hand
		};
		TextBlock downloadPathTxt = new TextBlock
		{
			Text = (string.IsNullOrEmpty(_downloadsPath) ? "No folder selected" : _downloadsPath),
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"],
			FontSize = 13.0,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(15.0, 0.0, 0.0, 0.0)
		};
		downloadPathPanel.Children.Add(downloadPathBtn);
		downloadPathPanel.Children.Add(downloadPathTxt);
		downloadToggle.Checked += delegate
		{
			_enableDownloads = true;
			SaveSession();
		};
		downloadToggle.Unchecked += delegate
		{
			_enableDownloads = false;
			SaveSession();
		};
		downloadPathBtn.Click += delegate
		{
			FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
			try
			{
				if (folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
				{
					_downloadsPath = folderBrowserDialog.SelectedPath;
					downloadPathTxt.Text = _downloadsPath;
					SaveSession();
				}
			}
			finally
			{
				((IDisposable)(object)folderBrowserDialog)?.Dispose();
			}
		};
		pagePanel.Children.Add(CreateCard("Downloads", new UIElement[2] { downloadToggle, downloadPathPanel }));
		TextBlock cacheLabel = new TextBlock
		{
			Text = "Network Streaming Buffer (Higher = less stutter, lower = instant play):",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"],
			FontSize = 14.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 15.0)
		};
		StackPanel cachePanel = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			Margin = new Thickness(0.0, 0.0, 0.0, 0.0)
		};
		System.Windows.Controls.RadioButton lowCacheRb = new System.Windows.Controls.RadioButton
		{
			Content = "Low Latency (300ms)",
			IsChecked = (_networkCacheMs == 300),
			Style = radioStyle,
			Margin = new Thickness(0.0, 0.0, 25.0, 0.0),
			FontSize = 14.0
		};
		System.Windows.Controls.RadioButton normCacheRb = new System.Windows.Controls.RadioButton
		{
			Content = "Standard (1500ms)",
			IsChecked = (_networkCacheMs == 1500),
			Style = radioStyle,
			Margin = new Thickness(0.0, 0.0, 25.0, 0.0),
			FontSize = 14.0
		};
		System.Windows.Controls.RadioButton highCacheRb = new System.Windows.Controls.RadioButton
		{
			Content = "Slow Network (5000ms)",
			IsChecked = (_networkCacheMs == 5000),
			Style = radioStyle,
			FontSize = 14.0
		};
		lowCacheRb.Checked += delegate
		{
			_networkCacheMs = 300;
			SaveSession();
		};
		normCacheRb.Checked += delegate
		{
			_networkCacheMs = 1500;
			SaveSession();
		};
		highCacheRb.Checked += delegate
		{
			_networkCacheMs = 5000;
			SaveSession();
		};
		cachePanel.Children.Add(lowCacheRb);
		cachePanel.Children.Add(normCacheRb);
		cachePanel.Children.Add(highCacheRb);
		pagePanel.Children.Add(CreateCard("Network & Caching", new UIElement[2] { cacheLabel, cachePanel }));
		System.Windows.Controls.CheckBox groupLibraryToggle = CreateSettingToggle("Group Library as Tabs", "Moves Playlists and Albums to top-level navigation tabs.", _groupLibraryTabs, new Thickness(0.0, 0.0, 0.0, 20.0));
		groupLibraryToggle.Checked += delegate
		{
			_groupLibraryTabs = true;
			SaveSession();
			UpdateTabVisibility();
			_ = _ = _ = _ = LoadLibraryAsync();
		};
		groupLibraryToggle.Unchecked += delegate
		{
			_groupLibraryTabs = false;
			SaveSession();
			UpdateTabVisibility();
			_ = _ = _ = _ = LoadLibraryAsync();
		};
		System.Windows.Controls.CheckBox animToggle = CreateSettingToggle("Reduce Animations", "Improves performance by disabling page transition fades.", _reduceAnimations, new Thickness(0.0, 0.0, 0.0, 20.0));
		animToggle.Checked += delegate
		{
			_reduceAnimations = true;
			SaveSession();
		};
		animToggle.Unchecked += delegate
		{
			_reduceAnimations = false;
			SaveSession();
		};
		System.Windows.Controls.CheckBox smoothScrollToggle = CreateSettingToggle("Disable Smooth Scrolling", "Removes the smooth momentum effect when scrolling.", _disableSmoothScrolling, new Thickness(0.0, 0.0, 0.0, 20.0));
		smoothScrollToggle.Checked += delegate
		{
			_disableSmoothScrolling = true;
			SaveSession();
		};
		smoothScrollToggle.Unchecked += delegate
		{
			_disableSmoothScrolling = false;
			SaveSession();
		};
		System.Windows.Controls.CheckBox onTopToggle = CreateSettingToggle("Always On Top", "Keep player hovering over other windows.", _alwaysOnTop, new Thickness(0.0, 0.0, 0.0, 20.0));
		onTopToggle.Checked += delegate
		{
			_alwaysOnTop = true;
			base.Topmost = true;
			SaveSession();
		};
		onTopToggle.Unchecked += delegate
		{
			_alwaysOnTop = false;
			base.Topmost = false;
			SaveSession();
		};
		System.Windows.Controls.CheckBox smtcToggle = CreateSettingToggle("Windows Media Controls", "Show playback in Windows volume overlay.", _enableSMTC, new Thickness(0.0, 0.0, 0.0, 20.0));
		smtcToggle.Checked += delegate
		{
			_enableSMTC = true;
			if (_smtcPlayer == null)
			{
				InitSMTC();
			}
			if (_player.IsPlaying && _smtc != null)
			{
				_smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
			}
			SaveSession();
		};
		smtcToggle.Unchecked += delegate
		{
			_enableSMTC = false;
			if (_smtcPlayer != null)
			{
				_smtcPlayer.Dispose();
				_smtcPlayer = null;
				_smtc = null;
			}
			SaveSession();
		};
		System.Windows.Controls.CheckBox discordToggle = CreateSettingToggle("Discord Rich Presence", "Show current song on Discord.", _enableDiscordRpc, new Thickness(0.0, 0.0, 0.0, 20.0));
		discordToggle.Checked += delegate
		{
			_enableDiscordRpc = true;
			InitDiscordRPC();
			UpdateDiscordRPC();
			SaveSession();
		};
		discordToggle.Unchecked += delegate
		{
			_enableDiscordRpc = false;
			DeinitDiscordRPC();
			SaveSession();
		};
		System.Windows.Controls.CheckBox statusToggle = CreateSettingToggle("Show Status Indicator", "Top right text displaying background actions.", _enableStatusIndicator, new Thickness(0.0, 0.0, 0.0, 20.0));
		statusToggle.Checked += delegate
		{
			_enableStatusIndicator = true;
			MainTopbarControl.StatusLabelRef.Visibility = Visibility.Visible;
			SaveSession();
		};
		statusToggle.Unchecked += delegate
		{
			_enableStatusIndicator = false;
			MainTopbarControl.StatusLabelRef.Visibility = Visibility.Collapsed;
			SaveSession();
		};
		System.Windows.Controls.CheckBox taskbarToggle = CreateSettingToggle("Show Taskbar Media Controls", "Display play/pause buttons on the taskbar thumbnail preview.", _showTaskbarMediaControls, new Thickness(0.0, 0.0, 0.0, 20.0));
		taskbarToggle.Checked += delegate
		{
			_showTaskbarMediaControls = true;
			UpdateTaskbarControlsVisibility();
			SaveSession();
		};
		taskbarToggle.Unchecked += delegate
		{
			_showTaskbarMediaControls = false;
			UpdateTaskbarControlsVisibility();
			SaveSession();
		};
		TextBlock volLabel = new TextBlock
		{
			Text = "Mouse Wheel Volume Scroll Step Size:",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"],
			FontSize = 14.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 15.0)
		};
		StackPanel volPanel = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			Margin = new Thickness(0.0, 0.0, 0.0, 0.0)
		};
		System.Windows.Controls.RadioButton step1Rb = new System.Windows.Controls.RadioButton
		{
			Content = "1%",
			IsChecked = (_volumeStep == 1),
			Style = radioStyle,
			Margin = new Thickness(0.0, 0.0, 25.0, 0.0),
			FontSize = 14.0
		};
		System.Windows.Controls.RadioButton step5Rb = new System.Windows.Controls.RadioButton
		{
			Content = "5%",
			IsChecked = (_volumeStep == 5),
			Style = radioStyle,
			Margin = new Thickness(0.0, 0.0, 25.0, 0.0),
			FontSize = 14.0
		};
		System.Windows.Controls.RadioButton step10Rb = new System.Windows.Controls.RadioButton
		{
			Content = "10%",
			IsChecked = (_volumeStep == 10),
			Style = radioStyle,
			FontSize = 14.0
		};
		step1Rb.Checked += delegate
		{
			_volumeStep = 1;
			SaveSession();
		};
		step5Rb.Checked += delegate
		{
			_volumeStep = 5;
			SaveSession();
		};
		step10Rb.Checked += delegate
		{
			_volumeStep = 10;
			SaveSession();
		};
		volPanel.Children.Add(step1Rb);
		volPanel.Children.Add(step5Rb);
		volPanel.Children.Add(step10Rb);
		pagePanel.Children.Add(CreateCard("User Experience", new UIElement[10] { groupLibraryToggle, animToggle, smoothScrollToggle, onTopToggle, smtcToggle, discordToggle, statusToggle, taskbarToggle, volLabel, volPanel }));
		StackPanel themePanel = new StackPanel
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 0.0)
		};
		StackPanel fontPanel = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			Margin = new Thickness(0.0, 0.0, 0.0, 25.0)
		};
		fontPanel.Children.Add(new TextBlock
		{
			Text = "Custom Font (.ttf, .otf):",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 10.0, 0.0)
		});
		TextBlock fontPathText = new TextBlock
		{
			Text = (string.IsNullOrEmpty(_customFontPath) ? "Default Font" : System.IO.Path.GetFileName(_customFontPath)),
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"],
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 10.0, 0.0),
			MaxWidth = 150.0,
			TextTrimming = TextTrimming.CharacterEllipsis
		};
		fontPanel.Children.Add(fontPathText);
		System.Windows.Controls.Button fontUploadBtn = new System.Windows.Controls.Button
		{
			Content = "Select",
			Style = (Style)FindResource("SettingsButtonStyle"),
			Padding = new Thickness(10.0, 4.0, 10.0, 4.0),
			Cursor = System.Windows.Input.Cursors.Hand
		};
		fontUploadBtn.Click += delegate
		{
			Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
			{
				Filter = "Font Files|*.ttf;*.otf;*.woff|All Files|*.*"
			};
			if (openFileDialog.ShowDialog() == true)
			{
				_customFontPath = openFileDialog.FileName;
				fontPathText.Text = System.IO.Path.GetFileName(_customFontPath);
				ApplyCustomFont();
				SaveSession();
			}
		};
		System.Windows.Controls.Button fontResetBtn = new System.Windows.Controls.Button
		{
			Content = "Reset Font",
			Style = (Style)FindResource("SettingsButtonStyle"),
			Margin = new Thickness(10.0, 0.0, 0.0, 0.0),
			Padding = new Thickness(10.0, 4.0, 10.0, 4.0),
			Cursor = System.Windows.Input.Cursors.Hand
		};
		fontResetBtn.Click += delegate
		{
			_customFontPath = "";
			fontPathText.Text = "Default Font";
			ApplyCustomFont();
			SaveSession();
		};
		fontPanel.Children.Add(fontUploadBtn);
		fontPanel.Children.Add(fontResetBtn);
		themePanel.Children.Add(fontPanel);
		TextBlock presetLabel = new TextBlock
		{
			Text = "Ready-to-use Themes:",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			FontSize = 14.0,
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		};
		themePanel.Children.Add(presetLabel);
		WrapPanel themePresetPanel = new WrapPanel
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 15.0)
		};
		List<System.Windows.Controls.Button> themeBtnsList = new List<System.Windows.Controls.Button>();
		System.Windows.Controls.CheckBox adaptiveToggle = null;
		Action updateSelectedThemeColors = null;
		updateSelectedThemeColors = delegate
		{
			for (int i = 0; i < ThemePresets.Themes.Count; i++)
			{
				AppTheme appTheme = ThemePresets.Themes[i];
				System.Windows.Controls.Button button = themeBtnsList[i];
				if (!_useAdaptiveTheme && _accentColor1 == appTheme.Accent1 && _bgGrad1 == appTheme.Bg1)
				{
					try
					{
						LinearGradientBrush linearGradientBrush = new LinearGradientBrush
						{
							StartPoint = new System.Windows.Point(0.0, 0.0),
							EndPoint = new System.Windows.Point(1.0, 1.0)
						};
						linearGradientBrush.GradientStops.Add(new GradientStop((System.Windows.Media.Color)ColorConverter.ConvertFromString(appTheme.Accent1), 0.0));
						linearGradientBrush.GradientStops.Add(new GradientStop((System.Windows.Media.Color)ColorConverter.ConvertFromString(appTheme.Accent2), 1.0));
						button.Foreground = linearGradientBrush;
					}
					catch
					{
						button.Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"];
					}
					button.BorderThickness = new Thickness(1.0);
					try
					{
						button.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(appTheme.Accent1));
					}
					catch
					{
						button.BorderBrush = System.Windows.Media.Brushes.Transparent;
					}
				}
				else
				{
					button.Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"];
					button.BorderThickness = new Thickness(0.0);
					button.BorderBrush = System.Windows.Media.Brushes.Transparent;
				}
			}
		};
		foreach (AppTheme t in ThemePresets.Themes)
		{
			System.Windows.Controls.Button btn = new System.Windows.Controls.Button
			{
				Content = t.Name,
				Style = (Style)FindResource("SettingsButtonStyle"),
				Margin = new Thickness(0.0, 0.0, 10.0, 10.0)
			};
			themeBtnsList.Add(btn);
			btn.Click += delegate
			{
				_useAdaptiveTheme = false;
				if (adaptiveToggle != null)
				{
					adaptiveToggle.IsChecked = false;
				}
				_accentColor1 = t.Accent1;
				_accentColor2 = t.Accent2;
				_bgGrad1 = t.Bg1;
				_bgGrad2 = t.Bg2;
				_bgGrad3 = t.Bg3;
				_cardBg = t.CardBg;
				System.Windows.Media.Color color = (System.Windows.Media.Color)ColorConverter.ConvertFromString(t.Bg1);
				System.Windows.Media.Color color2 = (System.Windows.Media.Color)ColorConverter.ConvertFromString(t.Bg3);
				if ((0.299 * (double)(int)color.R + 0.587 * (double)(int)color.G + 0.114 * (double)(int)color.B) / 255.0 > 0.5)
				{
					_sidebarBg = System.Windows.Media.Color.FromArgb(208, (byte)Math.Min(255, color.R + 15), (byte)Math.Min(255, color.G + 15), (byte)Math.Min(255, color.B + 15)).ToString();
				}
				else
				{
					_sidebarBg = System.Windows.Media.Color.FromArgb(208, (byte)(color.R / 2), (byte)(color.G / 2), (byte)(color.B / 2)).ToString();
				}
				_topbarBg = System.Windows.Media.Color.FromArgb(176, color.R, color.G, color.B).ToString();
				_bottombarBg = System.Windows.Media.Color.FromArgb(224, color2.R, color2.G, color2.B).ToString();
				_ = (System.Windows.Media.Color)ColorConverter.ConvertFromString(t.Accent1);
				ApplyThemeColors();
				SaveSession();
				updateSelectedThemeColors();
			};
			themePresetPanel.Children.Add(btn);
		}
		updateSelectedThemeColors();
		themePanel.Children.Add(themePresetPanel);
		StackPanel customThemePanel = new StackPanel
		{
			Visibility = Visibility.Collapsed,
			Margin = new Thickness(0.0, 15.0, 0.0, 0.0)
		};
		TextBlock customLabel = new TextBlock
		{
			Text = "Custom Theme Configuration:",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			FontSize = 14.0,
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0.0, 0.0, 0.0, 15.0)
		};
		customThemePanel.Children.Add(customLabel);
		StackPanel row1 = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal
		};
		row1.Children.Add(CreateColorInput("Accent Gradient Start", _accentColor1, delegate(string val)
		{
			_accentColor1 = val;
		}));
		row1.Children.Add(CreateColorInput("Accent Gradient End", _accentColor2, delegate(string val)
		{
			_accentColor2 = val;
		}));
		customThemePanel.Children.Add(row1);
		StackPanel row2 = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal
		};
		row2.Children.Add(CreateColorInput("Background Grad Top", _bgGrad1, delegate(string val)
		{
			_bgGrad1 = val;
		}));
		row2.Children.Add(CreateColorInput("Background Grad Mid", _bgGrad2, delegate(string val)
		{
			_bgGrad2 = val;
		}));
		customThemePanel.Children.Add(row2);
		StackPanel row3 = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal
		};
		row3.Children.Add(CreateColorInput("Background Grad Bot", _bgGrad3, delegate(string val)
		{
			_bgGrad3 = val;
		}));
		row3.Children.Add(CreateColorInput("Card Element Background", _cardBg, delegate(string val)
		{
			_cardBg = val;
		}));
		customThemePanel.Children.Add(row3);
		StackPanel row4 = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal
		};
		row4.Children.Add(CreateColorInput("Sidebar Background", _sidebarBg, delegate(string val)
		{
			_sidebarBg = val;
		}));
		row4.Children.Add(CreateColorInput("Topbar Background", _topbarBg, delegate(string val)
		{
			_topbarBg = val;
		}));
		customThemePanel.Children.Add(row4);
		StackPanel row5 = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal
		};
		row5.Children.Add(CreateColorInput("Bottom Player Background", _bottombarBg, delegate(string val)
		{
			_bottombarBg = val;
		}));
		customThemePanel.Children.Add(row5);
		StackPanel themeBtns = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
		};
		System.Windows.Controls.Button applyBtn = new System.Windows.Controls.Button
		{
			Content = "Apply & Reload",
			Style = (Style)FindResource("SettingsAccentButtonStyle"),
			Width = 130.0,
			Height = 32.0,
			Margin = new Thickness(0.0, 0.0, 10.0, 0.0),
			FontWeight = FontWeights.Bold
		};
		System.Windows.Controls.Button resetBtn = new System.Windows.Controls.Button
		{
			Content = "Reset",
			Style = (Style)FindResource("SettingsButtonStyle"),
			Width = 80.0,
			Height = 32.0
		};
		themeBtns.Children.Add(applyBtn);
		themeBtns.Children.Add(resetBtn);
		customThemePanel.Children.Add(themeBtns);
		System.Windows.Controls.Button toggleBtn = new System.Windows.Controls.Button
		{
			Content = "Customize",
			Style = (Style)FindResource("SettingsButtonStyle"),
			Margin = new Thickness(0.0, 0.0, 10.0, 10.0)
		};
		toggleBtn.Click += delegate
		{
			customThemePanel.Visibility = ((customThemePanel.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible);
		};
		themePresetPanel.Children.Add(toggleBtn);
		themePanel.Children.Add(customThemePanel);
		applyBtn.Click += delegate
		{
			_useAdaptiveTheme = false;
			if (adaptiveToggle != null)
			{
				adaptiveToggle.IsChecked = false;
			}
			ApplyThemeColors();
			SaveSession();
			updateSelectedThemeColors();
		};
		resetBtn.Click += delegate
		{
			_useAdaptiveTheme = false;
			if (adaptiveToggle != null)
			{
				adaptiveToggle.IsChecked = false;
			}
			_accentColor1 = "#00E5FF";
			_accentColor2 = "#7000FF";
			_bgGrad1 = "#1A1829";
			_bgGrad2 = "#0D0D12";
			_bgGrad3 = "#0A0A0E";
			_cardBg = "#0CFFFFFF";
			_sidebarBg = "#D0040406";
			_topbarBg = "#B01A1829";
			_bottombarBg = "#E00A0A0E";
			ApplyThemeColors();
			SaveSession();
			updateSelectedThemeColors();
		};
		adaptiveToggle = CreateSettingToggle("Adaptive Theme", "Dynamically change GUI colors based on the playing track's artwork.", _useAdaptiveTheme, new Thickness(0.0, 15.0, 0.0, 0.0));
		adaptiveToggle.Checked += delegate
		{
			_useAdaptiveTheme = true;
			ApplyThemeColors();
			SaveSession();
			updateSelectedThemeColors();
		};
		adaptiveToggle.Unchecked += delegate
		{
			_useAdaptiveTheme = false;
			ApplyThemeColors();
			SaveSession();
			updateSelectedThemeColors();
		};
		themePanel.Children.Add(adaptiveToggle);
		System.Windows.Controls.CheckBox hoverBordersToggle = CreateSettingToggle("Enable Hover Borders", "Shows a tinted border around cards and list items when hovering over them.", _enableHoverBorders, new Thickness(0.0, 20.0, 0.0, 0.0));
		hoverBordersToggle.Checked += delegate
		{
			_enableHoverBorders = true;
			SaveSession();
		};
		hoverBordersToggle.Unchecked += delegate
		{
			_enableHoverBorders = false;
			SaveSession();
		};
		themePanel.Children.Add(hoverBordersToggle);

		TextBlock brightnessLabel = new TextBlock
		{
			Text = "Theme Brightness",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"],
			FontSize = 14.0,
			Margin = new Thickness(0.0, 20.0, 0.0, 10.0)
		};
		TextBlock brightnessValueText = new TextBlock
		{
			Text = $"{Math.Round(_themeBrightness * 100)}%",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			FontSize = 14.0,
			VerticalAlignment = VerticalAlignment.Center
		};
		Slider brightnessSlider = new Slider
		{
			Style = (Style)FindResource("ModernSlider"),
			Minimum = 0.1,
			Maximum = 2.0,
			Value = _themeBrightness,
			TickFrequency = 0.1,
			IsSnapToTickEnabled = true,
			IsMoveToPointEnabled = true,
			Width = 250.0,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0.0, 0.0, 15.0, 0.0)
		};
		brightnessSlider.ValueChanged += delegate
		{
			_themeBrightness = Math.Round(brightnessSlider.Value, 1);
			brightnessValueText.Text = $"{Math.Round(_themeBrightness * 100)}%";
			ApplyThemeColors();
			SaveSession();
		};
		StackPanel brightnessPanel = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			Margin = new Thickness(0.0, 0.0, 0.0, 0.0)
		};
		brightnessPanel.Children.Add(brightnessSlider);
		brightnessPanel.Children.Add(brightnessValueText);
		themePanel.Children.Add(brightnessLabel);
		themePanel.Children.Add(brightnessPanel);

		pagePanel.Children.Add(CreateCard("Theme Customization", new UIElement[1] { themePanel }));

		System.Windows.Controls.CheckBox confettiToggle = CreateSettingToggle("Confetti", "Makes confetti appear when you hit a song!", _enableConfetti, new Thickness(0.0, 0.0, 0.0, 20.0));
		confettiToggle.Checked += delegate
		{
			_enableConfetti = true;
			SaveSession();
		};
		confettiToggle.Unchecked += delegate
		{
			_enableConfetti = false;
			SaveSession();
		};

		System.Windows.Controls.CheckBox vinylToggle = CreateSettingToggle("Spinning Vinyl Mode", "Makes the player thumbnail spin like a vinyl record while playing.", EnableVinylMode, new Thickness(0.0, 0.0, 0.0, 20.0));
		vinylToggle.Checked += delegate
		{
			EnableVinylMode = true;
			SaveSession();
			MainPlayerBarControl.UpdateVinylMode(true, _player != null && !_player.IsPlaying);
			UpdateSidebarVinylMode(true);
		};
		vinylToggle.Unchecked += delegate
		{
			EnableVinylMode = false;
			SaveSession();
			MainPlayerBarControl.UpdateVinylMode(false, _player != null && !_player.IsPlaying);
			UpdateSidebarVinylMode(false);
		};

		System.Windows.Controls.CheckBox wigglyToggle = CreateSettingToggle("Wiggly Progress Bar", "Makes the progress bar wavy instead of a straight line.", EnableWigglyProgress, new Thickness(0.0, 0.0, 0.0, 0.0));
		wigglyToggle.Checked += delegate
		{
			EnableWigglyProgress = true;
			SaveSession();
			MainPlayerBarControl.UpdateWigglyProgress(true);
		};
		wigglyToggle.Unchecked += delegate
		{
			EnableWigglyProgress = false;
			SaveSession();
			MainPlayerBarControl.UpdateWigglyProgress(false);
		};

		pagePanel.Children.Add(CreateCard("Fun", new UIElement[3] { confettiToggle, vinylToggle, wigglyToggle }));
		System.Windows.Controls.CheckBox gpuToggle = CreateSettingToggle("Disable GPU Hardware Acceleration", "Improves gaming performance when playing music in background.", _disableGPU, new Thickness(0.0, 0.0, 0.0, 20.0));
		gpuToggle.Checked += delegate
		{
			_disableGPU = true;
			SaveSession();
		};
		gpuToggle.Unchecked += delegate
		{
			_disableGPU = false;
			SaveSession();
		};
		TextBlock historyLabel = new TextBlock
		{
			Text = "History queue size (songs kept behind):",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"],
			FontSize = 14.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 5.0)
		};
		Border historyInput = CreateModernTextBox(_historySize.ToString(), delegate(string text)
		{
			if (int.TryParse(text, out var result) && result >= 0)
			{
				_historySize = result;
				SaveSession();
			}
		}, new Thickness(0.0, 0.0, 0.0, 20.0));
		TextBlock bufferLabel = new TextBlock
		{
			Text = "Forward queue size (songs loaded ahead):",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"],
			FontSize = 14.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 5.0)
		};
		Border bufferInput = CreateModernTextBox(_bufferSize.ToString(), delegate(string text)
		{
			if (int.TryParse(text, out var result) && result >= 0)
			{
				_bufferSize = result;
				_ = _ = _ = _ = MaintainPreloadBufferAsync();
				SaveSession();
			}
		}, new Thickness(0.0, 0.0, 0.0, 0.0));
		TextBlock playHistoryLabel = new TextBlock
		{
			Text = "Played songs history count:",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"],
			FontSize = 14.0,
			Margin = new Thickness(0.0, 15.0, 0.0, 5.0)
		};
		Border playHistoryInput = CreateModernTextBox(_playHistoryCount.ToString(), delegate(string text)
		{
			if (int.TryParse(text, out var result) && result >= 0)
			{
				_playHistoryCount = result;
				SaveSession();
			}
		}, new Thickness(0.0, 0.0, 0.0, 20.0));
		TextBlock homeLimitLabel = new TextBlock
		{
			Text = "Homepage sections to load:",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"],
			FontSize = 14.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 5.0)
		};
		Border homeLimitInput = CreateModernTextBox(_homeFeedLimit.ToString(), delegate(string text)
		{
			if (int.TryParse(text, out var result) && result > 0)
			{
				_homeFeedLimit = result;
				SaveSession();
			}
		}, new Thickness(0.0, 0.0, 0.0, 20.0));
		pagePanel.Children.Add(CreateCard("Advanced Tuning", new UIElement[9] { gpuToggle, historyLabel, historyInput, bufferLabel, bufferInput, playHistoryLabel, playHistoryInput, homeLimitLabel, homeLimitInput }));

		StackPanel filterPanel = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Vertical,
			Margin = new Thickness(0.0, 0.0, 0.0, 15.0)
		};
		filterPanel.Children.Add(new TextBlock
		{
			Text = "You can block categories by right-clicking their title on the Home Feed.",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"],
			FontSize = 13.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 15.0),
			TextWrapping = TextWrapping.Wrap
		});
		ItemsControl blockedList = new ItemsControl
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		};
		Action updateBlockedList = null;
		updateBlockedList = delegate
		{
			WrapPanel wrapPanel = new WrapPanel
			{
				Orientation = System.Windows.Controls.Orientation.Horizontal
			};
			if (_blockedCategories.Count == 0)
			{
				wrapPanel.Children.Add(new TextBlock
				{
					Text = "No blocked categories.",
					Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100)),
					FontStyle = FontStyles.Italic,
					Margin = new Thickness(0.0, 5.0, 0.0, 5.0)
				});
			}
			else
			{
				foreach (string bc in _blockedCategories.ToList())
				{
					Border chip = new Border
					{
						Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(15, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
						CornerRadius = new CornerRadius(13.0),
						Margin = new Thickness(0.0, 0.0, 10.0, 10.0),
						Padding = new Thickness(15.0, 6.0, 15.0, 6.0),
						Cursor = System.Windows.Input.Cursors.Hand
					};
					StackPanel stackPanel = new StackPanel
					{
						Orientation = System.Windows.Controls.Orientation.Horizontal,
						VerticalAlignment = VerticalAlignment.Center,
						IsHitTestVisible = false
					};
					stackPanel.Children.Add(new TextBlock
					{
						Text = bc,
						Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
						FontSize = 13.0,
						VerticalAlignment = VerticalAlignment.Center
					});
					stackPanel.Children.Add(new TextBlock
					{
						Text = "✕",
						Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"],
						FontSize = 12.0,
						FontWeight = FontWeights.Bold,
						Margin = new Thickness(10.0, 0.0, 0.0, 0.0),
						VerticalAlignment = VerticalAlignment.Center
					});
					chip.MouseEnter += delegate
					{
						FadeBorderBackgroundToColor(chip, System.Windows.Media.Color.FromArgb(35, byte.MaxValue, byte.MaxValue, byte.MaxValue));
					};
					chip.MouseLeave += delegate
					{
						FadeBorderBackgroundToColor(chip, System.Windows.Media.Color.FromArgb(15, byte.MaxValue, byte.MaxValue, byte.MaxValue));
					};
					chip.MouseLeftButtonDown += delegate
					{
						_blockedCategories.Remove(bc);
						SaveSession();
						updateBlockedList();
						UpdateTabVisibility();
					};
					chip.Child = stackPanel;
					wrapPanel.Children.Add(chip);
				}
			}
			blockedList.ItemsSource = new WrapPanel[1] { wrapPanel };
		};
		updateBlockedList();
		filterPanel.Children.Add(blockedList);
		StackPanel customBlockPanel = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
		};
		string currentCustomBlockText = "";
		Border customBlockInput = CreateModernTextBox("", delegate(string text)
		{
			currentCustomBlockText = text;
		}, new Thickness(0.0));
		customBlockInput.Width = 200.0;
		customBlockInput.ToolTip = "Block custom keyword";
		System.Windows.Controls.Button customBlockBtn = new System.Windows.Controls.Button
		{
			Content = "Block",
			Style = btnStyle,
			Margin = new Thickness(10.0, 0.0, 0.0, 0.0),
			Padding = new Thickness(15.0, 0.0, 15.0, 0.0),
			Cursor = System.Windows.Input.Cursors.Hand
		};
		customBlockBtn.Click += delegate
		{
			if (!string.IsNullOrWhiteSpace(currentCustomBlockText) && !_blockedCategories.Contains(currentCustomBlockText.Trim()))
			{
				_blockedCategories.Add(currentCustomBlockText.Trim());
				SaveSession();
				if (customBlockInput.Child is System.Windows.Controls.TextBox)
				{
					(customBlockInput.Child as System.Windows.Controls.TextBox).Text = "";
				}
				updateBlockedList();
				UpdateTabVisibility();
			}
		};
		customBlockPanel.Children.Add(customBlockInput);
		customBlockPanel.Children.Add(customBlockBtn);
		filterPanel.Children.Add(customBlockPanel);
		filterPanel.Children.Add(new TextBlock
		{
			Text = "Hidden Playlists & Albums",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			FontSize = 14.0,
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0.0, 20.0, 0.0, 10.0)
		});
		ItemsControl hiddenLibList = new ItemsControl
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		};
		Action updateHiddenLibList = null;
		updateHiddenLibList = delegate
		{
			WrapPanel wrapPanel = new WrapPanel
			{
				Orientation = System.Windows.Controls.Orientation.Horizontal
			};
			if (_hiddenLibraryItems.Count == 0)
			{
				wrapPanel.Children.Add(new TextBlock
				{
					Text = "No hidden items.",
					Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100)),
					FontStyle = FontStyles.Italic,
					Margin = new Thickness(0.0, 5.0, 0.0, 5.0)
				});
			}
			else
			{
				foreach (KeyValuePair<string, string> kvp in _hiddenLibraryItems.ToList())
				{
					Border chip = new Border
					{
						Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(15, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
						CornerRadius = new CornerRadius(13.0),
						Margin = new Thickness(0.0, 0.0, 10.0, 10.0),
						Padding = new Thickness(15.0, 6.0, 15.0, 6.0),
						Cursor = System.Windows.Input.Cursors.Hand
					};
					StackPanel stackPanel = new StackPanel
					{
						Orientation = System.Windows.Controls.Orientation.Horizontal,
						VerticalAlignment = VerticalAlignment.Center,
						IsHitTestVisible = false
					};
					stackPanel.Children.Add(new TextBlock
					{
						Text = kvp.Value,
						Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
						FontSize = 13.0,
						VerticalAlignment = VerticalAlignment.Center
					});
					stackPanel.Children.Add(new TextBlock
					{
						Text = "✕",
						Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"],
						FontSize = 12.0,
						FontWeight = FontWeights.Bold,
						Margin = new Thickness(10.0, 0.0, 0.0, 0.0),
						VerticalAlignment = VerticalAlignment.Center
					});
					chip.MouseEnter += delegate
					{
						FadeBorderBackgroundToColor(chip, System.Windows.Media.Color.FromArgb(35, byte.MaxValue, byte.MaxValue, byte.MaxValue));
					};
					chip.MouseLeave += delegate
					{
						FadeBorderBackgroundToColor(chip, System.Windows.Media.Color.FromArgb(15, byte.MaxValue, byte.MaxValue, byte.MaxValue));
					};
					chip.MouseLeftButtonDown += delegate
					{
						_hiddenLibraryItems.Remove(kvp.Key);
						SaveSession();
						updateHiddenLibList();
						_ = _ = _ = _ = LoadLibraryAsync();
					};
					chip.Child = stackPanel;
					wrapPanel.Children.Add(chip);
				}
			}
			hiddenLibList.ItemsSource = new WrapPanel[1] { wrapPanel };
		};
		updateHiddenLibList();
		filterPanel.Children.Add(hiddenLibList);
		pagePanel.Children.Add(CreateCard("Content Filtering", new UIElement[1] { filterPanel }));
		StackPanel maintenancePanel = new StackPanel();
		maintenancePanel.Children.Add(new TextBlock
		{
			Text = "Free up system memory by clearing the downloaded cover art image cache.",
			Foreground = System.Windows.Media.Brushes.Gray,
			FontSize = 13.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 15.0)
		});
		System.Windows.Controls.Button clearCacheBtn = new System.Windows.Controls.Button
		{
			Content = $"Clear Image Cache ({_imageCache.Count} items)",
			Style = btnStyle,
			Width = 250.0,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Left
		};
		clearCacheBtn.Click += delegate
		{
			_imageCache.Clear();
			GC.Collect();
			clearCacheBtn.Content = "Cache Cleared!";
		};
		maintenancePanel.Children.Add(clearCacheBtn);
		pagePanel.Children.Add(CreateCard("Maintenance", new UIElement[1] { maintenancePanel }));
		StackPanel aboutPanel = new StackPanel();
		StackPanel versionInfoPanel = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			Margin = new Thickness(0.0, 0.0, 0.0, 20.0)
		};
		Border logoBorder = new Border
		{
			Width = 64.0,
			Height = 64.0,
			CornerRadius = new CornerRadius(16.0),
			Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(10, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			BorderThickness = new Thickness(1.0),
			BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			Margin = new Thickness(0.0, 0.0, 20.0, 0.0)
		};
		System.Windows.Controls.Image logoImg = new System.Windows.Controls.Image
		{
			Source = new BitmapImage(new Uri("pack://application:,,,/Icons/highresapp.png")),
			Width = 40.0,
			Height = 40.0,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		logoBorder.Child = logoImg;
		StackPanel versionTextPanel = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center
		};
		versionTextPanel.Children.Add(new TextBlock
		{
			Text = "Spectre Music",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			FontSize = 24.0,
			FontWeight = FontWeights.Bold
		});
		versionTextPanel.Children.Add(new TextBlock
		{
			Text = "Version 1.0.0-alpha",
			Foreground = System.Windows.Media.Brushes.LightGray,
			FontSize = 14.0,
			Margin = new Thickness(0.0, 2.0, 0.0, 0.0)
		});
		versionTextPanel.Children.Add(new TextBlock
		{
			Text = "Made by Alpointernet",
			Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(140, 140, 140)),
			FontSize = 12.0,
			Margin = new Thickness(0.0, 2.0, 0.0, 0.0)
		});
		versionInfoPanel.Children.Add(logoBorder);
		versionInfoPanel.Children.Add(versionTextPanel);
		aboutPanel.Children.Add(versionInfoPanel);
		pagePanel.Children.Add(CreateCard("About Spectre", new UIElement[1] { aboutPanel }));
		await FadeInContentAsync(tId, delegate
		{
			ContentPanel.Children.Add(pagePanel);
			MainScrollViewer.ScrollToTop();
		});
		static Border CreateCard(string title, UIElement[] elements)
		{
			Border card = new Border
			{
				Background = (System.Windows.Media.Brush)System.Windows.Application.Current.MainWindow.Resources["CardBackground"],
				BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(15, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(12.0),
				Padding = new Thickness(25.0),
				Margin = new Thickness(0.0, 0.0, 0.0, 30.0)
			};
			StackPanel stack = new StackPanel
			{
				Children = { (UIElement)new TextBlock
				{
					Text = title,
					Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
					FontSize = 20.0,
					FontWeight = FontWeights.SemiBold,
					Margin = new Thickness(0.0, 0.0, 0.0, 25.0)
				} }
			};
			foreach (UIElement el in elements)
			{
				stack.Children.Add(el);
			}
			card.Child = stack;
			return card;
		}
		StackPanel CreateColorInput(string label, string currentValue, Action<string> onChange)
		{
			StackPanel p = new StackPanel
			{
				Orientation = System.Windows.Controls.Orientation.Horizontal,
				Margin = new Thickness(0.0, 0.0, 20.0, 15.0)
			};
			p.Children.Add(new TextBlock
			{
				Text = label,
				Foreground = System.Windows.Media.Brushes.LightGray,
				Width = 160.0,
				VerticalAlignment = VerticalAlignment.Center
			});
			Border colorPreview = new Border
			{
				Width = 24.0,
				Height = 24.0,
				CornerRadius = new CornerRadius(12.0),
				Margin = new Thickness(0.0, 0.0, 10.0, 0.0),
				BorderThickness = new Thickness(1.0),
				BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(50, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
				Cursor = System.Windows.Input.Cursors.Hand
			};
			try
			{
				colorPreview.Background = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(currentValue));
			}
			catch
			{
			}
			Border inputBorder = new Border
			{
				CornerRadius = new CornerRadius(13.0),
				Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(10, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
				BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(30, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
				BorderThickness = new Thickness(1.0)
			};
			System.Windows.Controls.TextBox input = new System.Windows.Controls.TextBox
			{
				Text = currentValue,
				Width = 85.0,
				Background = System.Windows.Media.Brushes.Transparent,
				Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
				BorderThickness = new Thickness(0.0),
				Padding = new Thickness(12.0, 6.0, 12.0, 6.0),
				VerticalAlignment = VerticalAlignment.Center
			};
			inputBorder.Child = input;
			input.TextChanged += delegate
			{
				try
				{
					colorPreview.Background = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(input.Text));
					onChange(input.Text);
				}
				catch
				{
				}
			};
			colorPreview.MouseLeftButtonDown += delegate
			{
				string text = ShowColorPickerDialog(input.Text);
				if (text != null)
				{
					input.Text = text;
				}
			};
			p.Children.Add(colorPreview);
			p.Children.Add(inputBorder);
			return p;
		}
		static Border CreateModernTextBox(string initialText, Action<string> onTextChanged, Thickness margin)
		{
			Border border = new Border
			{
				Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
				CornerRadius = new CornerRadius(11.0),
				BorderThickness = new Thickness(1.0),
				BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(30, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
				Margin = margin,
				Width = 120.0,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
				Padding = new Thickness(15.0, 4.0, 15.0, 4.0)
			};
			System.Windows.Controls.TextBox tb = new System.Windows.Controls.TextBox
			{
				Text = initialText,
				Background = System.Windows.Media.Brushes.Transparent,
				Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
				BorderThickness = new Thickness(0.0),
				FontSize = 14.0,
				VerticalAlignment = VerticalAlignment.Center,
				CaretBrush = System.Windows.Media.Brushes.White
			};
			tb.GotFocus += delegate
			{
				border.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 229, byte.MaxValue));
				border.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, byte.MaxValue, byte.MaxValue, byte.MaxValue));
			};
			tb.LostFocus += delegate
			{
				border.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(30, byte.MaxValue, byte.MaxValue, byte.MaxValue));
				border.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, byte.MaxValue, byte.MaxValue, byte.MaxValue));
			};
			tb.TextChanged += delegate
			{
				onTextChanged(tb.Text);
			};
			border.Child = tb;
			return border;
		}
		System.Windows.Controls.CheckBox CreateSettingToggle(string title, string description, bool isChecked, Thickness margin)
		{
			StackPanel panel = new StackPanel
			{
				Orientation = System.Windows.Controls.Orientation.Vertical
			};
			panel.Children.Add(new TextBlock
			{
				Text = title,
				FontSize = 14.0,
				Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"]
			});
			if (!string.IsNullOrEmpty(description))
			{
				panel.Children.Add(new TextBlock
				{
					Text = description,
					FontSize = 12.0,
					Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"],
					Margin = new Thickness(0.0, 4.0, 0.0, 0.0)
				});
			}
			return new System.Windows.Controls.CheckBox
			{
				Content = panel,
				IsChecked = isChecked,
				Style = toggleStyle,
				Margin = margin
			};
		}
	}

	private void SaveSession()
	{
		try
		{
			JsonObject obj = new JsonObject
			{
				["WindowWidth"] = base.Width,
				["WindowHeight"] = base.Height,
				["WindowLeft"] = base.Left,
				["WindowTop"] = base.Top,
				["WindowState"] = (int)base.WindowState,
				["SidebarMinimized"] = _isSidebarMinimized,
				["Volume"] = _player.Volume,
				["VideoId"] = _currentVideoId ?? "",
				["Title"] = _currentTitle ?? "",
				["Artist"] = _currentArtist ?? "",
				["Album"] = _currentAlbum ?? "",
				["AlbumId"] = _currentAlbumId ?? "",
				["ThumbUrl"] = _currentThumbUrl ?? "",
				["PrefetchEnabled"] = _prefetchEnabled,
				["LoudnessNormalization"] = _loudnessNormalization,
				["CrossfadeMs"] = _crossfadeMs,
				["ReduceAnimations"] = _reduceAnimations,
				["DisableSmoothScrolling"] = _disableSmoothScrolling,
				["EnableSMTC"] = _enableSMTC,
				["EnableDiscordRpc"] = _enableDiscordRpc,
				["EnableStatusIndicator"] = _enableStatusIndicator,
				["ShowTaskbarMediaControls"] = _showTaskbarMediaControls,
				["DiscordClientId"] = _discordClientId,
				["DiscordIconUrl"] = _discordIconUrl,
				["LastFmApiKey"] = LastFmManager.ApiKey,
				["LastFmSharedSecret"] = LastFmManager.SharedSecret,
				["LastFmSessionKey"] = LastFmManager.SessionKey,
				["LastFmUsername"] = LastFmManager.Username,
				["AlwaysOnTop"] = _alwaysOnTop,
				["NetworkCacheMs"] = _networkCacheMs,
				["DisableGPU"] = _disableGPU,
				["VolumeStep"] = _volumeStep,
				["EnableLocalMusic"] = _enableLocalMusic,
				["LocalMusicPath"] = _LocalMusicPath,
				["EnableDownloads"] = _enableDownloads,
				["EnableConfetti"] = _enableConfetti,
				["EnableVinylMode"] = EnableVinylMode,
				["EnableWigglyProgress"] = EnableWigglyProgress,
				["EnableBackgroundParticles"] = EnableBackgroundParticles,
				["DownloadsPath"] = _downloadsPath,
				["ExcludePlainVideoResults"] = _excludePlainVideoResults,
				["UseAdaptiveTheme"] = _useAdaptiveTheme,
				["AccentColor1"] = _accentColor1,
				["AccentColor2"] = _accentColor2,
				["BgGrad1"] = _bgGrad1,
				["BgGrad2"] = _bgGrad2,
				["BgGrad3"] = _bgGrad3,
				["CardBg"] = _cardBg,
				["SidebarBg"] = _sidebarBg,
				["TopbarBg"] = _topbarBg,
				["BottomBarBg"] = _bottombarBg,
				["HistorySize"] = _historySize,
				["PlayHistoryCount"] = _playHistoryCount,
				["BufferSize"] = _bufferSize,
				["HomeFeedLimit"] = _homeFeedLimit,
				["GroupLibraryTabs"] = _groupLibraryTabs,
				["EnableHoverBorders"] = _enableHoverBorders,
				["ThemeBrightness"] = _themeBrightness,
				["QueueIndex"] = _currentQueueIndex,
				["OriginalQueueSize"] = _originalQueueSize,
				["RecentSearches"] = JsonSerializer.SerializeToNode(_recentSearches)!.AsArray(),
				["BlockedCategories"] = JsonSerializer.SerializeToNode(_blockedCategories)!.AsArray(),
				["HiddenLibraryItems"] = JsonSerializer.SerializeToNode(_hiddenLibraryItems)!.AsObject(),
				["CustomFontPath"] = _customFontPath,
				["SavedRadios"] = JsonSerializer.SerializeToNode(_savedRadios)!.AsArray()
			};
			string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spectre", "session.json");
			string tmpPath = path + ".tmp";
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
			string jsonStr = obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
			string minifiedQueue = ((_currentQueue != null) ? _currentQueue.ToJsonString(new JsonSerializerOptions()) : "null");
			int lastBrace = jsonStr.LastIndexOf('}');
			if (lastBrace != -1)
			{
				jsonStr = jsonStr.Insert(lastBrace, ",\r\n  \"Queue\": " + minifiedQueue + "\r\n");
			}
			System.IO.File.WriteAllText(tmpPath, jsonStr);
			System.IO.File.Move(tmpPath, path, overwrite: true);
		}
		catch
		{
		}
	}

	private void ApplyCustomFont()
	{
		if (string.IsNullOrEmpty(_customFontPath) || !System.IO.File.Exists(_customFontPath))
		{
			base.FontFamily = new System.Windows.Media.FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#Montserrat, Segoe UI Emoji, Segoe UI Symbol");
			return;
		}
		try
		{
			string familyName = new GlyphTypeface(new Uri(_customFontPath)).FamilyNames.Values.FirstOrDefault();
			if (!string.IsNullOrEmpty(familyName))
			{
				base.FontFamily = new System.Windows.Media.FontFamily(new Uri(_customFontPath), "./#" + familyName + ", Segoe UI Emoji, Segoe UI Symbol");
			}
			else
			{
				base.FontFamily = new System.Windows.Media.FontFamily(new Uri(_customFontPath), "./#, Segoe UI Emoji, Segoe UI Symbol");
			}
		}
		catch
		{
			base.FontFamily = new System.Windows.Media.FontFamily(new Uri("pack://application:,,,/"), "./Fonts/#Montserrat, Segoe UI Emoji, Segoe UI Symbol");
		}
	}

		private void LoadWindowBounds()
	{
		try
		{
			string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spectre", "session.json");
			if (System.IO.File.Exists(path))
			{
				JsonObject json = JsonNode.Parse(System.IO.File.ReadAllText(path))!.AsObject();
				if (json["WindowWidth"] != null) base.Width = (double)json["WindowWidth"];
				if (json["WindowHeight"] != null) base.Height = (double)json["WindowHeight"];
				if (json["WindowLeft"] != null) base.Left = (double)json["WindowLeft"];
				if (json["WindowTop"] != null) base.Top = (double)json["WindowTop"];
				if (json["WindowState"] != null) base.WindowState = (WindowState)(int)json["WindowState"];
				if (json["AlwaysOnTop"] != null)
				{
					_alwaysOnTop = (bool)json["AlwaysOnTop"];
	
				}
				if (base.Left < SystemParameters.VirtualScreenLeft) base.Left = SystemParameters.VirtualScreenLeft;
				if (base.Top < SystemParameters.VirtualScreenTop) base.Top = SystemParameters.VirtualScreenTop;
			}
		}
		catch { }
	}

	private async Task LoadLastSessionAsync()
	{
		try
		{
			string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spectre", "session.json");
			if (System.IO.File.Exists(path))
			{
				JsonObject json = JsonNode.Parse(await System.IO.File.ReadAllTextAsync(path))!.AsObject();
				if (json["RecentSearches"] != null && json["RecentSearches"] is JsonArray arr)
				{
					_recentSearches = (from x in arr
						select (string?)x into x
						where !string.IsNullOrEmpty(x)
						select x).ToList();
				}
				if (json["SavedRadios"] != null)
				{
					try
					{
						_savedRadios = json["SavedRadios"].Deserialize<List<RadioStation>>() ?? new List<RadioStation>();
						if (_savedRadios.Any((RadioStation r) => r.Streams == null || r.Streams.Count == 0 || r.Streams.Any((RadioStream s) => s.Url.Contains("zeno.fm") || s.Url.Contains("streamafrica"))))
						{
							_savedRadios.Clear();
						}
					}
					catch
					{
					}
				}
				if (_savedRadios.Count == 0)
				{
					_savedRadios = GetDefaultRadios();
				}
				if (json["BlockedCategories"] != null)
				{
					_blockedCategories = json["BlockedCategories"].Deserialize<List<string>>() ?? new List<string>();
				}
				if (json["HiddenLibraryItems"] != null)
				{
					_hiddenLibraryItems = json["HiddenLibraryItems"].Deserialize<Dictionary<string, string>>() ?? new Dictionary<string, string>();
				}
				if (json["CustomFontPath"] != null)
				{
					_customFontPath = ((string?)json["CustomFontPath"]) ?? "";
				}
				ApplyCustomFont();
				if (json["Volume"] != null)
				{
					double vol = (double)json["Volume"];
					MainPlayerBarControl.VolumeSliderRef.Value = vol;
				}
				if (json["WindowWidth"] != null)
				{
					base.Width = (double)json["WindowWidth"];
				}
				if (json["WindowHeight"] != null)
				{
					base.Height = (double)json["WindowHeight"];
				}
				if (json["WindowLeft"] != null)
				{
					base.Left = (double)json["WindowLeft"];
				}
				if (json["WindowTop"] != null)
				{
					base.Top = (double)json["WindowTop"];
				}
				if (json["WindowState"] != null)
				{
					base.WindowState = (WindowState)(int)json["WindowState"];
				}
				if (json["SidebarMinimized"] != null)
				{
					_isSidebarMinimized = (bool)json["SidebarMinimized"];
					if (_isSidebarMinimized)
					{
						SidebarColumn.Width = new GridLength(_minimizedSidebarWidth);
						MainSidebar.SidebarTitlePanelRef.Margin = new Thickness(24.0, MainSidebar.SidebarTitlePanelRef.Margin.Top, 0.0, MainSidebar.SidebarTitlePanelRef.Margin.Bottom);
						MainSidebar.HomeNavPanelRef.Margin = new Thickness(8.0, MainSidebar.HomeNavPanelRef.Margin.Top, 8.0, MainSidebar.HomeNavPanelRef.Margin.Bottom);
						MainSidebar.LibraryPanelRef.Margin = new Thickness(8.0, MainSidebar.LibraryPanelRef.Margin.Top, 8.0, MainSidebar.LibraryPanelRef.Margin.Bottom);
						UpdateSidebarTextOpacity(0.0);
					}
				}
				if (base.Left < SystemParameters.VirtualScreenLeft)
				{
					base.Left = SystemParameters.VirtualScreenLeft;
				}
				if (base.Top < SystemParameters.VirtualScreenTop)
				{
					base.Top = SystemParameters.VirtualScreenTop;
				}
				if (json["PrefetchEnabled"] != null)
				{
					_prefetchEnabled = (bool)json["PrefetchEnabled"];
				}
				if (json["LoudnessNormalization"] != null)
				{
					_loudnessNormalization = (bool)json["LoudnessNormalization"];
				}
				if (json["CrossfadeMs"] != null)
				{
					_crossfadeMs = (int)json["CrossfadeMs"];
					if (_player != null)
					{
						_player.CrossfadeMs = _crossfadeMs;
					}
				}
				if (json["ReduceAnimations"] != null)
				{
					_reduceAnimations = (bool)json["ReduceAnimations"];
				}
				if (json["DisableSmoothScrolling"] != null)
				{
					_disableSmoothScrolling = (bool)json["DisableSmoothScrolling"];
				}
				if (json["EnableHoverBorders"] != null)
				{
					_enableHoverBorders = (bool)json["EnableHoverBorders"];
				}
				if (json["EnableSMTC"] != null)
				{
					_enableSMTC = (bool)json["EnableSMTC"];
				}
				if (json["EnableDiscordRpc"] != null)
				{
					_enableDiscordRpc = (bool)json["EnableDiscordRpc"];
				}
				if (json["EnableStatusIndicator"] != null)
				{
					_enableStatusIndicator = (bool)json["EnableStatusIndicator"];
				}
				if (json["ShowTaskbarMediaControls"] != null)
				{
					_showTaskbarMediaControls = (bool)json["ShowTaskbarMediaControls"];
				}
				UpdateTaskbarControlsVisibility();
				if (json["DiscordClientId"] != null)
				{
					_discordClientId = (string?)json["DiscordClientId"];
					if (_discordClientId == "345229890980937739")
					{
						_discordClientId = "1507766775104671996";
					}
				}
				if (json["DiscordIconUrl"] != null)
				{
					_discordIconUrl = (string?)json["DiscordIconUrl"];
					if (string.IsNullOrEmpty(_discordIconUrl))
					{
						_discordIconUrl = "https://images2.imgbox.com/61/29/T0YBRW3n_o.png";
					}
				}
				if (json["LastFmApiKey"] != null)
				{
					LastFmManager.ApiKey = (string?)json["LastFmApiKey"];
				}
				if (json["LastFmSharedSecret"] != null)
				{
					LastFmManager.SharedSecret = (string?)json["LastFmSharedSecret"];
				}
				if (json["LastFmSessionKey"] != null)
				{
					LastFmManager.SessionKey = (string?)json["LastFmSessionKey"];
				}
				if (json["LastFmUsername"] != null)
				{
					LastFmManager.Username = (string?)json["LastFmUsername"];
				}
				if (json["AlwaysOnTop"] != null)
				{
					_alwaysOnTop = (bool)json["AlwaysOnTop"];
				}
				if (json["NetworkCacheMs"] != null)
				{
					_networkCacheMs = (int)json["NetworkCacheMs"];
				}
				if (json["DisableGPU"] != null)
				{
					_disableGPU = (bool)json["DisableGPU"];
				}
				if (json["VolumeStep"] != null)
				{
					_volumeStep = (int)json["VolumeStep"];
				}
				if (json["EnableLocalMusic"] != null)
				{
					_enableLocalMusic = (bool)json["EnableLocalMusic"];
				}
				if (json["LocalMusicPath"] != null)
				{
					_LocalMusicPath = (string?)json["LocalMusicPath"];
				}
				if (json["EnableDownloads"] != null)
				{
					_enableDownloads = (bool)json["EnableDownloads"];
				}
				if (json["EnableConfetti"] != null)
				{
					_enableConfetti = (bool)json["EnableConfetti"];
				}
				if (json["EnableVinylMode"] != null)
				{
					EnableVinylMode = (bool)json["EnableVinylMode"];
				}
				if (json["EnableWigglyProgress"] != null)
				{
					EnableWigglyProgress = (bool)json["EnableWigglyProgress"];
				}
				if (json["EnableBackgroundParticles"] != null)
				{
					EnableBackgroundParticles = (bool)json["EnableBackgroundParticles"];
				}
				if (json["DownloadsPath"] != null)
				{
					_downloadsPath = (string?)json["DownloadsPath"];
				}
				if (json["ExcludePlainVideoResults"] != null)
				{
					_excludePlainVideoResults = (bool)json["ExcludePlainVideoResults"];
				}
				BackendService.Instance.ExcludePlainVideoResults = _excludePlainVideoResults;
				if (json["UseAdaptiveTheme"] != null)
				{
					_useAdaptiveTheme = (bool)json["UseAdaptiveTheme"];
				}
				if (json["AccentColor1"] != null)
				{
					_accentColor1 = (string?)json["AccentColor1"];
				}
				if (json["ThemeBrightness"] != null)
				{
					_themeBrightness = (double)json["ThemeBrightness"];
				}
				if (json["AccentColor2"] != null)
				{
					_accentColor2 = (string?)json["AccentColor2"];
				}
				if (json["BgGrad1"] != null)
				{
					_bgGrad1 = (string?)json["BgGrad1"];
				}
				if (json["BgGrad2"] != null)
				{
					_bgGrad2 = (string?)json["BgGrad2"];
				}
				if (json["BgGrad3"] != null)
				{
					_bgGrad3 = (string?)json["BgGrad3"];
				}
				if (json["CardBg"] != null)
				{
					_cardBg = (string?)json["CardBg"];
				}
				if (json["SidebarBg"] != null)
				{
					_sidebarBg = (string?)json["SidebarBg"];
				}
				if (json["TopbarBg"] != null)
				{
					_topbarBg = (string?)json["TopbarBg"];
				}
				if (json["BottomBarBg"] != null)
				{
					_bottombarBg = (string?)json["BottomBarBg"];
				}
				if (json["HistorySize"] != null)
				{
					_historySize = (int)json["HistorySize"];
				}
				if (json["PlayHistoryCount"] != null)
				{
					_playHistoryCount = (int)json["PlayHistoryCount"];
				}
				if (json["BufferSize"] != null)
				{
					_bufferSize = (int)json["BufferSize"];
				}
				if (json["HomeFeedLimit"] != null)
				{
					_homeFeedLimit = (int)json["HomeFeedLimit"];
				}
				if (json["GroupLibraryTabs"] != null)
				{
					_groupLibraryTabs = (bool)json["GroupLibraryTabs"];
				}
				UpdateTabVisibility(instant: true);
				ApplyThemeColors();

				if (!_enableSMTC)
				{
					if (_smtcPlayer != null)
					{
						_smtcPlayer.Dispose();
						_smtcPlayer = null;
						_smtc = null;
					}
				}
				else if (_smtcPlayer == null)
				{
					InitSMTC();
				}
				if (!_enableDiscordRpc)
				{
					DeinitDiscordRPC();
				}
				else
				{
					InitDiscordRPC();
				}
				MainTopbarControl.StatusLabelRef.Visibility = ((!_enableStatusIndicator) ? Visibility.Collapsed : Visibility.Visible);
				if (_disableGPU)
				{
					RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
				}
				string videoId = ((string?)json["VideoId"]) ?? "";
				string title = ((string?)json["Title"]) ?? "";
				string artist = ((string?)json["Artist"]) ?? "";
				string album = ((string?)json["Album"]) ?? "";
				string albumId = ((string?)json["AlbumId"]) ?? "";
				string thumbUrl = ((string?)json["ThumbUrl"]) ?? "";
				if (string.IsNullOrEmpty(videoId))
				{
					return;
				}
				_currentVideoId = videoId;
				_currentTitle = title;
				_currentArtist = artist;
				_currentAlbum = album;
				_currentAlbumId = albumId;
				_currentThumbUrl = thumbUrl;
				bool isRadio = videoId.StartsWith("radio:");
				SetTimelineVisibility(!isRadio, animate: false);
				if (json["Queue"] != null)
				{
					_currentQueue = json["Queue"] as JsonArray;
				}
				if (json["QueueIndex"] != null)
				{
					_currentQueueIndex = (int)json["QueueIndex"];
				}
				if (json["OriginalQueueSize"] != null)
				{
					_originalQueueSize = (int)json["OriginalQueueSize"];
				}
				if (_currentQueue == null || _currentQueue.Count == 0)
				{
					_currentQueue = new JsonArray(new JsonObject
					{
						["videoId"] = videoId,
						["title"] = title,
						["artists"] = new JsonArray(new JsonObject { ["name"] = artist }),
						["album"] = new JsonObject
						{
							["name"] = album,
							["id"] = albumId
						},
						["thumbnails"] = new JsonArray(new JsonObject { ["url"] = thumbUrl })
					});
					_currentQueueIndex = 0;
					_originalQueueSize = 1;
				}
				JsonArray sessionArtistsData = null;
				if (_currentQueueIndex >= 0 && _currentQueueIndex < _currentQueue.Count)
				{
					JsonNode qItem = _currentQueue[_currentQueueIndex];
					if ((string?)qItem["videoId"] == videoId)
					{
						sessionArtistsData = qItem["artists"] as JsonArray;
					}
				}
				base.Title = "Spectre";
				PlayerBarViewModel vm = App.Current.PlayerBarViewModel;
				if (vm != null)
				{
					vm.Title = title;
				}
				PopulateArtistLinks(MainPlayerBarControl.PlayerArtistPanelRef, artist, 12, sessionArtistsData);
				ContextMenu ctx = CreateSongContextMenu(videoId, "", "", null, hideLikedToggle: false, title, artist, thumbUrl, _currentAlbumId, _currentAlbum);
				MainPlayerBarControl.PlayerInfoPanelRef.ContextMenu = ctx;
				MainPlayerBarControl.PlayerTitleRef.ContextMenu = ctx;
				MainPlayerBarControl.PlayerArtistPanelRef.ContextMenu = ctx;
				UpdateSMTCMetadata(title, artist, thumbUrl);
				if (!string.IsNullOrEmpty(thumbUrl) && _imageCache.TryGetValue(thumbUrl, out BitmapImage cachedBmp))
				{
					UpdatePlayerThumbnail(cachedBmp);
				}
				else if (!string.IsNullOrEmpty(thumbUrl))
				{
					_ = Task.Run(async delegate
					{
						_ = 1;
						try
						{
							byte[] bytes = await BackendService.Instance.DownloadImageAsync(thumbUrl, CancellationToken.None);
							await base.Dispatcher.InvokeAsync(delegate
							{
								try
								{
									MemoryStream streamSource = new MemoryStream(bytes);
									BitmapImage bitmapImage = new BitmapImage();
									bitmapImage.BeginInit();
									bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
									bitmapImage.DecodePixelWidth = 600;
									bitmapImage.StreamSource = streamSource;
									bitmapImage.EndInit();
									bitmapImage.Freeze();
									if (_imageCache.Count > 100)
									{
										_imageCache.Clear();
									}
									_imageCache[thumbUrl] = bitmapImage;
									UpdatePlayerThumbnail(bitmapImage);
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
				_ = MaintainPreloadBufferAsync();
				_pauseRequested = true;
				_ = MaintainPreloadBufferAsync();
				_pauseRequested = true;
				_player.Stop();
				_ = GetStreamForPlaybackAsync(videoId, CancellationToken.None).ContinueWith(delegate(Task<PlaybackStreamInfo> t)
				{
					if (t.Result != null)
					{
						base.Dispatcher.Invoke(delegate
						{
							_currentStreamUrl = t.Result.Url;
							_crossfadeTriggeredForCurrentTrack = false;
							_player.Play(t.Result.Url);
							_player.Pause();
						});
					}
				});
			}
			MainSearchOverlay.UpdateRecentSearches(_recentSearches);
			MainPlayerBarControl.UpdateVinylMode(EnableVinylMode, _player != null && !_player.IsPlaying);
			UpdateSidebarVinylMode(EnableVinylMode);
			MainPlayerBarControl.UpdateWigglyProgress(EnableWigglyProgress);
		}
		catch
		{
		}
	}
}



