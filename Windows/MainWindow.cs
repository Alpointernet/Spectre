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

namespace Spectre;

public partial class MainWindow : Window
{
	private sealed class LyricSyllable
	{
		public long TimeMs { get; init; }

		public long DurationMs { get; init; }

		public string Text { get; init; } = "";

		public TextBlock? TextBlock { get; set; }

		public bool IsActive { get; set; }

		public GradientStop? SweepWhiteStop { get; set; }

		public GradientStop? SweepGrayStop { get; set; }
	}

	private sealed class LyricLine
	{
		public long TimeMs { get; set; }

		public string Text { get; init; } = "";

		public List<LyricSyllable>? Syllables { get; set; }

		public Border? Container { get; set; }

		public TextBlock? TextBlock { get; set; }

		public WrapPanel? SyllablesPanel { get; set; }

		public ScaleTransform? ScaleTransform { get; set; }
	}

	public struct RECT
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	public struct STYLESTRUCT
	{
		public int styleOld;

		public int styleNew;
	}

	private sealed class PlaybackHistoryEntry
	{
		public string VideoId { get; init; } = "";

		public string Title { get; init; } = "";

		public string Artist { get; init; } = "";

		public string ThumbUrl { get; init; } = "";
	}

	public sealed class PlaybackStreamInfo
	{
		public string Url { get; init; } = "";

		public string QualityLabel { get; init; } = "unknown quality";

		public string Provider { get; init; } = "Native";

		public string TrackingUrl { get; init; } = "";

		public long DurationMs { get; init; }
	}

	private string _lyricsVideoId = "";

	private bool _lyricsAreSynced;

	private string _lyricsSource = "";

	private bool _isLyricsLoading;

	private string _lastLyricsWarmKey = "";

	private List<LyricLine> _lyricLines = new List<LyricLine>();

	private int _lyricsOffsetMs;

	private int _currentLyricIndex = -1;

	private Random _rand = new Random();

	private ConcurrentDictionary<string, Task<JsonObject>> _lyricsTasks = new ConcurrentDictionary<string, Task<JsonObject>>();

	private StackPanel _homeCachePanel = new StackPanel();

	private StackPanel _exploreCachePanel = new StackPanel();

	private Dictionary<string, StackPanel> _pageCache = new Dictionary<string, StackPanel>();

	private Dictionary<string, (List<Border> elements, List<Func<UIElement>> actions)> _pageVirtualizationCache = new Dictionary<string, (List<Border>, List<Func<UIElement>>)>();

	private string _currentPageId = "home";

	private Stack<(string Id, UIElement Element, double ScrollOffset, Action Reload, bool Loading)> _backHistory = new Stack<(string, UIElement, double, Action, bool)>();

	private Stack<(string Id, UIElement Element, double ScrollOffset, Action Reload, bool Loading)> _forwardHistory = new Stack<(string, UIElement, double, Action, bool)>();

	private Action _currentReloadAction;

	private List<string> _recentSearches = new List<string>();

	private List<string> _blockedCategories = new List<string>();

	private Dictionary<string, string> _hiddenLibraryItems = new Dictionary<string, string>();

	private int _transitionId;

	private bool _isLoadingContent;

	private TextBlock? _statsMinutesValueText;

	private Border? _statsMinutesBorder;

	private Border? _statsSecondsRevealBorder;

	private TextBlock? _statsSecondsValueText;

	private Border? _statsMinutesUnitRevealBorder;

	private TextBlock? _statsMinutesUnitText;

	private long _statsCachedTotalMs;

	private bool _statsMinutesHovered;

	private int _statsMinutesAnimationToken;

	private TextBlock? _statsSongsValueText;

	private TextBlock? _statsOwedValueText;

	private TextBlock? _statsArtistsValueText;

	private bool _crossfadeTriggeredForCurrentTrack;

	private bool _lastFmScrobbled;

	private long _accumulatedMs;

	private DateTime _lastPlayClickTime = DateTime.MinValue;

	private bool _isRepeatOn;

	private double _previousVolume = 100.0;

	private ScrollViewer? _queueScrollViewerCache;

	private long _queueLastScrollTick;

	private List<RadioStation> _savedRadios = new List<RadioStation>();

	private List<dynamic> _builtInThemes = new List<object>
	{
		new
		{
			Id = "theme_dark",
			Name = "Deep Dark (Default)",
			Type = "built-in",
			Background = "#0F0F13",
			Topbar = "#1A1829",
			Bottombar = "#0A0A0E",
			Accent = "#7000FF, #00E5FF",
			IsCustom = false
		},
		new
		{
			Id = "theme_midnight",
			Name = "Midnight Blue",
			Type = "built-in",
			Background = "#0B0C10",
			Topbar = "#1F2833",
			Bottombar = "#050608",
			Accent = "#45A29E, #66FCF1",
			IsCustom = false
		},
		new
		{
			Id = "theme_forest",
			Name = "Forest Night",
			Type = "built-in",
			Background = "#121A14",
			Topbar = "#1D2B22",
			Bottombar = "#0C120E",
			Accent = "#2ECC71, #27AE60",
			IsCustom = false
		},
		new
		{
			Id = "theme_sunset",
			Name = "Synthwave Sunset",
			Type = "built-in",
			Background = "#1A0B1C",
			Topbar = "#2D1B36",
			Bottombar = "#110714",
			Accent = "#FF2E93, #FF8C00",
			IsCustom = false
		},
		new
		{
			Id = "theme_dracula",
			Name = "Dracula",
			Type = "built-in",
			Background = "#282A36",
			Topbar = "#44475A",
			Bottombar = "#1E1F29",
			Accent = "#FF79C6, #BD93F9",
			IsCustom = false
		},
		new
		{
			Id = "theme_amoled",
			Name = "AMOLED Black",
			Type = "built-in",
			Background = "#000000",
			Topbar = "#050505",
			Bottombar = "#000000",
			Accent = "#FFFFFF, #AAAAAA",
			IsCustom = false
		}
	};

	private string _customFontPath = "";

	private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

	private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

	private const int DWMWA_CAPTION_COLOR = 35;

	private const int GWL_STYLE = -16;

	private const int WS_CAPTION = 12582912;

	private const int WS_THICKFRAME = 262144;

	private const int WS_SYSMENU = 524288;

	private const int WS_MINIMIZEBOX = 131072;

	private const int WS_MAXIMIZEBOX = 65536;

	private const int SWP_NOSIZE = 1;

	private const int SWP_NOMOVE = 2;

	private const int SWP_NOZORDER = 4;

	private const int SWP_FRAMECHANGED = 32;

	private const int WM_SYSCOMMAND = 274;

	private const int SC_CLOSE = 61536;

	private const int SC_MINIMIZE = 61472;

	private const int SC_MAXIMIZE = 61488;

	private const int SC_RESTORE = 61728;

	private static Dictionary<string, string> _artistThumbCache = new Dictionary<string, string>();

	public static readonly DependencyProperty IsScrolledProperty = DependencyProperty.Register("IsScrolled", typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

	private bool isAnimating;



	private bool _logVisible;

	private const int MaxLogEntries = 300;

	private AudioEngine _player;

	private readonly YoutubeClient _youtube;

	private CancellationTokenSource? _playbackCts;

	private CancellationTokenSource? _playlistLoadCts;

	private Windows.Media.Playback.MediaPlayer? _smtcPlayer;

	private SystemMediaTransportControls? _smtc;

	private string _currentVideoId = "";

	private string _currentTitle = "";

	private string _currentArtist = "";

	private string _currentAlbum = "";

	private string _currentAlbumId = "";

	private string _currentThumbUrl = "";

	private string _currentStreamUrl = "";

	private bool _isTrackLoading;

	private bool _pauseRequested;

	private JsonArray? _cachedPlaylists;

	private JsonArray? _cachedAlbums;

	private string _cachedLibraryError;

	private HashSet<string> _playedVideoIds = new HashSet<string>();

	private HashSet<string> _likedVideoIds = new HashSet<string>();

	private HashSet<string> _savedAlbumIds = new HashSet<string>();

	private bool _likedSongsLoaded;

	private Stack<PlaybackHistoryEntry> _playbackHistory = new Stack<PlaybackHistoryEntry>();

	private bool _isQueueOpen;

	private double _queueSidebarTargetWidth;

	private double _queueSidebarStartWidth;

	private DateTime _queueAnimationStartTime;

	private bool _isQueueSidebarAnimating;

	private double _queueCurrentScrollY;

	private double _queueTargetScrollY;

	private double _queueScrollVelocity;

	private bool _queueIsScrolling;

	private int _queueRenderGeneration;

	private bool _isWindowResizing;

	private ConcurrentDictionary<string, Task<PlaybackStreamInfo>> _preloadTasks = new ConcurrentDictionary<string, Task<PlaybackStreamInfo>>();

	private ConcurrentDictionary<string, bool> _inflightThumbnails = new ConcurrentDictionary<string, bool>();

	private SemaphoreSlim _thumbnailSemaphore = new SemaphoreSlim(3, 3);

	private Storyboard? _thumbnailStoryboard;

	private SemaphoreSlim _preloadSemaphore = new SemaphoreSlim(2, 2);

	private SemaphoreSlim _autoplaySemaphore = new SemaphoreSlim(1, 1);

	private int _historySize = 10;

	private int _playHistoryCount = 50;

	private int _bufferSize = 10;

	private int _homeFeedLimit = 30;

	private bool _prefetchEnabled = true;

	private bool _loudnessNormalization;

	private bool _reduceAnimations;

	private bool _disableSmoothScrolling;

	private bool _enableSMTC = true;

	private bool _alwaysOnTop;

	private int _networkCacheMs = 1500;

	private bool _disableGPU;

	private int _volumeStep = 5;

	private bool _enableLocalMusic;

	private bool _groupLibraryTabs = true;

	private string _LocalMusicPath = "";

	private bool _enableDownloads = true;

	private bool _enableConfetti;

	public bool EnableVinylMode { get; set; }
	public bool EnableWigglyProgress { get; set; }
	public bool EnableBackgroundParticles { get; set; }

	private string _downloadsPath = "";

	private int _crossfadeMs;

	private bool _excludePlainVideoResults = true;

	private bool _enableDiscordRpc;

	private bool _enableStatusIndicator;

	private bool _showTaskbarMediaControls = true;

	private TaskbarItemInfo _taskbarItemInfoStore;

	private readonly DispatcherTimer _scrollWorkTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(140.0)
	};

