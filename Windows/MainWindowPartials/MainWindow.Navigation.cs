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
	private void PushCurrentPageToHistory()
	{
		if (ContentPanel.Children.Count <= 0)
		{
			return;
		}
		UIElement currentElement = ContentPanel.Children[0];
		if (_backHistory.Count > 0 && _backHistory.Peek().Element == currentElement)
		{
			return;
		}
		_backHistory.Push((_currentPageId, currentElement, MainScrollViewer.VerticalOffset, _currentReloadAction, _isLoadingContent));
		_forwardHistory.Clear();
		if (_backHistory.Count > 15)
		{
			(string, UIElement, double, Action, bool)[] arr = _backHistory.ToArray();
			_backHistory.Clear();
			for (int i = 14; i >= 0; i--)
			{
				_backHistory.Push(arr[i]);
			}
		}
		UpdateNavButtons();
	}

	private async void BackBtn_Click(object sender, RoutedEventArgs e)
	{
		if (_backHistory.Count <= 0)
		{
			return;
		}
		if (ContentPanel.Children.Count > 0)
		{
			_forwardHistory.Push((_currentPageId, ContentPanel.Children[0], MainScrollViewer.VerticalOffset, _currentReloadAction, _isLoadingContent));
		}
		(string Id, UIElement Element, double ScrollOffset, Action Reload, bool Loading) previous = _backHistory.Pop();
		_currentPageId = previous.Id;
		_currentReloadAction = previous.Reload;
		if (!previous.Loading || previous.Reload == null)
		{
			await FadeInContentAsync(await FadeOutContentAsync(), delegate
			{
				ContentPanel.Children.Add(previous.Element);
				if (_pageVirtualizationCache.TryGetValue(previous.Id, out (List<Border>, List<Func<UIElement>>) value))
				{
					_lazyVirtualizationElements.AddRange(value.Item1);
					_lazyVirtualizationActions.AddRange(value.Item2);
				}
				UpdateSidebarHighlight();
				UpdateNavButtons();
				MainScrollViewer.ScrollToVerticalOffset(previous.ScrollOffset);
			});
		}
		else
		{
			previous.Reload();
			UpdateSidebarHighlight();
			UpdateNavButtons();
		}
	}

	private async void ForwardBtn_Click(object sender, RoutedEventArgs e)
	{
		if (_forwardHistory.Count <= 0)
		{
			return;
		}
		if (ContentPanel.Children.Count > 0)
		{
			_backHistory.Push((_currentPageId, ContentPanel.Children[0], MainScrollViewer.VerticalOffset, _currentReloadAction, _isLoadingContent));
		}
		(string Id, UIElement Element, double ScrollOffset, Action Reload, bool Loading) next = _forwardHistory.Pop();
		_currentPageId = next.Id;
		_currentReloadAction = next.Reload;
		if (!next.Loading || next.Reload == null)
		{
			await FadeInContentAsync(await FadeOutContentAsync(), delegate
			{
				ContentPanel.Children.Add(next.Element);
				if (_pageVirtualizationCache.TryGetValue(next.Id, out (List<Border>, List<Func<UIElement>>) value))
				{
					_lazyVirtualizationElements.AddRange(value.Item1);
					_lazyVirtualizationActions.AddRange(value.Item2);
				}
				UpdateSidebarHighlight();
				UpdateNavButtons();
				MainScrollViewer.ScrollToVerticalOffset(next.ScrollOffset);
			});
		}
		else
		{
			next.Reload();
			UpdateSidebarHighlight();
			UpdateNavButtons();
		}
	}

	private void PlaylistsNavBtn_Click(object sender, MouseButtonEventArgs e)
	{
		if (e != null)
		{
			e.Handled = true;
			if (e.ChangedButton != MouseButton.Left)
			{
				return;
			}
		}
		_ = _ = _ = _ = LoadPlaylistsPageAsync(forceReload: false);
	}

	private void AlbumsNavBtn_Click(object sender, MouseButtonEventArgs e)
	{
		if (e != null)
		{
			e.Handled = true;
			if (e.ChangedButton != MouseButton.Left)
			{
				return;
			}
		}
		_ = _ = _ = _ = LoadAlbumsPageAsync(forceReload: false);
	}

	private async void HistoryBtn_Click(object sender, RoutedEventArgs e)
	{
		if (_currentPageId == "history")
		{
			return;
		}
		PushCurrentPageToHistory();
		_currentPageId = "history";
		UpdateSidebarHighlight();
		int tId = await FadeOutContentAsync();
		StackPanel pagePanel = new StackPanel
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 80.0)
		};
		pagePanel.Children.Add(new TextBlock
		{
			Text = "Listening History",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			FontSize = 36.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 20.0, 0.0, 30.0)
		});
		if (!System.IO.File.Exists(BackendService.AuthFilePath))
		{
			StackPanel loginRequiredPanel = new StackPanel
			{
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0.0, 60.0, 0.0, 0.0)
			};
			loginRequiredPanel.Children.Add(new System.Windows.Controls.Image
			{
				Source = (System.Windows.Media.DrawingImage)System.Windows.Application.Current.FindResource("lockIcon"),
				Width = 52.0,
				Height = 52.0,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
				Margin = new Thickness(0.0, 0.0, 0.0, 16.0),
				Opacity = 0.7
			});
			loginRequiredPanel.Children.Add(new TextBlock
			{
				Text = "You need to be logged in to view your history.",
				Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"],
				FontSize = 16.0,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
				Margin = new Thickness(0.0, 0.0, 0.0, 8.0),
				TextWrapping = TextWrapping.Wrap,
				TextAlignment = TextAlignment.Center
			});
			loginRequiredPanel.Children.Add(new TextBlock
			{
				Text = "Go to Account to connect your Google account.",
				Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
				FontSize = 13.0,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
				TextWrapping = TextWrapping.Wrap,
				TextAlignment = TextAlignment.Center
			});
			pagePanel.Children.Add(loginRequiredPanel);
			await FadeInContentAsync(tId, delegate
			{
				ContentPanel.Children.Add(pagePanel);
			});
			return;
		}
		try
		{
			if ((await BackendService.Instance.GetHistoryAsync(CancellationToken.None))["data"] is JsonArray { Count: >0 } items)
			{
				pagePanel.Children.Add(CreateListHeader(hasNumber: true, hasAlbum: true, hasDuration: true));
				StackPanel trackListPanel = new StackPanel
				{
					Margin = new Thickness(0.0, 0.0, 0.0, 20.0)
				};
				int count = 0;
				foreach (JsonNode item in items)
				{
					if (count >= _playHistoryCount)
					{
						break;
					}
					string title = ((string?)item["title"]) ?? "";
					string videoId = ((string?)item["videoId"]) ?? "";
					string played = ((string?)item["plays"]) ?? ((string?)item["album"]?["name"]) ?? ((string?)item["album"]) ?? "";
					string duration = ((string?)item["duration"]) ?? "";
					JsonArray artistsArr = item["artists"] as JsonArray;
					string artistStr = "";
					if (artistsArr != null)
					{
						List<string> artistNames = new List<string>();
						foreach (JsonNode a in artistsArr)
						{
							artistNames.Add(((string?)a["name"]) ?? "");
						}
						artistStr = string.Join(", ", artistNames);
					}
					JsonArray thumbArr = item["thumbnails"] as JsonArray;
					string thumbUrl = "";
					if (thumbArr != null && thumbArr.Count > 0)
					{
						thumbUrl = ((string?)thumbArr[thumbArr.Count - 1]["url"]) ?? "";
					}
					string albumId = ((string?)item["album"]?["id"]) ?? "";
					if (!string.IsNullOrEmpty(videoId) && !string.IsNullOrEmpty(title))
					{
						Border itemBorder = CreateTrackRow(videoId, title, artistStr, thumbUrl, (count + 1).ToString(), played, albumId, duration, "history");
						trackListPanel.Children.Add(itemBorder);
						count++;
					}
				}
				if (count == 0)
				{
					pagePanel.Children.Add(new TextBlock
					{
						Text = "No listening history found.",
						Foreground = System.Windows.Media.Brushes.Gray,
						FontSize = 16.0
					});
				}
				else
				{
					pagePanel.Children.Add(trackListPanel);
				}
			}
			else
			{
				pagePanel.Children.Add(new TextBlock
				{
					Text = "No listening history found.",
					Foreground = System.Windows.Media.Brushes.Gray,
					FontSize = 16.0
				});
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			await FadeInContentAsync(tId, delegate
			{
				ShowGlobalError("Failed to load history", ex2.Message, delegate
				{
					_ = _ = _ = _ = LoadLibraryAsync();
					_currentPageId = "";
					HistoryBtn_Click(null, null);
				});
			});
			return;
		}
		await FadeInContentAsync(tId, delegate
		{
			ContentPanel.Children.Add(pagePanel);
			MainScrollViewer.ScrollToTop();
		});
	}

	public void NavigateToPage(string pageId)
	{
		switch (pageId)
		{
		case "home":
			HomeNavBtn_Click(this, null);
			break;
		case "explore":
			ExploreNavBtn_Click(this, null);
			break;
		case "radio_page":
			RadioNavBtn_Click(this, null);
			break;
		case "playlists_page":
			PlaylistsNavBtn_Click(this, null);
			break;
		case "albums_page":
			AlbumsNavBtn_Click(this, null);
			break;
		case "local_files":
			LocalNavBtn_Click(this, null);
			break;
		case "stats_page":
			StatsNavBtn_Click(this, null);
			break;
		}
	}

	private async void HomeNavBtn_Click(object sender, MouseButtonEventArgs e)
	{
		if (e != null)
		{
			e.Handled = true;
		}
		bool force = _currentPageId == "home";
		if (!force)
		{
			PushCurrentPageToHistory();
		}
		_currentPageId = "home";
		UpdateSidebarHighlight();
		await LoadHomeFeedAsync(force);
	}

	private async void ExploreNavBtn_Click(object sender, MouseButtonEventArgs e)
	{
		if (e != null)
		{
			e.Handled = true;
		}
		bool force = _currentPageId == "explore";
		if (!force)
		{
			PushCurrentPageToHistory();
		}
		_currentPageId = "explore";
		UpdateSidebarHighlight();
		await LoadExploreFeedAsync(force);
	}

	private async void RadioNavBtn_Click(object sender, MouseButtonEventArgs e)
	{
		if (e != null)
		{
			e.Handled = true;
		}
		bool force = _currentPageId == "radio_page";
		if (!force)
		{
			PushCurrentPageToHistory();
		}
		_currentPageId = "radio_page";
		UpdateSidebarHighlight();
		await LoadRadioFeedAsync(force);
	}

	private void LocalNavBtn_Click(object sender, MouseButtonEventArgs e)
	{
		if (e != null)
		{
			e.Handled = true;
		}
		OpenLocalMusicPage();
	}

	private async void StatsNavBtn_Click(object sender, MouseButtonEventArgs e)
	{
		if (e != null)
		{
			e.Handled = true;
		}
		bool force = _currentPageId == "stats_page";
		if (!force)
		{
			PushCurrentPageToHistory();
		}
		_currentPageId = "stats_page";
		UpdateSidebarHighlight();
		await LoadStatsPageAsync(force);
	}
}




