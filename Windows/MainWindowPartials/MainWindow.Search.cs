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
	private void MainSearchControl_SearchRequested(object sender, string query)
	{
		MainSearchOverlay.Hide();
		MainTopbarControl.MainSearchControlRef.ClearFocus();
		_ = _ = _ = _ = PerformSearchAsync(query);
	}

	private void MainSearchControl_GotFocus(object sender, EventArgs e)
	{
		MainSearchOverlay.Show();
	}

	private void MainSearchControl_LostFocus(object sender, EventArgs e)
	{
		MainSearchOverlay.Hide();
	}

	private void MainSearchOverlay_RecentSearchClicked(object sender, string query)
	{
		MainTopbarControl.MainSearchControlRef.SetText(query);
		MainSearchOverlay.Hide();
		MainTopbarControl.MainSearchControlRef.ClearFocus();
		_ = _ = _ = _ = PerformSearchAsync(query);
	}

	private void MainSearchOverlay_RecentSearchRemoved(object sender, string query)
	{
		if (_recentSearches != null && _recentSearches.Contains(query))
		{
			_recentSearches.Remove(query);
			SaveSession();
			MainSearchOverlay.UpdateRecentSearches(_recentSearches);
		}
	}

	private async Task PerformSearchAsync(string query)
	{
		if (_recentSearches.Contains(query))
		{
			_recentSearches.Remove(query);
		}
		_recentSearches.Insert(0, query);
		if (_recentSearches.Count > 10)
		{
			_recentSearches.RemoveAt(_recentSearches.Count - 1);
		}
		if (_currentPageId != "search")
		{
			PushCurrentPageToHistory();
		}
		_currentPageId = "search";
		UpdateSidebarHighlight();
		int tId = await FadeOutContentAsync();
		try
		{
			if (!((await BackendService.Instance.SearchAsync(query, CancellationToken.None))["data"] is JsonObject data))
			{
				return;
			}
			ContentPanel.Children.Clear();
			StackPanel searchResultsPanel = new StackPanel();
			if (data["artists"] is JsonArray { Count: >0 } artists)
			{
				var (sv, grid) = CreateExpandableSection("Artists", artists.Count);
				foreach (JsonNode item2 in artists)
				{
					string id = ((string?)item2["browseId"]) ?? "";
					string title = ((string?)item2["artist"]) ?? "";
					string thumbs = "";
					if (item2["thumbnails"] is JsonArray { Count: >0 } tArr)
					{
						thumbs = ((string?)tArr[tArr.Count - 1]["url"]) ?? "";
					}
					grid.Children.Add(CreateTrackCard(id, title, "Artist", thumbs, "Artist"));
				}
				searchResultsPanel.Children.Add(sv);
			}
			if (data["songs"] is JsonArray { Count: >0 } songs)
			{
				(UIElement, System.Windows.Controls.Panel) tuple2 = CreateExpandableGridSection("Songs", 210.0, songs.Count);
				UIElement sectionContainer = tuple2.Item1;
				System.Windows.Controls.Panel gridPanel = tuple2.Item2;
				UniformGrid grid2 = (UniformGrid)gridPanel;
				grid2.SizeChanged += delegate(object s, SizeChangedEventArgs ev)
				{
					grid2.Columns = Math.Max(1, (int)(ev.NewSize.Width / 320.0));
				};
				foreach (JsonNode item in songs)
				{
					string videoId = ((string?)item["videoId"]) ?? "";
					string title2 = ((string?)item["title"]) ?? "";
					string artistsStr = "";
					JsonArray artistsToken = item["artists"] as JsonArray;
					if (artistsToken != null)
					{
						List<string> names = new List<string>();
						foreach (JsonNode a in artistsToken)
						{
							names.Add(((string?)a["name"]) ?? "");
						}
						artistsStr = string.Join(", ", names);
					}
					string thumbUrl = "";
					if (item["thumbnails"] is JsonArray { Count: >0 } thumbs2)
					{
						thumbUrl = ((string?)thumbs2[thumbs2.Count - 1]["url"]) ?? "";
					}
					if (!string.IsNullOrEmpty(artistsStr))
					{
						Border row = CreateTrackRow(videoId, title2, artistsStr, thumbUrl, "", artistsData: artistsToken, album: ((string?)item["album"]?["name"]) ?? "", albumId: ((string?)item["album"]?["id"]) ?? "");
						row.Margin = new Thickness(5.0);
						grid2.Children.Add(row);
					}
				}
				searchResultsPanel.Children.Add(sectionContainer);
			}
			if (data["albums"] is JsonArray { Count: >0 } albums)
			{
				var (sv2, grid3) = CreateExpandableSection("Albums", albums.Count);
				foreach (JsonNode item3 in albums)
				{
					string id2 = ((string?)item3["browseId"]) ?? "";
					string title3 = ((string?)item3["title"]) ?? "";
					string year = ((string?)item3["year"]) ?? "";
					string thumbs3 = "";
					if (item3["thumbnails"] is JsonArray { Count: >0 } tArr2)
					{
						thumbs3 = ((string?)tArr2[tArr2.Count - 1]["url"]) ?? "";
					}
					string artistsStr2 = "Album";
					if (item3["artists"] is JsonArray artistsToken2)
					{
						List<string> names2 = new List<string>();
						foreach (JsonNode a2 in artistsToken2)
						{
							names2.Add(((string?)a2["name"]) ?? "");
						}
						artistsStr2 = string.Join(", ", names2);
					}
					grid3.Children.Add(CreateTrackCard(id2, title3, string.IsNullOrEmpty(year) ? artistsStr2 : (artistsStr2 + " • " + year), thumbs3, "Album"));
				}
				searchResultsPanel.Children.Add(sv2);
			}
			await FadeInContentAsync(tId, delegate
			{
				ContentPanel.Children.Add(searchResultsPanel);
				MainScrollViewer.ScrollToTop();
			});
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			await FadeInContentAsync(tId, delegate
			{
				ShowGlobalError("Search failed", ex2.Message, delegate
				{
					_ = _ = _ = _ = LoadLibraryAsync();
					_ = _ = _ = _ = PerformSearchAsync(query);
				});
			});
		}
	}
}