	private bool _lastMainScrollIsScrolled;

	private double _lastTopbarOpacity = -1.0;

	private long _lastUnsyncedLyricsOpacityTick;

	private string _discordClientId = "1507766775104671996";

	private string _discordIconUrl = "https://files.catbox.moe/0wf35j.png";

	private DiscordManager? _discordManager;

	private string _accentColor1 = "#00E5FF";

	private string _accentColor2 = "#7000FF";

	private string _bgGrad1 = "#1A1829";

	private string _bgGrad2 = "#0D0D12";

	private double _themeBrightness = 1.0;

	private string _bgGrad3 = "#0A0A0E";

	private string _cardBg = "#0CFFFFFF";

	private string _sidebarBg = "#D0040406";

	private string _topbarBg = "#B01A1829";

	private string _bottombarBg = "#E00A0A0E";

	private bool _enableHoverBorders;

	private bool _useAdaptiveTheme;

	private readonly IQueueService _queueService;

	private bool _isSidebarMinimized;

	private double _expandedSidebarWidth = 240.0;

	private double _minimizedSidebarWidth = 64.0;

	private double targetScrollY;

	private double currentScrollY;

	private double _mainScrollVelocity;

	private List<Action<bool>> _lazyRenderActions = new List<Action<bool>>();

	private List<FrameworkElement> _lazyRenderElements = new List<FrameworkElement>();

	private List<bool> _lazyRenderStates = new List<bool>();

	private List<Func<UIElement>> _lazyVirtualizationActions = new List<Func<UIElement>>();

	private List<Border> _lazyVirtualizationElements = new List<Border>();

	private List<JsonNode> _allHeroCandidates = new List<JsonNode>();

	private bool _isVirtualizationQueued;

	private long _lastScrollTick;

	private StackPanel _radioCachePanel = new StackPanel
	{
		Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
	};

	private Grid? _addRadioPopup;

	private ScrollViewer? _playlistsCachePanel;

	private ScrollViewer? _albumsCachePanel;

	private Dictionary<string, BitmapImage> _imageCache = new Dictionary<string, BitmapImage>();

	private bool _isCreditsAnimating;

	private double _creditsTargetScrollY;

	private double _creditsCurrentScrollY;

	private double _creditsScrollVelocity;

	private long _creditsLastScrollTick;

	private bool _mainUseOldScrollMethod;
	
	private double _mainBaseScrollY;
	
	private bool _queueUseOldScrollMethod;
	
	private bool _creditsUseOldScrollMethod;

	private bool _isLyricsUserScrolled;

	private System.Windows.Media.Color _hoverBorderColor = System.Windows.Media.Color.FromArgb(70, byte.MaxValue, byte.MaxValue, byte.MaxValue);

	private Storyboard? _toastStoryboard;

	private Action? _globalErrorRetryAction;

	private bool _isLyricsViewOpen => _currentPageId == "lyrics";

	private bool _isShuffleOn
	{
		get
		{
			return _queueService.IsShuffleOn;
		}
		set
		{
			_queueService.IsShuffleOn = value;
		}
	}

	private JsonArray? _currentQueue
	{
		get
		{
			return _queueService.CurrentQueue;
		}
		set
		{
			_queueService.CurrentQueue = value ?? new JsonArray();
		}
	}

	private int _currentQueueIndex
	{
		get
		{
			return _queueService.CurrentQueueIndex;
		}
		set
		{
			_queueService.CurrentQueueIndex = value;
		}
	}

	private int _originalQueueSize
	{
		get
		{
			return _queueService.OriginalQueueSize;
		}
		set
		{
			_queueService.OriginalQueueSize = value;
		}
	}

	public static MainWindow? Instance { get; private set; }

	public bool IsScrolled
	{
		get
		{
			return (bool)GetValue(IsScrolledProperty);
		}
		set
		{
			SetValue(IsScrolledProperty, value);
		}
	}

	private void PlaybackTimer_Tick(object? sender, EventArgs e)
	{
		if (_isUserDraggingSlider)
		{
			return;
		}
		long current = _player.Time;
		long total = _player.Length;
		bool isSeeking = (DateTime.Now - _lastSeekTime).TotalMilliseconds < 1500.0;
		if (isSeeking)
		{
			current = ((!_player.IsPlaying) ? _seekTargetTime : (_seekTargetTime + (long)(DateTime.Now - _lastSeekTime).TotalMilliseconds));
			if (total > 0 && current > total)
			{
				current = total;
			}
		}
		if (total > 0)
		{
			MainPlayerBarControl.TimelineSliderRef.Maximum = total;
			PlayerBarViewModel vm = App.Current.PlayerBarViewModel;
			if (vm != null)
			{
				vm.TotalTimeText = TimeSpan.FromMilliseconds(total).ToString("m\\:ss");
			}
			if (_player.IsPlaying && !isSeeking)
			{
				if (_lastRecordedTime > 0 && current > _lastRecordedTime)
				{
					long delta = current - _lastRecordedTime;
					if (delta > 0 && delta < 3000)
					{
						_accumulatedMs += delta;
						if (_currentPageId == "stats_page" && _statsMinutesValueText != null && _statsMinutesBorder != null)
						{
							long num = _statsCachedTotalMs + _accumulatedMs;
							long ch = num / 3600000;
							long cm = num / 60000 % 60;
							long cs = num / 1000 % 60;
							string primary = ((ch > 0) ? ch.ToString() : cm.ToString());
							string unit = ((ch > 0) ? "hours" : "minutes");
							string expand = ((ch > 0) ? $"h {cm}m {cs}s" : $"m {cs}s");
							if (_statsMinutesHovered)
							{
								SetStatsMinutesValue(primary, unit, expand, showSeconds: true, animate: false);
							}
							else
							{
								SetStatsMinutesValue(primary, unit, "", showSeconds: false, animate: false);
							}
						}
						if (_accumulatedMs >= 10000)
						{
							_ = _ = _ = _ = StatsManager.AddListeningTimeAsync(_accumulatedMs);
							_statsCachedTotalMs += _accumulatedMs;
							_accumulatedMs = 0L;
						}
					}
				}
				_lastRecordedTime = current;
			}
			else
			{
				_lastRecordedTime = -1L;
			}
			if (_player.IsPlaying || isSeeking)
			{
				MainPlayerBarControl.TimelineSliderRef.BeginAnimation(RangeBase.ValueProperty, null);
				double currentVal = MainPlayerBarControl.TimelineSliderRef.Value;
				if (Math.Abs((double)current - currentVal) > 1000.0 || currentVal == 0.0)
				{
					MainPlayerBarControl.TimelineSliderRef.Value = current;
				}
				else if ((double)current < currentVal && !isSeeking)
				{
					MainPlayerBarControl.TimelineSliderRef.Value = currentVal;
				}
				else
				{
					MainPlayerBarControl.TimelineSliderRef.Value = currentVal + ((double)current - currentVal) * 0.25;
				}
				PlayerBarViewModel vmCurrent = App.Current.PlayerBarViewModel;
				if (vmCurrent != null)
				{
					vmCurrent.CurrentTimeText = TimeSpan.FromMilliseconds(current).ToString("m\\:ss");
				}
				WarmLyricsForCurrentTrack(total);
				if (!_lastFmScrobbled && !string.IsNullOrEmpty(_currentVideoId) && !_currentVideoId.StartsWith("radio:") && total > 0 && current >= 1000)
				{
					_lastFmScrobbled = true;
					long timestamp = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds() - current / 1000;
					_ = _ = _ = _ = LastFmManager.ScrobbleAsync(_currentTitle, _currentArtist, _currentAlbum, timestamp);
					string titleCopy = _currentTitle;
					string artistCopy = _currentArtist;
					string albumCopy = _currentAlbum;
					string thumbCopy = _currentThumbUrl;
					Task.Run(async delegate
					{
						await StatsManager.RecordPlayAsync(titleCopy, artistCopy, albumCopy, thumbCopy, timestamp, total);
						base.Dispatcher.Invoke(delegate
						{
							if (_currentPageId == "stats_page")
							{
								long totalScrobbles = StatsManager.TotalScrobbles;
								if (_statsSongsValueText != null)
								{
									_statsSongsValueText.Text = totalScrobbles.ToString();
								}
								if (_statsOwedValueText != null)
								{
									_statsOwedValueText.Text = $"${(double)totalScrobbles * 0.004:0.00}";
								}
							}
						});
					});
				}
				if (_crossfadeMs > 0 && !_crossfadeTriggeredForCurrentTrack && total - current <= _crossfadeMs)
				{
					_crossfadeTriggeredForCurrentTrack = true;
					PlayNextInQueue(useCrossfade: true, isCrossfadeTrigger: true);
				}
			}
			else
			{
				PlayerBarViewModel vmTime = App.Current.PlayerBarViewModel;
				if (vmTime != null)
				{
					vmTime.CurrentTimeText = TimeSpan.FromMilliseconds(current).ToString("m\\:ss");
				}
				MainPlayerBarControl.TimelineSliderRef.BeginAnimation(RangeBase.ValueProperty, null);
				MainPlayerBarControl.TimelineSliderRef.Value = current;
			}
		}
		if (_player.IsPlaying || isSeeking)
		{
			UpdateLyricsForTime(current);
		}
	}

	protected override void OnClosing(CancelEventArgs e)
	{
		SaveSession();
		base.OnClosing(e);
	}

