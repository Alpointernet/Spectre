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
	private void InitDiscordRPC()
	{
		if (_discordManager == null)
		{
			string id = (string.IsNullOrWhiteSpace(_discordClientId) ? "1507766775104671996" : _discordClientId.Trim());
			_discordManager = new DiscordManager(id, _discordIconUrl, _enableDiscordRpc);
		}
		_discordManager.SetEnabled(_enableDiscordRpc);
	}

	private void DeinitDiscordRPC()
	{
		_discordManager?.Dispose();
	}

	private void UpdateDiscordRPC()
	{
		bool isPlaying = _playbackTimer.IsEnabled;
		_discordManager?.UpdatePresence(_currentTitle, _currentArtist, _currentAlbum, _currentThumbUrl, isPlaying, _player.Time, _player.Length);
	}
}


