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
	private async Task LoadHomeFeedAsync(bool forceReload = false)
	{
		if (!forceReload && _homeCachePanel.Children.Count > 0)
		{
			await FadeInContentAsync(await FadeOutContentAsync(), delegate
			{
				ContentPanel.Children.Add(_homeCachePanel);
				ResetScroll();
			});
			return;
		}
		int tId = await FadeOutContentAsync();
		try
		{
			_homeCachePanel.Children.Clear();
			_lazyRenderElements.Clear();
			_lazyRenderActions.Clear();
			_lazyRenderStates.Clear();
			bool isFirstBatch = true;
			bool isUIUpdated = false;
			bool heroAdded = false;
			await foreach (JsonArray shelves in BackendService.Instance.GetHomeFeedStreamAsync(CancellationToken.None, _homeFeedLimit))
			{
				if (shelves == null || shelves.Count == 0)
				{
					continue;
				}
				List<JsonNode> sortedResults = shelves.OrderBy(delegate(JsonNode? JsonNode)
				{
					string text = ((string?)JsonNode?["title"]) ?? "";
					if (text.ToLower().Contains("quick picks"))
					{
						return 0;
					}
					return text.ToLower().Contains("listen again") ? 1 : 2;
				}).ToList();
				if (!heroAdded)
				{
					_allHeroCandidates.Clear();
					foreach (JsonNode shelf in sortedResults)
					{
						if (!(shelf["contents"] is JsonArray contents))
						{
							continue;
						}
						int idx = 0;
						foreach (JsonNode item in contents)
						{
							string vId = (string?)item["videoId"];
							string pId = (string?)item["playlistId"];
							bool isCard = item["isCard"]?.GetValue<bool>() ?? false;
							string t = ((string?)shelf["title"]) ?? "";
							if ((t.ToLower().Contains("quick picks") || t.ToLower().Contains("listen again")) && idx < 8)
							{
								idx++;
								continue;
							}
							if (isCard && !string.IsNullOrEmpty(pId) && string.IsNullOrEmpty(vId))
							{
								idx++;
								continue;
							}
							string rType = ((string?)item["resultType"]) ?? "";
							if (string.Equals(rType, "video", StringComparison.OrdinalIgnoreCase) || string.Equals(rType, "musicVideo", StringComparison.OrdinalIgnoreCase))
							{
								idx++;
								continue;
							}
							if (!string.IsNullOrEmpty(vId))
							{
								_allHeroCandidates.Add(item);
							}
							idx++;
						}
					}
					List<JsonNode> distinctCandidates = new List<JsonNode>();
					HashSet<string> seenIds = new HashSet<string>();
					foreach (JsonNode item2 in _allHeroCandidates)
					{
						string vid = ((string?)item2["videoId"]) ?? "";
						if (!seenIds.Contains(vid))
						{
							seenIds.Add(vid);
							distinctCandidates.Add(item2);
						}
					}
					if (distinctCandidates.Count > 0)
					{
						List<JsonNode> pickedItems = new List<JsonNode>();
						Random rnd = new Random();
						for (int i = 0; i < 3; i++)
						{
							if (distinctCandidates.Count <= 0)
							{
								break;
							}
							JsonNode picked = distinctCandidates[rnd.Next(distinctCandidates.Count)];
							distinctCandidates.Remove(picked);
							pickedItems.Add(picked);
						}
						List<(string, string, string, string, JsonArray)> heroData = new List<(string, string, string, string, JsonArray)>();
						foreach (JsonNode item3 in pickedItems)
						{
							string vId2 = ((string?)item3["videoId"]) ?? "";
							string t2 = ((string?)item3["title"]) ?? "";
							string a = "";
							JsonArray aTok = item3["artists"] as JsonArray;
							if (aTok != null)
							{
								List<string> names = new List<string>();
								foreach (JsonNode art in aTok)
								{
									names.Add(((string?)art["name"]) ?? "");
								}
								a = string.Join(", ", names);
							}
							string url = "";
							if (item3["thumbnails"] is JsonArray { Count: >0 } th)
							{
								url = ((string?)th[th.Count - 1]["url"]) ?? "";
							}
							if (!string.IsNullOrEmpty(url))
							{
								url = ((!url.Contains("=")) ? (url + "=w1200-h1200-l90-rj") : (url.Substring(0, url.IndexOf('=')) + "=w1200-h1200-l90-rj"));
							}
							heroData.Add((vId2, t2, a, url, aTok));
						}
						UIElement heroHeader = CreateHeroHeader(heroData);
						bool skipAnim = MainOverlayControl.LoadingOverlayRef.Visibility == Visibility.Visible;
						heroHeader.Opacity = skipAnim ? 1.0 : 0.0;
						TranslateTransform tt = (TranslateTransform)(heroHeader.RenderTransform = new TranslateTransform(0.0, skipAnim ? 0.0 : 20.0));
						_homeCachePanel.Children.Add(heroHeader);
						DoubleAnimation anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(500.0))
						{
							EasingFunction = new QuarticEase
							{
								EasingMode = EasingMode.EaseOut
							}
						};
						DoubleAnimation animY = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(500.0))
						{
							EasingFunction = new QuarticEase
							{
								EasingMode = EasingMode.EaseOut
							}
						};
						if (!skipAnim)
						{
							heroHeader.BeginAnimation(UIElement.OpacityProperty, anim);
							tt.BeginAnimation(TranslateTransform.YProperty, animY);
						}
						heroAdded = true;
					}
				}
				int delayMs = 50;
				foreach (JsonNode shelf2 in sortedResults)
				{
					if (!(shelf2["contents"] is JsonArray contents2))
					{
						continue;
					}
					List<JsonNode> validItems = new List<JsonNode>();
					foreach (JsonNode item4 in contents2)
					{
						if (!string.IsNullOrEmpty((string?)item4["videoId"]) || !string.IsNullOrEmpty((string?)item4["playlistId"]))
						{
							validItems.Add(item4);
						}
					}
					if (validItems.Count == 0)
					{
						continue;
					}
					string titleStr = ((string?)shelf2["title"]) ?? "Recommended";
					if (_blockedCategories.Any((string b) => titleStr.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0) || validItems.Count == 0)
					{
						continue;
					}
					int songCount = 0;
					int mixCount = 0;
					foreach (JsonNode item5 in validItems)
					{
						string vId3 = (string?)item5["videoId"];
						string pId2 = (string?)item5["playlistId"];
						string title = ((string?)item5["title"]) ?? "";
						bool num = !string.IsNullOrEmpty(pId2) && !string.IsNullOrEmpty(vId3) && (pId2.StartsWith("RD") || pId2.StartsWith("VLRD") || title.ToLower().Contains("mix"));
						bool isPlaylistOrAlbum = string.IsNullOrEmpty(vId3) && !string.IsNullOrEmpty(pId2);
						if (num || isPlaylistOrAlbum)
						{
							mixCount++;
						}
						else if (!string.IsNullOrEmpty(vId3))
						{
							songCount++;
						}
					}
					bool useCards = songCount >= mixCount;
					string lowerTitle = titleStr.ToLower();
					if (lowerTitle.Contains("mix") || lowerTitle.Contains("community") || lowerTitle.Contains("playlists") || lowerTitle.Contains("shows") || lowerTitle.Contains("podcasts"))
					{
						useCards = false;
					}
					else if (lowerTitle.Contains("new releases") || lowerTitle.Contains("albums") || lowerTitle.Contains("singles") || lowerTitle.Contains("quick picks") || lowerTitle.Contains("listen again"))
					{
						useCards = true;
					}
					System.Windows.Controls.Panel container = null;
					int initialCount = 16;
					Action<JsonNode> renderItem = delegate(JsonNode JsonNode)
					{
						string text = (string?)JsonNode?["videoId"];
						string text2 = (string?)JsonNode["playlistId"];
						string text3 = ((!string.IsNullOrEmpty(text)) ? text : (text2 ?? ""));
						string text4 = ((string?)JsonNode?["title"]) ?? "";
						string text5 = "";
						JsonArray JsonArray = JsonNode?["artists"] as JsonArray;
						if (JsonArray != null)
						{
							List<string> list = new List<string>();
							foreach (JsonNode current2 in JsonArray)
							{
								list.Add(((string?)current2["name"]) ?? "");
							}
							text5 = string.Join(", ", list);
						}
						JsonArray jArray2 = JsonNode?["thumbnails"] as JsonArray;
						string thumbUrl = "";
						if (jArray2 != null && jArray2.Count > 0)
						{
							thumbUrl = ((string?)jArray2[jArray2.Count - 1]["url"]) ?? "";
						}
						bool num2 = JsonNode["isCard"]?.GetValue<bool>() ?? false;
						string text6 = "Song";
						if (num2)
						{
							if (!string.IsNullOrEmpty(text2))
							{
								if (string.IsNullOrEmpty(text))
								{
									text6 = ((!text2.StartsWith("UC")) ? "Album" : "Artist");
								}
								else if (text2.StartsWith("RD") || text2.StartsWith("VLRD") || text4.ToLower().Contains("mix") || text4.ToLower().Contains("recap"))
								{
									text6 = "Mix";
									text3 = text2;
									if (text3.StartsWith("VL"))
									{
										text3 = text3.Substring(2);
									}
								}
								else
								{
									text6 = "Song";
								}
							}
						}
						else if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(text2))
						{
							text6 = ((!text2.StartsWith("UC")) ? "Album" : "Artist");
						}
						if (!(text6 == "Song") || !string.IsNullOrEmpty(text5))
						{
							if (useCards)
							{
								container.Children.Add(CreateTrackCard(text3, text4, text5, thumbUrl, text6, JsonArray));
							}
							else
							{
								container.Children.Add(CreateTrackTile(text3, text4, text5, thumbUrl, text6, JsonArray));
							}
						}
					};
					Action loadMore = delegate
					{
						if (container != null)
						{
							for (int j = initialCount; j < validItems.Count; j++)
							{
								renderItem(validItems[j]);
							}
						}
					};
					UIElement elementToAdd;
					if (useCards)
					{
						(UIElement, System.Windows.Controls.Panel) tuple = CreateExpandableSection(titleStr, validItems.Count, loadMore);
						UIElement sectionContainer = tuple.Item1;
						System.Windows.Controls.Panel wp = tuple.Item2;
						container = wp;
						elementToAdd = sectionContainer;
					}
					else
					{
						(UIElement, System.Windows.Controls.Panel) tuple2 = CreateExpandableGridSection(titleStr, 270.0, validItems.Count, loadMore);
						UIElement sectionContainer2 = tuple2.Item1;
						System.Windows.Controls.Panel gridPanel = tuple2.Item2;
						UniformGrid grid = (UniformGrid)gridPanel;
						grid.SizeChanged += delegate(object s, SizeChangedEventArgs e)
						{
							grid.Columns = Math.Max(1, (int)(e.NewSize.Width / 260.0));
						};
						container = grid;
						elementToAdd = sectionContainer2;
					}
					FrameworkElement feToAdd = elementToAdd as FrameworkElement;
					if (feToAdd != null)
					{
						feToAdd.MinHeight = (useCards ? 280 : 220);
						Action doRender = async delegate
						{
							for (int j = 0; j < Math.Min(validItems.Count, initialCount); j++)
							{
								renderItem(validItems[j]);
								await Dispatcher.Yield(DispatcherPriority.Background);
							}
							feToAdd.MinHeight = 0.0;
						};
						_ = _ = _ = _ = base.Dispatcher.InvokeAsync(delegate
						{
							doRender();
						});
					}
					bool skipAnim = MainOverlayControl.LoadingOverlayRef.Visibility == Visibility.Visible;
					elementToAdd.Opacity = skipAnim ? 1.0 : 0.0;
					elementToAdd.RenderTransform = new TranslateTransform(0.0, skipAnim ? 0.0 : 20.0);
					if (elementToAdd is FrameworkElement fe)
					{
						fe.Tag = titleStr.ToLower();
					}
					int priority = 2;
					if (titleStr.ToLower().Contains("quick picks"))
					{
						priority = 0;
					}
					else if (titleStr.ToLower().Contains("listen again"))
					{
						priority = 1;
					}
					int targetIndex = _homeCachePanel.Children.Count;
					for (int i2 = 0; i2 < _homeCachePanel.Children.Count; i2++)
					{
						FrameworkElement child = _homeCachePanel.Children[i2] as FrameworkElement;
						int childPriority = 2;
						if (child != null && child.Tag is string childTag)
						{
							if (childTag == "hero_header")
							{
								childPriority = -1;
							}
							else if (childTag.Contains("quick picks"))
							{
								childPriority = 0;
							}
							else if (childTag.Contains("listen again"))
							{
								childPriority = 1;
							}
						}
						if (priority < childPriority)
						{
							targetIndex = i2;
							break;
						}
					}
					_homeCachePanel.Children.Insert(targetIndex, elementToAdd);
					DoubleAnimation anim2 = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(500.0))
					{
						BeginTime = TimeSpan.FromMilliseconds(delayMs),
						EasingFunction = new QuarticEase
						{
							EasingMode = EasingMode.EaseOut
						}
					};
					DoubleAnimation animMove = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(500.0))
					{
						BeginTime = TimeSpan.FromMilliseconds(delayMs),
						EasingFunction = new QuarticEase
						{
							EasingMode = EasingMode.EaseOut
						}
					};
					if (!skipAnim)
					{
						elementToAdd.BeginAnimation(UIElement.OpacityProperty, anim2);
						elementToAdd.RenderTransform.BeginAnimation(TranslateTransform.YProperty, animMove);
					}
					delayMs += 80;
				}
				if (isFirstBatch)
				{
					isFirstBatch = false;
					await FadeInContentAsync(tId, delegate
					{
						ContentPanel.Children.Add(_homeCachePanel);
						ResetScroll();
						base.Dispatcher.InvokeAsync(CheckVisibilityOfLazyElements, DispatcherPriority.Background);
					});
					isUIUpdated = true;
				}
			}
			if (!isUIUpdated)
			{
				await FadeInContentAsync(tId, delegate
				{
					ContentPanel.Children.Add(_homeCachePanel);
					ResetScroll();
					base.Dispatcher.InvokeAsync(CheckVisibilityOfLazyElements, DispatcherPriority.Background);
				});
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			await FadeInContentAsync(tId, delegate
			{
				ShowGlobalError("Failed to load feed", ex2.Message, delegate
				{
					_ = _ = _ = _ = LoadLibraryAsync();
					_ = _ = _ = _ = LoadHomeFeedAsync(forceReload: true);
				});
			});
		}
	}

	private async Task LoadRadioFeedAsync(bool forceReload = false)
	{
		if (!forceReload && _radioCachePanel.Children.Count > 0)
		{
			await FadeInContentAsync(await FadeOutContentAsync(), delegate
			{
				ContentPanel.Children.Add(_radioCachePanel);
				ResetScroll();
			});
			return;
		}
		int tId = await FadeOutContentAsync();
		_radioCachePanel.Children.Clear();
		_lazyRenderElements.Clear();
		_lazyRenderActions.Clear();
		_lazyRenderStates.Clear();
		TextBlock title = new TextBlock
		{
			Text = "Radio Stations",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			FontSize = 32.0,
			FontWeight = FontWeights.Bold,
			VerticalAlignment = VerticalAlignment.Center
		};
		StackPanel leftSide = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal
		};
		leftSide.Children.Add(title);
		DockPanel dockPanel = new DockPanel
		{
			LastChildFill = false
		};
		DockPanel.SetDock(leftSide, Dock.Left);
		dockPanel.Children.Add(leftSide);
		dockPanel.Margin = new Thickness(0.0, 20.0, 0.0, 30.0);
		_radioCachePanel.Children.Add(dockPanel);
		UniformGrid wp = new UniformGrid
		{
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(5.0, 0.0, 5.0, 30.0)
		};
		wp.SizeChanged += delegate(object s, SizeChangedEventArgs e)
		{
			if (e.NewSize.Width != 0.0)
			{
				int num = Math.Max(1, (int)(e.NewSize.Width / 450.0));
				if (wp.Columns != num)
				{
					wp.Columns = num;
				}
			}
		};
		foreach (RadioStation radio in _savedRadios)
		{
			Border cardBorder = new Border
			{
				Background = (System.Windows.Media.Brush)System.Windows.Application.Current.MainWindow.Resources["CardBackground"],
				BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(15, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(10.0),
				Margin = new Thickness(10.0),
				Padding = new Thickness(10.0)
			};
			cardBorder.MouseEnter += delegate
			{
				FadeBorderBackgroundToResource(cardBorder, "CardHoverBrush");
			};
			cardBorder.MouseLeave += delegate
			{
				FadeBorderBackgroundToResource(cardBorder, "CardBackground");
			};
			Grid cardGrid = new Grid();
			cardGrid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = GridLength.Auto
			});
			cardGrid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(1.0, GridUnitType.Star)
			});
			Border iconBorder = new Border
			{
				Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 45)),
				CornerRadius = new CornerRadius(10.0),
				Width = 50.0,
				Height = 50.0,
				VerticalAlignment = VerticalAlignment.Top,
				Margin = new Thickness(0.0, 0.0, 15.0, 0.0),
				Child = new System.Windows.Controls.Image
				{
					Source = (System.Windows.Media.DrawingImage)System.Windows.Application.Current.FindResource("radioIcon"),
					Width = 24.0,
					Height = 24.0,
					HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center,
					Opacity = 0.8
				}
			};
			Grid.SetColumn(iconBorder, 0);
			cardGrid.Children.Add(iconBorder);
			StackPanel contentPanel = new StackPanel
			{
				VerticalAlignment = VerticalAlignment.Top
			};
			Grid.SetColumn(contentPanel, 1);
			TextBlock titleBlock = new TextBlock
			{
				Text = radio.Name,
				Foreground = System.Windows.Media.Brushes.White,
				FontSize = 18.0,
				FontWeight = FontWeights.Bold,
				Margin = new Thickness(0.0, 0.0, 0.0, 5.0)
			};
			TextBlock descBlock = new TextBlock
			{
				Text = radio.Description,
				Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(160, 160, 170)),
				FontSize = 12.0,
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(0.0, 0.0, 0.0, 15.0)
			};
			contentPanel.Children.Add(titleBlock);
			if (!string.IsNullOrEmpty(radio.Description))
			{
				contentPanel.Children.Add(descBlock);
			}
			WrapPanel streamsPanel = new WrapPanel
			{
				Orientation = System.Windows.Controls.Orientation.Horizontal
			};
			if (radio.Streams != null)
			{
				foreach (RadioStream stream in radio.Streams)
				{
					StackPanel streamStack = new StackPanel
					{
						Orientation = System.Windows.Controls.Orientation.Horizontal
					};
					streamStack.Children.Add(new System.Windows.Controls.Image
					{
						Source = (System.Windows.Media.DrawingImage)System.Windows.Application.Current.FindResource("playwhiteIcon"),
						Width = 10.0,
						Height = 10.0,
						Margin = new Thickness(0.0, 0.0, 6.0, 0.0),
						HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
						VerticalAlignment = VerticalAlignment.Center,
						Opacity = 0.8
					});
					streamStack.Children.Add(new TextBlock
					{
						Text = stream.Name,
						Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(150, 170, byte.MaxValue)),
						VerticalAlignment = VerticalAlignment.Center,
						FontSize = 13.0
					});
					Border streamBtn = new Border
					{
						Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 100, 120, byte.MaxValue)),
						BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 100, 120, byte.MaxValue)),
						BorderThickness = new Thickness(1.0),
						CornerRadius = new CornerRadius(15.0),
						Padding = new Thickness(12.0, 6.0, 12.0, 6.0),
						Margin = new Thickness(0.0, 0.0, 10.0, 10.0),
						Cursor = System.Windows.Input.Cursors.Hand,
						Tag = stream,
						Child = streamStack
					};
					streamBtn.MouseEnter += delegate
					{
						FadeBorderBackgroundToColor(streamBtn, System.Windows.Media.Color.FromArgb(60, 100, 120, byte.MaxValue));
					};
					streamBtn.MouseLeave += delegate
					{
						FadeBorderBackgroundToColor(streamBtn, System.Windows.Media.Color.FromArgb(20, 100, 120, byte.MaxValue));
					};
					streamBtn.MouseLeftButtonDown += async delegate(object s, MouseButtonEventArgs e)
					{
						e.Handled = true;
						_currentQueue = null;
						try
						{
							await PlayTrack("radio:" + stream.Url, stream.Name, radio.Name, radio.ThumbnailUrl);
						}
						catch (Exception ex)
						{
							AppLogger.Log("Playback: Radio play failed - " + ex.Message, LogLevel.Error);
						}
					};
					streamsPanel.Children.Add(streamBtn);
				}
			}
			contentPanel.Children.Add(streamsPanel);
			cardGrid.Children.Add(contentPanel);
			cardBorder.Child = cardGrid;
			ContextMenu ctx = new ContextMenu();
			MenuItem editItem = new MenuItem
			{
				Header = "Edit Radio",
				Foreground = System.Windows.Media.Brushes.White
			};
			editItem.Click += delegate
			{
				ShowAddRadioPopup(radio);
			};
			MenuItem deleteItem = new MenuItem
			{
				Header = "Delete Radio",
				Foreground = System.Windows.Media.Brushes.White
			};
			deleteItem.Click += delegate
			{
				_savedRadios.Remove(radio);
				SaveSession();
				_ = _ = _ = _ = LoadRadioFeedAsync(forceReload: true);
			};
			ctx.Items.Add(editItem);
			ctx.Items.Add(deleteItem);
			cardBorder.ContextMenu = ctx;
			wp.Children.Add(cardBorder);
		}
		_radioCachePanel.Children.Add(wp);
		await FadeInContentAsync(tId, delegate
		{
			ContentPanel.Children.Add(_radioCachePanel);
			ResetScroll();
			base.Dispatcher.InvokeAsync(CheckVisibilityOfLazyElements, DispatcherPriority.Background);
		});
	}

	private async Task LoadExploreFeedAsync(bool forceReload = false)
	{
		if (!forceReload && _exploreCachePanel.Children.Count > 0)
		{
			await FadeInContentAsync(await FadeOutContentAsync(), delegate
			{
				ContentPanel.Children.Add(_exploreCachePanel);
				ResetScroll();
			});
			return;
		}
		int tId = await FadeOutContentAsync();
		try
		{
			_exploreCachePanel.Children.Clear();
			_lazyRenderElements.Clear();
			_lazyRenderActions.Clear();
			_lazyRenderStates.Clear();
			bool isFirstBatch = true;
			bool isUIUpdated = false;
			bool heroAdded = false;
			await foreach (JsonArray shelves in BackendService.Instance.GetExploreFeedStreamAsync(CancellationToken.None))
			{
				if (shelves == null || shelves.Count == 0)
				{
					continue;
				}
				List<JsonNode> sortedResults = shelves.OrderBy(delegate(JsonNode? JsonNode)
				{
					string text = ((string?)JsonNode?["title"]) ?? "";
					if (text.ToLower().Contains("quick picks"))
					{
						return 0;
					}
					return text.ToLower().Contains("listen again") ? 1 : 2;
				}).ToList();
				if (!heroAdded)
				{
					_allHeroCandidates.Clear();
					foreach (JsonNode shelf in sortedResults)
					{
						if (!(shelf["contents"] is JsonArray contents))
						{
							continue;
						}
						int idx = 0;
						foreach (JsonNode item in contents)
						{
							string vId = (string?)item["videoId"];
							string pId = (string?)item["playlistId"];
							bool isCard = item["isCard"]?.GetValue<bool>() ?? false;
							string t = ((string?)shelf["title"]) ?? "";
							if ((t.ToLower().Contains("quick picks") || t.ToLower().Contains("listen again")) && idx < 8)
							{
								idx++;
								continue;
							}
							if (isCard && !string.IsNullOrEmpty(pId) && string.IsNullOrEmpty(vId))
							{
								idx++;
								continue;
							}
							string rType = ((string?)item["resultType"]) ?? "";
							if (string.Equals(rType, "video", StringComparison.OrdinalIgnoreCase) || string.Equals(rType, "musicVideo", StringComparison.OrdinalIgnoreCase))
							{
								idx++;
								continue;
							}
							if (!string.IsNullOrEmpty(vId))
							{
								_allHeroCandidates.Add(item);
							}
							idx++;
						}
					}
					List<JsonNode> distinctCandidates = new List<JsonNode>();
					HashSet<string> seenIds = new HashSet<string>();
					foreach (JsonNode item2 in _allHeroCandidates)
					{
						string vid = ((string?)item2["videoId"]) ?? "";
						if (!seenIds.Contains(vid))
						{
							seenIds.Add(vid);
							distinctCandidates.Add(item2);
						}
					}
					if (distinctCandidates.Count > 0)
					{
						List<JsonNode> pickedItems = new List<JsonNode>();
						Random rnd = new Random();
						for (int i = 0; i < 3; i++)
						{
							if (distinctCandidates.Count <= 0)
							{
								break;
							}
							JsonNode picked = distinctCandidates[rnd.Next(distinctCandidates.Count)];
							distinctCandidates.Remove(picked);
							pickedItems.Add(picked);
						}
						List<(string, string, string, string, JsonArray)> heroData = new List<(string, string, string, string, JsonArray)>();
						foreach (JsonNode item3 in pickedItems)
						{
							string vId2 = ((string?)item3["videoId"]) ?? "";
							string t2 = ((string?)item3["title"]) ?? "";
							string a = "";
							JsonArray aTok = item3["artists"] as JsonArray;
							if (aTok != null)
							{
								List<string> names = new List<string>();
								foreach (JsonNode art in aTok)
								{
									names.Add(((string?)art["name"]) ?? "");
								}
								a = string.Join(", ", names);
							}
							string url = "";
							if (item3["thumbnails"] is JsonArray { Count: >0 } th)
							{
								url = ((string?)th[th.Count - 1]["url"]) ?? "";
							}
							if (!string.IsNullOrEmpty(url))
							{
								url = ((!url.Contains("=")) ? (url + "=w1200-h1200-l90-rj") : (url.Substring(0, url.IndexOf('=')) + "=w1200-h1200-l90-rj"));
							}
							heroData.Add((vId2, t2, a, url, aTok));
						}
						UIElement heroHeader = CreateHeroHeader(heroData);
						bool skipAnim = MainOverlayControl.LoadingOverlayRef.Visibility == Visibility.Visible;
						heroHeader.Opacity = skipAnim ? 1.0 : 0.0;
						TranslateTransform tt = (TranslateTransform)(heroHeader.RenderTransform = new TranslateTransform(0.0, skipAnim ? 0.0 : 20.0));
						_exploreCachePanel.Children.Add(heroHeader);
						DoubleAnimation anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(500.0))
						{
							EasingFunction = new QuarticEase
							{
								EasingMode = EasingMode.EaseOut
							}
						};
						DoubleAnimation animY = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(500.0))
						{
							EasingFunction = new QuarticEase
							{
								EasingMode = EasingMode.EaseOut
							}
						};
						if (!skipAnim)
						{
							heroHeader.BeginAnimation(UIElement.OpacityProperty, anim);
							tt.BeginAnimation(TranslateTransform.YProperty, animY);
						}
						heroAdded = true;
					}
				}
				int delayMs = 50;
				foreach (JsonNode shelf2 in sortedResults)
				{
					if (!(shelf2["contents"] is JsonArray contents2))
					{
						continue;
					}
					List<JsonNode> validItems = new List<JsonNode>();
					foreach (JsonNode item4 in contents2)
					{
						if (!string.IsNullOrEmpty((string?)item4["videoId"]) || !string.IsNullOrEmpty((string?)item4["playlistId"]))
						{
							validItems.Add(item4);
						}
					}
					if (validItems.Count == 0)
					{
						continue;
					}
					string titleStr = ((string?)shelf2["title"]) ?? "Recommended";
					if (_blockedCategories.Any((string b) => titleStr.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0) || validItems.Count == 0)
					{
						continue;
					}
					int songCount = 0;
					int mixCount = 0;
					foreach (JsonNode item5 in validItems)
					{
						string vId3 = (string?)item5["videoId"];
						string pId2 = (string?)item5["playlistId"];
						string title = ((string?)item5["title"]) ?? "";
						bool num = !string.IsNullOrEmpty(pId2) && !string.IsNullOrEmpty(vId3) && (pId2.StartsWith("RD") || pId2.StartsWith("VLRD") || title.ToLower().Contains("mix"));
						bool isPlaylistOrAlbum = string.IsNullOrEmpty(vId3) && !string.IsNullOrEmpty(pId2);
						if (num || isPlaylistOrAlbum)
						{
							mixCount++;
						}
						else if (!string.IsNullOrEmpty(vId3))
						{
							songCount++;
						}
					}
					bool useCards = songCount >= mixCount;
					string lowerTitle = titleStr.ToLower();
					if (lowerTitle.Contains("mix") || lowerTitle.Contains("community") || lowerTitle.Contains("playlists") || lowerTitle.Contains("shows") || lowerTitle.Contains("podcasts"))
					{
						useCards = false;
					}
					else if (lowerTitle.Contains("new releases") || lowerTitle.Contains("albums") || lowerTitle.Contains("singles") || lowerTitle.Contains("quick picks") || lowerTitle.Contains("listen again"))
					{
						useCards = true;
					}
					System.Windows.Controls.Panel container = null;
					int initialCount = 16;
					Action<JsonNode> renderItem = delegate(JsonNode JsonNode)
					{
						string text = (string?)JsonNode?["videoId"];
						string text2 = (string?)JsonNode["playlistId"];
						string text3 = ((!string.IsNullOrEmpty(text)) ? text : (text2 ?? ""));
						string text4 = ((string?)JsonNode?["title"]) ?? "";
						string text5 = "";
						JsonArray JsonArray = JsonNode?["artists"] as JsonArray;
						if (JsonArray != null)
						{
							List<string> list = new List<string>();
							foreach (JsonNode current2 in JsonArray)
							{
								list.Add(((string?)current2["name"]) ?? "");
							}
							text5 = string.Join(", ", list);
						}
						JsonArray jArray2 = JsonNode?["thumbnails"] as JsonArray;
						string thumbUrl = "";
						if (jArray2 != null && jArray2.Count > 0)
						{
							thumbUrl = ((string?)jArray2[jArray2.Count - 1]["url"]) ?? "";
						}
						bool num2 = JsonNode["isCard"]?.GetValue<bool>() ?? false;
						string text6 = "Song";
						if (num2)
						{
							if (!string.IsNullOrEmpty(text2))
							{
								if (string.IsNullOrEmpty(text))
								{
									text6 = ((!text2.StartsWith("UC")) ? "Album" : "Artist");
								}
								else if (text2.StartsWith("RD") || text2.StartsWith("VLRD") || text4.ToLower().Contains("mix") || text4.ToLower().Contains("recap"))
								{
									text6 = "Mix";
									text3 = text2;
									if (text3.StartsWith("VL"))
									{
										text3 = text3.Substring(2);
									}
								}
								else
								{
									text6 = "Song";
								}
							}
						}
						else if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(text2))
						{
							text6 = ((!text2.StartsWith("UC")) ? "Album" : "Artist");
						}
						if (!(text6 == "Song") || !string.IsNullOrEmpty(text5))
						{
							if (useCards)
							{
								container.Children.Add(CreateTrackCard(text3, text4, text5, thumbUrl, text6, JsonArray));
							}
							else
							{
								container.Children.Add(CreateTrackTile(text3, text4, text5, thumbUrl, text6, JsonArray));
							}
						}
					};
					Action loadMore = delegate
					{
						if (container != null)
						{
							for (int j = initialCount; j < validItems.Count; j++)
							{
								renderItem(validItems[j]);
							}
						}
					};
					UIElement elementToAdd;
					if (useCards)
					{
						(UIElement, System.Windows.Controls.Panel) tuple = CreateExpandableSection(titleStr, validItems.Count, loadMore);
						UIElement sectionContainer = tuple.Item1;
						System.Windows.Controls.Panel wp = tuple.Item2;
						container = wp;
						elementToAdd = sectionContainer;
					}
					else
					{
						(UIElement, System.Windows.Controls.Panel) tuple2 = CreateExpandableGridSection(titleStr, 270.0, validItems.Count, loadMore);
						UIElement sectionContainer2 = tuple2.Item1;
						System.Windows.Controls.Panel gridPanel = tuple2.Item2;
						UniformGrid grid = (UniformGrid)gridPanel;
						grid.SizeChanged += delegate(object s, SizeChangedEventArgs e)
						{
							grid.Columns = Math.Max(1, (int)(e.NewSize.Width / 260.0));
						};
						container = grid;
						elementToAdd = sectionContainer2;
					}
					FrameworkElement feToAdd = elementToAdd as FrameworkElement;
					if (feToAdd != null)
					{
						feToAdd.MinHeight = (useCards ? 280 : 220);
						Action doRender = async delegate
						{
							for (int j = 0; j < Math.Min(validItems.Count, initialCount); j++)
							{
								renderItem(validItems[j]);
								await Dispatcher.Yield(DispatcherPriority.Background);
							}
							feToAdd.MinHeight = 0.0;
						};
						_ = _ = _ = _ = base.Dispatcher.InvokeAsync(delegate
						{
							doRender();
						});
					}
					bool skipAnim = MainOverlayControl.LoadingOverlayRef.Visibility == Visibility.Visible;
					elementToAdd.Opacity = skipAnim ? 1.0 : 0.0;
					elementToAdd.RenderTransform = new TranslateTransform(0.0, skipAnim ? 0.0 : 20.0);
					if (elementToAdd is FrameworkElement fe)
					{
						fe.Tag = titleStr.ToLower();
					}
					int priority = 2;
					if (titleStr.ToLower().Contains("quick picks"))
					{
						priority = 0;
					}
					else if (titleStr.ToLower().Contains("listen again"))
					{
						priority = 1;
					}
					int targetIndex = _exploreCachePanel.Children.Count;
					for (int i2 = 0; i2 < _exploreCachePanel.Children.Count; i2++)
					{
						FrameworkElement child = _exploreCachePanel.Children[i2] as FrameworkElement;
						int childPriority = 2;
						if (child != null && child.Tag is string childTag)
						{
							if (childTag == "hero_header")
							{
								childPriority = -1;
							}
							else if (childTag.Contains("quick picks"))
							{
								childPriority = 0;
							}
							else if (childTag.Contains("listen again"))
							{
								childPriority = 1;
							}
						}
						if (priority < childPriority)
						{
							targetIndex = i2;
							break;
						}
					}
					_exploreCachePanel.Children.Insert(targetIndex, elementToAdd);
					DoubleAnimation anim2 = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(500.0))
					{
						BeginTime = TimeSpan.FromMilliseconds(delayMs),
						EasingFunction = new QuarticEase
						{
							EasingMode = EasingMode.EaseOut
						}
					};
					DoubleAnimation animMove = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(500.0))
					{
						BeginTime = TimeSpan.FromMilliseconds(delayMs),
						EasingFunction = new QuarticEase
						{
							EasingMode = EasingMode.EaseOut
						}
					};
					if (!skipAnim)
					{
						elementToAdd.BeginAnimation(UIElement.OpacityProperty, anim2);
						elementToAdd.RenderTransform.BeginAnimation(TranslateTransform.YProperty, animMove);
					}
					delayMs += 80;
				}
				if (isFirstBatch)
				{
					isFirstBatch = false;
					await FadeInContentAsync(tId, delegate
					{
						ContentPanel.Children.Add(_exploreCachePanel);
						ResetScroll();
						base.Dispatcher.InvokeAsync(CheckVisibilityOfLazyElements, DispatcherPriority.Background);
					});
					isUIUpdated = true;
				}
			}
			if (!isUIUpdated)
			{
				await FadeInContentAsync(tId, delegate
				{
					ContentPanel.Children.Add(_exploreCachePanel);
					ResetScroll();
					base.Dispatcher.InvokeAsync(CheckVisibilityOfLazyElements, DispatcherPriority.Background);
				});
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			await FadeInContentAsync(tId, delegate
			{
				ShowGlobalError("Failed to load feed", ex2.Message, delegate
				{
					_ = _ = _ = _ = LoadLibraryAsync();
					_ = _ = _ = _ = LoadExploreFeedAsync(forceReload: true);
				});
			});
		}
	}

	private async Task LoadAlbumsPageAsync(bool forceReload)
	{
		if (_currentPageId == "albums_page" && !forceReload)
		{
			return;
		}
		PushCurrentPageToHistory();
		_currentPageId = "albums_page";
		UpdateSidebarHighlight();
		if (_albumsCachePanel != null && !forceReload)
		{
			await FadeInContentAsync(await FadeOutContentAsync(), delegate
			{
				ContentPanel.Children.Add(_albumsCachePanel);
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
				Text = "You need to be logged in to view your albums.",
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
		if (_cachedAlbums == null && _cachedLibraryError == null)
		{
			await LoadLibraryAsync();
		}
		if (_cachedLibraryError != null)
		{
			await FadeInContentAsync(tId, delegate
			{
				ShowGlobalError("Failed to load albums", _cachedLibraryError, delegate
				{
					_ = _ = _ = _ = LoadLibraryAsync();
					_ = _ = _ = _ = LoadAlbumsPageAsync(forceReload: true);
				});
			});
			return;
		}
		if (_cachedAlbums != null)
		{
			int delayMs = 0;
			foreach (JsonNode cachedAlbum in _cachedAlbums)
			{
				string title = ((string?)cachedAlbum["title"]) ?? "";
				string id = ((string?)cachedAlbum["browseId"]) ?? "";
				string tUrl = "";
				if (cachedAlbum["thumbnails"] is JsonArray { Count: >0 } thumbs)
				{
					tUrl = ((string?)thumbs[thumbs.Count - 1]["url"]) ?? "";
				}
				string year = ((string?)cachedAlbum["year"]) ?? "";
				string artists = "";
				if (cachedAlbum["artists"] is JsonArray artistsToken)
				{
					List<string> names = new List<string>();
					foreach (JsonNode a in artistsToken)
					{
						names.Add(((string?)a["name"]) ?? "");
					}
					artists = string.Join(", ", names);
				}
				string subtitle = (string.IsNullOrEmpty(year) ? artists : (artists + " • " + year));
				UIElement card = CreateTrackCard(id, title, subtitle, tUrl, "Album");
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
		_albumsCachePanel = sv;
		await FadeInContentAsync(tId, delegate
		{
			ContentPanel.Children.Add(_albumsCachePanel);
			ResetScroll();
		});
	}

	private async Task LoadLibraryAsync()
	{
		try
		{
			_cachedLibraryError = null;
			JsonObject json = await BackendService.Instance.GetLibraryPlaylistsAsync(CancellationToken.None);
			if (json["error"] != null)
			{
				throw new Exception(((string?)json["error"]) ?? "Failed to load library.");
			}
			JsonArray playlists = json["data"] as JsonArray;
			JsonArray albums = json["albums"] as JsonArray;
			_cachedPlaylists = playlists;
			_cachedAlbums = albums;
			_playlistsCachePanel = null;
			_albumsCachePanel = null;
			MainSidebar.LibraryPanelRef.Items.Clear();
			int delayMs = 0;
			if ((playlists != null && playlists.Count > 0) || (_enableLocalMusic && !string.IsNullOrEmpty(_LocalMusicPath) && Directory.Exists(_LocalMusicPath)))
			{
				bool isPlaylistsCollapsed = false;
				List<Border> playlistItems = new List<Border>();
				if (!_groupLibraryTabs)
				{
					Grid pHeaderGrid = new Grid
					{
						Margin = new Thickness(0.0, 10.0, 0.0, 10.0),
						Opacity = 0.0,
						Background = System.Windows.Media.Brushes.Transparent,
						Cursor = System.Windows.Input.Cursors.Hand
					};
					Border pHeaderSep = new Border
					{
						Height = 1.0,
						Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
						HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
						VerticalAlignment = VerticalAlignment.Center,
						Opacity = (_isSidebarMinimized ? 1 : 0)
					};
					StackPanel pHeaderSp = new StackPanel
					{
						Orientation = System.Windows.Controls.Orientation.Horizontal,
						HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
						VerticalAlignment = VerticalAlignment.Center,
						Opacity = ((!_isSidebarMinimized) ? 1 : 0)
					};
					TextBlock pHeaderText = new TextBlock
					{
						Text = "PLAYLISTS",
						Foreground = System.Windows.Media.Brushes.Gray,
						FontSize = 12.0,
						FontWeight = FontWeights.Bold
					};
					System.Windows.Controls.Image pHeaderArrow = new System.Windows.Controls.Image
					{
						Source = (System.Windows.Media.DrawingImage)System.Windows.Application.Current.FindResource("downarrowIcon"),
						Width = 10.0,
						Height = 10.0,
						Margin = new Thickness(5.0, 0.0, 0.0, 0.0),
						VerticalAlignment = VerticalAlignment.Center,
						Opacity = 0.6,
						RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
						RenderTransform = new RotateTransform(0.0)
					};
					pHeaderSp.Children.Add(pHeaderText);
					pHeaderSp.Children.Add(pHeaderArrow);
					pHeaderGrid.Children.Add(pHeaderSep);
					pHeaderGrid.Children.Add(pHeaderSp);
					MainSidebar.LibraryPanelRef.Items.Add(pHeaderGrid);
					pHeaderGrid.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
					{
						e.Handled = true;
						if (_isSidebarMinimized)
						{
							return;
						}
						isPlaylistsCollapsed = !isPlaylistsCollapsed;
						DoubleAnimation animation = new DoubleAnimation(isPlaylistsCollapsed ? 180 : 0, TimeSpan.FromMilliseconds(200.0));
						pHeaderArrow.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, animation);
						int num = 0;
						foreach (Border b in playlistItems)
						{
							TimeSpan value = TimeSpan.FromMilliseconds(isPlaylistsCollapsed ? ((playlistItems.Count - 1 - num) * 15) : (num * 15));
							if (isPlaylistsCollapsed)
							{
								DoubleAnimation animation2 = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(250.0))
								{
									BeginTime = value
								};
								b.BeginAnimation(UIElement.OpacityProperty, animation2);
								DoubleAnimation doubleAnimation = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(250.0))
								{
									BeginTime = value,
									EasingFunction = new QuadraticEase
									{
										EasingMode = EasingMode.EaseInOut
									}
								};
								doubleAnimation.Completed += delegate
								{
									if (isPlaylistsCollapsed)
									{
										b.Visibility = Visibility.Collapsed;
									}
								};
								b.BeginAnimation(FrameworkElement.HeightProperty, doubleAnimation);
							}
							else
							{
								b.Visibility = Visibility.Visible;
								DoubleAnimation animation3 = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(250.0))
								{
									BeginTime = value
								};
								b.BeginAnimation(UIElement.OpacityProperty, animation3);
								DoubleAnimation doubleAnimation2 = new DoubleAnimation(42.0, TimeSpan.FromMilliseconds(250.0))
								{
									BeginTime = value,
									EasingFunction = new QuadraticEase
									{
										EasingMode = EasingMode.EaseOut
									}
								};
								doubleAnimation2.Completed += delegate
								{
									if (!isPlaylistsCollapsed)
									{
										b.BeginAnimation(FrameworkElement.HeightProperty, null);
									}
								};
								b.BeginAnimation(FrameworkElement.HeightProperty, doubleAnimation2);
							}
							num++;
						}
					};
					DoubleAnimation animP = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(300.0))
					{
						BeginTime = TimeSpan.FromMilliseconds(delayMs)
					};
					pHeaderGrid.BeginAnimation(UIElement.OpacityProperty, animP);
					delayMs += 30;
				}
				if (playlists != null && !_groupLibraryTabs)
				{
					foreach (JsonNode item in playlists)
					{
						string title = ((string?)item["title"]) ?? "";
						string id = ((string?)item["playlistId"]) ?? "";
						if (_hiddenLibraryItems.ContainsKey(id))
						{
							continue;
						}
						string tUrl = "";
						if (item["thumbnails"] is JsonArray { Count: >0 } thumbs)
						{
							tUrl = ((string?)thumbs[thumbs.Count - 1]["url"]) ?? "";
						}
						Border rowBorder = new Border
						{
							Height = 42.0,
							CornerRadius = new CornerRadius(6.0),
							BorderThickness = new Thickness(1.0),
							Padding = new Thickness(7.0, 4.0, 0.0, 4.0),
							Margin = new Thickness(0.0, 1.0, 0.0, 1.0),
							Cursor = System.Windows.Input.Cursors.Hand,
							Tag = id,
							Background = System.Windows.Media.Brushes.Transparent,
							Opacity = 0.0
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
						imgBorder.Child = CreateImage(tUrl, 32, 32);
						ApplyImageOverlay(imgBorder);
						row.Children.Add(imgBorder);
						TextBlock txt = new TextBlock
						{
							Text = title,
							Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["SidebarTextBrush"],
							FontSize = 14.0,
							VerticalAlignment = VerticalAlignment.Center,
							TextTrimming = TextTrimming.CharacterEllipsis
						};
						row.Children.Add(txt);
						rowBorder.Child = row;
						rowBorder.MouseEnter += delegate
						{
							if (_currentPageId != id)
							{
								FadeBorderBackgroundToResource(rowBorder, "CardHoverBrush");
								FadeTextForegroundToColor(txt, ((SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"]).Color);
							}
						};
						rowBorder.MouseLeave += delegate
						{
							if (_currentPageId != id)
							{
								FadeBorderBackgroundToColor(rowBorder, Colors.Transparent);
								FadeTextForegroundToColor(txt, ((SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["SidebarTextBrush"]).Color);
							}
						};
						rowBorder.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
						{
							e.Handled = true;
							OpenPlaylistPage(id, title, "Playlist", tUrl, "Playlist");
						};
						rowBorder.ContextMenu = CreateCollectionContextMenu(id, title, "Playlist", rowBorder, txt);
						MainSidebar.LibraryPanelRef.Items.Add(rowBorder);
						playlistItems.Add(rowBorder);
						DoubleAnimation anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(300.0))
						{
							BeginTime = TimeSpan.FromMilliseconds(delayMs)
						};
						rowBorder.BeginAnimation(UIElement.OpacityProperty, anim);
						delayMs += 30;
					}
				}
			}
			_savedAlbumIds.Clear();
			if (albums == null || albums.Count <= 0)
			{
				return;
			}
			bool isAlbumsCollapsed = false;
			List<Border> albumItems = new List<Border>();
			if (_groupLibraryTabs)
			{
				return;
			}
			Grid aHeaderGrid = new Grid
			{
				Margin = new Thickness(0.0, 20.0, 0.0, 10.0),
				Opacity = 0.0,
				Background = System.Windows.Media.Brushes.Transparent,
				Cursor = System.Windows.Input.Cursors.Hand
			};
			Border aHeaderSep = new Border
			{
				Height = 1.0,
				Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
				HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
				VerticalAlignment = VerticalAlignment.Center,
				Opacity = (_isSidebarMinimized ? 1 : 0)
			};
			StackPanel aHeaderSp = new StackPanel
			{
				Orientation = System.Windows.Controls.Orientation.Horizontal,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Center,
				Opacity = ((!_isSidebarMinimized) ? 1 : 0)
			};
			TextBlock aHeaderText = new TextBlock
			{
				Text = "ALBUMS",
				Foreground = System.Windows.Media.Brushes.Gray,
				FontSize = 12.0,
				FontWeight = FontWeights.Bold
			};
			System.Windows.Controls.Image aHeaderArrow = new System.Windows.Controls.Image
			{
				Source = (System.Windows.Media.DrawingImage)System.Windows.Application.Current.FindResource("downarrowIcon"),
				Width = 10.0,
				Height = 10.0,
				Margin = new Thickness(5.0, 0.0, 0.0, 0.0),
				VerticalAlignment = VerticalAlignment.Center,
				Opacity = 0.6,
				RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
				RenderTransform = new RotateTransform(0.0)
			};
			aHeaderSp.Children.Add(aHeaderText);
			aHeaderSp.Children.Add(aHeaderArrow);
			aHeaderGrid.Children.Add(aHeaderSep);
			aHeaderGrid.Children.Add(aHeaderSp);
			MainSidebar.LibraryPanelRef.Items.Add(aHeaderGrid);
			aHeaderGrid.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
			{
				e.Handled = true;
				if (_isSidebarMinimized)
				{
					return;
				}
				isAlbumsCollapsed = !isAlbumsCollapsed;
				DoubleAnimation animation = new DoubleAnimation(isAlbumsCollapsed ? 180 : 0, TimeSpan.FromMilliseconds(200.0));
				aHeaderArrow.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, animation);
				int num = 0;
				foreach (Border b in albumItems)
				{
					TimeSpan value = TimeSpan.FromMilliseconds(isAlbumsCollapsed ? ((albumItems.Count - 1 - num) * 15) : (num * 15));
					if (isAlbumsCollapsed)
					{
						DoubleAnimation animation2 = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(250.0))
						{
							BeginTime = value
						};
						b.BeginAnimation(UIElement.OpacityProperty, animation2);
						DoubleAnimation doubleAnimation = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(250.0))
						{
							BeginTime = value,
							EasingFunction = new QuadraticEase
							{
								EasingMode = EasingMode.EaseInOut
							}
						};
						doubleAnimation.Completed += delegate
						{
							if (isAlbumsCollapsed)
							{
								b.Visibility = Visibility.Collapsed;
							}
						};
						b.BeginAnimation(FrameworkElement.HeightProperty, doubleAnimation);
					}
					else
					{
						b.Visibility = Visibility.Visible;
						DoubleAnimation animation3 = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(250.0))
						{
							BeginTime = value
						};
						b.BeginAnimation(UIElement.OpacityProperty, animation3);
						DoubleAnimation doubleAnimation2 = new DoubleAnimation(42.0, TimeSpan.FromMilliseconds(250.0))
						{
							BeginTime = value,
							EasingFunction = new QuadraticEase
							{
								EasingMode = EasingMode.EaseOut
							}
						};
						doubleAnimation2.Completed += delegate
						{
							if (!isAlbumsCollapsed)
							{
								b.BeginAnimation(FrameworkElement.HeightProperty, null);
							}
						};
						b.BeginAnimation(FrameworkElement.HeightProperty, doubleAnimation2);
					}
					num++;
				}
			};
			DoubleAnimation animA = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(300.0))
			{
				BeginTime = TimeSpan.FromMilliseconds(delayMs)
			};
			aHeaderGrid.BeginAnimation(UIElement.OpacityProperty, animA);
			delayMs += 30;
			foreach (JsonNode item2 in albums)
			{
				string title2 = ((string?)item2["title"]) ?? "";
				string id2 = ((string?)item2["browseId"]) ?? "";
				if (_hiddenLibraryItems.ContainsKey(id2))
				{
					continue;
				}
				if (!string.IsNullOrEmpty(id2))
				{
					_savedAlbumIds.Add(id2);
				}
				string tUrl2 = "";
				if (item2["thumbnails"] is JsonArray { Count: >0 } thumbs2)
				{
					tUrl2 = ((string?)thumbs2[thumbs2.Count - 1]["url"]) ?? "";
				}
				string year = ((string?)item2["year"]) ?? "";
				string artists = "";
				if (item2["artists"] is JsonArray artistsToken)
				{
					List<string> names = new List<string>();
					foreach (JsonNode a in artistsToken)
					{
						names.Add(((string?)a["name"]) ?? "");
					}
					artists = string.Join(", ", names);
				}
				string subtitle = (string.IsNullOrEmpty(year) ? artists : (artists + " • " + year));
				Border rowBorder2 = new Border
				{
					Height = 42.0,
					CornerRadius = new CornerRadius(6.0),
					BorderThickness = new Thickness(1.0),
					Padding = new Thickness(7.0, 4.0, 0.0, 4.0),
					Margin = new Thickness(0.0, 1.0, 0.0, 1.0),
					Cursor = System.Windows.Input.Cursors.Hand,
					Tag = id2,
					Background = System.Windows.Media.Brushes.Transparent,
					Opacity = 0.0
				};
				StackPanel row2 = new StackPanel
				{
					Orientation = System.Windows.Controls.Orientation.Horizontal
				};
				Border imgBorder2 = new Border
				{
					Width = 32.0,
					Height = 32.0,
					CornerRadius = new CornerRadius(4.0),
					Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 25, 35)),
					Margin = new Thickness(0.0, 0.0, 10.0, 0.0)
				};
				imgBorder2.Clip = new RectangleGeometry
				{
					Rect = new Rect(0.0, 0.0, 32.0, 32.0),
					RadiusX = 4.0,
					RadiusY = 4.0
				};
				imgBorder2.Child = CreateImage(tUrl2, 32, 32);
				ApplyImageOverlay(imgBorder2);
				row2.Children.Add(imgBorder2);
				TextBlock txt2 = new TextBlock
				{
					Text = title2,
					Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["SidebarTextBrush"],
					FontSize = 14.0,
					VerticalAlignment = VerticalAlignment.Center,
					TextTrimming = TextTrimming.CharacterEllipsis
				};
				row2.Children.Add(txt2);
				rowBorder2.Child = row2;
				rowBorder2.MouseEnter += delegate
				{
					if (_currentPageId != id2)
					{
						FadeBorderBackgroundToResource(rowBorder2, "CardHoverBrush");
						FadeTextForegroundToColor(txt2, ((SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"]).Color);
					}
				};
				rowBorder2.MouseLeave += delegate
				{
					if (_currentPageId != id2)
					{
						FadeBorderBackgroundToColor(rowBorder2, Colors.Transparent);
						FadeTextForegroundToColor(txt2, ((SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["SidebarTextBrush"]).Color);
					}
				};
				rowBorder2.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
				{
					e.Handled = true;
					OpenPlaylistPage(id2, title2, subtitle, tUrl2, "Album");
				};
				rowBorder2.ContextMenu = CreateCollectionContextMenu(id2, title2, "Album", rowBorder2, txt2);
				MainSidebar.LibraryPanelRef.Items.Add(rowBorder2);
				albumItems.Add(rowBorder2);
				DoubleAnimation anim2 = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(300.0))
				{
					BeginTime = TimeSpan.FromMilliseconds(delayMs)
				};
				rowBorder2.BeginAnimation(UIElement.OpacityProperty, anim2);
				delayMs += 30;
			}
		}
		catch (Exception ex)
		{
			_cachedLibraryError = ex.Message;
		}
	}

	private async Task LoadLikedSongsAsync()
	{
		try
		{
			if (!((await BackendService.Instance.GetLikedSongsAsync(CancellationToken.None))["data"]?["tracks"] is JsonArray tracks))
			{
				return;
			}
			HashSet<string> likedIds = new HashSet<string>();
			foreach (JsonNode item in tracks)
			{
				string videoId = ((string?)item["videoId"]) ?? "";
				if (!string.IsNullOrEmpty(videoId))
				{
					likedIds.Add(videoId);
				}
			}
			_likedVideoIds = likedIds;
			_likedSongsLoaded = true;
		}
		catch
		{
		}
	}

	private async Task LoadStatsPageAsync(bool forceReload = false)
	{
		int tId = await FadeOutContentAsync();
		ContentPanel.Children.Clear();
		StackPanel pagePanel = new StackPanel
		{
			Margin = new Thickness(0.0, 20.0, 0.0, 40.0)
		};
		await FadeInContentAsync(tId, delegate
		{
			ContentPanel.Children.Add(pagePanel);
			HighlightNowPlaying(_currentVideoId);
			MainScrollViewer.ScrollToTop();
		});
		if (tId != _transitionId)
		{
			return;
		}
		Action<UIElement, int> animateIn = delegate(UIElement el, int delay)
		{
			bool skipAnim = MainOverlayControl.LoadingOverlayRef.Visibility == Visibility.Visible;
			el.Opacity = skipAnim ? 1.0 : 0.0;
			el.RenderTransform = new TranslateTransform(0.0, skipAnim ? 0.0 : 20.0);
			DoubleAnimation animation = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(400.0))
			{
				BeginTime = TimeSpan.FromMilliseconds(delay),
				EasingFunction = new QuarticEase
				{
					EasingMode = EasingMode.EaseOut
				}
			};
			DoubleAnimation animation2 = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(400.0))
			{
				BeginTime = TimeSpan.FromMilliseconds(delay),
				EasingFunction = new QuarticEase
				{
					EasingMode = EasingMode.EaseOut
				}
			};
			el.BeginAnimation(UIElement.OpacityProperty, animation);
			el.RenderTransform.BeginAnimation(TranslateTransform.YProperty, animation2);
		};
		Task<List<StatsManager.TopItem>> topArtistsTask = StatsManager.GetTopArtistsAsync();
		Task<List<StatsManager.TopItem>> topTracksTask = StatsManager.GetTopTracksAsync();
		Task<List<StatsManager.RecentItem>> recentTracksTask = StatsManager.GetRecentTracksAsync();
		Task<List<StatsManager.TopItem>> topAlbumsTask = StatsManager.GetTopAlbumsAsync();
		Task<long> totalMsTask = StatsManager.GetTotalListeningMsAsync();
		Task<int> uniqueArtistsTask = StatsManager.GetUniqueArtistsCountAsync();
		await Task.WhenAll(topArtistsTask, topTracksTask, recentTracksTask, topAlbumsTask, totalMsTask, uniqueArtistsTask);
		if (tId != _transitionId)
		{
			return;
		}
		Grid metricsGrid = new Grid
		{
			Margin = new Thickness(-5.0, 0.0, -5.0, 40.0)
		};
		metricsGrid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		metricsGrid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		metricsGrid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		metricsGrid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		_statsCachedTotalMs = totalMsTask.Result;
		Border card1 = CreateMetricCard("0", "minutes", "Listened to music");
		card1.ToolTip = null;
		_statsMinutesValueText = card1.Tag as TextBlock;
		_statsMinutesBorder = card1;
		Action updateStatsCard = delegate
		{
			long num2 = _statsCachedTotalMs + _accumulatedMs;
			long num3 = num2 / 3600000;
			long value = num2 / 60000 % 60;
			long value2 = num2 / 1000 % 60;
			string primaryText = ((num3 > 0) ? num3.ToString() : value.ToString());
			string unitText = ((num3 > 0) ? "hours" : "minutes");
			string secondsText = ((num3 > 0) ? $"h {value}m {value2}s" : $"m {value2}s");
			if (_statsMinutesValueText != null)
			{
				if (_statsMinutesHovered)
				{
					SetStatsMinutesValue(primaryText, unitText, secondsText, showSeconds: true, animate: true);
				}
				else
				{
					SetStatsMinutesValue(primaryText, unitText, "", showSeconds: false, animate: true);
				}
			}
		};
		card1.MouseEnter += delegate
		{
			_statsMinutesHovered = true;
			updateStatsCard();
		};
		card1.MouseLeave += delegate
		{
			_statsMinutesHovered = false;
			updateStatsCard();
		};
		long num = _statsCachedTotalMs + _accumulatedMs;
		long init_ch = num / 3600000;
		long init_cm = num / 60000 % 60;
		SetStatsMinutesValue((init_ch > 0) ? init_ch.ToString() : init_cm.ToString(), (init_ch > 0) ? "hours" : "minutes", "", showSeconds: false, animate: false);
		Grid.SetColumn(card1, 0);
		metricsGrid.Children.Add(card1);
		Border card2 = CreateMetricCard(StatsManager.TotalScrobbles.ToString(), "songs", "Streamed overall");
		_statsSongsValueText = card2.Tag as TextBlock;
		Grid.SetColumn(card2, 1);
		metricsGrid.Children.Add(card2);
		double owed = (double)StatsManager.TotalScrobbles * 0.004;
		Border card3 = CreateMetricCard($"${owed:0.00}", "", "Owed to artists overall");
		_statsOwedValueText = card3.Tag as TextBlock;
		Grid.SetColumn(card3, 2);
		metricsGrid.Children.Add(card3);
		Border card4 = CreateMetricCard(uniqueArtistsTask.Result.ToString(), "artists", "Got your love");
		_statsArtistsValueText = card4.Tag as TextBlock;
		Grid.SetColumn(card4, 3);
		metricsGrid.Children.Add(card4);
		animateIn(metricsGrid, 0);
		pagePanel.Children.Add(metricsGrid);
		StackPanel tabsPanel = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			Margin = new Thickness(0.0, 0.0, 0.0, 20.0)
		};
		Grid contentContainer = new Grid
		{
			Margin = new Thickness(0.0)
		};
		StackPanel tStack = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Top
		};
		List<StatsManager.TopItem> topTracks = topTracksTask.Result;
		int rank = 1;
		foreach (StatsManager.TopItem t in topTracks)
		{
			tStack.Children.Add(CreateStatRowCard("Track", rank.ToString(), t.Name, t.Artist, $"{t.PlayCount} plays", t.ThumbUrl));
			rank++;
		}
		if (topTracks.Count == 0)
		{
			tStack.Children.Add(new TextBlock
			{
				Text = "No data yet",
				Foreground = System.Windows.Media.Brushes.Gray,
				FontStyle = FontStyles.Italic,
				Margin = new Thickness(10.0)
			});
		}
		StackPanel tracksCard = tStack;
		StackPanel aStack = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Top
		};
		List<StatsManager.TopItem> topArtists = topArtistsTask.Result;
		rank = 1;
		foreach (StatsManager.TopItem a in topArtists)
		{
			aStack.Children.Add(CreateStatRowCard("Artist", rank.ToString(), a.Name, "", $"{a.PlayCount} plays", a.ThumbUrl));
			rank++;
		}
		if (topArtists.Count == 0)
		{
			aStack.Children.Add(new TextBlock
			{
				Text = "No data yet",
				Foreground = System.Windows.Media.Brushes.Gray,
				FontStyle = FontStyles.Italic,
				Margin = new Thickness(10.0)
			});
		}
		StackPanel artistsCard = aStack;
		StackPanel albStack = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Top
		};
		List<StatsManager.TopItem> topAlbums = topAlbumsTask.Result;
		rank = 1;
		foreach (StatsManager.TopItem a2 in topAlbums)
		{
			albStack.Children.Add(CreateStatRowCard("Album", rank.ToString(), a2.Name, a2.Artist, $"{a2.PlayCount} plays", a2.ThumbUrl));
			rank++;
		}
		if (topAlbums.Count == 0)
		{
			albStack.Children.Add(new TextBlock
			{
				Text = "No data yet",
				Foreground = System.Windows.Media.Brushes.Gray,
				FontStyle = FontStyles.Italic,
				Margin = new Thickness(10.0)
			});
		}
		StackPanel albumsCard = albStack;
		StackPanel recStack = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Top
		};
		List<StatsManager.RecentItem> recentTracks = recentTracksTask.Result;
		foreach (StatsManager.RecentItem t2 in recentTracks)
		{
			DateTime dt = DateTimeOffset.FromUnixTimeSeconds(t2.Timestamp).LocalDateTime;
			string timeStr = ((dt.Date == DateTime.Today) ? dt.ToString("HH:mm") : dt.ToString("MMM dd"));
			recStack.Children.Add(CreateStatRowCard("Track", "", t2.Title, t2.Artist, timeStr, t2.ThumbUrl));
		}
		if (recentTracks.Count == 0)
		{
			recStack.Children.Add(new TextBlock
			{
				Text = "No data yet",
				Foreground = System.Windows.Media.Brushes.Gray,
				FontStyle = FontStyles.Italic,
				Margin = new Thickness(10.0)
			});
		}
		StackPanel recentCard = recStack;
		artistsCard.Visibility = Visibility.Collapsed;
		albumsCard.Visibility = Visibility.Collapsed;
		recentCard.Visibility = Visibility.Collapsed;
		contentContainer.Children.Add(tracksCard);
		contentContainer.Children.Add(artistsCard);
		contentContainer.Children.Add(albumsCard);
		contentContainer.Children.Add(recentCard);
		UIElement currentVisible = tracksCard;
		TextBlock tab1 = CreateTab("Top Tracks", tracksCard);
		tab1.Foreground = System.Windows.Media.Brushes.White;
		TextBlock tab2 = CreateTab("Top Artists", artistsCard);
		TextBlock tab3 = CreateTab("Top Albums", albumsCard);
		TextBlock tab4 = CreateTab("Recent Tracks", recentCard);
		tabsPanel.Children.Add(tab1);
		tabsPanel.Children.Add(tab2);
		tabsPanel.Children.Add(tab3);
		tabsPanel.Children.Add(tab4);
		animateIn(tabsPanel, 100);
		pagePanel.Children.Add(tabsPanel);
		animateIn(contentContainer, 150);
		pagePanel.Children.Add(contentContainer);
		Border CreateMetricCard(string value, string title, string subtitle)
		{
			Border obj = new Border
			{
				Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(15, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
				CornerRadius = new CornerRadius(12.0),
				Padding = new Thickness(25.0, 20.0, 25.0, 20.0),
				Margin = new Thickness(5.0)
			};
			StackPanel stack = new StackPanel();
			StackPanel valStack = new StackPanel
			{
				Orientation = System.Windows.Controls.Orientation.Horizontal,
				VerticalAlignment = VerticalAlignment.Bottom
			};
			TextBlock valText = new TextBlock
			{
				Text = value,
				FontSize = 36.0,
				FontWeight = FontWeights.Bold,
				Foreground = System.Windows.Media.Brushes.White,
				Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
			};
			valStack.Children.Add(valText);
			if (title == "minutes")
			{
				valText.Margin = new Thickness(0.0);
				TextBlock secondsText = new TextBlock
				{
					Text = "",
					FontSize = 36.0,
					FontWeight = FontWeights.Bold,
					Foreground = System.Windows.Media.Brushes.White,
					Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
				};
				Border secondsReveal = new Border
				{
					Width = 0.0,
					Opacity = 0.0,
					ClipToBounds = true,
					Child = secondsText
				};
				valStack.Children.Add(secondsReveal);
				_statsSecondsRevealBorder = secondsReveal;
				_statsSecondsValueText = secondsText;
			}
			if (!string.IsNullOrEmpty(title))
			{
				TextBlock titleText = new TextBlock
				{
					Text = title,
					FontSize = 14.0,
					FontWeight = FontWeights.Bold,
					Foreground = System.Windows.Media.Brushes.Gray,
					VerticalAlignment = VerticalAlignment.Bottom,
					Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
				};
				if (title == "minutes")
				{
					titleText.Margin = new Thickness(8.0, 0.0, 0.0, 8.0);
					Border unitReveal = new Border
					{
						ClipToBounds = true,
						Child = titleText
					};
					valStack.Children.Add(unitReveal);
					_statsMinutesUnitRevealBorder = unitReveal;
					_statsMinutesUnitText = titleText;
				}
				else
				{
					valStack.Children.Add(titleText);
				}
			}
			stack.Children.Add(valStack);
			TextBlock subText = new TextBlock
			{
				Text = subtitle,
				FontSize = 13.0,
				Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(150, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
				Margin = new Thickness(0.0, 5.0, 0.0, 0.0),
				TextWrapping = TextWrapping.Wrap
			};
			if (title == "")
			{
				subText.ToolTip = "Based on average payout of $0.003 to $0.005 per stream";
			}
			stack.Children.Add(subText);
			obj.Child = stack;
			obj.Tag = valText;
			return obj;
		}
		Border CreateStatRowCard(string type, string text, string title, string subtitle, string valueStr, string thumbUrl)
		{
			Border border = new Border
			{
				Background = (System.Windows.Media.Brush)System.Windows.Application.Current.MainWindow.Resources["CardBackground"],
				BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(15, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(10.0),
				Height = 60.0,
				Padding = new Thickness(0.0, 0.0, 12.0, 0.0),
				Margin = new Thickness(0.0, 5.0, 0.0, 5.0),
				HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
			};
			Grid grid = new Grid();
			int colIdx = 0;
			if (!string.IsNullOrEmpty(text))
			{
				grid.ColumnDefinitions.Add(new ColumnDefinition
				{
					Width = new GridLength(48.0)
				});
				TextBlock rankTxt = new TextBlock
				{
					Text = text,
					Foreground = System.Windows.Media.Brushes.Gray,
					FontSize = 14.0,
					FontWeight = FontWeights.Bold,
					VerticalAlignment = VerticalAlignment.Center,
					HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
					TextAlignment = TextAlignment.Center
				};
				Grid.SetColumn(rankTxt, colIdx);
				grid.Children.Add(rankTxt);
				colIdx++;
			}
			if (!string.IsNullOrEmpty(text))
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
			Border imgBorder = new Border
			{
				Width = 44.0,
				Height = 44.0,
				HorizontalAlignment = (string.IsNullOrEmpty(text) ? System.Windows.HorizontalAlignment.Center : System.Windows.HorizontalAlignment.Left),
				VerticalAlignment = VerticalAlignment.Center,
				Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 25, 35))
			};
			int cornerRadius = ((type == "Artist") ? 22 : 4);
			RectangleGeometry clipRect = new RectangleGeometry
			{
				Rect = new Rect(0.0, 0.0, 44.0, 44.0),
				RadiusX = cornerRadius,
				RadiusY = cornerRadius
			};
			imgBorder.Clip = clipRect;
			if (!string.IsNullOrEmpty(thumbUrl) && type != "Artist")
			{
				imgBorder.Child = CreateImage(thumbUrl, 44, 44);
			}
			else if (type == "Artist")
			{
				if (_artistThumbCache.ContainsKey(title))
				{
					imgBorder.Child = CreateImage(_artistThumbCache[title], 44, 44);
				}
				else
				{
					Task.Run(async delegate
					{
						try
						{
							if ((await BackendService.Instance.SearchAsync(title, CancellationToken.None))["data"] is JsonObject data && data["artists"] is JsonArray { Count: >0 } artists)
							{
								JsonNode JsonNode = artists[0];
								string tUrl = "";
								if (JsonNode?["thumbnails"] is JsonArray { Count: >0 } thumbs)
								{
									tUrl = ((string?)thumbs[thumbs.Count - 1]["url"]) ?? "";
								}
								if (!string.IsNullOrEmpty(tUrl))
								{
									_artistThumbCache[title] = tUrl;
									base.Dispatcher.Invoke(delegate
									{
										imgBorder.Child = CreateImage(tUrl, 44, 44);
									});
								}
							}
						}
						catch
						{
						}
					});
				}
			}
			ApplyImageOverlay(imgBorder);
			Grid.SetColumn(imgBorder, colIdx);
			grid.Children.Add(imgBorder);
			colIdx++;
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(1.0, GridUnitType.Star)
			});
			StackPanel titleStack = new StackPanel
			{
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(10.0, 0.0, 10.0, 0.0)
			};
			TextBlock titleTxt = new TextBlock
			{
				Text = title,
				Foreground = System.Windows.Media.Brushes.White,
				FontSize = 15.0,
				FontWeight = FontWeights.SemiBold,
				TextTrimming = TextTrimming.CharacterEllipsis
			};
			titleStack.Children.Add(titleTxt);
			if (!string.IsNullOrEmpty(subtitle))
			{
				TextBlock subTxt = new TextBlock
				{
					TextTrimming = TextTrimming.CharacterEllipsis,
					Margin = new Thickness(0.0, 2.0, 0.0, 0.0)
				};
				if (type == "Track" || type == "Album")
				{
					PopulateArtistLinks(subTxt, subtitle, 13);
				}
				else
				{
					subTxt.Text = subtitle;
					subTxt.Foreground = System.Windows.Media.Brushes.Gray;
					subTxt.FontSize = 13.0;
				}
				titleStack.Children.Add(subTxt);
			}
			Grid.SetColumn(titleStack, colIdx);
			grid.Children.Add(titleStack);
			colIdx++;
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = GridLength.Auto
			});
			TextBlock valTxt = new TextBlock
			{
				Text = valueStr,
				Foreground = System.Windows.Media.Brushes.Gray,
				FontSize = 13.0,
				VerticalAlignment = VerticalAlignment.Center
			};
			Grid.SetColumn(valTxt, colIdx);
			grid.Children.Add(valTxt);
			colIdx++;
			border.Child = grid;
			border.MouseEnter += delegate
			{
				FadeBorderBackgroundToResource(border, "CardHoverBrush");
			};
			border.MouseLeave += delegate
			{
				FadeBorderBackgroundToResource(border, "CardBackground");
			};
			border.MouseLeftButtonDown += async delegate(object s, MouseButtonEventArgs e)
			{
				e.Handled = true;
				Border loadingOverlay = CreateLoadingWaveOverlay(10.0);
				Grid.SetColumnSpan(loadingOverlay, (grid.ColumnDefinitions.Count <= 0) ? 1 : grid.ColumnDefinitions.Count);
				grid.Children.Insert(0, loadingOverlay);
				ShowLoadingOverlay(loadingOverlay);
				try
				{
					string query = (title + " " + subtitle).Trim();
					if ((await BackendService.Instance.SearchAsync(query, CancellationToken.None))["data"] is JsonObject data)
					{
						if (type == "Track")
						{
							if (data["songs"] is JsonArray { Count: >0 } songs)
							{
								JsonNode JsonNode = songs[0];
								string vid = ((string?)JsonNode?["videoId"]) ?? "";
								string t3 = ((string?)JsonNode?["title"]) ?? "";
								string a3 = "";
								JsonArray aList = JsonNode?["artists"] as JsonArray;
								if (aList != null && aList.Count > 0)
								{
									a3 = ((string?)aList[0]["name"]) ?? "";
								}
								string thumb = "";
								if (JsonNode?["thumbnails"] is JsonArray { Count: >0 } thumbs)
								{
									thumb = ((string?)thumbs[thumbs.Count - 1]["url"]) ?? "";
								}
								_ = _ = _ = _ = PlayTrack(vid, t3, a3, thumb, addToHistory: true, startPaused: false, useCrossfade: false, 0, aList);
							}
						}
						else if (type == "Artist")
						{
							if (data["artists"] is JsonArray { Count: >0 } artists)
							{
								JsonNode jToken2 = artists[0];
								string id = ((string?)jToken2["browseId"]) ?? "";
								string n = ((string?)jToken2["artist"]) ?? "";
								string thumb2 = "";
								if (jToken2["thumbnails"] is JsonArray { Count: >0 } thumbs2)
								{
									thumb2 = ((string?)thumbs2[thumbs2.Count - 1]["url"]) ?? "";
								}
								OpenArtistPage(id, n, thumb2);
							}
						}
						else if (type == "Album" && data["albums"] is JsonArray { Count: >0 } albums)
						{
							JsonNode jToken3 = albums[0];
							string id2 = ((string?)jToken3["browseId"]) ?? "";
							string n2 = ((string?)jToken3["title"]) ?? "";
							string thumb3 = "";
							if (jToken3["thumbnails"] is JsonArray { Count: >0 } thumbs3)
							{
								thumb3 = ((string?)thumbs3[thumbs3.Count - 1]["url"]) ?? "";
							}
							OpenPlaylistPage(id2, n2, title, thumb3, "Album");
						}
					}
				}
				finally
				{
					HideLoadingOverlay(loadingOverlay);
					await Task.Delay(250);
					grid.Children.Remove(loadingOverlay);
				}
			};
			border.Cursor = System.Windows.Input.Cursors.Hand;
			return border;
		}
		TextBlock CreateTab(string text, UIElement targetCard)
		{
			TextBlock tb = new TextBlock
			{
				Text = text,
				Foreground = System.Windows.Media.Brushes.Gray,
				FontSize = 16.0,
				FontWeight = FontWeights.Bold,
				Margin = new Thickness(0.0, 0.0, 25.0, 0.0),
				Cursor = System.Windows.Input.Cursors.Hand
			};
			tb.MouseLeftButtonDown += async delegate
			{
				if (currentVisible != targetCard)
				{
					UIElement oldCard = currentVisible;
					currentVisible = targetCard;
					foreach (TextBlock child in tabsPanel.Children)
					{
						child.Foreground = System.Windows.Media.Brushes.Gray;
					}
					tb.Foreground = System.Windows.Media.Brushes.White;
					DoubleAnimation animOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150.0));
					oldCard.BeginAnimation(UIElement.OpacityProperty, animOut);
					await Task.Delay(150);
					oldCard.Visibility = Visibility.Collapsed;
					targetCard.Opacity = 0.0;
					targetCard.Visibility = Visibility.Visible;
					DoubleAnimation animIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200.0));
					targetCard.BeginAnimation(UIElement.OpacityProperty, animIn);
				}
			};
			return tb;
		}
	}

	private async void OpenArtistPage(string channelId, string artistName, string thumbUrl, int preTId = -1)
	{
		int num;
		if (_currentPageId == channelId && _pageCache.ContainsKey(channelId))
		{
			_pageCache.Remove(channelId);
			_pageVirtualizationCache.Remove(channelId);
		}
		else if (_pageCache.ContainsKey(channelId))
		{
			if (_currentPageId != channelId)
			{
				PushCurrentPageToHistory();
			}
			_currentPageId = channelId;
			UpdateSidebarHighlight();
			num = ((preTId == -1) ? (await FadeOutContentAsync()) : preTId);
			int cachedTId = num;
			await FadeInContentAsync(cachedTId, delegate
			{
				ContentPanel.Children.Add(_pageCache[channelId]);
				MainScrollViewer.ScrollToTop();
			});
			return;
		}
		if (_currentPageId != channelId)
		{
			PushCurrentPageToHistory();
		}
		_currentPageId = channelId;
		UpdateSidebarHighlight();
		num = ((preTId == -1) ? (await FadeOutContentAsync()) : preTId);
		int tId = num;
		try
		{
			Task<JsonObject> songsTask = BackendService.Instance.GetArtistSongsAsync(channelId, artistName, CancellationToken.None, 14);
			Task<JsonObject> infoTask = BackendService.Instance.GetArtistInfoAsync(channelId, CancellationToken.None);
			await Task.WhenAll<JsonObject>(songsTask, infoTask);
			JsonObject jsonInfo = infoTask.Result;
			JsonObject jsonSongsFast = songsTask.Result;
			if (jsonInfo["error"] != null)
			{
				throw new Exception(((string?)jsonInfo["error"]) ?? "Error");
			}
			JsonObject dataInfo = jsonInfo["data"] as JsonObject;
			if (string.IsNullOrEmpty(thumbUrl) && dataInfo != null && dataInfo["thumbnails"] is JsonArray { Count: >0 } tArr)
			{
				thumbUrl = ((string?)tArr[tArr.Count - 1]["url"]) ?? "";
			}
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
				CornerRadius = new CornerRadius(100.0),
				Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(25, 25, 35)),
				Margin = new Thickness(0.0, 0.0, 40.0, 0.0)
			};
			imgBorder.Clip = new EllipseGeometry
			{
				Center = new System.Windows.Point(100.0, 100.0),
				RadiusX = 100.0,
				RadiusY = 100.0
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
			StackPanel topRow = new StackPanel
			{
				Orientation = System.Windows.Controls.Orientation.Horizontal
			};
			topRow.Children.Add(new TextBlock
			{
				Text = "Artist",
				Foreground = System.Windows.Media.Brushes.Gray,
				FontSize = 14.0
			});
			string subscribers = dataInfo?["subscribers"]?.ToString() ?? "";
			if (!string.IsNullOrEmpty(subscribers))
			{
				topRow.Children.Add(new TextBlock
				{
					Text = "•",
					Foreground = System.Windows.Media.Brushes.Gray,
					FontSize = 14.0,
					Margin = new Thickness(8.0, 0.0, 8.0, 0.0)
				});
				topRow.Children.Add(new TextBlock
				{
					Text = subscribers,
					Foreground = System.Windows.Media.Brushes.LightGray,
					FontSize = 14.0
				});
			}
			titlePanel.Children.Add(topRow);
			titlePanel.Children.Add(new TextBlock
			{
				Text = artistName,
				Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
				FontSize = 48.0,
				FontWeight = FontWeights.Bold,
				Margin = new Thickness(0.0, 10.0, 0.0, 0.0),
				TextWrapping = TextWrapping.Wrap
			});
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
				Height = 48.0
			};
			System.Windows.Controls.Button shuffleBtn = new System.Windows.Controls.Button
			{
				Style = (Style)FindResource("AccentButtonStyle"),
				Width = 48.0,
				Height = 48.0,
				Margin = new Thickness(15.0, 0.0, 0.0, 0.0)
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
			if (dataInfo != null)
			{
				JsonArray fastTracks = jsonSongsFast["tracks"] as JsonArray;
				if (fastTracks != null && fastTracks.Count > 0)
				{
					Grid headerGrid = new Grid
					{
						Margin = new Thickness(0.0, 0.0, 0.0, 15.0)
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
						Text = "Songs",
						Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
						FontSize = 24.0,
						FontWeight = FontWeights.Bold,
						VerticalAlignment = VerticalAlignment.Center
					};
					Grid.SetColumn(titleBlock, 0);
					headerGrid.Children.Add(titleBlock);
					Grid seeAllContainer = new Grid
					{
						Background = System.Windows.Media.Brushes.Transparent,
						Cursor = System.Windows.Input.Cursors.Hand,
						VerticalAlignment = VerticalAlignment.Center,
						Visibility = ((fastTracks.Count <= 8) ? Visibility.Collapsed : Visibility.Visible)
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
					Grid.SetColumn(seeAllContainer, 1);
					headerGrid.Children.Add(seeAllContainer);
					pagePanel.Children.Add(headerGrid);
					Border containerBorder = new Border
					{
						ClipToBounds = true
					};
					StackPanel spContainer = new StackPanel();
					UniformGrid trackGrid = new UniformGrid
					{
						Columns = 4
					};
					trackGrid.SizeChanged += delegate(object s, SizeChangedEventArgs ev)
					{
						trackGrid.Columns = Math.Max(1, (int)(ev.NewSize.Width / 320.0));
					};
					spContainer.Children.Add(trackGrid);
					containerBorder.Child = spContainer;
					bool isFullLoaded = false;
					bool isSeeAllClicked = false;
					bool isFetchingFull = false;
					bool isExpanded = false;
					double collapsedHeight = 0.0;
					JsonArray loadedFullTracks = null;
					bool isGridPopulatedWithFull = false;

					Action populateGridWithFull = delegate
					{
						if (isGridPopulatedWithFull || loadedFullTracks == null) return;
						trackGrid.Children.Clear();
						int num2 = 0;
						foreach (JsonNode current4 in loadedFullTracks)
						{
							string text = ((string?)current4["videoId"]) ?? "";
							if (!string.IsNullOrEmpty(text))
							{
								string title5 = ((string?)current4["title"]) ?? "";
								string artist = artistName;
								JsonArray JsonArray = current4["artists"] as JsonArray;
								if (JsonArray != null && JsonArray.Count > 0)
								{
									List<string> list = new List<string>();
									foreach (JsonNode current5 in JsonArray)
									{
										list.Add(((string?)current5["name"]) ?? "");
									}
									artist = string.Join(", ", list);
								}
								string thumbUrl2 = thumbUrl;
								if (current4["thumbnails"] is JsonArray { Count: >0 } jArray2)
								{
									thumbUrl2 = ((string?)jArray2[jArray2.Count - 1]["url"]) ?? "";
								}
								Border border = CreateTrackRow(text, title5, artist, thumbUrl2, "", "", "", "", channelId, loadedFullTracks, num2, "", hideLikedToggle: false, null, JsonArray);
								border.Margin = new Thickness(5.0);
								trackGrid.Children.Add(border);
								num2++;
							}
						}
						isGridPopulatedWithFull = true;
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
					Action fetchFullSongs = delegate
					{
						if (!(isFetchingFull || isFullLoaded))
						{
							isFetchingFull = true;
							Task.Run(async delegate
							{
								try
								{
									JsonArray fullTracks = (await BackendService.Instance.GetArtistSongsAsync(channelId, artistName, CancellationToken.None))["tracks"] as JsonArray;
									if (fullTracks != null && fullTracks.Count > 0)
									{
										base.Dispatcher.Invoke(delegate
										{
											loadedFullTracks = fullTracks;
											isFullLoaded = true;
											if (isSeeAllClicked)
											{
												populateGridWithFull();
												isExpanded = true;
												double num3 = collapsedHeight;
												trackGrid.Measure(new System.Windows.Size(containerBorder.ActualWidth, double.PositiveInfinity));
												double height = trackGrid.DesiredSize.Height;
												containerBorder.MaxHeight = num3;
												DoubleAnimation doubleAnimation = new DoubleAnimation(num3, height, TimeSpan.FromMilliseconds(450.0))
												{
													EasingFunction = new QuarticEase
													{
														EasingMode = EasingMode.EaseInOut
													}
												};
												doubleAnimation.Completed += delegate
												{
													containerBorder.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
													containerBorder.MaxHeight = double.PositiveInfinity;
												};
												containerBorder.BeginAnimation(FrameworkElement.MaxHeightProperty, doubleAnimation);
												DoubleAnimation doubleAnimation2 = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120.0));
												doubleAnimation2.Completed += delegate
												{
													seeAllBtn.Text = "Show less";
													seeAllBtn.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120.0)));
													arrowIcon.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(seeAllContainer.IsMouseOver ? 0.0 : 1.0, TimeSpan.FromMilliseconds(120.0)));
												};
												seeAllBtn.BeginAnimation(UIElement.OpacityProperty, doubleAnimation2);
												arrowIcon.BeginAnimation(UIElement.OpacityProperty, doubleAnimation2);
												seeAllContainer.IsHitTestVisible = true;
											}
											if (_currentQueue != null && _currentQueue.Count == fastTracks.Count)
											{
												string text2 = (string?)_currentQueue[_currentQueueIndex]["videoId"];
												int num4 = -1;
												for (int num5 = 0; num5 < fullTracks.Count; num5++)
												{
													if ((string?)fullTracks[num5]["videoId"] == text2)
													{
														num4 = num5;
														break;
													}
												}
												if (num4 != -1)
												{
													InitQueueAndShuffle((System.Text.Json.Nodes.JsonArray)fullTracks.DeepClone(), num4);
												}
											}
										});
									}
								}
								catch
								{
								}
							});
						}
					};
					Action<bool> startPlayback = delegate(bool shuffle)
					{
						if (shuffle && !_isShuffleOn)
						{
							_isShuffleOn = true;
							UpdateShuffleIcon();
						}
						fetchFullSongs();
						if (fastTracks != null && fastTracks.Count != 0)
						{
							int num2 = (shuffle ? new Random().Next(fastTracks.Count) : 0);
							InitQueueAndShuffle((System.Text.Json.Nodes.JsonArray)fastTracks.DeepClone(), num2);
							JsonNode JsonNode = fastTracks[num2];
							string text = ((string?)JsonNode?["videoId"]) ?? "";
							string title5 = ((string?)JsonNode?["title"]) ?? "";
							string thumbUrl2 = "";
							if (JsonNode?["thumbnails"] is JsonArray { Count: >0 } JsonArray)
							{
								thumbUrl2 = ((string?)JsonArray[JsonArray.Count - 1]["url"]) ?? "";
							}
							string artist = artistName;
							JsonArray jArray2 = JsonNode?["artists"] as JsonArray;
							if (jArray2 != null && jArray2.Count > 0)
							{
								List<string> list = new List<string>();
								foreach (JsonNode current4 in jArray2)
								{
									list.Add(((string?)current4["name"]) ?? "");
								}
								artist = string.Join(", ", list);
							}
							if (!string.IsNullOrEmpty(text))
							{
								_ = _ = _ = _ = PlayTrack(text, title5, artist, thumbUrl2, addToHistory: true, startPaused: false, useCrossfade: false, 0, jArray2);
							}
						}
					};
					playBtn.Click += delegate
					{
						startPlayback(obj: false);
					};
					shuffleBtn.Click += delegate
					{
						startPlayback(obj: true);
					};
					seeAllContainer.MouseLeftButtonDown += delegate
					{
						if (!isExpanded)
						{
							if (collapsedHeight == 0.0)
							{
								collapsedHeight = containerBorder.ActualHeight;
							}
							if (!isFullLoaded)
							{
								DoubleAnimation animation = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150.0));
								seeAllBtn.BeginAnimation(UIElement.OpacityProperty, animation);
								arrowIcon.BeginAnimation(UIElement.OpacityProperty, animation);
								seeAllContainer.IsHitTestVisible = false;
								isSeeAllClicked = true;
								fetchFullSongs();
							}
							else
							{
								populateGridWithFull();
								isExpanded = true;
								double num2 = collapsedHeight;
								trackGrid.Measure(new System.Windows.Size(containerBorder.ActualWidth, double.PositiveInfinity));
								double height = trackGrid.DesiredSize.Height;
								containerBorder.MaxHeight = num2;
								DoubleAnimation doubleAnimation = new DoubleAnimation(num2, height, TimeSpan.FromMilliseconds(450.0))
								{
									EasingFunction = new QuarticEase
									{
										EasingMode = EasingMode.EaseInOut
									}
								};
								doubleAnimation.Completed += delegate
								{
									containerBorder.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
									containerBorder.MaxHeight = double.PositiveInfinity;
								};
								containerBorder.BeginAnimation(FrameworkElement.MaxHeightProperty, doubleAnimation);
								DoubleAnimation doubleAnimation2 = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120.0));
								doubleAnimation2.Completed += delegate
								{
									seeAllBtn.Text = "Show less";
									seeAllBtn.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120.0)));
									arrowIcon.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(seeAllContainer.IsMouseOver ? 0.0 : 1.0, TimeSpan.FromMilliseconds(120.0)));
								};
								seeAllBtn.BeginAnimation(UIElement.OpacityProperty, doubleAnimation2);
								arrowIcon.BeginAnimation(UIElement.OpacityProperty, doubleAnimation2);
							}
						}
						else
						{
							isExpanded = false;
							double actualHeight = containerBorder.ActualHeight;
							double toValue = collapsedHeight;
							containerBorder.MaxHeight = actualHeight;
							DoubleAnimation animation2 = new DoubleAnimation(actualHeight, toValue, TimeSpan.FromMilliseconds(450.0))
							{
								EasingFunction = new QuarticEase
								{
									EasingMode = EasingMode.EaseInOut
								}
							};
							containerBorder.BeginAnimation(FrameworkElement.MaxHeightProperty, animation2);
							DoubleAnimation doubleAnimation3 = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120.0));
							doubleAnimation3.Completed += delegate
							{
								seeAllBtn.Text = "See all";
								seeAllBtn.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120.0)));
								arrowIcon.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(seeAllContainer.IsMouseOver ? 0.0 : 1.0, TimeSpan.FromMilliseconds(120.0)));
							};
							seeAllBtn.BeginAnimation(UIElement.OpacityProperty, doubleAnimation3);
							arrowIcon.BeginAnimation(UIElement.OpacityProperty, doubleAnimation3);
						}
					};
					int count = 0;
					foreach (JsonNode item in fastTracks)
					{
						string videoId = ((string?)item["videoId"]) ?? "";
						if (string.IsNullOrEmpty(videoId))
						{
							continue;
						}
						string title = ((string?)item["title"]) ?? "";
						string artistsStr = artistName;
						JsonArray artistsToken = item["artists"] as JsonArray;
						if (artistsToken != null && artistsToken.Count > 0)
						{
							List<string> names = new List<string>();
							foreach (JsonNode a in artistsToken)
							{
								names.Add(((string?)a["name"]) ?? "");
							}
							artistsStr = string.Join(", ", names);
						}
						string tUrl = thumbUrl;
						if (item["thumbnails"] is JsonArray { Count: >0 } thumbs)
						{
							tUrl = ((string?)thumbs[thumbs.Count - 1]["url"]) ?? "";
						}
						Border rowBorder = CreateTrackRow(videoId, title, artistsStr, tUrl, "", "", "", "", channelId, fastTracks, count, "", hideLikedToggle: false, fetchFullSongs, artistsToken);
						rowBorder.Margin = new Thickness(5.0);
						if (count < 8)
						{
							trackGrid.Children.Add(rowBorder);
						}
						count++;
					}
					pagePanel.Children.Add(containerBorder);
				}
			}
			SolidColorBrush defaultBrush;
			System.Windows.Media.Brush accentBrush;
			Border albumsTab;
			Border singlesTab;
			StackPanel albumsGridWrapper;
			StackPanel singlesGridWrapper;
			Grid albumsSeeAllWrapper;
			Grid singlesSeeAllWrapper;
			bool isAlbumsActive;
			if (dataInfo != null)
			{
				JsonObject obj = dataInfo["albums"] as JsonObject;
				JsonObject singlesInfo = dataInfo["singles"] as JsonObject;
				JsonArray albumsArray = obj?["results"] as JsonArray;
				JsonArray singlesArray = singlesInfo?["results"] as JsonArray;
				if ((albumsArray != null && albumsArray.Count > 0) || (singlesArray != null && singlesArray.Count > 0))
				{
					StackPanel releasesContainer = new StackPanel
					{
						Margin = new Thickness(0.0, 15.0, 0.0, 10.0)
					};
					releasesContainer.Children.Add(new TextBlock
					{
						Text = "Releases",
						Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
						FontSize = 24.0,
						FontWeight = FontWeights.Bold,
						Margin = new Thickness(0.0, 0.0, 0.0, 15.0)
					});
					Grid tabsGrid = new Grid
					{
						Margin = new Thickness(0.0, 0.0, 0.0, 15.0)
					};
					tabsGrid.ColumnDefinitions.Add(new ColumnDefinition
					{
						Width = new GridLength(1.0, GridUnitType.Star)
					});
					tabsGrid.ColumnDefinitions.Add(new ColumnDefinition
					{
						Width = GridLength.Auto
					});
					StackPanel tabsPanel = new StackPanel
					{
						Orientation = System.Windows.Controls.Orientation.Horizontal
					};
					Grid.SetColumn(tabsPanel, 0);
					tabsGrid.Children.Add(tabsPanel);
					Grid seeAllPanel = new Grid();
					Grid.SetColumn(seeAllPanel, 1);
					tabsGrid.Children.Add(seeAllPanel);
					defaultBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(15, byte.MaxValue, byte.MaxValue, byte.MaxValue));
					SolidColorBrush hoverBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(30, byte.MaxValue, byte.MaxValue, byte.MaxValue));
					accentBrush = (System.Windows.Media.Brush)FindResource("AccentGradient");
					albumsTab = new Border
					{
						Background = defaultBrush,
						CornerRadius = new CornerRadius(20.0),
						Padding = new Thickness(20.0, 8.0, 20.0, 8.0),
						Margin = new Thickness(0.0, 0.0, 10.0, 0.0),
						Cursor = System.Windows.Input.Cursors.Hand
					};
					albumsTab.Child = new TextBlock
					{
						Text = "Albums",
						Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
						FontWeight = FontWeights.Bold,
						FontSize = 14.0,
						VerticalAlignment = VerticalAlignment.Center
					};
					singlesTab = new Border
					{
						Background = defaultBrush,
						CornerRadius = new CornerRadius(20.0),
						Padding = new Thickness(20.0, 8.0, 20.0, 8.0),
						Margin = new Thickness(0.0, 0.0, 10.0, 0.0),
						Cursor = System.Windows.Input.Cursors.Hand
					};
					singlesTab.Child = new TextBlock
					{
						Text = "Singles and EPs",
						Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
						FontWeight = FontWeights.Bold,
						FontSize = 14.0,
						VerticalAlignment = VerticalAlignment.Center
					};
					tabsPanel.Children.Add(albumsTab);
					tabsPanel.Children.Add(singlesTab);
					Grid contentPanel = new Grid();
					albumsGridWrapper = new StackPanel();
					singlesGridWrapper = new StackPanel
					{
						Visibility = Visibility.Collapsed
					};
					albumsSeeAllWrapper = new Grid();
					singlesSeeAllWrapper = new Grid
					{
						Visibility = Visibility.Collapsed
					};
					seeAllPanel.Children.Add(albumsSeeAllWrapper);
					seeAllPanel.Children.Add(singlesSeeAllWrapper);
					contentPanel.Children.Add(albumsGridWrapper);
					contentPanel.Children.Add(singlesGridWrapper);
					releasesContainer.Children.Add(tabsGrid);
					releasesContainer.Children.Add(contentPanel);
					if (albumsArray != null && albumsArray.Count > 0)
					{
						(UIElement, System.Windows.Controls.Panel) tuple = CreateExpandableSection("Albums", albumsArray.Count);
						UIElement sv = tuple.Item1;
						System.Windows.Controls.Panel albumGrid = tuple.Item2;
						Grid obj2 = (Grid)((StackPanel)sv).Children[0];
						UIElement seeAllBtn2 = obj2.Children[1];
						obj2.Children.Remove(seeAllBtn2);
						obj2.Visibility = Visibility.Collapsed;
						albumsSeeAllWrapper.Children.Add(seeAllBtn2);
						((StackPanel)sv).Margin = new Thickness(0.0);
						foreach (JsonNode item2 in albumsArray)
						{
							string id = ((string?)item2["browseId"]) ?? "";
							string title2 = ((string?)item2["title"]) ?? "";
							string year = ((string?)item2["year"]) ?? "";
							string tUrl2 = "";
							if (item2["thumbnails"] is JsonArray { Count: >0 } thumbs2)
							{
								tUrl2 = ((string?)thumbs2[thumbs2.Count - 1]["url"]) ?? "";
							}
							albumGrid.Children.Add(CreateTrackCard(id, title2, string.IsNullOrEmpty(year) ? "Album" : year, tUrl2, "Album"));
						}
						albumsGridWrapper.Children.Add(sv);
					}
					if (singlesArray != null && singlesArray.Count > 0)
					{
						(UIElement, System.Windows.Controls.Panel) tuple2 = CreateExpandableSection("Singles and EPs", singlesArray.Count);
						UIElement sv2 = tuple2.Item1;
						System.Windows.Controls.Panel singleGrid = tuple2.Item2;
						Grid obj3 = (Grid)((StackPanel)sv2).Children[0];
						UIElement seeAllBtn3 = obj3.Children[1];
						obj3.Children.Remove(seeAllBtn3);
						obj3.Visibility = Visibility.Collapsed;
						singlesSeeAllWrapper.Children.Add(seeAllBtn3);
						((StackPanel)sv2).Margin = new Thickness(0.0);
						foreach (JsonNode item3 in singlesArray)
						{
							string id2 = ((string?)item3["browseId"]) ?? "";
							string title3 = ((string?)item3["title"]) ?? "";
							string year2 = ((string?)item3["year"]) ?? "";
							string tUrl3 = "";
							if (item3["thumbnails"] is JsonArray { Count: >0 } thumbs3)
							{
								tUrl3 = ((string?)thumbs3[thumbs3.Count - 1]["url"]) ?? "";
							}
							string typeStr = ((string?)item3["releaseType"]) ?? "Single";
							singleGrid.Children.Add(CreateTrackCard(id2, title3, string.IsNullOrEmpty(year2) ? typeStr : (typeStr + " • " + year2), tUrl3, "Single"));
						}
						singlesGridWrapper.Children.Add(sv2);
					}
					isAlbumsActive = true;
					albumsTab.MouseEnter += delegate
					{
						if (!isAlbumsActive)
						{
							albumsTab.Background = hoverBrush;
						}
					};
					albumsTab.MouseLeave += delegate
					{
						if (!isAlbumsActive)
						{
							albumsTab.Background = defaultBrush;
						}
					};
					albumsTab.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
					{
						e.Handled = true;
						SwitchTab(toAlbums: true, animate: true);
					};
					singlesTab.MouseEnter += delegate
					{
						if (isAlbumsActive)
						{
							singlesTab.Background = hoverBrush;
						}
					};
					singlesTab.MouseLeave += delegate
					{
						if (isAlbumsActive)
						{
							singlesTab.Background = defaultBrush;
						}
					};
					singlesTab.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
					{
						e.Handled = true;
						SwitchTab(toAlbums: false, animate: true);
					};
					if (albumsArray != null && albumsArray.Count > 0)
					{
						SwitchTab(toAlbums: true);
						if (singlesArray == null || singlesArray.Count == 0)
						{
							singlesTab.Visibility = Visibility.Collapsed;
						}
					}
					else
					{
						SwitchTab(toAlbums: false);
						albumsTab.Visibility = Visibility.Collapsed;
					}
					pagePanel.Children.Add(releasesContainer);
				}
				if (dataInfo["similarArtists"] is JsonArray { Count: >0 } similarArtists)
				{
					var (svSimilar, similarGrid) = CreateExpandableSection("Fans might also like", similarArtists.Count);
					foreach (JsonNode item4 in similarArtists)
					{
						string id3 = ((string?)item4["browseId"]) ?? "";
						string title4 = ((string?)item4["title"]) ?? "";
						string tUrl4 = "";
						if (item4["thumbnails"] is JsonArray { Count: >0 } thumbs4)
						{
							tUrl4 = ((string?)thumbs4[thumbs4.Count - 1]["url"]) ?? "";
						}
						similarGrid.Children.Add(CreateTrackCard(id3, title4, "Artist", tUrl4, "Artist"));
					}
					pagePanel.Children.Add(svSimilar);
				}
				string description = dataInfo?["description"]?.ToString() ?? "";
				if (!string.IsNullOrEmpty(description))
				{
					pagePanel.Children.Add(CreateExpandableAboutSection(description));
				}
			}
			if (_pageCache.Count >= 3)
			{
				_pageCache.Clear();
			}
			_pageCache[channelId] = pagePanel;
			await FadeInContentAsync(tId, delegate
			{
				ContentPanel.Children.Add(pagePanel);
				MainScrollViewer.ScrollToTop();
			});
			async void SwitchTab(bool toAlbums, bool animate = false)
			{
				if (!(isAlbumsActive == toAlbums && animate))
				{
					isAlbumsActive = toAlbums;
					albumsTab.Background = (toAlbums ? accentBrush : defaultBrush);
					singlesTab.Background = (toAlbums ? defaultBrush : accentBrush);
					if (animate)
					{
						DoubleAnimation fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150.0));
						if (toAlbums)
						{
							singlesGridWrapper.BeginAnimation(UIElement.OpacityProperty, fadeOut);
							singlesSeeAllWrapper.BeginAnimation(UIElement.OpacityProperty, fadeOut);
						}
						else
						{
							albumsGridWrapper.BeginAnimation(UIElement.OpacityProperty, fadeOut);
							albumsSeeAllWrapper.BeginAnimation(UIElement.OpacityProperty, fadeOut);
						}
						await Task.Delay(150);
						if (isAlbumsActive != toAlbums)
						{
							return;
						}
					}
					albumsGridWrapper.Visibility = ((!toAlbums) ? Visibility.Collapsed : Visibility.Visible);
					singlesGridWrapper.Visibility = (toAlbums ? Visibility.Collapsed : Visibility.Visible);
					albumsSeeAllWrapper.Visibility = ((!toAlbums) ? Visibility.Collapsed : Visibility.Visible);
					singlesSeeAllWrapper.Visibility = (toAlbums ? Visibility.Collapsed : Visibility.Visible);
					if (animate)
					{
						DoubleAnimation fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(150.0));
						if (toAlbums)
						{
							albumsGridWrapper.BeginAnimation(UIElement.OpacityProperty, fadeIn);
							albumsSeeAllWrapper.BeginAnimation(UIElement.OpacityProperty, fadeIn);
						}
						else
						{
							singlesGridWrapper.BeginAnimation(UIElement.OpacityProperty, fadeIn);
							singlesSeeAllWrapper.BeginAnimation(UIElement.OpacityProperty, fadeIn);
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			await FadeInContentAsync(tId, delegate
			{
				ShowGlobalError("Failed to load artist", ex2.Message, delegate
				{
					_ = _ = _ = _ = LoadLibraryAsync();
					OpenArtistPage(channelId, artistName, thumbUrl, preTId);
				});
			});
		}
	}

	private async void OpenLocalMusicPage()
	{
		if (_currentPageId == "local_files" && _pageCache.ContainsKey("local_files"))
		{
			_pageCache.Remove("local_files");
			_pageVirtualizationCache.Remove("local_files");
		}
		else if (_pageCache.ContainsKey("local_files"))
		{
			if (_currentPageId != "local_files")
			{
				PushCurrentPageToHistory();
			}
			_currentPageId = "local_files";
			UpdateSidebarHighlight();
			await FadeInContentAsync(await FadeOutContentAsync(), delegate
			{
				ContentPanel.Children.Add(_pageCache["local_files"]);
				HighlightNowPlaying(_currentVideoId);
				MainScrollViewer.ScrollToTop();
			});
			return;
		}
		if (_currentPageId != "local_files")
		{
			PushCurrentPageToHistory();
		}
		_currentPageId = "local_files";
		UpdateSidebarHighlight();
		int tId = await FadeOutContentAsync();
		ContentPanel.Children.Clear();
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
		System.Windows.Shapes.Path iconPath = new System.Windows.Shapes.Path
		{
			Data = Geometry.Parse("M 0 0 L 18 0 L 18 18 L 0 18 Z M 9 4 A 5 5 0 1 0 9 14 A 5 5 0 1 0 9 4 M 9 6 A 3 3 0 1 1 9 12 A 3 3 0 1 1 9 6"),
			Fill = System.Windows.Media.Brushes.White,
			Stretch = Stretch.Uniform,
			Width = 100.0,
			Height = 100.0,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		imgBorder.Child = iconPath;
		ApplyImageOverlay(imgBorder);
		headerPanel.Children.Add(imgBorder);
		StackPanel titlePanel = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center
		};
		titlePanel.Children.Add(new TextBlock
		{
			Text = "Playlist",
			Foreground = System.Windows.Media.Brushes.Gray,
			FontSize = 14.0
		});
		titlePanel.Children.Add(new TextBlock
		{
			Text = "Local Music",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			FontSize = 48.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 10.0, 0.0, 0.0),
			TextWrapping = TextWrapping.Wrap
		});
		titlePanel.Children.Add(new TextBlock
		{
			Text = "Your personal collection",
			Foreground = System.Windows.Media.Brushes.Gray,
			FontSize = 16.0,
			Margin = new Thickness(0.0, 5.0, 0.0, 0.0)
		});
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
			Height = 48.0
		};
		System.Windows.Controls.Button shuffleBtn = new System.Windows.Controls.Button
		{
			Style = (Style)FindResource("AccentButtonStyle"),
			Width = 48.0,
			Height = 48.0,
			Margin = new Thickness(15.0, 0.0, 0.0, 0.0)
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
		StackPanel grid = new StackPanel();
		pagePanel.Children.Add(grid);
		ContentPanel.Children.Add(pagePanel);
		if (_pageCache.Count >= 3)
		{
			_pageCache.Clear();
		}
		_pageCache["local_files"] = pagePanel;
		await FadeInContentAsync(tId, delegate
		{
		});
		_ = _ = _ = _ = Task.Run(delegate
		{
			try
			{
				List<string> list = (from f in Directory.GetFiles(_LocalMusicPath, "*.*", SearchOption.AllDirectories)
					where f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".opus", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)
					select f).ToList();
				JsonArray localQueue = new JsonArray();
				int num = 1;
				foreach (string file in list)
				{
					string title = System.IO.Path.GetFileNameWithoutExtension(file);
					string artist = "Local Artist";
					string thumbUrl = "";
					try
					{
						using TagLib.File file2 = TagLib.File.Create(file);
						if (!string.IsNullOrEmpty(file2.Tag.Title))
						{
							title = file2.Tag.Title;
						}
						if (file2.Tag.Performers.Length != 0)
						{
							artist = file2.Tag.Performers[0];
						}
						if (!string.IsNullOrEmpty(file2.Tag.Album))
						{
							_ = file2.Tag.Album;
						}
						if (file2.Tag.Pictures.Length != 0)
						{
							IPicture picture = file2.Tag.Pictures[0];
							string text = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "spectre_local_thumb_" + file.GetHashCode() + ".jpg");
							if (!System.IO.File.Exists(text))
							{
								System.IO.File.WriteAllBytes(text, picture.Data.Data);
							}
							thumbUrl = "file:///" + text.Replace('\\', '/');
						}
					}
					catch
					{
					}
					JsonObject JsonObject = new JsonObject
					{
						["videoId"] = "local:" + file,
						["title"] = title,
						["artists"] = new JsonArray(new JsonObject { ["name"] = artist }),
						["thumbnails"] = new JsonArray()
					};
					if (!string.IsNullOrEmpty(thumbUrl))
					{
						((JsonArray)JsonObject["thumbnails"]).Add(new JsonObject { ["url"] = thumbUrl });
					}
					localQueue.Add(JsonObject);
					int currentIndex = num;
					base.Dispatcher.InvokeAsync(delegate
					{
						Border element = CreateTrackRow("local:" + file, title, artist, thumbUrl, currentIndex.ToString(), "", "", "", "local_files", localQueue, currentIndex - 1);
						grid.Children.Add(element);
					});
					num++;
				}
				base.Dispatcher.InvokeAsync(delegate
				{
					Action<bool> startLocalPlayback = delegate(bool shuffle)
					{
						if (localQueue.Count != 0)
						{
							if (shuffle && !_isShuffleOn)
							{
								_isShuffleOn = true;
								UpdateShuffleIcon();
							}
							int num2 = (shuffle ? new Random().Next(localQueue.Count) : 0);
							InitQueueAndShuffle((System.Text.Json.Nodes.JsonArray)localQueue.DeepClone(), num2);
							JsonNode JsonNode = localQueue[num2];
							string text2 = ((string?)JsonNode?["videoId"]) ?? "";
							string title2 = ((string?)JsonNode?["title"]) ?? "";
							string text3 = "Local Artist";
							JsonArray JsonArray = JsonNode?["artists"] as JsonArray;
							if (JsonArray != null && JsonArray.Count > 0)
							{
								text3 = ((string?)JsonArray[0]["name"]) ?? text3;
							}
							string thumbUrl2 = "";
							if (JsonNode?["thumbnails"] is JsonArray { Count: >0 } jArray2)
							{
								thumbUrl2 = ((string?)jArray2[jArray2.Count - 1]["url"]) ?? "";
							}
							if (!string.IsNullOrEmpty(text2))
							{
								_ = _ = _ = _ = PlayTrack(text2, title2, text3, thumbUrl2, addToHistory: true, startPaused: false, useCrossfade: false, 0, JsonArray);
							}
						}
					};
					playBtn.Click += delegate
					{
						startLocalPlayback(obj: false);
					};
					shuffleBtn.Click += delegate
					{
						startLocalPlayback(obj: true);
					};
				});
			}
			catch
			{
			}
		});
	}

	private async Task OpenAccountPageAsync()
	{
		if (_currentPageId == "account")
		{
			return;
		}
		_currentPageId = "account";
		UpdateSidebarHighlight();
		int tId = await FadeOutContentAsync();
		Grid pageGrid = new Grid
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 80.0)
		};
		pageGrid.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		pageGrid.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		pageGrid.RowDefinitions.Add(new RowDefinition
		{
			Height = new GridLength(1.0, GridUnitType.Star)
		});
		pageGrid.Children.Add(new TextBlock
		{
			Text = "Account",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			FontSize = 36.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 20.0, 0.0, 30.0)
		});
		Border bannerBorder = new Border
		{
			HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
			Background = (System.Windows.Media.Brush)System.Windows.Application.Current.MainWindow.Resources["CardBackground"],
			BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(15, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(16.0),
			Padding = new Thickness(30.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 30.0)
		};
		Grid.SetRow(bannerBorder, 1);
		Grid bannerGrid = new Grid();
		bannerGrid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		bannerGrid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		bannerGrid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		Border avatarContainer = new Border
		{
			Width = 100.0,
			Height = 100.0,
			CornerRadius = new CornerRadius(50.0),
			Margin = new Thickness(0.0, 0.0, 25.0, 0.0),
			Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(30, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			BorderThickness = new Thickness(1.0),
			ClipToBounds = true
		};
		TextBlock avatarIcon = new TextBlock
		{
			Text = "\ud83d\udc64",
			FontSize = 40.0,
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		avatarContainer.Child = avatarIcon;
		Grid.SetColumn(avatarContainer, 0);
		bannerGrid.Children.Add(avatarContainer);
		StackPanel infoStack = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center
		};
		TextBlock nameLabel = new TextBlock
		{
			Text = "Not Logged In",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			FontSize = 28.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 0.0, 0.0, 5.0)
		};
		infoStack.Children.Add(nameLabel);
		TextBlock emailLabel = new TextBlock
		{
			Text = "Connect your account to sync your library",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"],
			FontSize = 14.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
		};
		infoStack.Children.Add(emailLabel);
		Border statusPanel = new Border
		{
			Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 0, 229, byte.MaxValue)),
			BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 0, 229, byte.MaxValue)),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(10.0, 4.0, 10.0, 4.0),
			HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
			Visibility = Visibility.Collapsed
		};
		TextBlock loggedInLabel = new TextBlock
		{
			Text = "Connected",
			Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 229, byte.MaxValue)),
			FontSize = 12.0,
			FontWeight = FontWeights.SemiBold
		};
		statusPanel.Child = loggedInLabel;
		infoStack.Children.Add(statusPanel);
		Grid.SetColumn(infoStack, 1);
		bannerGrid.Children.Add(infoStack);
		StackPanel btnStack = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center
		};
		System.Windows.Controls.Button loginBtn = new System.Windows.Controls.Button
		{
			Content = "Start Google Login",
			Height = 40.0,
			FontSize = 14.0,
			FontWeight = FontWeights.Bold,
			Background = System.Windows.Media.Brushes.White,
			Foreground = System.Windows.Media.Brushes.Black,
			BorderThickness = new Thickness(0.0),
			Margin = new Thickness(0.0, 0.0, 10.0, 0.0),
			Padding = new Thickness(25.0, 0.0, 25.0, 0.0),
			Cursor = System.Windows.Input.Cursors.Hand
		};
		ControlTemplate loginTemplate = (ControlTemplate)XamlReader.Parse("\r\n                <ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>\r\n                    <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='20' Padding='{TemplateBinding Padding}'>\r\n                        <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' />\r\n                    </Border>\r\n                    <ControlTemplate.Triggers>\r\n                        <Trigger Property='IsMouseOver' Value='True'>\r\n                            <Setter Property='Opacity' Value='0.8' />\r\n                        </Trigger>\r\n                        <Trigger Property='IsPressed' Value='True'>\r\n                            <Setter Property='Opacity' Value='0.6' />\r\n                        </Trigger>\r\n                    </ControlTemplate.Triggers>\r\n                </ControlTemplate>");
		loginBtn.Template = loginTemplate;
		System.Windows.Controls.Button logoutBtn = new System.Windows.Controls.Button
		{
			Content = "Log Out",
			Height = 40.0,
			FontSize = 14.0,
			FontWeight = FontWeights.Bold,
			Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 248, 113, 113)),
			Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 113, 113)),
			BorderThickness = new Thickness(1.0),
			BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 248, 113, 113)),
			Cursor = System.Windows.Input.Cursors.Hand,
			Visibility = Visibility.Collapsed,
			Padding = new Thickness(25.0, 0.0, 25.0, 0.0)
		};
		logoutBtn.Template = (ControlTemplate)XamlReader.Parse("\r\n                <ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>\r\n                    <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='20' Padding='{TemplateBinding Padding}'>\r\n                        <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' />\r\n                    </Border>\r\n                    <ControlTemplate.Triggers>\r\n                        <Trigger Property='IsMouseOver' Value='True'>\r\n                            <Setter Property='Background' Value='#30F87171' />\r\n                        </Trigger>\r\n                        <Trigger Property='IsPressed' Value='True'>\r\n                            <Setter Property='Background' Value='#40F87171' />\r\n                        </Trigger>\r\n                    </ControlTemplate.Triggers>\r\n                </ControlTemplate>");
		btnStack.Children.Add(loginBtn);
		btnStack.Children.Add(logoutBtn);
		Grid.SetColumn(btnStack, 2);
		bannerGrid.Children.Add(btnStack);
		bannerBorder.Child = bannerGrid;
		pageGrid.Children.Add(bannerBorder);
		Border instructionsPanel = new Border
		{
			HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
			MaxWidth = 600.0,
			Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(10, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(20.0)
		};
		Grid.SetRow(instructionsPanel, 2);
		StackPanel instructionsStack = new StackPanel();
		instructionsStack.Children.Add(new TextBlock
		{
			Text = "How to Login",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			FontWeight = FontWeights.Bold,
			FontSize = 16.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 15.0)
		});
		instructionsStack.Children.Add(new TextBlock
		{
			Text = "1. Click 'Start Google Login' to open a secure browser window.",
			Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0),
			TextWrapping = TextWrapping.Wrap
		});
		instructionsStack.Children.Add(new TextBlock
		{
			Text = "2. Log into your Google Account normally.",
			Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0),
			TextWrapping = TextWrapping.Wrap
		});
		instructionsStack.Children.Add(new TextBlock
		{
			Text = "3. Once you see the YouTube Music homepage, click 'I'm Logged In'.",
			Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
			TextWrapping = TextWrapping.Wrap
		});
		instructionsPanel.Child = instructionsStack;
		pageGrid.Children.Add(instructionsPanel);
		Action checkLogin = async delegate
		{
			if (System.IO.File.Exists(BackendService.AuthFilePath))
			{
				try
				{
					loggedInLabel.Text = "Loading...";
					statusPanel.Visibility = Visibility.Visible;
					logoutBtn.Visibility = Visibility.Visible;
					loginBtn.Content = "Re-login";
					loginBtn.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, byte.MaxValue, byte.MaxValue, byte.MaxValue));
					loginBtn.Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"];
					loginBtn.Template = loginTemplate;
					instructionsPanel.Visibility = Visibility.Collapsed;
					JsonObject obj = await BackendService.Instance.GetAccountInfoAsync(CancellationToken.None);
					string accountName = ((string?)obj["data"]?["accountName"]) ?? "Google Account";
					string accountEmail = ((string?)obj["data"]?["accountEmail"]) ?? "";
					string accountPhoto = ((string?)obj["data"]?["accountPhoto"]) ?? "";
					nameLabel.Text = accountName;
					if (!string.IsNullOrEmpty(accountEmail))
					{
						emailLabel.Text = accountEmail;
					}
					else
					{
						emailLabel.Text = "Your library is synced";
					}
					if (!string.IsNullOrEmpty(accountPhoto))
					{
						UIElement img = CreateImage(accountPhoto, 100, 100);
						RectangleGeometry clipRect = new RectangleGeometry
						{
							Rect = new Rect(0.0, 0.0, 100.0, 100.0),
							RadiusX = 50.0,
							RadiusY = 50.0
						};
						img.Clip = clipRect;
						avatarContainer.Child = img;
						avatarContainer.BorderThickness = new Thickness(0.0);
					}
					loggedInLabel.Text = "✓ Connected to YouTube Music";
					return;
				}
				catch
				{
					if (!System.IO.File.Exists(BackendService.AuthFilePath))
					{
						statusPanel.Visibility = Visibility.Collapsed;
						logoutBtn.Visibility = Visibility.Collapsed;
						loginBtn.Content = "Start Google Login";
						loginBtn.Background = System.Windows.Media.Brushes.White;
						loginBtn.Foreground = System.Windows.Media.Brushes.Black;
						loginBtn.Template = loginTemplate;
						instructionsPanel.Visibility = Visibility.Visible;
						nameLabel.Text = "Not Logged In";
						emailLabel.Text = "Connect your account to sync your library";
						avatarContainer.Child = avatarIcon;
						avatarContainer.BorderThickness = new Thickness(1.0);
						return;
					}
					nameLabel.Text = "Google Account";
					emailLabel.Text = "Your library is synced";
					loggedInLabel.Text = "✓ Connected";
					instructionsPanel.Visibility = Visibility.Collapsed;
					return;
				}
			}
			statusPanel.Visibility = Visibility.Collapsed;
			logoutBtn.Visibility = Visibility.Collapsed;
			loginBtn.Content = "Start Google Login";
			loginBtn.Background = System.Windows.Media.Brushes.White;
			loginBtn.Foreground = System.Windows.Media.Brushes.Black;
			loginBtn.Template = loginTemplate;
			instructionsPanel.Visibility = Visibility.Visible;
			nameLabel.Text = "Not Logged In";
			emailLabel.Text = "Connect your account to sync your library";
			avatarContainer.Child = avatarIcon;
			avatarContainer.BorderThickness = new Thickness(1.0);
		};
		checkLogin();
		logoutBtn.Click += delegate
		{
			BackendService.Instance.ClearAuth();
			checkLogin();
			CheckLoginStatus();
			SetSidebarState(minimize: true);
			UpdateTabVisibility();
			PushCurrentPageToHistory();
			_currentPageId = "explore";
			UpdateSidebarHighlight();
			_ = _ = _ = _ = LoadExploreFeedAsync(forceReload: true);
			_ = _ = _ = _ = LoadLibraryAsync();
		};
		loginBtn.Click += delegate
		{
			LoginWindow loginWindow = new LoginWindow();
			loginWindow.Owner = this;
			loginWindow.ShowDialog();
			if (loginWindow.IsLoginSuccessful)
			{
				CheckLoginStatus();
				checkLogin();
				SetSidebarState(minimize: false);
				PushCurrentPageToHistory();
				_currentPageId = "home";
				UpdateTabVisibility();
				UpdateSidebarHighlight();
				_ = _ = _ = _ = LoadHomeFeedAsync(forceReload: true);
				_ = _ = _ = _ = LoadLibraryAsync();
				_ = _ = _ = _ = LoadLikedSongsAsync();
			}
		};
		pageGrid.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		pageGrid.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		pageGrid.Children.Add(new TextBlock
		{
			Text = "Connections",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			FontSize = 24.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 40.0, 0.0, 20.0)
		});
		Grid.SetRow(pageGrid.Children[pageGrid.Children.Count - 1] as FrameworkElement, 3);
		Border lfmBorder = new Border
		{
			HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
			Background = (System.Windows.Media.Brush)System.Windows.Application.Current.MainWindow.Resources["CardBackground"],
			BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(15, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(16.0),
			Padding = new Thickness(30.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 30.0)
		};
		Grid.SetRow(lfmBorder, 4);
		Grid lfmGrid = new Grid();
		lfmGrid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		lfmGrid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		lfmGrid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		Border lfmIconContainer = new Border
		{
			Width = 60.0,
			Height = 60.0,
			CornerRadius = new CornerRadius(30.0),
			Margin = new Thickness(0.0, 0.0, 25.0, 0.0),
			Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 213, 16, 7)),
			BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 213, 16, 7)),
			BorderThickness = new Thickness(1.0)
		};
		lfmIconContainer.Child = new TextBlock
		{
			Text = "aud",
			FontSize = 20.0,
			FontWeight = FontWeights.Bold,
			Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(213, 16, 7)),
			HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		Grid.SetColumn(lfmIconContainer, 0);
		lfmGrid.Children.Add(lfmIconContainer);
		StackPanel lfmInfoStack = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center
		};
		TextBlock lfmName = new TextBlock
		{
			Text = "Last.fm",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
			FontSize = 22.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 0.0, 0.0, 5.0)
		};
		TextBlock lfmDesc = new TextBlock
		{
			Text = "Scrobble your tracks automatically",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"],
			FontSize = 14.0
		};
		lfmInfoStack.Children.Add(lfmName);
		lfmInfoStack.Children.Add(lfmDesc);
		Grid.SetColumn(lfmInfoStack, 1);
		lfmGrid.Children.Add(lfmInfoStack);
		StackPanel lfmBtnStack = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center
		};
		System.Windows.Controls.Button lfmConnectBtn = new System.Windows.Controls.Button
		{
			Content = "Connect",
			Height = 40.0,
			FontSize = 14.0,
			FontWeight = FontWeights.Bold,
			Background = System.Windows.Media.Brushes.White,
			Foreground = System.Windows.Media.Brushes.Black,
			BorderThickness = new Thickness(0.0),
			Margin = new Thickness(0.0, 0.0, 10.0, 0.0),
			Padding = new Thickness(25.0, 0.0, 25.0, 0.0),
			Cursor = System.Windows.Input.Cursors.Hand,
			Template = loginTemplate
		};
		System.Windows.Controls.Button lfmDisconnectBtn = new System.Windows.Controls.Button
		{
			Content = "Disconnect",
			Height = 40.0,
			FontSize = 14.0,
			FontWeight = FontWeights.Bold,
			Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 248, 113, 113)),
			Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 113, 113)),
			BorderThickness = new Thickness(1.0),
			BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 248, 113, 113)),
			Cursor = System.Windows.Input.Cursors.Hand,
			Visibility = Visibility.Collapsed,
			Padding = new Thickness(25.0, 0.0, 25.0, 0.0),
			Template = logoutBtn.Template
		};
		lfmBtnStack.Children.Add(lfmConnectBtn);
		lfmBtnStack.Children.Add(lfmDisconnectBtn);
		Grid.SetColumn(lfmBtnStack, 2);
		lfmGrid.Children.Add(lfmBtnStack);
		lfmBorder.Child = lfmGrid;
		pageGrid.Children.Add(lfmBorder);
		Action updateLfmState = delegate
		{
			if (LastFmManager.IsEnabled)
			{
				lfmDesc.Text = "Connected as " + LastFmManager.Username;
				lfmDesc.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 229, byte.MaxValue));
				lfmConnectBtn.Visibility = Visibility.Collapsed;
				lfmDisconnectBtn.Visibility = Visibility.Visible;
			}
			else
			{
				lfmDesc.Text = "Scrobble your tracks automatically";
				lfmDesc.Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"];
				lfmConnectBtn.Visibility = Visibility.Visible;
				lfmDisconnectBtn.Visibility = Visibility.Collapsed;
			}
		};
		updateLfmState();
		lfmDisconnectBtn.Click += delegate
		{
			LastFmManager.SessionKey = "";
			LastFmManager.Username = "";
			LastFmManager.ApiKey = "";
			LastFmManager.SharedSecret = "";
			updateLfmState();
			SaveSession();
		};
		lfmConnectBtn.Click += delegate
		{
			LastFmLoginWindow lastFmLoginWindow = new LastFmLoginWindow
			{
				Owner = this
			};
			lastFmLoginWindow.ShowDialog();
			if (lastFmLoginWindow.IsLoginSuccessful)
			{
				LastFmManager.ApiKey = lastFmLoginWindow.ScrapedApiKey;
				LastFmManager.SharedSecret = lastFmLoginWindow.ScrapedSharedSecret;
				LastFmManager.SessionKey = lastFmLoginWindow.ScrapedSessionKey;
				LastFmManager.Username = lastFmLoginWindow.ScrapedUsername;
				updateLfmState();
				SaveSession();
			}
		};
		await FadeInContentAsync(tId, delegate
		{
			ContentPanel.Children.Add(pageGrid);
			MainScrollViewer.ScrollToTop();
		});
	}
}