	private List<RadioStation> GetDefaultRadios()
	{
		return new List<RadioStation>
		{
			new RadioStation
			{
				Name = "Asia DREAM Radio",
				Description = "The Heart of J-Pop Music - J-Pop, J-Rock, J-HipHop, J-Jazz, Classics.",
				ThumbnailUrl = "https://cdn-icons-png.flaticon.com/512/3659/3659858.png",
				Streams = new List<RadioStream>
				{
					new RadioStream
					{
						Name = "Japan Hits",
						Url = "https://quincy.torontocast.com:2020/;?type=http",
						Icon = "♪"
					},
					new RadioStream
					{
						Name = "Natsukashii",
						Url = "https://quincy.torontocast.com:2070/;?type=http",
						Icon = "♪"
					},
					new RadioStream
					{
						Name = "J-Pop Kawaii",
						Url = "https://kathy.torontocast.com:3060/;?type=http",
						Icon = "♪"
					},
					new RadioStream
					{
						Name = "J-Pop Power",
						Url = "https://kathy.torontocast.com:3560/;?type=http",
						Icon = "♪"
					},
					new RadioStream
					{
						Name = "Jazz Sakura",
						Url = "https://kathy.torontocast.com:3330/;?type=http",
						Icon = "♪"
					},
					new RadioStream
					{
						Name = "J-Rock",
						Url = "https://kathy.torontocast.com:3340/;?type=http",
						Icon = "♪"
					},
					new RadioStream
					{
						Name = "J-Club / Hip Hop",
						Url = "https://kathy.torontocast.com:3350/;?type=http",
						Icon = "♪"
					},
					new RadioStream
					{
						Name = "Bandstand Jazz",
						Url = "https://cast1.torontocast.com/bandstand",
						Icon = "♪"
					}
				}
			},
			new RadioStation
			{
				Name = "Vocaloid Radio",
				Description = "Vocaloid Hits from Japan. We play it all!",
				ThumbnailUrl = "https://cdn-radiotime-logos.tunein.com/s221579q.png",
				Streams = new List<RadioStream>
				{
					new RadioStream
					{
						Name = "Play",
						Url = "https://vocaloid.radioca.st/stream",
						Icon = "▶"
					}
				}
			}
		};
	}

	protected override void OnStateChanged(EventArgs e)
	{
		base.OnStateChanged(e);
		if (MaxBtnIcon != null)
		{
			MaxBtnIcon.Source = (ImageSource)System.Windows.Application.Current.FindResource((base.WindowState == WindowState.Maximized) ? "restoredownIcon" : "maximizeIcon");
		}
		if (RootGrid != null)
		{
			RootGrid.Margin = ((base.WindowState == WindowState.Maximized) ? new Thickness(8.0) : new Thickness(0.0));
		}
	}

	protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
	{
		base.OnRenderSizeChanged(sizeInfo);
		if (MainOverlayControl.CreditsOverlayRef != null && MainOverlayControl.CreditsOverlayRef.Visibility == Visibility.Visible && MainOverlayControl.CreditsDialogBorderRef != null)
		{
			MainOverlayControl.CreditsDialogBorderRef.MaxHeight = GetCreditsDialogMaxHeight();
			MainOverlayControl.CreditsDialogBorderRef.Height = Math.Min(GetCreditsDialogContentHeight(), MainOverlayControl.CreditsDialogBorderRef.MaxHeight);
		}
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern int GetWindowLong(nint hWnd, int nIndex);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

	[DllImport("user32.dll")]
	private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, int flags);

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		nint hwnd = new WindowInteropHelper(this).Handle;
		int style = GetWindowLong(hwnd, -16);
		SetWindowLong(hwnd, -16, style | 0xC00000 | 0x40000 | 0x80000 | 0x20000 | 0x10000);
		SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 39);
		int useImmersiveDarkMode = 1;
		DwmSetWindowAttribute(hwnd, 20, ref useImmersiveDarkMode, 4);
		DwmSetWindowAttribute(hwnd, 19, ref useImmersiveDarkMode, 4);
		try
		{
			System.Windows.Media.Color topbarColor = (System.Windows.Media.Color)ColorConverter.ConvertFromString(_topbarBg);
			int colorRef = topbarColor.R | (topbarColor.G << 8) | (topbarColor.B << 16);
			DwmSetWindowAttribute(hwnd, 35, ref colorRef, 4);
		}
		catch
		{
		}
		HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
	}

	[DllImport("user32.dll")]
	private static extern bool GetClientRect(nint hWnd, out RECT lpRect);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool GetWindowRect(nint hwnd, out RECT lpRect);

	[DllImport("user32.dll")]
	private static extern int FillRect(nint hDC, ref RECT lprc, nint hbr);

	[DllImport("gdi32.dll")]
	private static extern bool DeleteObject(nint hObject);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool LockWindowUpdate(nint hWndLock);

	private Task _initPlaybackTask;

	public MainWindow()
	{
		_queueService = App.Current.QueueService;
		Instance = this;
		InitializeComponent();
		LoadWindowBounds();
		MainSidebar.SizeChanged += (s, e) =>
		{
			if (_isSidebarCoverExpanded && !_isSidebarMinimized)
			{
				MainSidebar.SidebarCoverContainerRef.BeginAnimation(FrameworkElement.HeightProperty, null);
				MainSidebar.SidebarCoverContainerRef.Height = MainSidebar.ActualWidth;
			}
		};
		_scrollWorkTimer.Tick += delegate
		{
			_scrollWorkTimer.Stop();
			_isVirtualizationQueued = false;
			CheckVisibilityOfLazyElements();
		};
		ContextMenu sidebarContextMenu = new ContextMenu();
		Style ctxStyle = TryFindResource(typeof(ContextMenu)) as Style;
		if (ctxStyle != null)
		{
			sidebarContextMenu.Style = ctxStyle;
		}
		Style menuItemStyle = TryFindResource(typeof(MenuItem)) as Style;
		if (menuItemStyle != null)
		{
			sidebarContextMenu.ItemContainerStyle = menuItemStyle;
		}
		MenuItem createPlaylistMenuItem = new MenuItem
		{
			Header = "Create Playlist"
		};
		createPlaylistMenuItem.Click += delegate
		{
			StartInlinePlaylistCreation();
		};
		sidebarContextMenu.Items.Add(createPlaylistMenuItem);
		MainSidebar.SidebarBorderRef.ContextMenu = sidebarContextMenu;
		ContextMenu radioCm = new ContextMenu();
		if (ctxStyle != null)
		{
			radioCm.Style = ctxStyle;
		}
		if (menuItemStyle != null)
		{
			radioCm.ItemContainerStyle = menuItemStyle;
		}
		MenuItem addRadioMenuItem = new MenuItem
		{
			Header = "Add Custom Radio"
		};
		addRadioMenuItem.Click += delegate
		{
			ShowAddRadioPopup();
		};
		radioCm.Items.Add(addRadioMenuItem);
		MenuItem hideRadio = new MenuItem
		{
			Header = "Hide this tab",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"]
		};
		hideRadio.Click += delegate
		{
			if (!_blockedCategories.Contains("Tab: Radio"))
			{
				_blockedCategories.Add("Tab: Radio");
			}
			UpdateTabVisibility();
			SaveSession();
		};
		radioCm.Items.Add(hideRadio);
		MainSidebar.RadioNavBorderRef.ContextMenu = radioCm;
		ContextMenu localCm = new ContextMenu();
		if (ctxStyle != null)
		{
			localCm.Style = ctxStyle;
		}
		if (menuItemStyle != null)
		{
			localCm.ItemContainerStyle = menuItemStyle;
		}
		MenuItem hideLocal = new MenuItem
		{
			Header = "Hide this tab",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"]
		};
		hideLocal.Click += delegate
		{
			if (!_blockedCategories.Contains("Tab: Local"))
			{
				_blockedCategories.Add("Tab: Local");
			}
			UpdateTabVisibility();
			SaveSession();
		};
		localCm.Items.Add(hideLocal);
		MainSidebar.LocalNavBorderRef.ContextMenu = localCm;
		ContextMenu statsCm = new ContextMenu();
		if (ctxStyle != null)
		{
			statsCm.Style = ctxStyle;
		}
		if (menuItemStyle != null)
		{
			statsCm.ItemContainerStyle = menuItemStyle;
		}
		MenuItem hideStats = new MenuItem
		{
			Header = "Hide this tab",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"]
		};
		hideStats.Click += delegate
		{
			if (!_blockedCategories.Contains("Tab: Stats"))
			{
				_blockedCategories.Add("Tab: Stats");
			}
			UpdateTabVisibility();
			SaveSession();
		};
		statsCm.Items.Add(hideStats);
		MainSidebar.StatsNavBorderRef.ContextMenu = statsCm;
		ContextMenu exploreCm = new ContextMenu();
		if (ctxStyle != null)
		{
			exploreCm.Style = ctxStyle;
		}
		if (menuItemStyle != null)
		{
			exploreCm.ItemContainerStyle = menuItemStyle;
		}
		MenuItem hideExplore = new MenuItem
		{
			Header = "Hide this tab",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"]
		};
		hideExplore.Click += delegate
		{
			if (!_blockedCategories.Contains("Tab: Explore"))
			{
				_blockedCategories.Add("Tab: Explore");
			}
			UpdateTabVisibility();
			SaveSession();
		};
		exploreCm.Items.Add(hideExplore);
		MainSidebar.ExploreNavBorderRef.ContextMenu = exploreCm;
		ContextMenu playlistsCm = new ContextMenu();
		if (ctxStyle != null)
		{
			playlistsCm.Style = ctxStyle;
		}
		if (menuItemStyle != null)
		{
			playlistsCm.ItemContainerStyle = menuItemStyle;
		}
		MenuItem hidePlaylists = new MenuItem
		{
			Header = "Hide this tab",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"]
		};
		hidePlaylists.Click += delegate
		{
			if (!_blockedCategories.Contains("Tab: Playlists"))
			{
				_blockedCategories.Add("Tab: Playlists");
			}
			UpdateTabVisibility();
			SaveSession();
		};
		playlistsCm.Items.Add(hidePlaylists);
		MainSidebar.PlaylistsNavBorderRef.ContextMenu = playlistsCm;
		ContextMenu albumsCm = new ContextMenu();
		if (ctxStyle != null)
		{
			albumsCm.Style = ctxStyle;
		}
		if (menuItemStyle != null)
		{
			albumsCm.ItemContainerStyle = menuItemStyle;
		}
		MenuItem hideAlbums = new MenuItem
		{
			Header = "Hide this tab",
			Foreground = (SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"]
		};
		hideAlbums.Click += delegate
		{
			if (!_blockedCategories.Contains("Tab: Albums"))
			{
				_blockedCategories.Add("Tab: Albums");
			}
			UpdateTabVisibility();
			SaveSession();
		};
		albumsCm.Items.Add(hideAlbums);
		MainSidebar.AlbumsNavBorderRef.ContextMenu = albumsCm;
		_taskbarItemInfoStore = base.TaskbarItemInfo;
		MainTopbarControl.StatusLabelRef.Visibility = Visibility.Collapsed;
		AppLogger.MessageLogged += OnLogReceived;
		base.Dispatcher.InvokeAsync(delegate
		{
			foreach (LogMessage entry in AppLogger.GetRecentEntries())
			{
				AddLogToRichTextBox(entry);
			}
		}, DispatcherPriority.Background);
		try
		{
			string p = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spectre", "session.json");
			if (System.IO.File.Exists(p))
			{
				JsonObject j = JsonNode.Parse(System.IO.File.ReadAllText(p))!.AsObject();
				if (j["LoudnessNormalization"] != null)
				{
					_ = (bool)j["LoudnessNormalization"];
				}
				if (j["NetworkCacheMs"] != null)
				{
					_ = (int)j["NetworkCacheMs"];
				}
				if (j["DisableGPU"] != null && (bool)j["DisableGPU"])
				{
					RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
				}
			}
		}
		catch
		{
		}
		_initPlaybackTask = Task.Run(() =>
		{
			IPlaybackService playbackService = App.Current.PlaybackService;
			base.Dispatcher.Invoke(delegate
			{
				_player = ((PlaybackService)playbackService)._engine;
				_player.EndReached += delegate
				{
					base.Dispatcher.Invoke(delegate
					{
						if (!_isRepeatOn)
						{
							NextBtn_Click(null, null);
						}
						else
						{
							_player.Time = 0L;
							_player.Resume();
						}
					});
				};
				_player.Playing += delegate
				{
					base.Dispatcher.InvokeAsync(delegate
					{
						if (_pauseRequested)
						{
							_player.Pause();
							_playbackTimer.Stop();
							UpdatePlayPauseIconState(isPaused: true);
							TaskbarPlayPauseBtn.ImageSource = (ImageSource)FindResource("ThumbPlayIcon");
							TaskbarPlayPauseBtn.Description = "Play";
							if (_smtc != null)
							{
								_smtc.PlaybackStatus = MediaPlaybackStatus.Paused;
							}
							PlaybackTimer_Tick(null, EventArgs.Empty);
						}
						else
						{
							_playbackTimer.Start();
							UpdatePlayPauseIconState(isPaused: false);
							TaskbarPlayPauseBtn.ImageSource = (ImageSource)FindResource("ThumbPauseIcon");
							TaskbarPlayPauseBtn.Description = "Pause";
							if (_smtc != null)
							{
								_smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
							}
							UpdateDiscordRPC();
							base.Title = _currentArtist + " - " + _currentTitle;
						}
					});
				};
				_player.Paused += delegate
				{
					base.Dispatcher.InvokeAsync(delegate
					{
						_playbackTimer.Stop();
						UpdatePlayPauseIconState(isPaused: true);
						TaskbarPlayPauseBtn.ImageSource = (ImageSource)FindResource("ThumbPlayIcon");
						TaskbarPlayPauseBtn.Description = "Play";
						if (_smtc != null)
						{
							_smtc.PlaybackStatus = MediaPlaybackStatus.Paused;
						}
						UpdateDiscordRPC();
						base.Title = "Spectre";
					});
				};
				if (MainPlayerBarControl?.VolumeSliderRef != null)
				{
					_player.Volume = (int)MainPlayerBarControl.VolumeSliderRef.Value;
				}
				_player.CrossfadeMs = _crossfadeMs;
			});
		});

		_youtube = new YoutubeClient();
		ApplyThemeColors();
		_playbackTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(33.0)
		};
		_playbackTimer.Tick += PlaybackTimer_Tick;
		foreach (object child in MainSidebar.HomeNavPanelRef.Children)
		{
			SidebarTab tab = child as SidebarTab;
			if (tab == null)
			{
				continue;
			}
			tab.MouseEnter += delegate
			{
				if (_currentPageId != tab.PageId)
				{
					FadeBorderBackgroundToResource(tab.GetMainBorder(), "CardHoverBrush");
					FadeTextForegroundToColor(tab.GetTitleText(), ((SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["TextBrush"]).Color);
				}
			};
			tab.MouseLeave += delegate
			{
				if (_currentPageId != tab.PageId)
				{
					FadeBorderBackgroundToColor(tab.GetMainBorder(), Colors.Transparent);
					FadeTextForegroundToColor(tab.GetTitleText(), ((SolidColorBrush)System.Windows.Application.Current.MainWindow.Resources["SidebarTextBrush"]).Color);
				}
			};
		}
		base.PreviewKeyDown += MainWindow_PreviewKeyDown;
		RegisterMvvmMessages();
		QueueViewModel queueVm = App.Current.QueueViewModel;
		if (queueVm != null)
		{
			MainQueueControl.DataContext = queueVm;
		}
		SidebarViewModel sidebarVm = App.Current.SidebarViewModel;
		if (sidebarVm != null)
		{
			MainSidebar.DataContext = sidebarVm;
		}
		base.Dispatcher.InvokeAsync(delegate
		{
			InitSMTC();
			InitDiscordRPC();
		}, DispatcherPriority.Background);
		base.Loaded += async delegate
		{
			await LoadLastSessionAsync();
			ResetShuffleState();
			if (System.IO.File.Exists(BackendService.AuthFilePath))
			{
				_ = Task.Run(async delegate
				{
					try
					{
						JsonObject info = await BackendService.Instance.GetAccountInfoAsync(CancellationToken.None);
						string name = ((string?)info["data"]?["accountName"]) ?? "User";
						string photo = ((string?)info["data"]?["accountPhoto"]) ?? "";
						if (name == "User" && string.IsNullOrEmpty(photo))
						{
							await Dispatcher.InvokeAsync(delegate
							{
								_ = RefreshSessionSilentlyAsync();
							});
						}
					}
					catch
					{
					}
				});
			}
			CheckLoginStatus();
			SetSidebarState(!System.IO.File.Exists(BackendService.AuthFilePath), animate: false);
			await Task.Delay(50);
			if (System.IO.File.Exists(BackendService.AuthFilePath))
			{
				Task homeTask = LoadHomeFeedAsync();
				Task libTask = LoadLibraryAsync();
				_ = _ = _ = _ = LoadLikedSongsAsync();
				UpdateSidebarHighlight();
				await Task.WhenAll(homeTask, libTask, _initPlaybackTask);
			}
			else
			{
				_currentPageId = "explore";
				Task exploreTask = LoadExploreFeedAsync();
				Task libTask2 = LoadLibraryAsync();
				UpdateSidebarHighlight();
				await Task.WhenAll(exploreTask, libTask2, _initPlaybackTask);
			}
			MainOverlayControl.LoadingOverlayContentContainerRef.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
			ScaleTransform exitScale = new ScaleTransform(1.0, 1.0);
			MainOverlayControl.LoadingOverlayContentContainerRef.RenderTransform = exitScale;

			DoubleAnimationUsingKeyFrames scaleAnimation = new DoubleAnimationUsingKeyFrames();
			scaleAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(1.05, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200)), new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }));
			scaleAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(0.5, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(500)), new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }));
			scaleAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(3.5, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1000)), new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }));

			DoubleAnimation fadeOutOverlay = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(400.0));
			fadeOutOverlay.BeginTime = TimeSpan.FromMilliseconds(600.0);
			fadeOutOverlay.Completed += delegate
			{
				MainOverlayControl.LoadingOverlayRef.Visibility = Visibility.Collapsed;
				MainOverlayControl.StopLoadingAnimations();
			};
			
			exitScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
			exitScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
			MainOverlayControl.LoadingOverlayRef.BeginAnimation(UIElement.OpacityProperty, fadeOutOverlay);
		};
	}

	private void RegisterMvvmMessages()
	{
		WeakReferenceMessenger.Default.Register<PlayPauseMessage>(this, delegate
		{
			PlayPauseBtn_Click(null, null);
		});
		WeakReferenceMessenger.Default.Register<NextTrackMessage>(this, delegate
		{
			NextBtn_Click(null, null);
		});
		WeakReferenceMessenger.Default.Register<PrevTrackMessage>(this, delegate
		{
			PrevBtn_Click(null, null);
		});
		WeakReferenceMessenger.Default.Register<ShuffleMessage>(this, delegate
		{
			ShuffleBtn_Click(null, null);
		});
		WeakReferenceMessenger.Default.Register<RepeatMessage>(this, delegate
		{
			RepeatBtn_Click(null, null);
		});
		WeakReferenceMessenger.Default.Register<ToggleQueueMessage>(this, delegate
		{
			QueueBtn_Click(null, null);
		});
		WeakReferenceMessenger.Default.Register<ToggleLyricsMessage>(this, delegate
		{
			LyricsBtn_Click(null, null);
		});
		WeakReferenceMessenger.Default.Register(this, async delegate(object r, PlayQueueItemMessage m)
		{
			MainWindow main = (MainWindow)r;
			main._currentQueueIndex = m.TargetIndex;
			main._isTrackLoading = true;
			PlayerBarViewModel vm = App.Current.PlayerBarViewModel;
			if (vm != null)
			{
				vm.CurrentTimeText = "Loading...";
			}
			try
			{
				await main.PlayTrack(m.VideoId, m.Title, m.Artist, m.ThumbnailUrl);
			}
			finally
			{
				main._isTrackLoading = false;
			}
		});
		WeakReferenceMessenger.Default.Register(this, delegate(object r, NavigateMessage m)
		{
			((MainWindow)r).NavigateToPage(m.PageId);
		});
	}

	private bool IsChildOfLibraryItemOrHome(DependencyObject current)
	{
		while (current != null)
		{
			if (current == MainSidebar.HomeNavBorderRef)
			{
				return true;
			}
			if (current == MainSidebar.ExploreNavBorderRef)
			{
				return true;
			}
			if (current == MainSidebar.PlaylistsNavBorderRef)
			{
				return true;
			}
			if (current == MainSidebar.AlbumsNavBorderRef)
			{
				return true;
			}
			if (current == MainSidebar.RadioNavBorderRef)
			{
				return true;
			}
			if (current == MainSidebar.LocalNavBorderRef)
			{
				return true;
			}
			if (current == MainSidebar.StatsNavBorderRef)
			{
				return true;
			}
			if (current is Border b && b.Parent == MainSidebar.LibraryPanelRef)
			{
				return true;
			}
			if (current is Grid g && g.Parent == MainSidebar.LibraryPanelRef)
			{
				return true;
			}
			if (current is TextBlock tb && tb.Parent == MainSidebar.LibraryPanelRef)
			{
				return true;
			}
			if (current is System.Windows.Controls.Primitives.ScrollBar)
			{
				return true;
			}
			current = VisualTreeHelper.GetParent(current);
		}
		return false;
	}

	private void Sidebar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (!IsChildOfLibraryItemOrHome(e.OriginalSource as DependencyObject))
		{
			SetSidebarState(!_isSidebarMinimized);
		}
	}

	public void SetSidebarState(bool minimize, bool animate = true)
	{
		if (_isSidebarMinimized == minimize)
		{
			return;
		}
		if (animate)
		{
			MainScrollViewer.Width = MainScrollViewer.ActualWidth;
			DoubleAnimation fadeOutMain = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120.0));
			MainScrollViewer.BeginAnimation(UIElement.OpacityProperty, fadeOutMain);
		}
		_isSidebarMinimized = minimize;
		if (_isSidebarCoverExpanded)
		{
			double coverTargetHeight = _isSidebarMinimized ? 0.0 : _expandedSidebarWidth;
			double thumbTargetWidth = _isSidebarMinimized ? 48.0 : 0.0;
			Thickness marginTarget = _isSidebarMinimized ? new Thickness(15, 0, 0, 0) : new Thickness(0, 0, 0, 0);

			if (animate)
			{
				DoubleAnimation coverAnim = new DoubleAnimation(coverTargetHeight, new Duration(TimeSpan.FromMilliseconds(250)))
				{
					EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
				};
				MainSidebar.SidebarCoverContainerRef.BeginAnimation(FrameworkElement.HeightProperty, coverAnim);

				DoubleAnimation thumbAnim = new DoubleAnimation(thumbTargetWidth, new Duration(TimeSpan.FromMilliseconds(250)))
				{
					EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
				};
				MainPlayerBarControl.PlayerThumbnailContainerRef.BeginAnimation(FrameworkElement.WidthProperty, thumbAnim);

				ThicknessAnimation marginAnim = new ThicknessAnimation(marginTarget, new Duration(TimeSpan.FromMilliseconds(250)))
				{
					EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
				};
				MainPlayerBarControl.PlayerTextPanelRef.BeginAnimation(FrameworkElement.MarginProperty, marginAnim);
			}
			else
			{
				MainSidebar.SidebarCoverContainerRef.BeginAnimation(FrameworkElement.HeightProperty, null);
				MainSidebar.SidebarCoverContainerRef.Height = coverTargetHeight;

				MainPlayerBarControl.PlayerThumbnailContainerRef.BeginAnimation(FrameworkElement.WidthProperty, null);
				MainPlayerBarControl.PlayerThumbnailContainerRef.Width = thumbTargetWidth;

				MainPlayerBarControl.PlayerTextPanelRef.BeginAnimation(FrameworkElement.MarginProperty, null);
				MainPlayerBarControl.PlayerTextPanelRef.Margin = marginTarget;
			}
		}
		double targetWidth = (_isSidebarMinimized ? _minimizedSidebarWidth : _expandedSidebarWidth);
		double startWidth = SidebarColumn.ActualWidth;
		double targetTitleMargin = (_isSidebarMinimized ? 24 : 20);
		double startTitleMargin = MainSidebar.SidebarTitlePanelRef.Margin.Left;
		double targetItemMargin = (_isSidebarMinimized ? 8 : 20);
		double startItemMargin = MainSidebar.HomeNavPanelRef.Margin.Left;
		double durationMs = 250.0;
		DateTime startTime = DateTime.Now;
		List<FrameworkElement> invisibleElements = new List<FrameworkElement>();
		try
		{
			double viewportHeight = MainScrollViewer.ViewportHeight;
			foreach (UIElement child in ContentPanel.Children)
			{
				if (child is StackPanel sp)
				{
					foreach (UIElement child2 in sp.Children)
					{
						if (child2 is FrameworkElement fe)
						{
							Rect bounds = fe.TransformToAncestor(MainScrollViewer).TransformBounds(new Rect(0.0, 0.0, fe.ActualWidth, fe.ActualHeight));
							if (bounds.Bottom < 0.0 || bounds.Top > viewportHeight)
							{
								invisibleElements.Add(fe);
								fe.Width = fe.ActualWidth;
								fe.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
							}
						}
					}
				}
				else if (child is FrameworkElement fe2)
				{
					Rect bounds2 = fe2.TransformToAncestor(MainScrollViewer).TransformBounds(new Rect(0.0, 0.0, fe2.ActualWidth, fe2.ActualHeight));
					if (bounds2.Bottom < 0.0 || bounds2.Top > viewportHeight)
					{
						invisibleElements.Add(fe2);
						fe2.Width = fe2.ActualWidth;
						fe2.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
					}
				}
			}
		}
		catch
		{
		}
		if (!animate)
		{
			SidebarColumn.Width = new GridLength(targetWidth);
			MainSidebar.SidebarTitlePanelRef.Margin = new Thickness(targetTitleMargin, MainSidebar.SidebarTitlePanelRef.Margin.Top, 0.0, MainSidebar.SidebarTitlePanelRef.Margin.Bottom);
			MainSidebar.HomeNavPanelRef.Margin = new Thickness(targetItemMargin, MainSidebar.HomeNavPanelRef.Margin.Top, targetItemMargin, MainSidebar.HomeNavPanelRef.Margin.Bottom);
			MainSidebar.LibraryPanelRef.Margin = new Thickness(targetItemMargin, MainSidebar.LibraryPanelRef.Margin.Top, targetItemMargin, MainSidebar.LibraryPanelRef.Margin.Bottom);
			UpdateSidebarTextOpacity((!_isSidebarMinimized) ? 1 : 0);
			{
				foreach (FrameworkElement item in invisibleElements)
				{
					item.Width = double.NaN;
					item.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
				}
				return;
			}
		}
		EventHandler renderingLoop = null;
		renderingLoop = delegate
		{
			double totalMilliseconds = (DateTime.Now - startTime).TotalMilliseconds;
			if (totalMilliseconds >= durationMs)
			{
				CompositionTarget.Rendering -= renderingLoop;
				SidebarColumn.Width = new GridLength(targetWidth);
				MainSidebar.SidebarTitlePanelRef.Margin = new Thickness(targetTitleMargin, MainSidebar.SidebarTitlePanelRef.Margin.Top, 0.0, MainSidebar.SidebarTitlePanelRef.Margin.Bottom);
				MainSidebar.HomeNavPanelRef.Margin = new Thickness(targetItemMargin, MainSidebar.HomeNavPanelRef.Margin.Top, targetItemMargin, MainSidebar.HomeNavPanelRef.Margin.Bottom);
				MainSidebar.LibraryPanelRef.Margin = new Thickness(targetItemMargin, MainSidebar.LibraryPanelRef.Margin.Top, targetItemMargin, MainSidebar.LibraryPanelRef.Margin.Bottom);
				UpdateSidebarTextOpacity((!_isSidebarMinimized) ? 1 : 0);
				foreach (FrameworkElement item2 in invisibleElements)
				{
					item2.Width = double.NaN;
					item2.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
				}
				MainScrollViewer.Width = double.NaN;
				DoubleAnimation animation = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200.0));
				MainScrollViewer.BeginAnimation(UIElement.OpacityProperty, animation);
			}
			else
			{
				double num = totalMilliseconds / durationMs;
				num = 1.0 - Math.Pow(1.0 - num, 3.0);
				SidebarColumn.Width = new GridLength(startWidth + (targetWidth - startWidth) * num);
				double left = startTitleMargin + (targetTitleMargin - startTitleMargin) * num;
				MainSidebar.SidebarTitlePanelRef.Margin = new Thickness(left, MainSidebar.SidebarTitlePanelRef.Margin.Top, 0.0, MainSidebar.SidebarTitlePanelRef.Margin.Bottom);
				double num2 = startItemMargin + (targetItemMargin - startItemMargin) * num;
				MainSidebar.HomeNavPanelRef.Margin = new Thickness(num2, MainSidebar.HomeNavPanelRef.Margin.Top, num2, MainSidebar.HomeNavPanelRef.Margin.Bottom);
				MainSidebar.LibraryPanelRef.Margin = new Thickness(num2, MainSidebar.LibraryPanelRef.Margin.Top, num2, MainSidebar.LibraryPanelRef.Margin.Bottom);
				double opacity = (_isSidebarMinimized ? (1.0 - num) : num);
				UpdateSidebarTextOpacity(opacity);
			}
		};
		CompositionTarget.Rendering += renderingLoop;
	}

	private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (e.Key == Key.Space)
		{
			if (!(Keyboard.FocusedElement is System.Windows.Controls.TextBox))
			{
				PlayPauseBtn_Click(null, null);
				e.Handled = true;
			}
		}
		else if (e.Key == Key.F9)
		{
			ToggleLogOverlay();
			e.Handled = true;
		}
		else if (e.Key == Key.Escape && _logVisible)
		{
			HideLogOverlay();
			e.Handled = true;
		}
	}

	private Task<bool>? _refreshSessionTask;

	public Task<bool> RefreshSessionSilentlyAsync()
	{
		if (_refreshSessionTask != null && !_refreshSessionTask.IsCompleted)
		{
			return _refreshSessionTask;
		}
		_refreshSessionTask = RefreshSessionSilentlyInternalAsync();
		return _refreshSessionTask;
	}

	private async Task<bool> RefreshSessionSilentlyInternalAsync()
	{
		if (!Dispatcher.CheckAccess())
		{
			return await Dispatcher.InvokeAsync(RefreshSessionSilentlyInternalAsync).Task.Unwrap();
		}

		try
		{
			if (!System.IO.File.Exists(BackendService.AuthFilePath))
			{
				return false;
			}

			string userDataFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spectre", "WebView2");
			var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
			
			var window = new Window
			{
				Width = 0,
				Height = 0,
				WindowStyle = WindowStyle.None,
				ShowInTaskbar = false,
				ShowActivated = false,
				Visibility = Visibility.Hidden,
				Opacity = 0
			};
			var webView = new Microsoft.Web.WebView2.Wpf.WebView2();
			window.Content = webView;
			window.Show();

			try
			{
				await webView.EnsureCoreWebView2Async(env);

			string capturedAuthUser = "0";
			var tcs = new TaskCompletionSource<bool>();

			webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
			webView.CoreWebView2.WebResourceRequested += (s, e) =>
			{
				try
				{
					if (e.Request.Headers.Contains("x-goog-authuser"))
					{
						capturedAuthUser = e.Request.Headers.GetHeader("x-goog-authuser");
					}
				}
				catch { }
			};

			webView.CoreWebView2.NavigationCompleted += async (s, e) =>
			{
				if (e.IsSuccess)
				{
					try
					{
						var cookies = await webView.CoreWebView2.CookieManager.GetCookiesAsync("https://youtube.com");
						string loggedInStr = await webView.CoreWebView2.ExecuteScriptAsync("typeof ytcfg !== 'undefined' && ytcfg.get ? ytcfg.get('LOGGED_IN') === true : false");
						bool hasSapisid = cookies.Any(c => c.Name == "SAPISID");
						bool hasLoginInfo = cookies.Any(c => c.Name == "LOGIN_INFO");

						if (hasSapisid && hasLoginInfo && loggedInStr == "true")
						{
							string cookieString = string.Join("; ", cookies.Select(c => c.Name + "=" + c.Value));
							var sapisidCookie = cookies.FirstOrDefault(c => c.Name == "SAPISID");

							if (sapisidCookie != null)
							{
								string json = JsonSerializer.Serialize(new Dictionary<string, string>
								{
									{ "cookie", cookieString },
									{ "sapisid", sapisidCookie.Value },
									{ "userAgent", webView.CoreWebView2.Settings.UserAgent },
									{ "authUser", capturedAuthUser }
								}, new JsonSerializerOptions { WriteIndented = true });

								System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(BackendService.AuthFilePath));
								BackendService.SaveAuthData(json);
								tcs.TrySetResult(true);
								return;
							}
						}
					}
					catch { }
				}
			};

			webView.CoreWebView2.Navigate("https://music.youtube.com");

			var resultTask = await Task.WhenAny(tcs.Task, Task.Delay(15000));
			if (resultTask == tcs.Task)
			{
				bool success = await tcs.Task;
				if (success)
				{
					AppLogger.Log("Session: Silently refreshed cookies via background WebView", LogLevel.Success);
					return true;
				}
			}
			
			AppLogger.Log("Session: Failed to refresh cookies via background WebView", LogLevel.Error);
			return false;
			}
			finally
			{
				webView.Dispose();
				window.Close();
			}
		}
		catch (Exception ex)
		{
			AppLogger.Log("Session: Failed to refresh cookies via background WebView - " + ex.Message, LogLevel.Error);
			return false;
		}
	}

	private void HighlightInPanel(UIElement element, string videoId)
	{
		if (element == null)
		{
			return;
		}
		Border b = element as Border;
		if (b != null && b.Tag is string id && id != "loading" && id != "loading_queued")
		{
			if (id == videoId)
			{
				b.SetResourceReference(Border.BackgroundProperty, "CardBackground");
				b.BorderThickness = new Thickness(1.0);
				LinearGradientBrush? playingBrush = System.Windows.Application.Current.MainWindow.Resources["PlayingBorderBrush"] as LinearGradientBrush;
				if (b.BorderBrush is LinearGradientBrush existingBrush)
				{
					if (playingBrush != null &&
					    existingBrush.GradientStops.Count == playingBrush.GradientStops.Count &&
					    existingBrush.GradientStops[0].Color == playingBrush.GradientStops[0].Color &&
					    existingBrush.GradientStops[1].Color == playingBrush.GradientStops[1].Color)
					{
						DoubleAnimation anim = new DoubleAnimation(0.4, TimeSpan.FromMilliseconds(300.0))
						{
							EasingFunction = new QuadraticEase
							{
								EasingMode = EasingMode.EaseOut
							}
						};
						existingBrush.BeginAnimation(System.Windows.Media.Brush.OpacityProperty, anim);
					}
					else if (playingBrush != null)
					{
						LinearGradientBrush newBrush = playingBrush.Clone();
						newBrush.Opacity = existingBrush.Opacity;
						b.BorderBrush = newBrush;
						DoubleAnimation anim = new DoubleAnimation(0.4, TimeSpan.FromMilliseconds(300.0))
						{
							EasingFunction = new QuadraticEase
							{
								EasingMode = EasingMode.EaseOut
							}
						};
						newBrush.BeginAnimation(System.Windows.Media.Brush.OpacityProperty, anim);
					}
				}
				else if (playingBrush != null)
				{
					LinearGradientBrush newBrush = playingBrush.Clone();
					newBrush.Opacity = 0.06;
					b.BorderBrush = newBrush;
					DoubleAnimation anim2 = new DoubleAnimation(0.4, TimeSpan.FromMilliseconds(300.0))
					{
						EasingFunction = new QuadraticEase
						{
							EasingMode = EasingMode.EaseOut
						}
					};
					newBrush.BeginAnimation(System.Windows.Media.Brush.OpacityProperty, anim2);
				}
			}
			else
			{
				b.SetResourceReference(Border.BackgroundProperty, "CardBackground");
				b.BorderThickness = new Thickness(1.0);
				if (b.BorderBrush is LinearGradientBrush oldBrush)
				{
					DoubleAnimation anim3 = new DoubleAnimation(0.06, TimeSpan.FromMilliseconds(300.0))
					{
						EasingFunction = new QuadraticEase
						{
							EasingMode = EasingMode.EaseOut
						}
					};
					anim3.Completed += delegate
					{
						if ((string)b.Tag != _currentVideoId)
						{
							b.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(15, byte.MaxValue, byte.MaxValue, byte.MaxValue));
						}
					};
					oldBrush.BeginAnimation(System.Windows.Media.Brush.OpacityProperty, anim3);
				}
				else
				{
					b.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(15, byte.MaxValue, byte.MaxValue, byte.MaxValue));
				}
			}
		}
		if (element is System.Windows.Controls.Panel panel)
		{
			{
				foreach (UIElement child in panel.Children)
				{
					HighlightInPanel(child, videoId);
				}
				return;
			}
		}
		if (element is Decorator { Child: not null } decorator)
		{
			HighlightInPanel(decorator.Child, videoId);
		}
		else if (element is ContentControl { Content: UIElement ccChild })
		{
			HighlightInPanel(ccChild, videoId);
		}
	}

	private void LargeCoverOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		DoubleAnimation fade = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(200.0))
		{
			EasingFunction = new QuarticEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		DoubleAnimation scale = new DoubleAnimation(0.8, TimeSpan.FromMilliseconds(200.0))
		{
			EasingFunction = new QuarticEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		fade.Completed += delegate
		{
			MainOverlayControl.LargeCoverOverlayRef.Visibility = Visibility.Collapsed;
		};
		MainOverlayControl.LargeCoverOverlayRef.BeginAnimation(UIElement.OpacityProperty, fade);
		MainOverlayControl.LargeCoverScaleRef.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
		MainOverlayControl.LargeCoverScaleRef.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
	}

	private void ToggleFullscreen()
	{
		if (base.WindowState == WindowState.Maximized)
		{
			base.WindowState = WindowState.Normal;
		}
		else
		{
			base.WindowState = WindowState.Maximized;
		}
	}

	private void UpdateTaskbarControlsVisibility()
	{
		if (_showTaskbarMediaControls)
		{
			if (base.TaskbarItemInfo == null)
			{
				base.TaskbarItemInfo = _taskbarItemInfoStore;
			}
		}
		else
		{
			base.TaskbarItemInfo = null;
		}
	}

	private System.Windows.Threading.DispatcherTimer? _themeTransitionTimer;
	private int _themeTransitionStep;
	private const int ThemeTransitionMaxSteps = 15;
	private System.Windows.Media.Color _srcAccent1, _srcAccent2, _srcBg1, _srcBg2, _srcBg3, _srcCardBg, _srcSidebarBg, _srcTopbarBg, _srcBottombarBg;
	private System.Windows.Media.Color _dstAccent1, _dstAccent2, _dstBg1, _dstBg2, _dstBg3, _dstCardBg, _dstSidebarBg, _dstTopbarBg, _dstBottombarBg;

	private System.Windows.Media.Color AdjustBrightness(System.Windows.Media.Color color, double multiplier)
	{
		if (multiplier == 1.0) return color;
		HslColor hsl = HslColor.FromRgb(color.R, color.G, color.B);
		hsl.L = Math.Min(1.0, Math.Max(0.0, hsl.L * multiplier));
		hsl.A = color.A;
		return hsl.ToRgb();
	}

	private void ApplyThemeColors()
	{
		try
		{
			string accent1 = _accentColor1;
			string accent2 = _accentColor2;
			string bg1 = _bgGrad1;
			string bg2 = _bgGrad2;
			string bg3 = _bgGrad3;
			string cardBg = _cardBg;
			string sidebarBg = _sidebarBg;
			string topbarBg = _topbarBg;
			string bottombarBg = _bottombarBg;

			if (_useAdaptiveTheme && MainPlayerBarControl?.PlayerThumbnailRef?.Fill is ImageBrush brush && brush.ImageSource is BitmapSource bitmapSource)
			{
				AdaptiveThemeColors adaptive = ExtractThemeFromImage(bitmapSource);
				if (adaptive != null)
				{
					accent1 = adaptive.Accent1;
					accent2 = adaptive.Accent2;
					bg1 = adaptive.Bg1;
					bg2 = adaptive.Bg2;
					bg3 = adaptive.Bg3;
					cardBg = adaptive.CardBg;
					sidebarBg = adaptive.SidebarBg;
					topbarBg = adaptive.TopbarBg;
					bottombarBg = adaptive.BottombarBg;
				}
			}

			System.Windows.Media.Color a1 = (System.Windows.Media.Color)ColorConverter.ConvertFromString(accent1);
			System.Windows.Media.Color a2 = (System.Windows.Media.Color)ColorConverter.ConvertFromString(accent2);
			System.Windows.Media.Color b1 = AdjustBrightness((System.Windows.Media.Color)ColorConverter.ConvertFromString(bg1), _themeBrightness);
			System.Windows.Media.Color b2 = AdjustBrightness((System.Windows.Media.Color)ColorConverter.ConvertFromString(bg2), _themeBrightness);
			System.Windows.Media.Color b3 = AdjustBrightness((System.Windows.Media.Color)ColorConverter.ConvertFromString(bg3), _themeBrightness);
			System.Windows.Media.Color cardBgColor = AdjustBrightness((System.Windows.Media.Color)ColorConverter.ConvertFromString(cardBg), _themeBrightness);
			System.Windows.Media.Color sidebarColor = AdjustBrightness((System.Windows.Media.Color)ColorConverter.ConvertFromString(sidebarBg), _themeBrightness);
			System.Windows.Media.Color bottombarColor = AdjustBrightness((System.Windows.Media.Color)ColorConverter.ConvertFromString(bottombarBg), _themeBrightness);
			System.Windows.Media.Color topbarColor = AdjustBrightness((System.Windows.Media.Color)ColorConverter.ConvertFromString(topbarBg), _themeBrightness);

			if (System.Windows.Application.Current == null || System.Windows.Application.Current.MainWindow == null)
			{
				return;
			}

			if (base.IsLoaded)
			{
				_themeTransitionTimer?.Stop();

				_srcAccent1 = GetResourceColor("AccentGradient", 0, a1);
				_srcAccent2 = GetResourceColor("AccentGradient", 1, a2);
				_srcBg1 = GetResourceColor("MainBackground", 0, b1);
				_srcBg2 = GetResourceColor("MainBackground", 1, b2);
				_srcBg3 = GetResourceColor("MainBackground", 2, b3);
				_srcCardBg = GetResourceColor("CardBackground", 0, cardBgColor);
				_srcSidebarBg = GetResourceColor("SidebarBrush", 0, sidebarColor);
				_srcTopbarBg = GetResourceColor("TopbarBrush", 0, topbarColor);
				_srcBottombarBg = GetResourceColor("BottomBarBrush", 0, bottombarColor);

				_dstAccent1 = a1;
				_dstAccent2 = a2;
				_dstBg1 = b1;
				_dstBg2 = b2;
				_dstBg3 = b3;
				_dstCardBg = cardBgColor;
				_dstSidebarBg = sidebarColor;
				_dstTopbarBg = topbarColor;
				_dstBottombarBg = bottombarColor;

				_themeTransitionStep = 0;
				if (_themeTransitionTimer == null)
				{
					_themeTransitionTimer = new System.Windows.Threading.DispatcherTimer
					{
						Interval = TimeSpan.FromMilliseconds(30.0)
					};
					_themeTransitionTimer.Tick += ThemeTransitionTimer_Tick;
				}
				_themeTransitionTimer.Start();
			}
			else
			{
				ApplyThemeColorsInstant(a1, a2, b1, b2, b3, cardBgColor, sidebarColor, bottombarColor, topbarColor);
			}
		}
		catch (Exception ex)
		{
			AppLogger.Log("Outer Theme error: " + ex.ToString(), LogLevel.Error);
		}
	}

	private void ThemeTransitionTimer_Tick(object? sender, EventArgs e)
	{
		_themeTransitionStep++;
		double t = (double)_themeTransitionStep / ThemeTransitionMaxSteps;
		t = t * (2.0 - t); // Ease out quadratic

		if (t >= 1.0)
		{
			_themeTransitionTimer?.Stop();
			ApplyThemeColorsInstant(_dstAccent1, _dstAccent2, _dstBg1, _dstBg2, _dstBg3, _dstCardBg, _dstSidebarBg, _dstBottombarBg, _dstTopbarBg, updateDwm: true);
		}
		else
		{
			System.Windows.Media.Color a1 = LerpColor(_srcAccent1, _dstAccent1, t);
			System.Windows.Media.Color a2 = LerpColor(_srcAccent2, _dstAccent2, t);
			System.Windows.Media.Color b1 = LerpColor(_srcBg1, _dstBg1, t);
			System.Windows.Media.Color b2 = LerpColor(_srcBg2, _dstBg2, t);
			System.Windows.Media.Color b3 = LerpColor(_srcBg3, _dstBg3, t);
			System.Windows.Media.Color cardBgColor = LerpColor(_srcCardBg, _dstCardBg, t);
			System.Windows.Media.Color sidebarColor = LerpColor(_srcSidebarBg, _dstSidebarBg, t);
			System.Windows.Media.Color topbarColor = LerpColor(_srcTopbarBg, _dstTopbarBg, t);
			System.Windows.Media.Color bottombarColor = LerpColor(_srcBottombarBg, _dstBottombarBg, t);

			ApplyThemeColorsInstant(a1, a2, b1, b2, b3, cardBgColor, sidebarColor, bottombarColor, topbarColor, updateDwm: false);
		}
	}

	private void ApplyThemeColorsInstant(
		System.Windows.Media.Color a1, System.Windows.Media.Color a2,
		System.Windows.Media.Color b1, System.Windows.Media.Color b2, System.Windows.Media.Color b3,
		System.Windows.Media.Color cardBgColor, System.Windows.Media.Color sidebarColor,
		System.Windows.Media.Color bottombarColor, System.Windows.Media.Color topbarColor,
		bool updateDwm = true)
	{
		try
		{
			_hoverBorderColor = System.Windows.Media.Color.FromArgb(70, a1.R, a1.G, a1.B);

			System.Windows.Media.Color hoverC1 = System.Windows.Media.Color.FromArgb(byte.MaxValue, (byte)Math.Min(255, a1.R + 50), (byte)Math.Min(255, a1.G + 50), (byte)Math.Min(255, a1.B + 50));
			System.Windows.Media.Color hoverC2 = System.Windows.Media.Color.FromArgb(byte.MaxValue, (byte)Math.Min(255, a2.R + 50), (byte)Math.Min(255, a2.G + 50), (byte)Math.Min(255, a2.B + 50));

			System.Windows.Media.Color contextMenuColor = System.Windows.Media.Color.FromRgb(topbarColor.R, topbarColor.G, topbarColor.B);
			System.Windows.Media.Color loadingColor = System.Windows.Media.Color.FromArgb(byte.MaxValue, (byte)(b1.R / 2), (byte)(b1.G / 2), (byte)(b1.B / 2));
			System.Windows.Media.Color cardHoverColor = System.Windows.Media.Color.FromArgb((byte)Math.Min(255, cardBgColor.A + 20), cardBgColor.R, cardBgColor.G, cardBgColor.B);
			System.Windows.Media.Color cardPressedColor = System.Windows.Media.Color.FromArgb((byte)Math.Min(255, cardBgColor.A + 40), cardBgColor.R, cardBgColor.G, cardBgColor.B);
			System.Windows.Media.Color activePlayingColor = System.Windows.Media.Color.FromArgb(18, a1.R, a1.G, a1.B);
			System.Windows.Media.Color searchOverlayColor = System.Windows.Media.Color.FromArgb(230, b3.R, b3.G, b3.B);

			LinearGradientBrush accentBrush = new LinearGradientBrush { StartPoint = new System.Windows.Point(0.0, 0.0), EndPoint = new System.Windows.Point(1.0, 1.0) };
			accentBrush.GradientStops.Add(new GradientStop(a1, 0.0));
			accentBrush.GradientStops.Add(new GradientStop(a2, 1.0));
			accentBrush.Freeze();

			LinearGradientBrush hoverBrush = new LinearGradientBrush { StartPoint = new System.Windows.Point(0.0, 0.0), EndPoint = new System.Windows.Point(1.0, 1.0) };
			hoverBrush.GradientStops.Add(new GradientStop(hoverC1, 0.0));
			hoverBrush.GradientStops.Add(new GradientStop(hoverC2, 1.0));
			hoverBrush.Freeze();

			LinearGradientBrush bgBrush = new LinearGradientBrush { StartPoint = new System.Windows.Point(0.0, 0.0), EndPoint = new System.Windows.Point(0.0, 1.0) };
			bgBrush.GradientStops.Add(new GradientStop(b1, 0.0));
			bgBrush.GradientStops.Add(new GradientStop(b2, 0.4));
			bgBrush.GradientStops.Add(new GradientStop(b3, 1.0));
			bgBrush.Freeze();

			LinearGradientBrush playingBorderBrush = new LinearGradientBrush { StartPoint = new System.Windows.Point(0.0, 0.0), EndPoint = new System.Windows.Point(1.0, 1.0), Opacity = 0.4 };
			playingBorderBrush.GradientStops.Add(new GradientStop(a1, 0.0));
			playingBorderBrush.GradientStops.Add(new GradientStop(a2, 1.0));
			playingBorderBrush.Freeze();

			LinearGradientBrush topbarFadeBrush = new LinearGradientBrush { StartPoint = new System.Windows.Point(0.0, 0.0), EndPoint = new System.Windows.Point(0.0, 1.0) };
			topbarFadeBrush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(250, b1.R, b1.G, b1.B), 0.0));
			topbarFadeBrush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(250, b1.R, b1.G, b1.B), 0.38));
			topbarFadeBrush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(200, b1.R, b1.G, b1.B), 0.54));
			topbarFadeBrush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(120, b1.R, b1.G, b1.B), 0.74));
			topbarFadeBrush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0, b1.R, b1.G, b1.B), 1.0));
			topbarFadeBrush.Freeze();

			SolidColorBrush cardBrush = new SolidColorBrush(cardBgColor);
			cardBrush.Freeze();

			SolidColorBrush sidebarBrush = new SolidColorBrush(sidebarColor);
			sidebarBrush.Freeze();

			SolidColorBrush bottomBrush = new SolidColorBrush(bottombarColor);
			bottomBrush.Freeze();

			SolidColorBrush topBrush = new SolidColorBrush(topbarColor);
			topBrush.Freeze();

			SolidColorBrush contextMenuBrush = new SolidColorBrush(contextMenuColor);
			contextMenuBrush.Freeze();

			SolidColorBrush loadingBrush = new SolidColorBrush(loadingColor);
			loadingBrush.Freeze();

			SolidColorBrush hoverCardBrush = new SolidColorBrush(cardHoverColor);
			hoverCardBrush.Freeze();

			SolidColorBrush pressCardBrush = new SolidColorBrush(cardPressedColor);
			pressCardBrush.Freeze();

			SolidColorBrush activeBrush = new SolidColorBrush(activePlayingColor);
			activeBrush.Freeze();

			SolidColorBrush searchBrush = new SolidColorBrush(searchOverlayColor);
			searchBrush.Freeze();

			System.Windows.Media.Color toastBgColor;
			System.Windows.Media.Color toastBorderColor;
			if (b1.R + b1.G + b1.B > 600)
			{
				toastBgColor = System.Windows.Media.Color.FromArgb(240, b1.R, b1.G, b1.B);
				toastBorderColor = System.Windows.Media.Color.FromArgb(48, 0, 0, 0);
			}
			else
			{
				toastBgColor = System.Windows.Media.Color.FromArgb(240, b1.R, b1.G, b1.B);
				toastBorderColor = System.Windows.Media.Color.FromArgb(48, 255, 255, 255);
			}
			SolidColorBrush toastBgBrush = new SolidColorBrush(toastBgColor);
			toastBgBrush.Freeze();
			SolidColorBrush toastBorderBrush = new SolidColorBrush(toastBorderColor);
			toastBorderBrush.Freeze();

			var resources = System.Windows.Application.Current.MainWindow.Resources;
			resources["AccentGradient"] = accentBrush;
			resources["AccentGradientHover"] = hoverBrush;
			resources["MainBackground"] = bgBrush;
			resources["PlayingBorderBrush"] = playingBorderBrush;
			resources["TopbarFadeBrush"] = topbarFadeBrush;

			resources["CardBackground"] = cardBrush;
			resources["SidebarBrush"] = sidebarBrush;
			resources["BottomBarBrush"] = bottomBrush;
			resources["TopbarBrush"] = topBrush;
			resources["ContextMenuBackground"] = contextMenuBrush;
			resources["LoadingOverlayBrush"] = loadingBrush;
			resources["CardHoverBrush"] = hoverCardBrush;
			resources["CardPressedBrush"] = pressCardBrush;
			resources["ActivePlayingBrush"] = activeBrush;
			resources["SearchOverlayBackground"] = searchBrush;
			resources["ToastBackground"] = toastBgBrush;
			resources["ToastBorderBrush"] = toastBorderBrush;

			if (updateDwm)
			{
				try
				{
					nint handle = new System.Windows.Interop.WindowInteropHelper(System.Windows.Application.Current.MainWindow).Handle;
					int colorRef = topbarColor.R | (topbarColor.G << 8) | (topbarColor.B << 16);
					DwmSetWindowAttribute(handle, 35, ref colorRef, 4);
				}
				catch (Exception ex)
				{
					AppLogger.Log("DwmTheme error: " + ex.ToString(), LogLevel.Error);
				}
			}

			if (!string.IsNullOrEmpty(_currentVideoId))
			{
				HighlightNowPlaying(_currentVideoId);
			}
		}
		catch (Exception ex)
		{
			AppLogger.Log("Theme apply error: " + ex.ToString(), LogLevel.Error);
		}
	}

	private AdaptiveThemeColors? ExtractThemeFromImage(BitmapSource bmp)
	{
		try
		{
			FormatConvertedBitmap cb = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0.0);
			int width = cb.PixelWidth;
			int height = cb.PixelHeight;
			int stride = width * 4;
			byte[] pixels = new byte[height * stride];
			cb.CopyPixels(pixels, stride, 0);

			const int numBins = 18;
			int[] hueBinCounts = new int[numBins];
			double[] hueBinSatSums = new double[numBins];
			double[] hueBinLigSums = new double[numBins];
			double[] hueBinRedSums = new double[numBins];
			double[] hueBinGreenSums = new double[numBins];
			double[] hueBinBlueSums = new double[numBins];

			int validPixelCount = 0;
			double totalR = 0, totalG = 0, totalB = 0;

			// Sample a grid of pixels (max 4000 pixels) to avoid performance lag while preserving color correctness
			int step = Math.Max(1, (pixels.Length / 4) / 4000);
			int byteStep = step * 4;

			for (int i = 0; i < pixels.Length; i += byteStep)
			{
				byte b = pixels[i];
				byte g = pixels[i + 1];
				byte r = pixels[i + 2];

				totalR += r;
				totalG += g;
				totalB += b;

				HslColor hsl = HslColor.FromRgb(r, g, b);
				if (hsl.S > 0.25 && hsl.L > 0.15 && hsl.L < 0.85)
				{
					int bin = (int)(hsl.H / (360.0 / numBins)) % numBins;
					hueBinCounts[bin]++;
					hueBinSatSums[bin] += hsl.S;
					hueBinLigSums[bin] += hsl.L;
					validPixelCount++;
				}
			}

			int numPixels = pixels.Length / (4 * step);
			if (numPixels <= 0) numPixels = 1;

			HslColor accentHsl;
			int dominantBin = -1;
			int maxCount = 0;
			for (int i = 0; i < numBins; i++)
			{
				if (hueBinCounts[i] > maxCount)
				{
					maxCount = hueBinCounts[i];
					dominantBin = i;
				}
			}

			// Only consider a hue bin dominant if it represents at least 2% of the sampled pixels
			if (dominantBin != -1 && maxCount > (numPixels * 0.02))
			{
				double avgS = hueBinSatSums[dominantBin] / maxCount;
				double avgL = hueBinLigSums[dominantBin] / maxCount;
				double avgH = (dominantBin * (360.0 / numBins)) + (360.0 / numBins / 2.0);
				
				double accentS = Math.Min(1.0, avgS * 1.2);
				double accentL = Math.Min(0.75, Math.Max(0.40, avgL));
				
				accentHsl = new HslColor { H = avgH, S = accentS, L = accentL, A = 255 };
			}
			else
			{
				byte avgR = (byte)(totalR / numPixels);
				byte avgG = (byte)(totalG / numPixels);
				byte avgB = (byte)(totalB / numPixels);
				accentHsl = HslColor.FromRgb(avgR, avgG, avgB);
				
				if (accentHsl.S < 0.25)
				{
					accentHsl.S = 0; // Snap to grayscale to prevent muddy colors
					accentHsl.L = Math.Min(0.85, Math.Max(0.45, accentHsl.L));
				}
				else
				{
					accentHsl.S = Math.Min(1.0, accentHsl.S * 1.2);
					accentHsl.L = Math.Min(0.75, Math.Max(0.40, accentHsl.L));
				}
			}

			System.Windows.Media.Color accColor1 = accentHsl.ToRgb();

			HslColor accentHsl2 = accentHsl;
			accentHsl2.H = (accentHsl.H + 40.0) % 360.0;
			if (accentHsl.S == 0)
			{
				accentHsl2.L = Math.Max(0.2, accentHsl.L - 0.2);
			}
			System.Windows.Media.Color accColor2 = accentHsl2.ToRgb();

			double bgSatMult = accentHsl.S;

			HslColor bgHsl1 = new HslColor { H = accentHsl.H, S = 0.32 * bgSatMult, L = 0.12, A = 255 };
			HslColor bgHsl2 = new HslColor { H = accentHsl.H, S = 0.22 * bgSatMult, L = 0.08, A = 255 };
			HslColor bgHsl3 = new HslColor { H = accentHsl.H, S = 0.14 * bgSatMult, L = 0.05, A = 255 };

			System.Windows.Media.Color bg1 = bgHsl1.ToRgb();
			System.Windows.Media.Color bg2 = bgHsl2.ToRgb();
			System.Windows.Media.Color bg3 = bgHsl3.ToRgb();

			HslColor sidebarHsl = new HslColor { H = accentHsl.H, S = 0.25 * bgSatMult, L = 0.07, A = 208 };
			HslColor topbarHsl = new HslColor { H = accentHsl.H, S = 0.24 * bgSatMult, L = 0.11, A = 176 };
			HslColor bottombarHsl = new HslColor { H = accentHsl.H, S = 0.20 * bgSatMult, L = 0.08, A = 224 };

			System.Windows.Media.Color sidebar = sidebarHsl.ToRgb();
			System.Windows.Media.Color topbar = topbarHsl.ToRgb();
			System.Windows.Media.Color bottombar = bottombarHsl.ToRgb();

			return new AdaptiveThemeColors
			{
				Accent1 = accColor1.ToString(),
				Accent2 = accColor2.ToString(),
				Bg1 = bg1.ToString(),
				Bg2 = bg2.ToString(),
				Bg3 = bg3.ToString(),
				CardBg = "#0CFFFFFF",
				SidebarBg = sidebar.ToString(),
				TopbarBg = topbar.ToString(),
				BottombarBg = bottombar.ToString()
			};
		}
		catch
		{
			return null;
		}
	}

	private class AdaptiveThemeColors
	{
		public string Accent1 { get; set; } = "";
		public string Accent2 { get; set; } = "";
		public string Bg1 { get; set; } = "";
		public string Bg2 { get; set; } = "";
		public string Bg3 { get; set; } = "";
		public string CardBg { get; set; } = "";
		public string SidebarBg { get; set; } = "";
		public string TopbarBg { get; set; } = "";
		public string BottombarBg { get; set; } = "";
	}

	protected override void OnClosed(EventArgs e)
	{
		SaveSession();
		base.OnClosed(e);
	}

	private System.Windows.Media.Color LerpColor(System.Windows.Media.Color from, System.Windows.Media.Color to, double t)
	{
		byte a = (byte)(from.A + (to.A - from.A) * t);
		byte r = (byte)(from.R + (to.R - from.R) * t);
		byte g = (byte)(from.G + (to.G - from.G) * t);
		byte b = (byte)(from.B + (to.B - from.B) * t);
		return System.Windows.Media.Color.FromArgb(a, r, g, b);
	}

	private System.Windows.Media.Color GetResourceColor(string key, int stopIndex, System.Windows.Media.Color fallback)
	{
		try
		{
			var resources = System.Windows.Application.Current?.MainWindow?.Resources;
			if (resources != null && resources.Contains(key))
			{
				var res = resources[key];
				if (res is SolidColorBrush scb)
				{
					return scb.Color;
				}
				if (res is LinearGradientBrush lgb && stopIndex < lgb.GradientStops.Count)
				{
					return lgb.GradientStops[stopIndex].Color;
				}
			}
		}
		catch
		{
		}
		return fallback;
	}

}






