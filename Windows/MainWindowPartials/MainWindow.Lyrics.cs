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
	private void LyricsBtn_Click(object sender, RoutedEventArgs e)
	{
		if (_currentPageId == "lyrics")
		{
			BackBtn_Click(null, null);
		}
		else
		{
			_ = _ = _ = _ = ShowLyricsViewAsync();
		}
	}

	private async Task ShowLyricsViewAsync(bool replaceCurrentLyrics = false)
	{
		if (string.IsNullOrEmpty(_currentVideoId))
		{
			MainTopbarControl.StatusLabelRef.Text = "No song selected";
			return;
		}
		if (!replaceCurrentLyrics && _currentPageId != "lyrics")
		{
			PushCurrentPageToHistory();
		}
		_isLyricsUserScrolled = false;
		bool useCached = _lyricsVideoId == _currentVideoId && _lyricLines.Count > 0;
		_currentPageId = "lyrics";
		_isLyricsLoading = true;
		_currentReloadAction = delegate
		{
			_ = _ = _ = _ = ShowLyricsViewAsync(replaceCurrentLyrics: true);
		};
		UpdateLyricsIcon();
		UpdateSidebarHighlight();
		int tId = await FadeOutContentAsync();
		if (!useCached)
		{
			_lyricsVideoId = _currentVideoId;
			_currentLyricIndex = -1;
			_lyricLines.Clear();
			try
			{
				JObject json = await LoadLyricsForTrackAsync(_currentVideoId, _currentTitle, _currentArtist, _player.Length, CancellationToken.None);
				if (!_isLyricsViewOpen || _lyricsVideoId != _currentVideoId)
				{
					return;
				}
				ApplyLyricsJson(json);
				await FadeInContentAsync(tId, delegate
				{
					RenderLyricsView(_lyricsAreSynced, _lyricsSource);
					base.Dispatcher.InvokeAsync(delegate
					{
						_isLyricsLoading = false;
						if (_lyricsAreSynced)
						{
							UpdateLyricsForTime(_player.Time, forceScroll: true, immediateScroll: true);
						}
						else
						{
							_isLyricsUserScrolled = false;
							MainScrollViewer.ScrollToTop();
							UpdateUnsyncedLyricsOpacity();
						}
					}, DispatcherPriority.Loaded);
				});
				return;
			}
			catch
			{
				await FadeInContentAsync(tId, delegate
				{
					RenderLyricsView(isSynced: false, "");
					_isLyricsLoading = false;
				});
				return;
			}
		}
		await FadeInContentAsync(tId, delegate
		{
			RenderLyricsView(_lyricsAreSynced, _lyricsSource);
			base.Dispatcher.InvokeAsync(delegate
			{
				_isLyricsLoading = false;
				if (_lyricsAreSynced)
				{
					UpdateLyricsForTime(_player.Time, forceScroll: true, immediateScroll: true);
				}
				else
				{
					_isLyricsUserScrolled = false;
					MainScrollViewer.ScrollToTop();
					UpdateUnsyncedLyricsOpacity();
				}
			}, DispatcherPriority.Loaded);
		});
	}

	private string GetLyricsCacheKey(string videoId, string title, string artist, long durationMs)
	{
		long durationBucket = ((durationMs > 0) ? (durationMs / 1000) : 0);
		return $"{videoId}|{durationBucket}|{title}|{artist}";
	}

	private Task<JObject> LoadLyricsForTrackAsync(string videoId, string title, string artist, long durationMs, CancellationToken token)
	{
		if (_lyricsTasks.Count > 25)
		{
			foreach (string staleKey in _lyricsTasks.Keys.Take(Math.Max(1, _lyricsTasks.Count - 20)).ToList())
			{
				_lyricsTasks.TryRemove(staleKey, out Task<JObject> _);
			}
		}
		string key = GetLyricsCacheKey(videoId, title, artist, durationMs);
		return _lyricsTasks.GetOrAdd(key, (string _) => BackendService.Instance.GetLyricsAsync(videoId, title, artist, durationMs, token));
	}

	private void WarmLyricsForCurrentTrack(long durationMs)
	{
		if (string.IsNullOrEmpty(_currentVideoId) || _currentVideoId.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		string videoId = _currentVideoId;
		string title = _currentTitle;
		string artist = _currentArtist;
		string key = GetLyricsCacheKey(videoId, title, artist, durationMs);
		if (_lastLyricsWarmKey == key)
		{
			return;
		}
		_lastLyricsWarmKey = key;
		LoadLyricsForTrackAsync(videoId, title, artist, durationMs, CancellationToken.None).ContinueWith(delegate(Task<JObject> t)
		{
			if (t.IsFaulted)
			{
				_lyricsTasks.TryRemove(key, out Task<JObject> _);
				if (_lastLyricsWarmKey == key)
				{
					_lastLyricsWarmKey = "";
				}
			}
		}, TaskScheduler.Default);
	}

	private void ApplyLyricsJson(JObject json)
	{
		JArray lines = json["lines"] as JArray;
		_lyricsAreSynced = (bool?)json["synced"] == true;
		_lyricsSource = ((string?)json["source"]) ?? "";
		_lyricsOffsetMs = 0;
		UpdateLyricsOffsetUI();
		_lyricLines = new List<LyricLine>();
		if (lines == null)
		{
			return;
		}
		bool hasRealSyllables = false;
		if (_lyricsAreSynced)
		{
			int meaningfulLines = 0;
			foreach (JToken item in lines)
			{
				if (!(item["syllables"] is JArray { Count: >1 } syllablesArr))
				{
					continue;
				}
				long firstTime = ((long?)syllablesArr.First?["timeMs"]).GetValueOrDefault();
				long lastTime = ((long?)syllablesArr.Last?["timeMs"]).GetValueOrDefault();
				long totalDuration = 0L;
				foreach (JToken syl in syllablesArr)
				{
					totalDuration += ((long?)syl["durationMs"]).GetValueOrDefault();
				}
				if (lastTime > firstTime || totalDuration > 0)
				{
					meaningfulLines++;
				}
			}
			if (meaningfulLines > 0 && (double)meaningfulLines >= (double)lines.Count * 0.1)
			{
				hasRealSyllables = true;
			}
			foreach (JToken item2 in lines)
			{
				if ((((string?)item2["text"]) ?? "").Split(new string[2] { "\\n", "\n" }, StringSplitOptions.None).Length > 3)
				{
					_lyricsAreSynced = false;
					break;
				}
			}
		}
		foreach (JToken line in lines)
		{
			string text = ((string?)line["text"]) ?? "";
			long timeMs = ((long?)line["timeMs"]).GetValueOrDefault();
			if (!string.IsNullOrEmpty(text))
			{
				text = text.Replace("\\n", "\n").Replace("\\r", "");
				if (text.StartsWith("\"") && text.EndsWith("\"") && text.Length >= 2)
				{
					text = text.Substring(1, text.Length - 2);
				}
				if (text.Contains('\n') && !_lyricsAreSynced)
				{
					string[] array = text.Split(new char[1] { '\n' }, StringSplitOptions.None);
					for (int i = 0; i < array.Length; i++)
					{
						string s = array[i].Trim();
						if (string.IsNullOrWhiteSpace(s))
						{
							s = "•••";
						}
						_lyricLines.Add(new LyricLine
						{
							TimeMs = timeMs,
							Text = s
						});
					}
					continue;
				}
			}
			if (string.IsNullOrWhiteSpace(text))
			{
				text = "•••";
			}
			LyricLine lyricLine = new LyricLine
			{
				TimeMs = timeMs,
				Text = text
			};
			JArray syllablesArr2 = (hasRealSyllables ? (line["syllables"] as JArray) : null);
			if (syllablesArr2 != null && syllablesArr2.Count > 0)
			{
				lyricLine.Syllables = new List<LyricSyllable>();
				foreach (JToken item3 in syllablesArr2)
				{
					string rawText = ((string?)item3["text"]) ?? "";
					long tMs = ((long?)item3["timeMs"]).GetValueOrDefault();
					long dMs = ((long?)item3["durationMs"]).GetValueOrDefault();
					bool hasLetters = false;
					for (int i = 0; i < rawText.Length; i++)
					{
						if (char.IsLetterOrDigit(rawText[i]))
						{
							hasLetters = true;
							break;
						}
					}

					bool shouldMerge = !hasLetters;

					if (shouldMerge && lyricLine.Syllables.Count > 0)
					{
						LyricSyllable prev = lyricLine.Syllables[lyricLine.Syllables.Count - 1];
						lyricLine.Syllables[lyricLine.Syllables.Count - 1] = new LyricSyllable
						{
							TimeMs = prev.TimeMs,
							DurationMs = prev.DurationMs + dMs,
							Text = prev.Text + rawText
						};
					}
					else
					{
						lyricLine.Syllables.Add(new LyricSyllable
						{
							TimeMs = tMs,
							DurationMs = dMs,
							Text = rawText
						});
					}
				}
			}
			_lyricLines.Add(lyricLine);
		}
	}

	private void UpdateLyricsIcon()
	{
		if (_currentPageId == "lyrics")
		{
			MainPlayerBarControl.LyricsIconOffRef.Visibility = Visibility.Visible;
			MainPlayerBarControl.LyricsIconOnRef.Visibility = Visibility.Visible;
		}
		else
		{
			MainPlayerBarControl.LyricsIconOffRef.Visibility = Visibility.Visible;
			MainPlayerBarControl.LyricsIconOnRef.Visibility = Visibility.Collapsed;
		}
	}

	private UIElement CreateLyricsHeader(string title, string subtitle)
	{
		return new StackPanel
		{
			Margin = new Thickness(0.0, 12.0, 0.0, 36.0),
			Children = 
			{
				(UIElement)new TextBlock
				{
					Text = title,
					Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"],
					FontSize = 42.0,
					FontWeight = FontWeights.Bold,
					TextWrapping = TextWrapping.Wrap
				},
				(UIElement)new TextBlock
				{
					Text = subtitle,
					Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextSecondaryBrush"],
					FontSize = 14.0,
					Margin = new Thickness(0.0, 8.0, 0.0, 0.0)
				}
			}
		};
	}

	private void RenderLyricsView(bool isSynced, string source)
	{
		UpdateLyricsOffsetUI();
		if (!isSynced && _lyricLines.Count > 0)
		{
			ShowToast("Unsynced lyrics - scroll manually", 5);
		}
		ContentPanel.Children.Clear();
		if (_lyricLines.Count == 0)
		{
			Grid container = new Grid
			{
				HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
				VerticalAlignment = System.Windows.VerticalAlignment.Stretch
			};

			TextBlock textBlock = new TextBlock
			{
				Text = "No lyrics available",
				Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(125, 125, 125)),
				FontSize = 28.0,
				FontWeight = FontWeights.SemiBold,
				TextAlignment = TextAlignment.Center,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
				VerticalAlignment = System.Windows.VerticalAlignment.Center
			};

			container.Children.Add(textBlock);

			void UpdateHeight()
			{
				double availableHeight = MainScrollViewer.ActualHeight - ContentPanel.Margin.Top - ContentPanel.Margin.Bottom;
				if (availableHeight > 0)
				{
					container.Height = availableHeight;
				}
			}

			UpdateHeight();

			SizeChangedEventHandler handler = null;
			handler = (s, e) =>
			{
				if (!ContentPanel.Children.Contains(container))
				{
					MainScrollViewer.SizeChanged -= handler;
					return;
				}
				UpdateHeight();
			};

			MainScrollViewer.SizeChanged += handler;
			ContentPanel.Children.Add(container);
			return;
		}
		Border topSpacer = new Border
		{
			Height = 220.0,
			Background = System.Windows.Media.Brushes.Transparent
		};
		ContentPanel.Children.Add(topSpacer);
		foreach (LyricLine line in _lyricLines)
		{
			UIElement childContent;
			if (line.Syllables != null && line.Syllables.Count > 0)
			{
				WrapPanel wrapPanel = new WrapPanel
				{
					HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
					MaxWidth = 1040.0,
					RenderTransformOrigin = new System.Windows.Point(0.5, 0.5)
				};
				line.SyllablesPanel = wrapPanel;
				foreach (LyricSyllable syl in line.Syllables)
				{
					TextBlock sylText = (syl.TextBlock = new TextBlock
					{
						Text = syl.Text,
						Foreground = (isSynced ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(125, 125, 125)) : System.Windows.Media.Brushes.White),
						FontSize = 28.0,
						FontWeight = FontWeights.SemiBold,
						TextAlignment = TextAlignment.Center
					});
					wrapPanel.Children.Add(sylText);
				}
				childContent = wrapPanel;
				ScaleTransform scale = (ScaleTransform)(wrapPanel.RenderTransform = new ScaleTransform(1.0, 1.0));
				line.ScaleTransform = scale;
			}
			else
			{
				TextBlock text = new TextBlock
				{
					Text = line.Text,
					Foreground = (isSynced ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(125, 125, 125)) : System.Windows.Media.Brushes.White),
					FontSize = 28.0,
					FontWeight = FontWeights.SemiBold,
					TextWrapping = TextWrapping.Wrap,
					TextAlignment = TextAlignment.Center,
					HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
					MaxWidth = 1040.0,
					RenderTransformOrigin = new System.Windows.Point(0.5, 0.5)
				};
				line.TextBlock = text;
				childContent = text;
				ScaleTransform scale2 = (ScaleTransform)(text.RenderTransform = new ScaleTransform(1.0, 1.0));
				line.ScaleTransform = scale2;
			}
			Border container = new Border
			{
				Padding = new Thickness(22.0, 14.0, 22.0, 14.0),
				Margin = new Thickness(0.0, 3.0, 0.0, 3.0),
				CornerRadius = new CornerRadius(10.0),
				Cursor = (isSynced ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow),
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
				Child = childContent,
				Tag = line
			};
			if (isSynced)
			{
				container.MouseEnter += delegate
				{
					if (_lyricLines.IndexOf(line) != _currentLyricIndex)
					{
						if (line.TextBlock != null)
						{
							line.TextBlock.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(210, 210, 210));
						}
						else if (line.Syllables != null)
						{
							foreach (LyricSyllable current in line.Syllables)
							{
								if (current.TextBlock != null)
								{
									current.TextBlock.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(210, 210, 210));
								}
							}
						}
					}
				};
				container.MouseLeave += delegate
				{
					if (_lyricLines.IndexOf(line) != _currentLyricIndex)
					{
						if (line.TextBlock != null)
						{
							line.TextBlock.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(125, 125, 125));
						}
						else if (line.Syllables != null)
						{
							foreach (LyricSyllable current in line.Syllables)
							{
								if (current.TextBlock != null)
								{
									current.TextBlock.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(125, 125, 125));
								}
							}
						}
					}
				};
				container.MouseLeftButtonDown += delegate
				{
					if (!string.IsNullOrEmpty(_currentVideoId))
					{
						_player.Time = line.TimeMs;
						_isLyricsUserScrolled = false;
						UpdateLyricsForTime(line.TimeMs, forceScroll: true);
						if (MainPlayerBarControl.TimelineSliderRef.Maximum < (double)line.TimeMs)
						{
							MainPlayerBarControl.TimelineSliderRef.Maximum = line.TimeMs;
						}
						MainPlayerBarControl.TimelineSliderRef.BeginAnimation(RangeBase.ValueProperty, null);
						MainPlayerBarControl.TimelineSliderRef.Value = line.TimeMs;
						UpdateLyricsForTime(line.TimeMs, forceScroll: true);
					}
				};
			}
			line.Container = container;
			ContentPanel.Children.Add(container);
		}
		ContentPanel.Children.Add(new Border
		{
			Height = 260.0,
			Background = System.Windows.Media.Brushes.Transparent
		});
	}

	private void UpdateUnsyncedLyricsOpacity()
	{
		// Removed fading away effect on top and bottom for unsynced lyrics
	}

	private long _lastActualTime = -1;
	private DateTime _lastActualTimeReceived = DateTime.UtcNow;

	private void UpdateLyricsForTime(long timeMs, bool forceScroll = false, bool immediateScroll = false)
	{
		if (!_isLyricsViewOpen || _isLyricsLoading || _lyricLines.Count == 0 || !_lyricsAreSynced)
		{
			return;
		}

		long actualTime = timeMs;
		if (actualTime != _lastActualTime)
		{
			_lastActualTime = actualTime;
			_lastActualTimeReceived = DateTime.UtcNow;
		}

		long effectiveTimeMs;
		if (_player.IsPlaying && !forceScroll)
		{
			double elapsed = (DateTime.UtcNow - _lastActualTimeReceived).TotalMilliseconds;
			effectiveTimeMs = actualTime + (long)elapsed - _lyricsOffsetMs;
		}
		else
		{
			effectiveTimeMs = actualTime - _lyricsOffsetMs;
		}

		int index = -1;
		for (int i = 0; i < _lyricLines.Count && _lyricLines[i].TimeMs <= effectiveTimeMs; i++)
		{
			index = i;
		}

		if (_isLyricsUserScrolled && !isAnimating && index >= 0 && index < _lyricLines.Count)
		{
			LyricLine currentLine = _lyricLines[index];
			if (currentLine.Container != null && currentLine.Container.IsLoaded)
			{
				try
				{
					double target = currentLine.Container.TransformToAncestor(ContentPanel).Transform(new System.Windows.Point(0.0, 0.0)).Y - MainScrollViewer.ViewportHeight / 2.0 + currentLine.Container.ActualHeight / 2.0;
					target = Math.Max(0.0, Math.Min(MainScrollViewer.ScrollableHeight, target));
					if (Math.Abs(MainScrollViewer.VerticalOffset - target) < 150.0)
					{
						_isLyricsUserScrolled = false;
						forceScroll = true;
					}
				}
				catch { }
			}
		}
		if (!forceScroll && index == _currentLyricIndex)
		{
			if (index >= 0 && index < _lyricLines.Count)
			{
				if (_lyricLines[index].Syllables != null)
				{
					UpdateSyllablesForTime(_lyricLines[index], timeMs);
				}
				UpdateGlowEffectForTime(index, effectiveTimeMs);
			}
			return;
		}
		if (_currentLyricIndex >= 0 && _currentLyricIndex < _lyricLines.Count)
		{
			ApplyLyricLineStyle(_lyricLines[_currentLyricIndex], isCurrent: false);
		}
		_currentLyricIndex = index;
		if (_currentLyricIndex >= 0 && _currentLyricIndex < _lyricLines.Count)
		{
			LyricLine currentLine = _lyricLines[_currentLyricIndex];
			ApplyLyricLineStyle(currentLine, isCurrent: true);
			if (currentLine.Syllables != null)
			{
				UpdateSyllablesForTime(currentLine, timeMs);
			}
			UpdateGlowEffectForTime(_currentLyricIndex, effectiveTimeMs);
			CenterLyricLine(currentLine, immediateScroll);
		}
		else if (_currentLyricIndex < 0 && _lyricLines.Count > 0)
		{
			CenterLyricLine(_lyricLines[0], immediateScroll);
		}
	}

	private double GetLyricLineDuration(int index)
	{
		if (index < 0 || index >= _lyricLines.Count)
		{
			return 5000.0;
		}
		LyricLine line = _lyricLines[index];
		if (line.Syllables != null && line.Syllables.Count > 0)
		{
			long lastSyllableEnd = line.Syllables[^1].TimeMs + line.Syllables[^1].DurationMs;
			long duration = lastSyllableEnd - line.TimeMs;
			if (duration > 0)
			{
				return duration;
			}
		}
		if (index + 1 < _lyricLines.Count)
		{
			long diff = _lyricLines[index + 1].TimeMs - line.TimeMs;
			if (diff > 8000)
			{
				return 5000.0;
			}
			return diff;
		}
		long totalLength = _player.Length;
		if (totalLength > line.TimeMs)
		{
			long diff = totalLength - line.TimeMs;
			if (diff > 8000)
			{
				return 5000.0;
			}
			return diff;
		}
		return 5000.0;
	}

	private void UpdateGlowEffectForTime(int index, long effectiveTimeMs)
	{
		if (index < 0 || index >= _lyricLines.Count)
		{
			return;
		}
		LyricLine line = _lyricLines[index];
		if (line.Container == null)
		{
			return;
		}
		double duration = GetLyricLineDuration(index);
		if (duration <= 0)
		{
			return;
		}
		double progress = (double)(effectiveTimeMs - line.TimeMs) / duration;
		double intensity = 0.0;
		if (progress >= 0.0 && progress <= 1.0)
		{
			if (progress < 0.35)
			{
				intensity = Math.Sin((progress / 0.35) * (Math.PI / 2.0));
			}
			else if (progress < 0.80)
			{
				intensity = 1.0;
			}
			else
			{
				intensity = Math.Cos(((progress - 0.80) / 0.20) * (Math.PI / 2.0));
			}
		}
		if (line.Container.Effect is DropShadowEffect dropShadow)
		{
			dropShadow.BlurRadius = intensity * 50.0;
			dropShadow.Opacity = intensity * 0.85;
		}
	}

	private LinearGradientBrush GetCurrentLyricBrush()
	{
		LinearGradientBrush brush = new LinearGradientBrush
		{
			StartPoint = new System.Windows.Point(0.0, 0.0),
			EndPoint = new System.Windows.Point(1.0, 0.0)
		};
		try
		{
			if (System.Windows.Application.Current != null && System.Windows.Application.Current.MainWindow != null)
			{
				if (System.Windows.Application.Current.MainWindow.Resources["AccentGradient"] is LinearGradientBrush accentBrush && accentBrush.GradientStops.Count > 1)
				{
					System.Windows.Media.Color c1 = accentBrush.GradientStops[0].Color;
					System.Windows.Media.Color c2 = accentBrush.GradientStops[1].Color;
					
					byte r1 = (byte)((c1.R + 255 * 2) / 3);
					byte g1 = (byte)((c1.G + 255 * 2) / 3);
					byte b1 = (byte)((c1.B + 255 * 2) / 3);

					byte r2 = (byte)((c2.R + 255 * 2) / 3);
					byte g2 = (byte)((c2.G + 255 * 2) / 3);
					byte b2 = (byte)((c2.B + 255 * 2) / 3);

					brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(r1, g1, b1), 0.0));
					brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(r2, g2, b2), 1.0));
					return brush;
				}
			}
		}
		catch { }
		brush.GradientStops.Add(new GradientStop(Colors.White, 0.0));
		brush.GradientStops.Add(new GradientStop(Colors.White, 1.0));
		return brush;
	}

	private void UpdateSyllablesForTime(LyricLine line, long timeMs)
	{
		if (line.Syllables == null)
		{
			return;
		}
		long effectiveTimeMs = timeMs - _lyricsOffsetMs;
		long lookaheadMs = 200L;
		foreach (LyricSyllable syl in line.Syllables)
		{
			if (syl.TextBlock == null)
			{
				continue;
			}
			long durationMs = ((syl.DurationMs > 0) ? syl.DurationMs : 200);
			long elapsedMs = effectiveTimeMs - syl.TimeMs;
			if (elapsedMs < -lookaheadMs)
			{
				if (syl.IsActive)
				{
					syl.IsActive = false;
					syl.SweepWhiteStop = null;
					syl.SweepGrayStop = null;
					syl.TextBlock.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(125, 125, 125));
				}
			}
			else if (elapsedMs >= durationMs)
			{
				if (syl.SweepWhiteStop != null || !syl.IsActive)
				{
					syl.IsActive = true;
					syl.SweepWhiteStop = null;
					syl.SweepGrayStop = null;
					syl.TextBlock.Foreground = GetCurrentLyricBrush();
				}
			}
			else
			{
				if (syl.IsActive && syl.SweepWhiteStop == null)
				{
					syl.IsActive = false;
				}
				if (!syl.IsActive)
			{
				syl.IsActive = true;
				LinearGradientBrush activeBrush = GetCurrentLyricBrush();
				System.Windows.Media.Color color1 = activeBrush.GradientStops[0].Color;
				System.Windows.Media.Color color2 = activeBrush.GradientStops[1].Color;
				
				double startProgress = ((elapsedMs > 0) ? Math.Min(1.0, (double)elapsedMs / (double)durationMs) : 0.0);
				long totalAnimMs = durationMs - Math.Max(0L, elapsedMs);
				long delayMs = ((elapsedMs < 0) ? (-elapsedMs) : 0);
				GradientStop whiteStop = new GradientStop(color2, startProgress);
				GradientStop grayStop = new GradientStop(System.Windows.Media.Color.FromRgb(125, 125, 125), startProgress);
				LinearGradientBrush brush = new LinearGradientBrush
				{
					StartPoint = new System.Windows.Point(0.0, 0.0),
					EndPoint = new System.Windows.Point(1.0, 0.0)
				};
				brush.GradientStops.Add(new GradientStop(color1, 0.0));
				brush.GradientStops.Add(whiteStop);
				brush.GradientStops.Add(grayStop);
				brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(125, 125, 125), 1.0));
				syl.SweepWhiteStop = whiteStop;
				syl.SweepGrayStop = grayStop;
				syl.TextBlock.Foreground = brush;
				DoubleAnimation sweepAnim = new DoubleAnimation
				{
					From = startProgress,
					To = 1.0,
					Duration = TimeSpan.FromMilliseconds(Math.Max(16L, totalAnimMs)),
					BeginTime = TimeSpan.FromMilliseconds(delayMs)
				};
				whiteStop.BeginAnimation(GradientStop.OffsetProperty, sweepAnim);
				grayStop.BeginAnimation(GradientStop.OffsetProperty, sweepAnim);
			}
			}
		}
	}

	private void FreezeSyllableAnimations()
	{
		if (_currentLyricIndex < 0 || _currentLyricIndex >= _lyricLines.Count)
		{
			return;
		}
		LyricLine line = _lyricLines[_currentLyricIndex];
		if (line.Syllables == null)
		{
			return;
		}
		foreach (LyricSyllable syl in line.Syllables)
		{
			if (syl.IsActive && syl.SweepWhiteStop != null)
			{
				double currentOffset = syl.SweepWhiteStop.Offset;
				syl.SweepWhiteStop.BeginAnimation(GradientStop.OffsetProperty, null);
				syl.SweepGrayStop?.BeginAnimation(GradientStop.OffsetProperty, null);
				syl.SweepWhiteStop.Offset = currentOffset;
				if (syl.SweepGrayStop != null)
				{
					syl.SweepGrayStop.Offset = currentOffset;
				}
			}
		}
	}

	private void ResumeSyllableAnimations(long timeMs)
	{
		if (_currentLyricIndex < 0 || _currentLyricIndex >= _lyricLines.Count)
		{
			return;
		}
		LyricLine line = _lyricLines[_currentLyricIndex];
		if (line.Syllables == null)
		{
			return;
		}
		long effectiveTimeMs = timeMs - _lyricsOffsetMs;
		foreach (LyricSyllable syl in line.Syllables)
		{
			if (syl.IsActive && syl.SweepWhiteStop != null)
			{
				long durationMs = ((syl.DurationMs > 0) ? syl.DurationMs : 200);
				long elapsedMs = effectiveTimeMs - syl.TimeMs;
				if (elapsedMs >= 0 && elapsedMs < durationMs)
				{
					double progress = Math.Max(0.0, Math.Min(1.0, (double)elapsedMs / (double)durationMs));
					long remainingMs = Math.Max(16L, durationMs - elapsedMs);
					DoubleAnimation sweepAnim = new DoubleAnimation
					{
						From = progress,
						To = 1.0,
						Duration = TimeSpan.FromMilliseconds(remainingMs)
					};
					syl.SweepWhiteStop.BeginAnimation(GradientStop.OffsetProperty, sweepAnim);
					syl.SweepGrayStop?.BeginAnimation(GradientStop.OffsetProperty, sweepAnim);
				}
			}
		}
	}

	private void ApplyLyricLineStyle(LyricLine line, bool isCurrent)
	{
		if (line.Container == null)
		{
			return;
		}
		if (line.TextBlock != null)
		{
			line.TextBlock.Foreground = (isCurrent ? GetCurrentLyricBrush() : new SolidColorBrush(System.Windows.Media.Color.FromRgb(125, 125, 125)));
			line.TextBlock.FontWeight = FontWeights.SemiBold;
		}
		else if (line.Syllables != null)
		{
			foreach (LyricSyllable syl in line.Syllables)
			{
				if (syl.TextBlock != null)
				{
					syl.IsActive = false;
					syl.SweepWhiteStop = null;
					syl.SweepGrayStop = null;
					syl.TextBlock.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(125, 125, 125));
					syl.TextBlock.FontWeight = FontWeights.SemiBold;
				}
			}
		}
		if (!isCurrent)
		{
			line.Container.Effect = null;
		}
		else
		{
			System.Windows.Media.Color glowColor = Colors.White;
			try
			{
				if (System.Windows.Application.Current != null && System.Windows.Application.Current.MainWindow != null)
				{
					if (System.Windows.Application.Current.MainWindow.Resources["AccentGradient"] is LinearGradientBrush accentBrush && accentBrush.GradientStops.Count > 0)
					{
						glowColor = accentBrush.GradientStops[0].Color;
					}
				}
			}
			catch { }
			
			line.Container.Effect = new System.Windows.Media.Effects.DropShadowEffect
			{
				Color = glowColor,
				Direction = 0,
				ShadowDepth = 0,
				BlurRadius = 0,
				Opacity = 0
			};
		}
		line.Container.Background = System.Windows.Media.Brushes.Transparent;
		AnimateLyricScale(line, isCurrent ? 1.16 : 1.0);
	}

	private void AnimateLyricScale(LyricLine line, double targetScale)
	{
		if (line.ScaleTransform != null)
		{
			DoubleAnimation animation = new DoubleAnimation
			{
				To = targetScale,
				Duration = TimeSpan.FromMilliseconds(260.0),
				EasingFunction = new CubicEase
				{
					EasingMode = EasingMode.EaseOut
				}
			};
			line.ScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
			line.ScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
		}
	}

	private void CenterLyricLine(LyricLine line, bool immediateScroll = false)
	{
		if (line.Container == null || _isLyricsUserScrolled)
		{
			return;
		}
		if (!line.Container.IsLoaded)
		{
			RoutedEventHandler? loadedHandler = null;
			loadedHandler = (s, e) =>
			{
				line.Container.Loaded -= loadedHandler;
				CenterLyricLine(line, immediateScroll);
			};
			line.Container.Loaded += loadedHandler;
			return;
		}
		try
		{
			double target = line.Container.TransformToAncestor(ContentPanel).Transform(new System.Windows.Point(0.0, 0.0)).Y - MainScrollViewer.ViewportHeight / 2.0 + line.Container.ActualHeight / 2.0;
			target = Math.Max(0.0, Math.Min(MainScrollViewer.ScrollableHeight, target));
			if (immediateScroll)
			{
				targetScrollY = target;
				currentScrollY = target;
				MainScrollViewer.ScrollToVerticalOffset(target);
			}
			else
			{
				SmoothScrollTo(target);
			}
		}
		catch
		{
		}
	}

	private void UpdateLyricsOffsetUI()
	{
		if (_lyricLines.Count > 0 && _lyricsAreSynced)
		{
			SetLyricsOffsetPanelVisibility(visible: true);
			string newText = ((_lyricsOffsetMs > 0) ? "+" : "") + ((double)_lyricsOffsetMs / 1000.0).ToString("0.0") + "s";
			if (MainTopbarControl.LyricsOffsetValueTextRef.Text != newText)
			{
				MainTopbarControl.LyricsOffsetValueTextRef.Text = newText;
				if (MainTopbarControl.LyricsOffsetValueTextRef.RenderTransform is ScaleTransform scale)
				{
					DoubleAnimation anim = new DoubleAnimation(1.4, 1.0, TimeSpan.FromMilliseconds(250.0))
					{
						EasingFunction = new QuadraticEase
						{
							EasingMode = EasingMode.EaseOut
						}
					};
					scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
					scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
				}
			}
		}
		else
		{
			SetLyricsOffsetPanelVisibility(visible: false);
		}
	}

	private void SetLyricsOffsetPanelVisibility(bool visible)
	{
		if (visible && MainTopbarControl.LyricsOffsetPanelRef.Visibility != Visibility.Visible)
		{
			MainTopbarControl.LyricsOffsetPanelRef.Opacity = 0.0;
			MainTopbarControl.LyricsOffsetPanelRef.Visibility = Visibility.Visible;
			DoubleAnimation fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200.0));
			MainTopbarControl.LyricsOffsetPanelRef.BeginAnimation(UIElement.OpacityProperty, fadeIn);
		}
		else
		{
			if (visible || MainTopbarControl.LyricsOffsetPanelRef.Visibility != Visibility.Visible)
			{
				return;
			}
			DoubleAnimation fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(200.0));
			fadeOut.Completed += delegate
			{
				if (MainTopbarControl.LyricsOffsetPanelRef.Opacity == 0.0)
				{
					MainTopbarControl.LyricsOffsetPanelRef.Visibility = Visibility.Collapsed;
				}
			};
			MainTopbarControl.LyricsOffsetPanelRef.BeginAnimation(UIElement.OpacityProperty, fadeOut);
		}
	}

	private void LyricsOffsetMinusBtn_Click(object sender, MouseButtonEventArgs e)
	{
		_lyricsOffsetMs -= 500;
		UpdateLyricsOffsetUI();
		if (_player.IsPlaying)
		{
			UpdateLyricsForTime(_player.Time, forceScroll: true);
		}
	}

	private void LyricsOffsetPlusBtn_Click(object sender, MouseButtonEventArgs e)
	{
		_lyricsOffsetMs += 500;
		UpdateLyricsOffsetUI();
		if (_player.IsPlaying)
		{
			UpdateLyricsForTime(_player.Time, forceScroll: true);
		}
	}
}
