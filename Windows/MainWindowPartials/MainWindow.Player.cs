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
	private async Task LoadPlaylistsPageAsync(bool forceReload)
	{
		if (_currentPageId == "playlists_page" && !forceReload)
		{
			return;
		}
		PushCurrentPageToHistory();
		_currentPageId = "playlists_page";
		UpdateSidebarHighlight();
		if (_playlistsCachePanel != null && !forceReload)
		{
			await FadeInContentAsync(await FadeOutContentAsync(), delegate
			{
				ContentPanel.Children.Add(_playlistsCachePanel);
				ResetScroll();
			});
			return;
		}
		int tId = await FadeOutContentAsync();
		ScrollViewer sv = new ScrollViewer
		{
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			Padding = new Thickness(0.0, 0.0, 20.0, 20.0)
		};
		UniformGrid wp = new UniformGrid
		{
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(0.0, 20.0, 0.0, 0.0)
		};
		wp.SizeChanged += delegate(object s, SizeChangedEventArgs e)
		{
			if (e.NewSize.Width != 0.0)
			{
				int num = Math.Max(1, (int)(e.NewSize.Width / 180.0));
				if (wp.Columns != num)
				{
					wp.Columns = num;
				}
			}
		};
		if (!System.IO.File.Exists(BackendService.AuthFilePath))
		{
			StackPanel loginPanel = new StackPanel
			{
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
				Margin = new Thickness(0.0, 80.0, 0.0, 0.0)
			};
			loginPanel.Children.Add(new System.Windows.Controls.Image
			{
				Source = (System.Windows.Media.DrawingImage)System.Windows.Application.Current.FindResource("lockIcon"),
				Width = 52.0,
				Height = 52.0,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
				Margin = new Thickness(0.0, 0.0, 0.0, 16.0),
				Opacity = 0.7
			});
			loginPanel.Children.Add(new TextBlock
			{
				Text = "You need to be logged in to view your playlists.",
				Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"],
				FontSize = 16.0,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
				Margin = new Thickness(0.0, 0.0, 0.0, 8.0),
				TextWrapping = TextWrapping.Wrap,
				TextAlignment = TextAlignment.Center
			});
			loginPanel.Children.Add(new TextBlock
			{
				Text = "Go to Account to connect your Google account.",
				Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
				FontSize = 13.0,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
				TextWrapping = TextWrapping.Wrap,
				TextAlignment = TextAlignment.Center
			});
			await FadeInContentAsync(tId, delegate
			{
				ContentPanel.Children.Add(loginPanel);
			});
			return;
		}
		if (_cachedPlaylists == null && _cachedLibraryError == null)
		{
			await LoadLibraryAsync();
		}
		if (_cachedLibraryError != null)
		{
			await FadeInContentAsync(tId, delegate
			{
				ShowGlobalError("Failed to load playlists", _cachedLibraryError, delegate
				{
					_ = _ = _ = _ = LoadLibraryAsync();
					_ = _ = _ = _ = LoadPlaylistsPageAsync(forceReload: true);
				});
			});
			return;
		}
		if (_cachedPlaylists != null)
		{
			int delayMs = 0;
			foreach (JsonNode cachedPlaylist in _cachedPlaylists)
			{
				string title = ((string?)cachedPlaylist["title"]) ?? "";
				string id = ((string?)cachedPlaylist["playlistId"]) ?? "";
				string tUrl = "";
				if (cachedPlaylist["thumbnails"] is JsonArray { Count: >0 } thumbs)
				{
					tUrl = ((string?)thumbs[thumbs.Count - 1]["url"]) ?? "";
				}
				UIElement card = CreateTrackCard(id, title, "Playlist", tUrl, "Playlist");
				bool skipAnim = MainOverlayControl.LoadingOverlayRef.Visibility == Visibility.Visible;
				((FrameworkElement)card).Opacity = skipAnim ? 1.0 : 0.0;
				DoubleAnimation anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(300.0))
				{
					BeginTime = TimeSpan.FromMilliseconds(delayMs)
				};
				((FrameworkElement)card).BeginAnimation(UIElement.OpacityProperty, anim);
				delayMs += 15;
				wp.Children.Add(card);
			}
		}
		if (sv.Content == null)
		{
			sv.Content = wp;
		}
		_playlistsCachePanel = sv;
		await FadeInContentAsync(tId, delegate
		{
			ContentPanel.Children.Add(_playlistsCachePanel);
			ResetScroll();
		});
	}

	private async void OpenPlaylistPage(string playlistId, string title, string subtitle, string thumbUrl, string type)
	{
		if (_playlistLoadCts != null)
		{
			try
			{
				_playlistLoadCts.Cancel();
				_playlistLoadCts.Dispose();
			}
			catch {}
			_playlistLoadCts = null;
		}
		System.Threading.CancellationTokenSource newCts = new System.Threading.CancellationTokenSource();
		_playlistLoadCts = newCts;
		System.Threading.CancellationToken ct = newCts.Token;
		if (_currentPageId == playlistId && _pageCache.ContainsKey(playlistId))
		{
			_pageCache.Remove(playlistId);
			_pageVirtualizationCache.Remove(playlistId);
		}
		else if (_pageCache.ContainsKey(playlistId))
		{
			if (_currentPageId != playlistId)
			{
				PushCurrentPageToHistory();
			}
			_currentPageId = playlistId;
			UpdateSidebarHighlight();
			await FadeInContentAsync(await FadeOutContentAsync(), delegate
			{
				ContentPanel.Children.Add(_pageCache[playlistId]);
				HighlightNowPlaying(_currentVideoId);
				ResetScroll();
			});
			return;
		}
		if (_currentPageId != playlistId)
		{
			PushCurrentPageToHistory();
		}
		_currentPageId = playlistId;
		UpdateSidebarHighlight();
		int tId = await FadeOutContentAsync();
		try
		{
			// BUILD skeleton/initial page panel using passed parameters immediately
			StackPanel pagePanel = new StackPanel();
			StackPanel headerPanel = new StackPanel
			{
				Orientation = System.Windows.Controls.Orientation.Horizontal,
				Margin = new Thickness(0.0, 20.0, 0.0, 30.0)
			};

			Border imgBorder = new Border
			{
				Width = 200.0,
				Height = 200.0,
				CornerRadius = new CornerRadius(8.0),
				Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 25, 35)),
				Margin = new Thickness(0.0, 0.0, 30.0, 0.0)
			};
			imgBorder.Clip = new RectangleGeometry
			{
				Rect = new Rect(0.0, 0.0, 200.0, 200.0),
				RadiusX = 8.0,
				RadiusY = 8.0
			};
			imgBorder.Child = CreateImage(thumbUrl, 200, 200);
			ApplyImageOverlay(imgBorder);
			imgBorder.Cursor = System.Windows.Input.Cursors.Hand;
			imgBorder.MouseDown += (sender, e) =>
			{
				if (e.ChangedButton == System.Windows.Input.MouseButton.Left && e.ClickCount == 1)
				{
					e.Handled = true;
					System.Windows.Media.Brush brush = GetImageBorderBrush(imgBorder);
					string url = GetImageBorderUrl(imgBorder);
					if (brush != null && !string.IsNullOrEmpty(url))
					{
						EnlargeImage(brush, url);
					}
				}
			};
			headerPanel.Children.Add(imgBorder);

			StackPanel titlePanel = new StackPanel
			{
				VerticalAlignment = VerticalAlignment.Center
			};
			titlePanel.Children.Add(new TextBlock
			{
				Text = type,
				Foreground = System.Windows.Media.Brushes.Gray,
				FontSize = 14.0
			});

			TextBlock titleBlock = new TextBlock
			{
				Text = title,
				Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
				FontSize = 48.0,
				FontWeight = FontWeights.Bold,
				Margin = new Thickness(0.0, 10.0, 0.0, 0.0),
				TextWrapping = TextWrapping.Wrap
			};
			titlePanel.Children.Add(titleBlock);

			// Subtitle (holds artist names, links, or description)
			TextBlock subtitleBlock = new TextBlock
			{
				TextTrimming = TextTrimming.CharacterEllipsis,
				Margin = new Thickness(0.0, 5.0, 0.0, 0.0),
				Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"],
				FontSize = 16.0
			};
			if (!string.IsNullOrEmpty(subtitle))
			{
				subtitleBlock.Text = subtitle;
			}
			titlePanel.Children.Add(subtitleBlock);

			// Play and Shuffle Button Row (disabled initially until tracks are loaded)
			StackPanel btnRow = new StackPanel
			{
				Orientation = System.Windows.Controls.Orientation.Horizontal,
				Margin = new Thickness(0.0, 20.0, 0.0, 0.0),
				HorizontalAlignment = System.Windows.HorizontalAlignment.Left
			};
			System.Windows.Controls.Button playBtn = new System.Windows.Controls.Button
			{
				Content = "Play",
				Style = (Style)FindResource("AccentButtonStyle"),
				FontSize = 16.0,
				FontWeight = FontWeights.Bold,
				Width = 120.0,
				Height = 48.0,
				IsEnabled = false
			};
			System.Windows.Controls.Button shuffleBtn = new System.Windows.Controls.Button
			{
				Style = (Style)FindResource("AccentButtonStyle"),
				Width = 48.0,
				Height = 48.0,
				Margin = new Thickness(15.0, 0.0, 0.0, 0.0),
				IsEnabled = false
			};
			shuffleBtn.Content = new System.Windows.Controls.Image
			{
				Source = (System.Windows.Media.DrawingImage)System.Windows.Application.Current.FindResource("shuffleIcon"),
				Width = 20.0,
				Height = 20.0,
				VerticalAlignment = VerticalAlignment.Center,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center
			};
			btnRow.Children.Add(playBtn);
			btnRow.Children.Add(shuffleBtn);
			titlePanel.Children.Add(btnRow);
			headerPanel.Children.Add(titlePanel);
			pagePanel.Children.Add(headerPanel);

			// Create list header and tracks grid
			string albumHeader = ((type == "Album" || type == "Single" || type == "EP") ? "Plays" : "Album");
			UIElement listHeader = CreateListHeader(hasNumber: true, hasAlbum: true, hasDuration: true, albumHeader);
			pagePanel.Children.Add(listHeader);

			StackPanel grid = new StackPanel();
			pagePanel.Children.Add(grid);

			// Cache and FADE IN immediately
			if (_pageCache.Count >= 3)
			{
				_pageCache.Clear();
			}
			_pageCache[playlistId] = pagePanel;

			await FadeInContentAsync(tId, delegate
			{
				ContentPanel.Children.Add(pagePanel);
				HighlightNowPlaying(_currentVideoId);
				ResetScroll();
			});

			// Now fetch data asynchronously in background
			_ = Task.Run(async delegate
			{
				try
				{
					if (ct.IsCancellationRequested) return;
					JsonObject json = ((!(type == "Mix")) ? (await BackendService.Instance.GetPlaylistTracksAsync(playlistId, ct)) : (await BackendService.Instance.GetMixTracksAsync(playlistId, ct)));
					if (ct.IsCancellationRequested) return;
					if (json["error"] != null)
					{
						throw new Exception(((string?)json["error"]) ?? "Error");
					}
					if (!(json["data"] is JsonObject data))
					{
						throw new Exception("No data returned.");
					}

					// Update UI controls on the main thread
					await base.Dispatcher.InvokeAsync(async delegate
					{
						if (ct.IsCancellationRequested || _currentPageId != playlistId)
						{
							return;
						}

						// Update title if changed
						string displayTitle = ((string?)data["title"]) ?? title;
						titleBlock.Text = displayTitle;

						// Update image if higher resolution thumbnail is available
						string highResThumb = thumbUrl;
						if (data["thumbnails"] is JsonArray { Count: >0 } tArr)
						{
							highResThumb = ((string?)tArr[tArr.Count - 1]["url"]) ?? thumbUrl;
							if (highResThumb != thumbUrl && string.IsNullOrEmpty(thumbUrl))
							{
								if (imgBorder.Child is Grid gridControl && gridControl.Children.Count > 0 && gridControl.Children[0] is Border innerBorder)
								{
									innerBorder.Child = CreateImage(highResThumb, 200, 200);
								}
								else
								{
									imgBorder.Child = CreateImage(highResThumb, 200, 200);
									ApplyImageOverlay(imgBorder);
								}
							}
						}

						// Update Subtitle/Artist Links
						string actualArtistName = "";
						JsonArray artistsArr = data["artists"] as JsonArray;
						if (artistsArr != null && artistsArr.Count > 0)
						{
							actualArtistName = ((string?)artistsArr[0]["name"]) ?? "";
						}
						string displaySubtitle = subtitle;
						if (type == "Album" && artistsArr != null && artistsArr.Count > 0)
						{
							List<string> artistNames = new List<string>();
							foreach (JsonNode a in artistsArr)
							{
								artistNames.Add(((string?)a["name"]) ?? "");
							}
							displaySubtitle = string.Join(", ", artistNames);
							string yearStr = ((string?)data["year"]) ?? "";
							if (!string.IsNullOrEmpty(yearStr))
							{
								displaySubtitle = displaySubtitle + " • " + yearStr;
							}
						}
						if (!string.IsNullOrEmpty(displaySubtitle))
						{
							if (type == "Album")
							{
								subtitleBlock.Inlines.Clear();
								string artistsPart = displaySubtitle;
								string yearPart = "";
								if (displaySubtitle.Contains("•"))
								{
									string[] parts = displaySubtitle.Split("•");
									artistsPart = parts[0].Trim();
									if (parts.Length > 1)
									{
										yearPart = " • " + parts[1].Trim();
									}
								}
								PopulateArtistLinks(subtitleBlock, artistsPart, 16);
								if (!string.IsNullOrEmpty(yearPart))
								{
									subtitleBlock.Inlines.Add(new System.Windows.Documents.Run(yearPart)
									{
										Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"],
										FontSize = 16.0
									});
								}
							}
							else
							{
								subtitleBlock.Text = displaySubtitle;
							}
						}

						string safeArtist = actualArtistName;
						if (string.IsNullOrEmpty(safeArtist) && (type == "Album" || type == "Single" || type == "EP") && !string.IsNullOrEmpty(displaySubtitle))
						{
							safeArtist = displaySubtitle.Split("•")[0].Trim();
						}

						JsonArray tracks = data["tracks"] as JsonArray;

						// Define Play / Shuffle Click handlers now that tracks are available
						Action<bool> startPlayback = delegate(bool shuffle)
						{
							if (shuffle && !_isShuffleOn)
							{
								_isShuffleOn = true;
								UpdateShuffleIcon();
							}
							if (tracks != null && tracks.Count > 0)
							{
								foreach (JsonNode item2 in tracks)
								{
									if (item2 is JsonObject JsonObject && !(JsonObject["thumbnails"] is JsonArray { Count: not 0 }) && !string.IsNullOrEmpty(highResThumb))
									{
										JsonObject["thumbnails"] = new JsonArray(new JsonObject { ["url"] = highResThumb });
									}
								}
								int num = (shuffle ? new Random().Next(tracks.Count) : 0);
								InitQueueAndShuffle((System.Text.Json.Nodes.JsonArray)tracks.DeepClone(), num);
								JsonNode JsonNode = tracks[num];
								string text = ((string?)JsonNode["videoId"]) ?? "";
								string title2 = ((string?)JsonNode["title"]) ?? "";
								string artist = subtitle;
								JsonArray jArray2 = JsonNode["artists"] as JsonArray;
								if (jArray2 == null || jArray2.Count == 0)
								{
									JsonNode["artists"] = new JsonArray(new JsonObject { ["name"] = safeArtist });
									jArray2 = JsonNode["artists"] as JsonArray;
								}
								if (jArray2 != null && jArray2.Count > 0)
								{
									List<string> list = new List<string>();
									foreach (JsonNode current in jArray2)
									{
										list.Add(((string?)current["name"]) ?? "");
									}
									artist = string.Join(", ", list);
								}
								string thumbUrl2 = highResThumb;
								if (JsonNode["thumbnails"] is JsonArray { Count: >0 } jArray3)
								{
									thumbUrl2 = ((string?)jArray3[jArray3.Count - 1]["url"]) ?? "";
								}
								if (!string.IsNullOrEmpty(text))
								{
									JsonArray artistsData = JsonNode["artists"] as JsonArray;
									_ = PlayTrack(text, title2, artist, thumbUrl2, addToHistory: true, startPaused: false, useCrossfade: false, 0, artistsData);
								}
							}
						};

						playBtn.Click += delegate { startPlayback(obj: false); };
						shuffleBtn.Click += delegate { startPlayback(obj: true); };
						playBtn.IsEnabled = true;
						shuffleBtn.IsEnabled = true;

						// Add expandable about description if any
						string albumDescription = data["description"]?.ToString() ?? "";
						if ((type == "Album" || type == "Single" || type == "EP") && !string.IsNullOrWhiteSpace(albumDescription))
						{
							pagePanel.Children.Add(CreateExpandableAboutSection(albumDescription));
						}

						// Load track list rows
						if (tracks != null && tracks.Count > 0)
						{
							bool isLikedSongsPage = type == "Playlist" && IsLikedSongsPage(playlistId, displayTitle);
							for (int i = 0; i < tracks.Count; i++)
							{
								JsonNode item = tracks[i];
								string videoId = ((string?)item["videoId"]) ?? "";
								if (!string.IsNullOrEmpty(videoId))
								{
									string tTitle = ((string?)item["title"]) ?? "";
									string artistsStr = subtitle;
									JsonArray artistsToken = item["artists"] as JsonArray;
									if (artistsToken == null || artistsToken.Count == 0)
									{
										if (item is JsonObject itemObj2)
										{
											itemObj2["artists"] = new JsonArray(new JsonObject { ["name"] = safeArtist });
										}
										artistsToken = item["artists"] as JsonArray;
									}
									if (artistsToken != null && artistsToken.Count > 0)
									{
										List<string> names = new List<string>();
										foreach (JsonNode a2 in artistsToken)
										{
											names.Add(((string?)a2["name"]) ?? "");
										}
										artistsStr = string.Join(", ", names);
									}
									string tUrl = highResThumb;
									if (item["thumbnails"] is JsonArray { Count: >0 } thumbs)
									{
										tUrl = ((string?)thumbs[thumbs.Count - 1]["url"]) ?? "";
									}
									else if (item is JsonObject itemObj3 && !string.IsNullOrEmpty(highResThumb))
									{
										itemObj3["thumbnails"] = new JsonArray(new JsonObject { ["url"] = highResThumb });
									}
									string setVideoId = ((string?)item["setVideoId"]) ?? "";
									string album = "";
									string albumId = "";
									if (item["album"] is JsonObject albumObj)
									{
										album = ((string?)albumObj["name"]) ?? "";
										albumId = ((string?)albumObj["id"]) ?? "";
									}
									else if (item["album"] != null)
									{
										album = ((string?)item["album"]) ?? "";
									}
									if (string.IsNullOrEmpty(album) && type == "Album")
									{
										album = displayTitle;
										albumId = playlistId;
									}
									string duration = ((string?)item["duration"]) ?? "";
									string plays = ((string?)item["plays"]) ?? "";
									if (string.IsNullOrEmpty(album) && !string.IsNullOrEmpty(plays))
									{
										album = plays;
									}
									string f_videoId = videoId;
									string f_tTitle = tTitle;
									string f_artistsStr = artistsStr;
									string f_tUrl = tUrl;
									string f_album = album;
									string f_albumId = albumId;
									string f_duration = duration;
									string f_type = type;
									string f_playlistId = playlistId;
									JsonArray f_tracks = tracks;
									int f_i = i;
									string f_setVideoId = setVideoId;
									string f_isLikedSongsPage = (isLikedSongsPage ? "true" : "false");
									JsonArray f_artistsToken = artistsToken;
									string f_displayAlbum = null;
									if (type == "Album" || type == "Single" || type == "EP")
									{
										f_displayAlbum = plays;
									}
									Border lazyContainer = new Border
									{
										Height = 70.0,
										Background = System.Windows.Media.Brushes.Transparent
									};
									_lazyVirtualizationActions.Add(() => CreateTrackRow(f_videoId, f_tTitle, f_artistsStr, f_tUrl, (f_i + 1).ToString(), f_album, f_albumId, f_duration, (f_type == "Playlist") ? f_playlistId : "", f_tracks, f_i, f_setVideoId, f_isLikedSongsPage == "true", null, f_artistsToken, f_displayAlbum));
									_lazyVirtualizationElements.Add(lazyContainer);
									grid.Children.Add(lazyContainer);
								}
							}
							// Materialize the first 15 items one-by-one with a delay to show a beautiful top-to-bottom loading sequence
							_ = Task.Run(async delegate
							{
								int initialVisibleCount = Math.Min(15, _lazyVirtualizationElements.Count);
								for (int i = 0; i < initialVisibleCount; i++)
								{
									if (ct.IsCancellationRequested || _currentPageId != playlistId)
									{
										return;
									}
									int index = i;
									await base.Dispatcher.InvokeAsync(delegate
									{
										if (ct.IsCancellationRequested || _currentPageId != playlistId)
										{
											return;
										}
										Border border = _lazyVirtualizationElements[index];
										if (border.Child == null)
										{
											Func<UIElement> action = _lazyVirtualizationActions[index];
											if (action != null)
											{
												UIElement newEl = action();
												border.Tag = newEl;
												border.Child = newEl;
											}
										}
									});
									await Task.Delay(35);
								}
								await base.Dispatcher.InvokeAsync(delegate
								{
									if (ct.IsCancellationRequested || _currentPageId != playlistId)
									{
										return;
									}
									CheckVisibilityOfLazyElements();
								});
							});
						}
					});
				}
				catch (Exception ex)
				{
					Exception ex2 = ex;
					await base.Dispatcher.InvokeAsync(async delegate
					{
						if (ct.IsCancellationRequested || _currentPageId != playlistId)
						{
							return;
						}
						ShowGlobalError("Failed to load " + type.ToLower(), ex2.Message, delegate
						{
							_ = LoadLibraryAsync();
							OpenPlaylistPage(playlistId, title, subtitle, thumbUrl, type);
						});
					});
				}
			});
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			await FadeInContentAsync(tId, delegate
			{
				ShowGlobalError("Failed to load " + type.ToLower(), ex2.Message, delegate
				{
					_ = LoadLibraryAsync();
					OpenPlaylistPage(playlistId, title, subtitle, thumbUrl, type);
				});
			});
		}
	}

	private void PopulatePlaylistSubmenu(MenuItem addMenu, string videoId, string thumbUrl = "")
	{
		addMenu.Items.Clear();
		if (_cachedPlaylists != null && _cachedPlaylists.Count > 0)
		{
			foreach (JsonNode pl in _cachedPlaylists)
			{
				string plTitle = ((string?)pl["title"]) ?? "";
				string plId = ((string?)pl["playlistId"]) ?? "";
				if (string.IsNullOrEmpty(plTitle) || string.IsNullOrEmpty(plId))
				{
					continue;
				}
				MenuItem plItem = new MenuItem
				{
					Header = plTitle
				};
				plItem.Click += async delegate
				{
					_ = 1;
					try
					{
						await BackendService.Instance.AddPlaylistItemAsync(plId, videoId, CancellationToken.None);
						ShowToast("Added to " + plTitle);
						if (_pageCache.ContainsKey(plId))
						{
							_pageCache.Remove(plId);
						}
						string officialThumbUrl = "";
						try
						{
							if ((await BackendService.Instance.GetPlaylistTracksAsync(plId, CancellationToken.None))["data"]?["thumbnails"] is JsonArray { Count: >0 } thumbs)
							{
								officialThumbUrl = ((string?)thumbs[thumbs.Count - 1]["url"]) ?? "";
							}
						}
						catch
						{
						}
						if (!string.IsNullOrEmpty(officialThumbUrl))
						{
							foreach (UIElement item in (IEnumerable)MainSidebar.LibraryPanelRef.Items)
							{
								if (item is Border b && b.Tag as string == plId)
								{
									if (b.Child is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is Border imgBorder)
									{
										imgBorder.Child = CreateImage(officialThumbUrl, 32, 32);
										ApplyImageOverlay(imgBorder);
									}
									break;
								}
							}
						}
					}
					catch
					{
						ShowToast("Failed to add");
					}
				};
				addMenu.Items.Add(plItem);
			}
			if (addMenu.Items.Count == 0)
			{
				addMenu.Items.Add(new MenuItem
				{
					Header = "No playlists loaded",
					IsEnabled = false
				});
			}
		}
		else
		{
			addMenu.Items.Add(new MenuItem
			{
				Header = "No playlists loaded",
				IsEnabled = false
			});
		}
	}

	private void InitSMTC()
	{
		if (!_enableSMTC)
		{
			return;
		}
		try
		{
			_smtcPlayer = new Windows.Media.Playback.MediaPlayer();
			_smtcPlayer.CommandManager.IsEnabled = false;
			InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream();
			_smtcPlayer.Source = MediaSource.CreateFromStream(stream, "audio/wav");
			_smtcPlayer.Volume = 0.0;
			_smtcPlayer.IsLoopingEnabled = true;
			_smtcPlayer.Play();
			_smtc = _smtcPlayer.SystemMediaTransportControls;
			_smtc.IsEnabled = true;
			_smtc.IsPlayEnabled = true;
			_smtc.IsPauseEnabled = true;
			_smtc.IsNextEnabled = true;
			_smtc.IsPreviousEnabled = true;
			_smtc.ButtonPressed += delegate(SystemMediaTransportControls s, SystemMediaTransportControlsButtonPressedEventArgs args)
			{
				base.Dispatcher.Invoke(delegate
				{
					switch (args.Button)
					{
					case SystemMediaTransportControlsButton.Play:
					case SystemMediaTransportControlsButton.Pause:
						PlayPauseBtn_Click(null, null);
						break;
					case SystemMediaTransportControlsButton.Next:
						NextBtn_Click(null, null);
						break;
					case SystemMediaTransportControlsButton.Previous:
						PrevBtn_Click(null, null);
						break;
					}
				});
			};
		}
		catch
		{
		}
	}

	private void UpdateSMTCMetadata(string title, string artist, string thumbnailUrl)
	{
		if (_smtc == null)
		{
			return;
		}
		try
		{
			SystemMediaTransportControlsDisplayUpdater updater = _smtc.DisplayUpdater;
			updater.Type = MediaPlaybackType.Music;
			updater.MusicProperties.Title = title;
			updater.MusicProperties.Artist = artist;
			if (!string.IsNullOrEmpty(thumbnailUrl))
			{
				if (thumbnailUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
				{
					try
					{
						updater.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(thumbnailUrl));
					}
					catch
					{
					}
				}
				else
				{
					updater.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(thumbnailUrl));
				}
			}
			updater.Update();
		}
		catch
		{
		}
		UpdateDiscordRPC();
	}

	private async Task PlayTrack(string videoId, string title, string artist, string thumbUrl, bool addToHistory = true, bool startPaused = false, bool useCrossfade = false, int transitionDirection = 0, JsonArray? artistsData = null, JsonObject? albumData = null)
	{
		if (transitionDirection == 0)
		{
			if ((DateTime.Now - _lastPlayClickTime).TotalMilliseconds < 500.0)
			{
				return;
			}
			_lastPlayClickTime = DateTime.Now;
		}
		thumbUrl = GetProcessedThumbUrl(thumbUrl);
		if (_enableConfetti && transitionDirection == 0)
		{
			TriggerConfetti(Mouse.GetPosition(this));
		}
		if (videoId == _currentVideoId && transitionDirection == 0)
		{
			if (_player == null)
			{
				return;
			}
			_player.Time = 0L;
			if (!_player.IsPlaying && !startPaused)
			{
				_player.Resume();
				UpdatePlayPauseIconState(isPaused: false);
				TaskbarPlayPauseBtn.ImageSource = (ImageSource)FindResource("ThumbPauseIcon");
				TaskbarPlayPauseBtn.Description = "Pause";
				if (_smtc != null)
				{
					_smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
				}
			}
			return;
		}
		_playbackCts?.Cancel();
		_playbackCts = new CancellationTokenSource();
		CancellationToken token = _playbackCts.Token;
		if (addToHistory && !string.IsNullOrEmpty(_currentVideoId) && _currentVideoId != videoId)
		{
			_playbackHistory.Push(new PlaybackHistoryEntry
			{
				VideoId = _currentVideoId,
				Title = _currentTitle,
				Artist = _currentArtist,
				ThumbUrl = _currentThumbUrl
			});
		}
		_playbackTimer.Stop();
		_isTrackLoading = true;
		_pauseRequested = startPaused;
		_playedVideoIds.Add(videoId);
		if (_currentQueue == null || _currentQueue.Count == 0)
		{
			_currentQueue = new JsonArray(new JsonObject
			{
				["videoId"] = videoId,
				["title"] = title,
				["artists"] = artistsData?.DeepClone() ?? new JsonArray(new JsonObject { ["name"] = artist }),
				["album"] = albumData?.DeepClone() ?? new JsonObject(),
				["thumbnails"] = new JsonArray(new JsonObject { ["url"] = thumbUrl })
			});
			_currentQueueIndex = 0;
			_originalQueueSize = 1;
		}
		_currentVideoId = videoId;
		_currentTitle = title;
		_currentArtist = artist;
		_currentAlbum = albumData?["name"]?.ToString() ?? "";
		_currentAlbumId = albumData?["id"]?.ToString() ?? "";
		bool isRadio = _currentVideoId.StartsWith("radio:");
		SetTimelineVisibility(!isRadio);
		WarmLyricsForCurrentTrack(0L);
		base.Title = (startPaused ? "Spectre" : (artist + " - " + title));
		HighlightNowPlaying(videoId);
		_currentThumbUrl = thumbUrl;
		string previousTitleForTransition = MainPlayerBarControl.PlayerTitleRef?.Text;
		string previousArtistForTransition = MainPlayerBarControl.PlayerArtistPanelRef?.Text;
		System.Windows.Media.Brush previousThumbBrushForTransition = MainPlayerBarControl.PlayerThumbnailRef?.Fill;
		PlayerBarViewModel vm = App.Current.PlayerBarViewModel;
		if (vm != null)
		{
			vm.Title = title;
		}
		JsonArray currentArtistsData = artistsData;
		if (currentArtistsData == null && _currentQueue != null && _currentQueueIndex >= 0 && _currentQueueIndex < _currentQueue.Count)
		{
			JsonNode qItem = _currentQueue[_currentQueueIndex];
			if ((string?)qItem["videoId"] == videoId)
			{
				currentArtistsData = qItem["artists"] as JsonArray;
			}
		}
		PopulateArtistLinks(MainPlayerBarControl.PlayerArtistPanelRef, artist, 12, currentArtistsData);
		ContextMenu ctx = CreateSongContextMenu(videoId, "", "", null, hideLikedToggle: false, title, artist, thumbUrl, _currentAlbumId, _currentAlbum);
		MainPlayerBarControl.PlayerInfoPanelRef.ContextMenu = ctx;
		MainPlayerBarControl.PlayerTitleRef.ContextMenu = ctx;
		MainPlayerBarControl.PlayerArtistPanelRef.ContextMenu = ctx;
		if (transitionDirection != 0 && MainPlayerBarControl.PlayerInfoPanelRef != null)
		{
			MainPlayerBarControl.AnimateTonearmSkip();
			AnimateSidebarTonearmSkip();
			if (MainPlayerBarControl.OldPlayerInfoPanelRef != null)
			{
				MainPlayerBarControl.OldPlayerTitleRef.Text = previousTitleForTransition ?? "";
				MainPlayerBarControl.OldPlayerArtistPanelRef.Inlines.Clear();
				PopulateArtistLinks(MainPlayerBarControl.OldPlayerArtistPanelRef, previousArtistForTransition ?? "");
				MainPlayerBarControl.OldPlayerThumbnailRef.Fill = previousThumbBrushForTransition;
				MainPlayerBarControl.OldPlayerInfoPanelRef.Visibility = Visibility.Visible;
				TranslateTransform oldTransform = new TranslateTransform(0.0, 0.0);
				MainPlayerBarControl.OldPlayerInfoPanelRef.RenderTransform = oldTransform;
				DoubleAnimation oldAnimX = new DoubleAnimation(0.0, -transitionDirection * 20, new Duration(TimeSpan.FromMilliseconds(350.0)))
				{
					EasingFunction = new QuadraticEase
					{
						EasingMode = EasingMode.EaseOut
					}
				};
				oldTransform.BeginAnimation(TranslateTransform.XProperty, oldAnimX);
				DoubleAnimation oldAnimOp = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromMilliseconds(350.0)))
				{
					EasingFunction = new QuadraticEase
					{
						EasingMode = EasingMode.EaseOut
					}
				};
				MainPlayerBarControl.OldPlayerInfoPanelRef.Opacity = 1.0;
				MainPlayerBarControl.OldPlayerInfoPanelRef.BeginAnimation(UIElement.OpacityProperty, oldAnimOp);
			}
			TranslateTransform transform = new TranslateTransform(transitionDirection * 20, 0.0);
			MainPlayerBarControl.PlayerInfoPanelRef.RenderTransform = transform;
			DoubleAnimation animX = new DoubleAnimation(0.0, new Duration(TimeSpan.FromMilliseconds(350.0)))
			{
				EasingFunction = new QuadraticEase
				{
					EasingMode = EasingMode.EaseOut
				}
			};
			transform.BeginAnimation(TranslateTransform.XProperty, animX);
			DoubleAnimation animOp = new DoubleAnimation(0.0, 1.0, new Duration(TimeSpan.FromMilliseconds(350.0)))
			{
				EasingFunction = new QuadraticEase
				{
					EasingMode = EasingMode.EaseOut
				}
			};
			MainPlayerBarControl.PlayerInfoPanelRef.BeginAnimation(UIElement.OpacityProperty, animOp);
		}
		if (_isQueueOpen)
		{
			RenderQueueSidebar();
		}
		if (_isLyricsViewOpen)
		{
			_ = _ = _ = _ = ShowLyricsViewAsync(replaceCurrentLyrics: true);
		}
		PlayerBarViewModel vmTime = App.Current.PlayerBarViewModel;
		if (vmTime != null)
		{
			vmTime.CurrentTimeText = "0:00";
		}
		MainPlayerBarControl.TimelineSliderRef.BeginAnimation(RangeBase.ValueProperty, null);
		MainPlayerBarControl.TimelineSliderRef.Value = 0.0;
		MainTopbarControl.StatusLabelRef.Text = "";
		UpdatePlayPauseIconState(_pauseRequested);
		TaskbarPlayPauseBtn.ImageSource = (ImageSource)FindResource(_pauseRequested ? "ThumbPlayIcon" : "ThumbPauseIcon");
		TaskbarPlayPauseBtn.Description = (_pauseRequested ? "Play" : "Pause");
		UpdateSMTCMetadata(title, artist, thumbUrl);
		if (!string.IsNullOrEmpty(thumbUrl) && _imageCache.TryGetValue(thumbUrl, out BitmapImage cachedBmp))
		{
			UpdatePlayerThumbnail(cachedBmp);
		}
		else if (!string.IsNullOrEmpty(thumbUrl))
		{
			_ = _ = _ = _ = Task.Run(async delegate
			{
				_ = 1;
				try
				{
					byte[] bytes;
					if (thumbUrl.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
					{
						string localPath = new Uri(thumbUrl).LocalPath;
						bytes = System.IO.File.ReadAllBytes(localPath);
					}
					else
					{
						bytes = await BackendService.Instance.DownloadImageAsync(thumbUrl, CancellationToken.None);
					}
					await base.Dispatcher.InvokeAsync(delegate
					{
						try
						{
							BitmapImage bitmapImage = new BitmapImage();
							using (MemoryStream streamSource = new MemoryStream(bytes))
							{
								bitmapImage.BeginInit();
								bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
								bitmapImage.DecodePixelWidth = 600;
								bitmapImage.StreamSource = streamSource;
								bitmapImage.EndInit();
							}
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
		else
		{
			UpdatePlayerThumbnail(null);
		}
		try
		{
			PlaybackStreamInfo streamInfo = null;
			if (_preloadTasks.TryGetValue(videoId, out Task<PlaybackStreamInfo> task))
			{
				try
				{
					streamInfo = await task;
				}
				catch
				{
				}
			}
			if (streamInfo == null)
			{
				streamInfo = await GetStreamForPlaybackAsync(videoId, token);
				if (_prefetchEnabled)
				{
					_preloadTasks[videoId] = Task.FromResult(streamInfo);
				}
			}
			if (token.IsCancellationRequested)
			{
				return;
			}
			if (addToHistory && !string.IsNullOrEmpty(streamInfo.Url))
			{
				_ = _ = _ = _ = Task.Run(() => BackendService.Instance.AddHistoryItemAsync(videoId, CancellationToken.None));
			}
			_currentStreamUrl = streamInfo.Url;
			_isTrackLoading = false;
			MainTopbarControl.StatusLabelRef.Text = "Playing via " + streamInfo.Provider + " - " + streamInfo.QualityLabel;
			_crossfadeTriggeredForCurrentTrack = false;
			_player.Play(streamInfo.Url, useCrossfade, streamInfo.Provider == "Radio");
			if (_pauseRequested)
			{
				_player.Pause();
			}
			if (!videoId.StartsWith("radio:"))
			{
				_lastFmScrobbled = false;
				_ = _ = _ = _ = LastFmManager.UpdateNowPlayingAsync(_currentTitle, _currentArtist, _currentAlbum);
			}
			_ = _ = _ = _ = MaintainPreloadBufferAsync();
		}
		catch (Exception value)
		{
			if (!token.IsCancellationRequested)
			{
				_isTrackLoading = false;
				PlayerBarViewModel vmTime_tmp = App.Current.PlayerBarViewModel;
				if (vmTime_tmp != null)
				{
					vmTime_tmp.CurrentTimeText = "Error";
				}
				MainTopbarControl.StatusLabelRef.Text = "Playback failed. See logs for details.";
				AppLogger.Log($"Playback: PlayTrack failed for '{title}' ({videoId}) - {value.Message}", LogLevel.Error);
			}
		}
	}

	private void UpdatePlayerThumbnail(ImageSource? bmp)
	{
		ImageBrush newBrush = null;
		if (bmp != null)
		{
			newBrush = new ImageBrush(bmp)
			{
				Stretch = Stretch.UniformToFill,
				AlignmentX = AlignmentX.Center,
				AlignmentY = AlignmentY.Center
			};
		}
		if (_thumbnailStoryboard != null)
		{
			try
			{
				_thumbnailStoryboard.Stop();
			}
			catch
			{
			}
			_thumbnailStoryboard = null;
		}
		if (MainPlayerBarControl.PlayerThumbnailRef.Fill == null)
		{
			MainPlayerBarControl.PlayerThumbnailRef.Fill = newBrush;
			if (_useAdaptiveTheme)
			{
				ApplyThemeColors();
			}
			MainPlayerBarControl.PlayerThumbnailRef.Opacity = 0.0;
			DoubleAnimation fadeIn = new DoubleAnimation(0.0, 1.0, new Duration(TimeSpan.FromMilliseconds(350.0)))
			{
				EasingFunction = new QuadraticEase
				{
					EasingMode = EasingMode.EaseOut
				}
			};
			MainPlayerBarControl.PlayerThumbnailRef.BeginAnimation(UIElement.OpacityProperty, fadeIn);
			return;
		}
		MainPlayerBarControl.PlayerThumbnailOldRef.Fill = MainPlayerBarControl.PlayerThumbnailRef.Fill;
		MainPlayerBarControl.PlayerThumbnailOldRef.Opacity = 1.0;
		MainPlayerBarControl.PlayerThumbnailRef.Fill = newBrush;
		MainSidebar.SidebarCoverImageRef.Fill = newBrush;
		if (_useAdaptiveTheme)
		{
			ApplyThemeColors();
		}
		MainPlayerBarControl.PlayerThumbnailRef.Opacity = 0.0;
		Storyboard sb = new Storyboard();
		DoubleAnimation fadeIn2 = new DoubleAnimation(0.0, 1.0, new Duration(TimeSpan.FromMilliseconds(350.0)))
		{
			EasingFunction = new QuadraticEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		Storyboard.SetTarget(fadeIn2, MainPlayerBarControl.PlayerThumbnailRef);
		Storyboard.SetTargetProperty(fadeIn2, new PropertyPath(UIElement.OpacityProperty));
		DoubleAnimation fadeOut = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromMilliseconds(350.0)))
		{
			EasingFunction = new QuadraticEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		Storyboard.SetTarget(fadeOut, MainPlayerBarControl.PlayerThumbnailOldRef);
		Storyboard.SetTargetProperty(fadeOut, new PropertyPath(UIElement.OpacityProperty));
		sb.Children.Add(fadeIn2);
		sb.Children.Add(fadeOut);
		sb.Completed += delegate
		{
			MainPlayerBarControl.PlayerThumbnailOldRef.Fill = null;
			MainPlayerBarControl.PlayerThumbnailOldRef.Opacity = 0.0;
		};
		_thumbnailStoryboard = sb;
		sb.Begin();
	}

	private void UpdatePreloadBuffer()
	{
		if (_currentQueue == null || !_prefetchEnabled)
		{
			return;
		}
		HashSet<string> activeIds = new HashSet<string>();
		int num = Math.Max(0, _currentQueueIndex - _historySize);
		int end = Math.Min(_currentQueue.Count - 1, _currentQueueIndex + _bufferSize);
		List<int> indices = new List<int>();
		for (int i = num; i <= end; i++)
		{
			indices.Add(i);
		}
		indices.Sort(delegate(int a, int b)
		{
			int num2 = Math.Abs(a - _currentQueueIndex);
			int num3 = Math.Abs(b - _currentQueueIndex);
			return (num2 != num3) ? num2.CompareTo(num3) : b.CompareTo(a);
		});
		foreach (int i2 in indices)
		{
			string vid = ((string?)_currentQueue[i2]["videoId"]) ?? "";
			if (string.IsNullOrEmpty(vid))
			{
				continue;
			}
			activeIds.Add(vid);
			if (_preloadTasks.ContainsKey(vid))
			{
				continue;
			}
			_ = (string?)_currentQueue[i2]["title"];
			if (_currentQueue[i2]["artists"] is JsonArray { Count: >0 } artistsToken)
			{
				if ((string?)artistsToken[0]["name"] != null)
				{
				}
			}
			else
			{
				_ = (string?)_currentQueue[i2]["artist"];
			}
			_preloadTasks[vid] = GetStreamForPlaybackThrottledAsync(vid, CancellationToken.None);
			PrefetchThumbnail(_currentQueue[i2]);
		}
		List<string> keysToRemove = new List<string>();
		foreach (string key in _preloadTasks.Keys)
		{
			if (!activeIds.Contains(key))
			{
				keysToRemove.Add(key);
			}
		}
		foreach (string key2 in keysToRemove)
		{
			_preloadTasks.TryRemove(key2, out Task<PlaybackStreamInfo> _);
		}
	}

	private async Task MaintainPreloadBufferAsync()
	{
		if (_currentQueue != null)
		{
			UpdatePreloadBuffer();
			try
			{
				await ExpandQueueWithAutoplayAsync();
			}
			catch
			{
			}
			UpdatePreloadBuffer();
		}
	}

	private async Task<PlaybackStreamInfo> GetStreamForPlaybackThrottledAsync(string videoId, CancellationToken token)
	{
		await _preloadSemaphore.WaitAsync(token);
		try
		{
			return await GetStreamForPlaybackAsync(videoId, token);
		}
		finally
		{
			_preloadSemaphore.Release();
		}
	}

	private async Task<PlaybackStreamInfo> GetStreamForPlaybackAsync(string videoId, CancellationToken token)
	{
		if (videoId.StartsWith("local:"))
		{
			string fileUri = new Uri(videoId.Substring(6)).AbsoluteUri;
			return new PlaybackStreamInfo
			{
				Url = fileUri,
				QualityLabel = "Local Audio",
				Provider = "Local"
			};
		}
		if (videoId.StartsWith("radio:"))
		{
			string streamUrl = videoId.Substring(6).Trim();
			return new PlaybackStreamInfo
			{
				Url = streamUrl,
				QualityLabel = "Internet Radio",
				Provider = "Radio"
			};
		}
		try
		{
			return await BackendService.Instance.FetchStreamUrlAsync(videoId, token);
		}
		catch (Exception)
		{
			throw new Exception("All playback routes failed.");
		}
	}

	private void UpdatePlayPauseIconState(bool isPaused)
	{
		try
		{
			((System.Windows.Media.Animation.Storyboard)MainPlayerBarControl.PlayPauseBtnRef.Resources[isPaused ? "ToPlayAnim" : "ToPauseAnim"]).Begin();
		}
		catch
		{
		}

		if (EnableVinylMode || EnableWigglyProgress)
		{
			if (isPaused)
			{
				if (EnableVinylMode)
				{
					MainPlayerBarControl.PauseVinylRotation();
					PauseSidebarVinylRotation();
				}
				if (EnableWigglyProgress)
				{
					MainPlayerBarControl.PauseWiggly();
				}
			}
			else
			{
				if (EnableVinylMode)
				{
					MainPlayerBarControl.ResumeVinylRotation();
					ResumeSidebarVinylRotation();
				}
				if (EnableWigglyProgress)
				{
					MainPlayerBarControl.ResumeWiggly();
				}
			}
		}
	}

	private void PrevBtn_Click(object sender, RoutedEventArgs e)
	{
		if (_player.Time > 3000)
		{
			_player.Time = 0L;
			return;
		}
		if (_currentQueue != null && _currentQueueIndex > 0)
		{
			_currentQueueIndex--;
			JsonNode prevItem = _currentQueue[_currentQueueIndex];
			string vid = ((string?)prevItem["videoId"]) ?? "";
			string title = ((string?)prevItem["title"]) ?? "";
			string artist = "";
			if (prevItem["artists"] is JsonArray { Count: >0 } artistsArr)
			{
				List<string> names = new List<string>();
				foreach (JsonNode a in artistsArr)
				{
					names.Add(((string?)a["name"]) ?? "");
				}
				artist = string.Join(", ", names);
			}
			string thumbUrl = "";
			if (prevItem["thumbnails"] is JsonArray { Count: >0 } thumbs)
			{
				thumbUrl = ((string?)thumbs[thumbs.Count - 1]["url"]) ?? "";
			}
			if (!string.IsNullOrEmpty(vid))
			{
				_ = _ = _ = _ = PlayTrack(vid, title, artist, thumbUrl, addToHistory: false, _pauseRequested, useCrossfade: false, -1);
				return;
			}
		}
		if (_playbackHistory.Count != 0)
		{
			PlaybackHistoryEntry previous = _playbackHistory.Pop();
			_currentQueueIndex = FindQueueIndexByVideoId(previous.VideoId);
			_ = _ = _ = _ = PlayTrack(previous.VideoId, previous.Title, previous.Artist, previous.ThumbUrl, addToHistory: false, _pauseRequested, useCrossfade: false, -1);
		}
	}

	private void PlayPauseBtn_Click(object sender, RoutedEventArgs e)
	{
		if (_player.IsPlaying)
		{
			_pauseRequested = true;
			_player.Pause();
			base.Dispatcher.InvokeAsync(delegate
			{
				FreezeSyllableAnimations();
			}, DispatcherPriority.Background);
		}
		else if (_isTrackLoading)
		{
			_pauseRequested = !_pauseRequested;
			UpdatePlayPauseIconState(_pauseRequested);
			TaskbarPlayPauseBtn.ImageSource = (ImageSource)FindResource(_pauseRequested ? "ThumbPlayIcon" : "ThumbPauseIcon");
			TaskbarPlayPauseBtn.Description = (_pauseRequested ? "Play" : "Pause");
			base.Title = (_pauseRequested ? "Spectre" : (_currentArtist + " - " + _currentTitle));
		}
		else if (!string.IsNullOrEmpty(_currentVideoId))
		{
			_pauseRequested = false;
			_player.Resume();
			base.Dispatcher.InvokeAsync(delegate
			{
				ResumeSyllableAnimations((long)MainPlayerBarControl.TimelineSliderRef.Value);
			}, DispatcherPriority.Background);
		}
	}

	private void NextBtn_Click(object sender, RoutedEventArgs e)
	{
		PlayNextInQueue();
	}

	private void TaskbarPrevBtn_Click(object sender, EventArgs e)
	{
		PrevBtn_Click(null, null);
	}

	private void TaskbarPlayPauseBtn_Click(object sender, EventArgs e)
	{
		PlayPauseBtn_Click(null, null);
	}

	private void TaskbarNextBtn_Click(object sender, EventArgs e)
	{
		NextBtn_Click(null, null);
	}

	private void RepeatBtn_Click(object sender, RoutedEventArgs e)
	{
		_isRepeatOn = !_isRepeatOn;
		MainPlayerBarControl.RepeatIconOffRef.Visibility = Visibility.Visible;
		MainPlayerBarControl.RepeatIconOnRef.Visibility = _isRepeatOn ? Visibility.Visible : Visibility.Collapsed;
	}

	private void VolumeIcon_Click(object sender, RoutedEventArgs e)
	{
		if (MainPlayerBarControl.VolumeSliderRef.Value > 0.0)
		{
			_previousVolume = MainPlayerBarControl.VolumeSliderRef.Value;
			MainPlayerBarControl.VolumeSliderRef.Value = 0.0;
		}
		else
		{
			MainPlayerBarControl.VolumeSliderRef.Value = ((_previousVolume > 0.0) ? _previousVolume : 100.0);
		}
	}

	private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		int oldVal = (int)e.OldValue;
		int newVal = (int)e.NewValue;

		if (oldVal != newVal && _player != null)
		{
			_player.Volume = newVal;
		}

		if (MainPlayerBarControl.VolumeIconRef != null)
		{
			bool wasMuted = oldVal == 0;
			bool isMuted = newVal == 0;

			if (wasMuted != isMuted || MainPlayerBarControl.VolumeIconRef.Source == null)
			{
				string resourceKey = isMuted ? "volumemutedIcon" : "volumeIcon";
				MainPlayerBarControl.VolumeIconRef.Source = (ImageSource)System.Windows.Application.Current.FindResource(resourceKey);
			}
		}
	}

	private void VolumeSlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		e.Handled = true;
		if (e.Delta > 0)
		{
			MainPlayerBarControl.VolumeSliderRef.Value = Math.Min(MainPlayerBarControl.VolumeSliderRef.Maximum, MainPlayerBarControl.VolumeSliderRef.Value + (double)_volumeStep);
		}
		else
		{
			MainPlayerBarControl.VolumeSliderRef.Value = Math.Max(MainPlayerBarControl.VolumeSliderRef.Minimum, MainPlayerBarControl.VolumeSliderRef.Value - (double)_volumeStep);
		}
	}

	private void SetTimelineVisibility(bool visible, bool animate = true)
	{
		ScaleTransform scaleTransform = MainPlayerBarControl.TimelineGridRef.LayoutTransform as ScaleTransform;
		if (scaleTransform == null)
		{
			scaleTransform = new ScaleTransform(1.0, 1.0);
			MainPlayerBarControl.TimelineGridRef.LayoutTransform = scaleTransform;
		}
		if (!animate)
		{
			MainPlayerBarControl.TimelineGridRef.Visibility = ((!visible) ? Visibility.Collapsed : Visibility.Visible);
			MainPlayerBarControl.TimelineGridRef.Opacity = (visible ? 1.0 : 0.0);
			scaleTransform.ScaleY = (visible ? 1.0 : 0.0);
			return;
		}
		bool isCurrentlyVisible = MainPlayerBarControl.TimelineGridRef.Visibility == Visibility.Visible && scaleTransform.ScaleY > 0.0;
		if (visible == isCurrentlyVisible)
		{
			return;
		}
		if (visible)
		{
			MainPlayerBarControl.TimelineGridRef.Visibility = Visibility.Visible;
		}
		Storyboard sb = new Storyboard();
		DoubleAnimation opacityAnim = new DoubleAnimation(visible ? 1.0 : 0.0, new Duration(TimeSpan.FromMilliseconds(300.0)))
		{
			EasingFunction = new QuadraticEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		Storyboard.SetTarget(opacityAnim, MainPlayerBarControl.TimelineGridRef);
		Storyboard.SetTargetProperty(opacityAnim, new PropertyPath("Opacity"));
		sb.Children.Add(opacityAnim);
		DoubleAnimation scaleAnim = new DoubleAnimation(visible ? 1.0 : 0.0, new Duration(TimeSpan.FromMilliseconds(300.0)))
		{
			EasingFunction = new QuadraticEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		Storyboard.SetTarget(scaleAnim, MainPlayerBarControl.TimelineGridRef);
		Storyboard.SetTargetProperty(scaleAnim, new PropertyPath("LayoutTransform.ScaleY"));
		sb.Children.Add(scaleAnim);
		if (!visible)
		{
			sb.Completed += delegate
			{
				MainPlayerBarControl.TimelineGridRef.Visibility = Visibility.Collapsed;
			};
		}
		sb.Begin();
	}

	private void HighlightNowPlaying(string videoId)
	{
		if (_homeCachePanel != null && !ContentPanel.Children.Contains(_homeCachePanel))
		{
			HighlightInPanel(_homeCachePanel, videoId);
		}
		foreach (StackPanel panel in _pageCache.Values)
		{
			if (panel != null && !ContentPanel.Children.Contains(panel))
			{
				HighlightInPanel(panel, videoId);
			}
		}
		HighlightInPanel(ContentPanel, videoId);
	}

	private bool _isSidebarCoverExpanded = false;

	private void ToggleSidebarCover()
	{
		_isSidebarCoverExpanded = !_isSidebarCoverExpanded;
		if (_isSidebarCoverExpanded)
		{
			MainSidebar.SidebarCoverImageRef.Fill = MainPlayerBarControl.PlayerThumbnailRef.Fill;
			double targetHeight = _isSidebarMinimized ? 0.0 : MainSidebar.ActualWidth;
			double targetThumbWidth = _isSidebarMinimized ? 48.0 : 0.0;
			Thickness targetMargin = _isSidebarMinimized ? new Thickness(15, 0, 0, 0) : new Thickness(0, 0, 0, 0);

			DoubleAnimation expandAnim = new DoubleAnimation(0.0, targetHeight, new Duration(TimeSpan.FromMilliseconds(300)))
			{
				EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
			};
			MainSidebar.SidebarCoverContainerRef.BeginAnimation(FrameworkElement.HeightProperty, expandAnim);

			DoubleAnimation shrinkAnim = new DoubleAnimation(48.0, targetThumbWidth, new Duration(TimeSpan.FromMilliseconds(300)))
			{
				EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
			};
			MainPlayerBarControl.PlayerThumbnailContainerRef.BeginAnimation(FrameworkElement.WidthProperty, shrinkAnim);

			ThicknessAnimation marginAnim = new ThicknessAnimation(targetMargin, new Duration(TimeSpan.FromMilliseconds(300)))
			{
				EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
			};
			MainPlayerBarControl.PlayerTextPanelRef.BeginAnimation(FrameworkElement.MarginProperty, marginAnim);
		}
		else
		{
			DoubleAnimation shrinkAnim = new DoubleAnimation(MainSidebar.SidebarCoverContainerRef.Height, 0.0, new Duration(TimeSpan.FromMilliseconds(300)))
			{
				EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
			};
			MainSidebar.SidebarCoverContainerRef.BeginAnimation(FrameworkElement.HeightProperty, shrinkAnim);

			DoubleAnimation expandAnim = new DoubleAnimation(MainPlayerBarControl.PlayerThumbnailContainerRef.Width, 48.0, new Duration(TimeSpan.FromMilliseconds(300)))
			{
				EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
			};
			MainPlayerBarControl.PlayerThumbnailContainerRef.BeginAnimation(FrameworkElement.WidthProperty, expandAnim);

			ThicknessAnimation marginAnim = new ThicknessAnimation(new Thickness(15, 0, 0, 0), new Duration(TimeSpan.FromMilliseconds(300)))
			{
				EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
			};
			MainPlayerBarControl.PlayerTextPanelRef.BeginAnimation(FrameworkElement.MarginProperty, marginAnim);
		}
	}

	public void UpdateSidebarVinylMode(bool enabled)
	{
		if (enabled)
		{
			MainSidebar.SidebarCoverImageRef.RadiusX = 1000;
			MainSidebar.SidebarCoverImageRef.RadiusY = 1000;
			MainSidebar.SidebarVinylHoleRef.Visibility = Visibility.Visible;
			MainSidebar.SidebarVinylHoleInnerRef.Visibility = Visibility.Visible;
			MainSidebar.SidebarGramophoneTonearmRef.Visibility = Visibility.Visible;

			DoubleAnimation armAnim = new DoubleAnimation(20, new Duration(TimeSpan.FromMilliseconds(500)))
			{
				EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
			};
			MainSidebar.SidebarTonearmRotationRef.BeginAnimation(RotateTransform.AngleProperty, armAnim);

			MainSidebar.StartVinylRotation();
			if (_player != null && !_player.IsPlaying)
			{
				MainSidebar.PauseVinylRotation();
			}
		}
		else
		{
			MainSidebar.SidebarCoverImageRef.RadiusX = 8;
			MainSidebar.SidebarCoverImageRef.RadiusY = 8;
			MainSidebar.SidebarVinylHoleRef.Visibility = Visibility.Collapsed;
			MainSidebar.SidebarVinylHoleInnerRef.Visibility = Visibility.Collapsed;
			MainSidebar.SidebarGramophoneTonearmRef.Visibility = Visibility.Collapsed;
			MainSidebar.SidebarTonearmRotationRef.BeginAnimation(RotateTransform.AngleProperty, null);
			MainSidebar.SidebarTonearmRotationRef.Angle = 0;
			MainSidebar.StopVinylRotation();
		}
	}

	public void PauseSidebarVinylRotation()
	{
		MainSidebar.PauseVinylRotation();
		if (MainSidebar.SidebarGramophoneTonearmRef.Visibility == Visibility.Visible)
		{
			DoubleAnimation armAnim = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(400)))
			{
				EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
			};
			MainSidebar.SidebarTonearmRotationRef.BeginAnimation(RotateTransform.AngleProperty, armAnim);
		}
	}

	public void ResumeSidebarVinylRotation()
	{
		MainSidebar.ResumeVinylRotation();
		if (MainSidebar.SidebarGramophoneTonearmRef.Visibility == Visibility.Visible)
		{
			DoubleAnimation armAnim = new DoubleAnimation(20, new Duration(TimeSpan.FromMilliseconds(400)))
			{
				EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
			};
			MainSidebar.SidebarTonearmRotationRef.BeginAnimation(RotateTransform.AngleProperty, armAnim);
		}
	}

	public void AnimateSidebarTonearmSkip()
	{
		if (MainSidebar.SidebarGramophoneTonearmRef.Visibility == Visibility.Visible)
		{
			Storyboard sb = new Storyboard();
			DoubleAnimation armOff = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(200)))
			{
				EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
			};
			DoubleAnimation armOn = new DoubleAnimation(20, new Duration(TimeSpan.FromMilliseconds(300)))
			{
				BeginTime = TimeSpan.FromMilliseconds(300),
				EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
			};
			Storyboard.SetTarget(armOff, MainSidebar.SidebarTonearmRotationRef);
			Storyboard.SetTargetProperty(armOff, new PropertyPath(RotateTransform.AngleProperty));
			Storyboard.SetTarget(armOn, MainSidebar.SidebarTonearmRotationRef);
			Storyboard.SetTargetProperty(armOn, new PropertyPath(RotateTransform.AngleProperty));
			sb.Children.Add(armOff);
			sb.Children.Add(armOn);
			sb.Begin();
		}
	}

	private void SidebarCoverContainer_MouseDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton == MouseButton.Middle)
		{
			e.Handled = true;
			if (_isSidebarCoverExpanded)
			{
				ToggleSidebarCover();
			}
		}
	}

	private string ToHighResUrl(string url, int targetSize = 800)
	{
		if (string.IsNullOrEmpty(url))
		{
			return url;
		}
		if (url.Contains("googleusercontent.com") || url.Contains("ggpht.com"))
		{
			int eqIndex = url.LastIndexOf("=");
			if (eqIndex > 0)
			{
				string param = url.Substring(eqIndex + 1);
				if (param.Contains("w") && param.Contains("h"))
				{
					string newParam = Regex.Replace(param, @"w\d+", $"w{targetSize}");
					newParam = Regex.Replace(newParam, @"h\d+", $"h{targetSize}");
					return url.Substring(0, eqIndex + 1) + newParam;
				}
				else if (param.Contains("s"))
				{
					string newParam = Regex.Replace(param, @"s\d+", $"s{targetSize}");
					return url.Substring(0, eqIndex + 1) + newParam;
				}
				else
				{
					return url.Substring(0, eqIndex + 1) + $"s{targetSize}";
				}
			}
			else
			{
				return url + $"=s{targetSize}";
			}
		}
		return url;
	}

	private void EnlargeImage(System.Windows.Media.Brush initialBrush, string highResUrl)
	{
		if (initialBrush == null)
		{
			return;
		}
		MainOverlayControl.LargeCoverRectRef.Fill = initialBrush;
		MainOverlayControl.LargeCoverOverlayRef.Visibility = Visibility.Visible;
		DoubleAnimation fade = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(250.0))
		{
			EasingFunction = new QuarticEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		DoubleAnimation scale = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(350.0))
		{
			EasingFunction = new BackEase
			{
				Amplitude = 0.5,
				EasingMode = EasingMode.EaseOut
			}
		};
		MainOverlayControl.LargeCoverOverlayRef.BeginAnimation(UIElement.OpacityProperty, fade);
		MainOverlayControl.LargeCoverScaleRef.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
		MainOverlayControl.LargeCoverScaleRef.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
		if (string.IsNullOrEmpty(highResUrl))
		{
			return;
		}
		string processedUrl = ToHighResUrl(highResUrl, 800);
		Task.Run(async delegate
		{
			try
			{
				byte[] bytes;
				if (processedUrl.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
				{
					string localPath = new Uri(processedUrl).LocalPath;
					bytes = System.IO.File.ReadAllBytes(localPath);
				}
				else
				{
					bytes = await BackendService.Instance.DownloadImageAsync(processedUrl, CancellationToken.None);
				}
				await base.Dispatcher.InvokeAsync(delegate
				{
					try
					{
						BitmapImage bitmapImage = new BitmapImage();
						using (MemoryStream streamSource = new MemoryStream(bytes))
						{
							bitmapImage.BeginInit();
							bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
							bitmapImage.DecodePixelWidth = 1000;
							bitmapImage.StreamSource = streamSource;
							bitmapImage.EndInit();
						}
						bitmapImage.Freeze();
						MainOverlayControl.LargeCoverRectRef.Fill = new ImageBrush(bitmapImage)
						{
							Stretch = Stretch.UniformToFill
						};
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

	private System.Windows.Media.Brush? GetImageBorderBrush(Border imgBorder)
	{
		if (imgBorder.Child is Grid grid && grid.Children.Count > 0 && grid.Children[0] is Border innerBorder && innerBorder.Child is System.Windows.Shapes.Rectangle rect)
		{
			return rect.Fill;
		}
		else if (imgBorder.Child is System.Windows.Shapes.Rectangle directRect)
		{
			return directRect.Fill;
		}
		return null;
	}

	private string? GetImageBorderUrl(Border imgBorder)
	{
		if (imgBorder.Child is Grid grid && grid.Children.Count > 0 && grid.Children[0] is Border innerBorder && innerBorder.Child is System.Windows.Shapes.Rectangle rect)
		{
			return rect.Tag as string;
		}
		else if (imgBorder.Child is System.Windows.Shapes.Rectangle directRect)
		{
			return directRect.Tag as string;
		}
		return null;
	}

	private void PlayerThumbnailBorder_MouseDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton == MouseButton.Middle)
		{
			e.Handled = true;
			MainOverlayControl.LargeCoverOverlayRef.Visibility = Visibility.Collapsed;
			MainOverlayControl.LargeCoverOverlayRef.BeginAnimation(UIElement.OpacityProperty, null);
			ToggleSidebarCover();
			return;
		}
		if (e.ChangedButton == MouseButton.Left)
		{
			if (e.ClickCount != 1)
			{
				return;
			}
			e.Handled = true;
			if (MainPlayerBarControl.PlayerThumbnailRef.Fill == null)
			{
				return;
			}
			EnlargeImage(MainPlayerBarControl.PlayerThumbnailRef.Fill, _currentThumbUrl);
		}
	}

	private void StartInlinePlaylistCreation()
	{
		if (_isSidebarMinimized)
		{
			SetSidebarState(minimize: false);
		}
		Border rowBorder = new Border
		{
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(8.0, 5.0, 0.0, 5.0),
			Margin = new Thickness(0.0, 1.0, 0.0, 1.0),
			Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, byte.MaxValue, byte.MaxValue, byte.MaxValue))
		};
		StackPanel row = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal
		};
		Border imgBorder = new Border
		{
			Width = 32.0,
			Height = 32.0,
			CornerRadius = new CornerRadius(4.0),
			Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 25, 35)),
			Margin = new Thickness(0.0, 0.0, 10.0, 0.0)
		};
		imgBorder.Clip = new RectangleGeometry
		{
			Rect = new Rect(0.0, 0.0, 32.0, 32.0),
			RadiusX = 4.0,
			RadiusY = 4.0
		};
		row.Children.Add(imgBorder);
		System.Windows.Controls.TextBox nameInput = new System.Windows.Controls.TextBox
		{
			Background = System.Windows.Media.Brushes.Transparent,
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			BorderThickness = new Thickness(0.0),
			FontSize = 14.0,
			VerticalAlignment = VerticalAlignment.Center,
			Width = 150.0,
			CaretBrush = System.Windows.Media.Brushes.White
		};
		row.Children.Add(nameInput);
		rowBorder.Child = row;
		int insertIndex = 0;
		for (int i = 0; i < MainSidebar.LibraryPanelRef.Items.Count; i++)
		{
			if (MainSidebar.LibraryPanelRef.Items[i] is Grid g && g.Children.Count > 1 && ((g.Children[1] is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is TextBlock { Text: "PLAYLISTS" }) || g.Children[1] is TextBlock { Text: "PLAYLISTS" }))
			{
				insertIndex = i + 1;
				break;
			}
		}
		rowBorder.Height = 0.0;
		rowBorder.Opacity = 0.0;
		MainSidebar.LibraryPanelRef.Items.Insert(insertIndex, rowBorder);
		Storyboard storyboard = new Storyboard();
		DoubleAnimation hIn = new DoubleAnimation(0.0, 42.0, TimeSpan.FromMilliseconds(150.0));
		Storyboard.SetTarget(hIn, rowBorder);
		Storyboard.SetTargetProperty(hIn, new PropertyPath("Height"));
		storyboard.Children.Add(hIn);
		DoubleAnimation oIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(150.0))
		{
			BeginTime = TimeSpan.FromMilliseconds(50.0)
		};
		Storyboard.SetTarget(oIn, rowBorder);
		Storyboard.SetTargetProperty(oIn, new PropertyPath("Opacity"));
		storyboard.Children.Add(oIn);
		storyboard.Completed += delegate
		{
			rowBorder.Height = double.NaN;
			nameInput.Focus();
		};
		storyboard.Begin();
		bool isProcessing = false;
		nameInput.KeyDown += delegate(object s, System.Windows.Input.KeyEventArgs e)
		{
			if (e.Key == Key.Return)
			{
				e.Handled = true;
				CommitPlaylist();
			}
			else if (e.Key == Key.Escape)
			{
				e.Handled = true;
				AnimateAndRemove();
			}
		};
		nameInput.LostFocus += delegate
		{
			if (!isProcessing)
			{
				CommitPlaylist();
			}
		};
		void AnimateAndRemove()
		{
			if (double.IsNaN(rowBorder.Height))
			{
				rowBorder.Height = rowBorder.ActualHeight;
			}
			Storyboard storyboard2 = new Storyboard();
			DoubleAnimation hOut = new DoubleAnimation(rowBorder.ActualHeight, 0.0, TimeSpan.FromMilliseconds(150.0))
			{
				BeginTime = TimeSpan.FromMilliseconds(50.0)
			};
			Storyboard.SetTarget(hOut, rowBorder);
			Storyboard.SetTargetProperty(hOut, new PropertyPath("Height"));
			storyboard2.Children.Add(hOut);
			DoubleAnimation oOut = new DoubleAnimation(rowBorder.Opacity, 0.0, TimeSpan.FromMilliseconds(150.0));
			Storyboard.SetTarget(oOut, rowBorder);
			Storyboard.SetTargetProperty(oOut, new PropertyPath("Opacity"));
			storyboard2.Children.Add(oOut);
			storyboard2.Completed += delegate
			{
				if (MainSidebar.LibraryPanelRef.Items.Contains(rowBorder))
				{
					MainSidebar.LibraryPanelRef.Items.Remove(rowBorder);
				}
			};
			storyboard2.Begin();
		}
		async void CommitPlaylist()
		{
			if (!isProcessing)
			{
				string title = nameInput.Text.Trim();
				if (!string.IsNullOrEmpty(title))
				{
					isProcessing = true;
					nameInput.IsEnabled = false;
					try
					{
						string newId = ((string?)(await BackendService.Instance.CreatePlaylistAsync(title, "PUBLIC", CancellationToken.None))["playlistId"]) ?? "";
						if (!string.IsNullOrEmpty(newId) && !newId.StartsWith("VL"))
						{
							newId = "VL" + newId;
						}
						if (!string.IsNullOrEmpty(newId))
						{
							if (_cachedPlaylists != null)
							{
								JsonObject newPl = new JsonObject
								{
									["playlistId"] = newId,
									["title"] = title
								};
								_cachedPlaylists.Add(newPl);
							}
							nameInput.Visibility = Visibility.Collapsed;
							TextBlock txt = new TextBlock
							{
								Text = title,
								Foreground = System.Windows.Media.Brushes.LightGray,
								FontSize = 14.0,
								VerticalAlignment = VerticalAlignment.Center,
								TextTrimming = TextTrimming.CharacterEllipsis
							};
							row.Children.Add(txt);
							rowBorder.Background = System.Windows.Media.Brushes.Transparent;
							rowBorder.Cursor = System.Windows.Input.Cursors.Hand;
							rowBorder.Tag = newId;
							rowBorder.MouseEnter += delegate
							{
								if (_currentPageId != newId)
								{
									FadeBorderBackgroundToResource(rowBorder, "CardHoverBrush");
									FadeTextForegroundToColor(txt, ((SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"]).Color);
								}
							};
							rowBorder.MouseLeave += delegate
							{
								if (_currentPageId != newId)
								{
									FadeBorderBackgroundToColor(rowBorder, Colors.Transparent);
									FadeTextForegroundToColor(txt, ((SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["SidebarTextBrush"]).Color);
								}
							};
							rowBorder.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
							{
								e.Handled = true;
								OpenPlaylistPage(newId, title, "Playlist", "", "Playlist");
							};
							rowBorder.ContextMenu = CreateCollectionContextMenu(newId, title, "Playlist", rowBorder, txt);
						}
						else
						{
							AnimateAndRemove();
						}
						return;
					}
					catch
					{
						AnimateAndRemove();
						return;
					}
				}
				AnimateAndRemove();
			}
		}
	}
}







