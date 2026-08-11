using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Spectre.Services;

namespace Spectre;

public partial class LastFmLoginWindow : Window
{
	private string _currentToken = "";

	private DispatcherTimer? _pollTimer;

	private int _step = 1;

	public bool IsLoginSuccessful { get; private set; }

	public string ScrapedApiKey { get; private set; } = "";

	public string ScrapedSharedSecret { get; private set; } = "";

	public string ScrapedSessionKey { get; private set; } = "";

	public string ScrapedUsername { get; private set; } = "";

	public LastFmLoginWindow()
	{
		InitializeComponent();
		InitializeAsync();
	}

	private async void InitializeAsync()
	{
		string userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spectre", "WebView2");
		CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
		await webView.EnsureCoreWebView2Async(environment);
		webView.CoreWebView2.Navigate("https://www.last.fm/api/accounts");
	}

	private void DoneButton_Click(object sender, RoutedEventArgs e)
	{
		if (_step == 1)
		{
			webView.Visibility = Visibility.Collapsed;
			ManualEntryOverlay.Visibility = Visibility.Visible;
		}
	}

	private void ManualCancel_Click(object sender, RoutedEventArgs e)
	{
		ManualEntryOverlay.Visibility = Visibility.Collapsed;
		webView.Visibility = Visibility.Visible;
	}

	private async void ManualSubmit_Click(object sender, RoutedEventArgs e)
	{
		string ak = ManualApiKeyBox.Text.Trim();
		string ss = ManualSharedSecretBox.Text.Trim();
		if (string.IsNullOrEmpty(ak) || string.IsNullOrEmpty(ss))
		{
			MessageBox.Show("Please enter both an API Key and a Shared Secret.", "Missing Keys", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		ManualEntryOverlay.Visibility = Visibility.Collapsed;
		webView.Visibility = Visibility.Visible;
		ScrapedApiKey = ak;
		ScrapedSharedSecret = ss;
		_step = 2;
		StepText.Text = "Step 2: Authorize Spectre";
		DescText.Text = "Please click 'Yes, Allow Access' to connect Spectre to your Last.fm account.";
		DoneButton.Visibility = Visibility.Collapsed;
		_currentToken = await LastFmManager.GetAuthTokenAsync(ak, ss);
		if (!string.IsNullOrEmpty(_currentToken))
		{
			webView.CoreWebView2.Navigate("http://www.last.fm/api/auth/?api_key=" + ak + "&token=" + _currentToken);
			StartPolling();
			return;
		}
		MessageBox.Show("Failed to get auth token from Last.fm. Check your API Keys.", "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
		DoneButton.Visibility = Visibility.Visible;
		DoneButton.Content = "I have my keys";
		_step = 1;
	}

	private void StartPolling()
	{
		_pollTimer = new DispatcherTimer();
		_pollTimer.Interval = TimeSpan.FromSeconds(2.0);
		_pollTimer.Tick += async delegate
		{
			(string, string)? session = await LastFmManager.GetSessionAsync(ScrapedApiKey, ScrapedSharedSecret, _currentToken);
			if (session.HasValue)
			{
				_pollTimer.Stop();
				ScrapedSessionKey = session.Value.Item1;
				ScrapedUsername = session.Value.Item2;
				IsLoginSuccessful = true;
				Close();
			}
		};
		_pollTimer.Start();
	}

	protected override void OnClosed(EventArgs e)
	{
		_pollTimer?.Stop();
		base.OnClosed(e);
	}
}
