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
	private async Task ExpandQueueWithAutoplayAsync()
	{
		System.Text.Json.Nodes.JsonArray targetQueue = _currentQueue;
		if (targetQueue == null || targetQueue.Count == 0)
		{
			return;
		}
		await _autoplaySemaphore.WaitAsync();
		try
		{
			if (targetQueue != _currentQueue || _currentQueue.Count - _currentQueueIndex > Math.Max(_bufferSize, 25))
			{
				return;
			}
			string lastVideoId = ((string?)_currentQueue[_currentQueue.Count - 1]["videoId"]) ?? "";
			if (string.IsNullOrEmpty(lastVideoId))
			{
				return;
			}
			System.Text.Json.Nodes.JsonObject nextData = await BackendService.Instance.FetchAutoplayNextAsync(lastVideoId, CancellationToken.None);
			if (targetQueue != _currentQueue || !(nextData["data"]?["tracks"] is System.Text.Json.Nodes.JsonArray items))
			{
				return;
			}
			HashSet<string> existingIds = new HashSet<string>();
			foreach (System.Text.Json.Nodes.JsonNode qItem in _currentQueue)
			{
				existingIds.Add(((string?)qItem["videoId"]) ?? "");
			}
			List<System.Text.Json.Nodes.JsonNode> newItems = new List<System.Text.Json.Nodes.JsonNode>();
			foreach (System.Text.Json.Nodes.JsonNode item in items)
			{
				string vid = ((string?)item["videoId"]) ?? "";
				if (!string.IsNullOrEmpty(vid) && !existingIds.Contains(vid))
				{
					newItems.Add(item);
				}
			}
			foreach (System.Text.Json.Nodes.JsonNode item2 in newItems)
			{
				_currentQueue.Add(item2?.DeepClone());
			}
		}
		finally
		{
			_autoplaySemaphore.Release();
		}
	}

	private async void PlayNextInQueue(bool useCrossfade = false, bool isCrossfadeTrigger = false)
	{
		if (string.IsNullOrEmpty(_currentVideoId))
		{
			return;
		}
		if (_isRepeatOn && !string.IsNullOrEmpty(_currentStreamUrl))
		{
			_crossfadeTriggeredForCurrentTrack = false;
			_player.Play(_currentStreamUrl, useCrossfade, _currentVideoId != null && _currentVideoId.StartsWith("radio:"));
			return;
		}
		if (_currentQueue != null && _currentQueue.Count > 0)
		{
			int nextQueueIndex = GetNextQueueIndex();
			if (nextQueueIndex == -1)
			{
				_isTrackLoading = true;
				PlayerBarViewModel vmTime = App.Current.PlayerBarViewModel;
				if (vmTime != null)
				{
					vmTime.CurrentTimeText = "Loading...";
				}
				try
				{
					await ExpandQueueWithAutoplayAsync();
				}
				catch
				{
				}
				nextQueueIndex = GetNextQueueIndex();
				_isTrackLoading = false;
			}
			if (nextQueueIndex != -1)
			{
				_currentQueueIndex = nextQueueIndex;
				System.Text.Json.Nodes.JsonNode nextItem = _currentQueue[_currentQueueIndex];
				string nId = ((string?)nextItem["videoId"]) ?? "";
				string nTitle = ((string?)nextItem["title"]) ?? "Next Song";
				string nArtist = "";
				if (nextItem["artist"] != null && nextItem["artist"] is System.Text.Json.Nodes.JsonValue)
				{
					nArtist = ((string?)nextItem["artist"]) ?? "";
				}
				else if (nextItem["artists"] is System.Text.Json.Nodes.JsonArray artistsToken)
				{
					List<string> names = new List<string>();
					foreach (System.Text.Json.Nodes.JsonNode a in artistsToken)
					{
						names.Add(((string?)a["name"]) ?? "");
					}
					nArtist = string.Join(", ", names);
				}
				string nThumb = "";
				if (nextItem["thumbUrl"] != null && nextItem["thumbUrl"] is System.Text.Json.Nodes.JsonValue)
				{
					nThumb = ((string?)nextItem["thumbUrl"]) ?? "";
				}
				else
				{
					System.Text.Json.Nodes.JsonArray thumbs = nextItem["thumbnails"] as System.Text.Json.Nodes.JsonArray;
					if (thumbs == null || thumbs.Count == 0)
					{
						thumbs = nextItem["thumbnail"] as System.Text.Json.Nodes.JsonArray;
						if (thumbs == null || thumbs.Count == 0)
						{
							System.Text.Json.Nodes.JsonNode thumbObj = nextItem["thumbnail"];
							if (thumbObj != null && thumbObj is System.Text.Json.Nodes.JsonObject)
							{
								thumbs = thumbObj["thumbnails"] as System.Text.Json.Nodes.JsonArray;
							}
						}
					}
					if (thumbs != null && thumbs.Count > 0)
					{
						nThumb = ((string?)thumbs[thumbs.Count - 1]["url"]) ?? "";
					}
				}
				if (string.IsNullOrEmpty(nThumb) && !string.IsNullOrEmpty(_currentThumbUrl))
				{
					nThumb = _currentThumbUrl;
				}
				await PlayTrack(nId, nTitle, nArtist, nThumb, addToHistory: true, _pauseRequested, useCrossfade, 1);
				return;
			}
		}
		if (isCrossfadeTrigger)
		{
			return;
		}
		_ = _ = _ = _ = Task.Run(delegate
		{
			try
			{
				_player.Stop();
			}
			catch
			{
			}
		});
	}

	private void ShuffleBtn_Click(object sender, RoutedEventArgs e)
	{
		_isShuffleOn = !_isShuffleOn;
		UpdateShuffleIcon();
		if (_isShuffleOn)
		{
			ShuffleRemainingQueue();
		}
		else
		{
			UnshuffleRemainingQueue();
		}
		if (_isQueueOpen)
		{
			RenderQueueSidebar();
		}
	}

	private void UnshuffleRemainingQueue()
	{
		_queueService.UnshuffleRemainingQueue();
		_currentQueue = _queueService.CurrentQueue;
		_currentQueueIndex = _queueService.CurrentQueueIndex;
	}

	private void InitQueueAndShuffle(System.Text.Json.Nodes.JsonArray newQueue, int newIndex)
	{
		_queueService.InitQueueAndShuffle(newQueue, newIndex);
		_currentQueue = _queueService.CurrentQueue;
		_currentQueueIndex = _queueService.CurrentQueueIndex;
		_originalQueueSize = _queueService.OriginalQueueSize;
	}

	private void ShuffleRemainingQueue()
	{
		_queueService.ShuffleRemainingQueue();
		_currentQueue = _queueService.CurrentQueue;
		_currentQueueIndex = _queueService.CurrentQueueIndex;
	}

	private int GetNextQueueIndex()
	{
		return _queueService.GetNextQueueIndex();
	}

	private int FindQueueIndexByVideoId(string videoId)
	{
		return _queueService.FindQueueIndexByVideoId(videoId);
	}

	private void QueueBtn_Click(object sender, RoutedEventArgs e)
	{
		_isQueueOpen = !_isQueueOpen;
		if (_isQueueOpen)
		{
			MainPlayerBarControl.QueueIconOffRef.Visibility = Visibility.Visible;
			MainPlayerBarControl.QueueIconOnRef.Visibility = Visibility.Visible;
		}
		else
		{
			MainPlayerBarControl.QueueIconOffRef.Visibility = Visibility.Visible;
			MainPlayerBarControl.QueueIconOnRef.Visibility = Visibility.Collapsed;
		}
		MainScrollViewer.CacheMode = new System.Windows.Media.BitmapCache { EnableClearType = false, RenderAtScale = 1.0 };
		MainScrollViewer.Width = MainScrollViewer.ActualWidth;
		MainScrollViewer.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
		MainTopbarControl.Width = MainTopbarControl.ActualWidth;
		MainTopbarControl.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
		MainSearchOverlay.Width = MainSearchOverlay.ActualWidth;
		MainSearchOverlay.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
		MainQueueControl.Width = 350;
		MainQueueControl.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
		DoubleAnimation fadeOutMain = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120.0));
		MainScrollViewer.BeginAnimation(UIElement.OpacityProperty, fadeOutMain);
		_queueSidebarTargetWidth = (_isQueueOpen ? 350 : 0);
		_queueSidebarStartWidth = QueueSidebarColumn.Width.Value;
		_queueAnimationStartTime = DateTime.Now;
		if (_isQueueOpen)
		{
			RenderQueueSidebar();
			MainQueueControl.QueueSidebarBorderRef.Visibility = Visibility.Visible;
		}
		else
		{
			_queueRenderGeneration++;
		}
		if (!_isQueueSidebarAnimating)
		{
			_isQueueSidebarAnimating = true;
			CompositionTarget.Rendering += QueueSidebarAnimationLoop;
		}
	}

	private void QueueSidebarAnimationLoop(object? sender, EventArgs e)
	{
		double elapsed = (DateTime.Now - _queueAnimationStartTime).TotalMilliseconds;
		double durationMs = 300.0;
		if (elapsed >= durationMs)
		{
			CompositionTarget.Rendering -= QueueSidebarAnimationLoop;
			_isQueueSidebarAnimating = false;
			QueueSidebarColumn.Width = new GridLength(_queueSidebarTargetWidth);
			if (!_isQueueOpen)
			{
				MainQueueControl.QueueSidebarBorderRef.Visibility = Visibility.Collapsed;
			}
			else
			{
				RenderQueueSidebar();
			}
			MainQueueControl.Width = double.NaN;
			MainQueueControl.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
			MainTopbarControl.Width = double.NaN;
			MainTopbarControl.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
			MainSearchOverlay.Width = double.NaN;
			MainSearchOverlay.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
			MainScrollViewer.Width = double.NaN;
			MainScrollViewer.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
			DoubleAnimation fadeInMain = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200.0));
			fadeInMain.Completed += delegate
			{
				if (double.IsNaN(MainScrollViewer.Width))
				{
					MainScrollViewer.CacheMode = null;
				}
			};
			MainScrollViewer.BeginAnimation(UIElement.OpacityProperty, fadeInMain);
		}
		else
		{
			double progress = elapsed / durationMs;
			progress = 1.0 - Math.Pow(1.0 - progress, 3.0);
			QueueSidebarColumn.Width = new GridLength(_queueSidebarStartWidth + (_queueSidebarTargetWidth - _queueSidebarStartWidth) * progress);
		}
	}

	private ScrollViewer? GetQueueScrollViewer()
	{
		if (_queueScrollViewerCache != null)
		{
			return _queueScrollViewerCache;
		}
		if (VisualTreeHelper.GetChildrenCount(MainQueueControl.QueueSidebarPanelRef) > 0 && VisualTreeHelper.GetChild(MainQueueControl.QueueSidebarPanelRef, 0) is Decorator border)
		{
			_queueScrollViewerCache = border.Child as ScrollViewer;
		}
		return _queueScrollViewerCache;
	}

	private void QueueScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		ScrollViewer? sv = GetQueueScrollViewer();
		if (sv == null)
		{
			return;
		}
		e.Handled = true;
		double baseScrollY = ((!_queueIsScrolling || !(Math.Abs(_queueTargetScrollY - _queueCurrentScrollY) > 1.0) || Math.Sign(_queueTargetScrollY - _queueCurrentScrollY) == Math.Sign(-e.Delta)) ? (_queueIsScrolling ? _queueTargetScrollY : sv.VerticalOffset) : sv.VerticalOffset);
		double targetOffset = Math.Max(0.0, baseScrollY - (double)e.Delta * 0.85);
		if (_disableSmoothScrolling)
		{
			sv.ScrollToVerticalOffset(Math.Max(0.0, Math.Min(sv.ScrollableHeight, targetOffset)));
			_queueCurrentScrollY = targetOffset;
			_queueTargetScrollY = targetOffset;
			return;
		}
		
		double diff = Math.Abs(targetOffset - _queueCurrentScrollY);
		if (diff > 150.0)
		{
			_queueUseOldScrollMethod = true;
		}
		else if (!_queueIsScrolling)
		{
			_queueUseOldScrollMethod = false;
		}
		
		_queueTargetScrollY = targetOffset;
		if (!_queueIsScrolling)
		{
			_queueCurrentScrollY = sv.VerticalOffset;
			_queueIsScrolling = true;
			_queueLastScrollTick = 0;
			_queueScrollVelocity = 0.0;
			CompositionTarget.Rendering += QueueScrollAnimationLoop;
		}
	}

	private void QueueScrollAnimationLoop(object? sender, EventArgs e)
	{
		ScrollViewer sv = GetQueueScrollViewer();
		if (sv == null)
		{
			return;
		}
		long now = Stopwatch.GetTimestamp();
		double dt;
		if (_queueLastScrollTick == 0)
		{
			dt = 0.016;
		}
		else
		{
			dt = (double)(now - _queueLastScrollTick) / (double)Stopwatch.Frequency;
		}
		_queueLastScrollTick = now;
		
		if (dt <= 0.0)
		{
			dt = 0.016;
		}
		if (dt > 0.05)
		{
			dt = 0.05;
		}
		if (Math.Abs(sv.VerticalOffset - _queueCurrentScrollY) > 5.0)
		{
			_queueCurrentScrollY = sv.VerticalOffset;
			_queueTargetScrollY = _queueCurrentScrollY;
			_queueIsScrolling = false;
			CompositionTarget.Rendering -= QueueScrollAnimationLoop;
			return;
		}
		double clampedTarget = Math.Min(sv.ScrollableHeight, _queueTargetScrollY);
		double displacement = _queueCurrentScrollY - clampedTarget;

		if (Math.Abs(displacement) < 0.5 && (_queueUseOldScrollMethod || Math.Abs(_queueScrollVelocity) < 10.0))
		{
			_queueCurrentScrollY = clampedTarget;
			_queueScrollVelocity = 0.0;
			sv.ScrollToVerticalOffset(_queueCurrentScrollY);
			CompositionTarget.Rendering -= QueueScrollAnimationLoop;
			_queueIsScrolling = false;
		}
		else
		{
			if (_queueUseOldScrollMethod)
			{
				double factor = 1.0 - Math.Exp(-20.0 * dt);
				_queueCurrentScrollY += (clampedTarget - _queueCurrentScrollY) * factor;
			}
			else
			{
				double w = 25.0;
				double c1 = displacement;
				double c2 = _queueScrollVelocity + w * displacement;
				double expTerm = Math.Exp(-w * dt);
				
				double newDisplacement = (c1 + c2 * dt) * expTerm;
				_queueScrollVelocity = (c2 - w * (c1 + c2 * dt)) * expTerm;
				
				_queueCurrentScrollY = clampedTarget + newDisplacement;
			}
			sv.ScrollToVerticalOffset(_queueCurrentScrollY);
		}
	}

	private void RenderQueueSidebar()
	{
		int generation = ++_queueRenderGeneration;
		QueueViewModel queueVm = App.Current.QueueViewModel;
		if (queueVm == null)
		{
			return;
		}
		System.Text.Json.Nodes.JsonArray queue = _currentQueue;
		int currentQueueIndex = _currentQueueIndex;
		if (queue == null || queue.Count == 0)
		{
			queueVm.Items.Clear();
			return;
		}
		int startIdx = Math.Max(0, currentQueueIndex);
		int endIdx = Math.Min(queue.Count, startIdx + 50);
		List<QueueItemViewModel> newItems = new List<QueueItemViewModel>();
		for (int i = startIdx; i < endIdx; i++)
		{
			if (generation != _queueRenderGeneration)
			{
				return;
			}
			System.Text.Json.Nodes.JsonNode item = queue[i];
			string videoId = ((string?)item["videoId"]) ?? "";
			string title = ((string?)item["title"]) ?? "Unknown";
			string artist = "Unknown Artist";
			if (item["artist"] != null)
			{
				artist = ((string?)item["artist"]) ?? "Unknown Artist";
			}
			else if (item["artists"] != null && item["artists"] is System.Text.Json.Nodes.JsonArray artistsToken)
			{
				List<string> names = new List<string>();
				foreach (System.Text.Json.Nodes.JsonNode a in artistsToken)
				{
					names.Add(((string?)a["name"]) ?? "");
				}
				artist = string.Join(", ", names);
			}
			string thumbUrl = "";
			if (item["thumbUrl"] != null && item["thumbUrl"] is System.Text.Json.Nodes.JsonValue)
			{
				thumbUrl = ((string?)item["thumbUrl"]) ?? "";
			}
			else if (item["thumbnails"] != null && item["thumbnails"] is System.Text.Json.Nodes.JsonArray { Count: >0 } thumbsToken)
			{
				thumbUrl = ((string?)thumbsToken[thumbsToken.Count - 1]["url"]) ?? "";
			}
			if (string.IsNullOrEmpty(thumbUrl))
			{
				System.Text.Json.Nodes.JsonNode thumbObj = item["thumbnail"];
				if (thumbObj != null && thumbObj is System.Text.Json.Nodes.JsonObject)
				{
					if (thumbObj["thumbnails"] is System.Text.Json.Nodes.JsonArray { Count: >0 } nested)
					{
						thumbUrl = ((string?)nested[nested.Count - 1]["url"]) ?? "";
					}
				}
				else if (thumbObj != null && thumbObj is System.Text.Json.Nodes.JsonArray { Count: >0 } nested2)
				{
					thumbUrl = ((string?)nested2[nested2.Count - 1]["url"]) ?? "";
				}
			}
			if (string.IsNullOrEmpty(thumbUrl) && !string.IsNullOrEmpty(_currentThumbUrl))
			{
				thumbUrl = _currentThumbUrl;
			}
			bool isPlaying = i == currentQueueIndex;
			string numberText = (isPlaying ? "▶" : (i - startIdx + 1).ToString());
			newItems.Add(new QueueItemViewModel
			{
				Index = i,
				Title = title,
				Artist = artist,
				ThumbnailUrl = thumbUrl,
				VideoId = videoId,
				IsPlaying = isPlaying,
				NumberText = numberText
			});
		}

		if (generation != _queueRenderGeneration)
		{
			return;
		}

		int targetCount = newItems.Count;
		for (int k = 0; k < targetCount; k++)
		{
			QueueItemViewModel desired = newItems[k];
			if (k < queueVm.Items.Count)
			{
				QueueItemViewModel existing = queueVm.Items[k];
				if (existing.VideoId == desired.VideoId &&
				    existing.Title == desired.Title &&
				    existing.Artist == desired.Artist &&
				    existing.ThumbnailUrl == desired.ThumbnailUrl &&
				    existing.Index == desired.Index &&
				    existing.IsPlaying == desired.IsPlaying &&
				    existing.NumberText == desired.NumberText)
				{
					continue;
				}

				existing.Index = desired.Index;
				existing.Title = desired.Title;
				existing.Artist = desired.Artist;
				if (existing.ThumbnailUrl != desired.ThumbnailUrl)
				{
					existing.ThumbnailUrl = desired.ThumbnailUrl;
				}
				existing.VideoId = desired.VideoId;
				existing.IsPlaying = desired.IsPlaying;
				existing.NumberText = desired.NumberText;
			}
			else
			{
				queueVm.Items.Add(desired);
			}
		}

		while (queueVm.Items.Count > targetCount)
		{
			queueVm.Items.RemoveAt(queueVm.Items.Count - 1);
		}
	}

	private void ResetShuffleState()
	{
		_isShuffleOn = false;
		UpdateShuffleIcon();
	}

	private void UpdateShuffleIcon()
	{
		MainPlayerBarControl.ShuffleIconOffRef.Visibility = Visibility.Visible;
		MainPlayerBarControl.ShuffleIconOnRef.Visibility = _isShuffleOn ? Visibility.Visible : Visibility.Collapsed;
	}
}
