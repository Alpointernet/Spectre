using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;


namespace Spectre;

public partial class LoginWindow : Window
{
	private string _capturedAuthUser = "0";

	public bool IsLoginSuccessful { get; private set; }

	public LoginWindow()
	{
		InitializeComponent();
		InitializeWebViewAsync();
	}

	private async void InitializeWebViewAsync()
	{
		_ = 1;
		try
		{
			string userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spectre", "WebView2");
			CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
			await webView.EnsureCoreWebView2Async(env);
			webView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
			webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
			webView.CoreWebView2.WebResourceRequested += delegate(object? s, CoreWebView2WebResourceRequestedEventArgs e)
			{
				try
				{
					if (e.Request.Headers.Contains("x-goog-authuser"))
					{
						_capturedAuthUser = e.Request.Headers.GetHeader("x-goog-authuser");
					}
				}
				catch
				{
				}
			};
			webView.CoreWebView2.Navigate("https://music.youtube.com");
		}
		catch (Exception ex)
		{
			StatusTextBlock.Text = "Failed to initialize login window: " + ex.Message;
		}
	}

	private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
	{
		if (e.IsSuccess)
		{
			StatusTextBlock.Visibility = Visibility.Collapsed;
			webView.Visibility = Visibility.Visible;
			DoneButton.Visibility = Visibility.Visible;
		}
	}

	private async void DoneButton_Click(object sender, RoutedEventArgs e)
	{
		webView.Visibility = Visibility.Collapsed;
		DoneButton.Visibility = Visibility.Collapsed;
		StatusTextBlock.Visibility = Visibility.Visible;
		StatusTextBlock.Text = "Checking login status...";
		List<CoreWebView2Cookie> cookies = await webView.CoreWebView2.CookieManager.GetCookiesAsync("https://youtube.com");
		string loggedInStr = await webView.CoreWebView2.ExecuteScriptAsync("typeof ytcfg !== 'undefined' && ytcfg.get ? ytcfg.get('LOGGED_IN') === true : false");
		bool num = cookies.Any((CoreWebView2Cookie c) => c.Name == "SAPISID");
		bool hasLoginInfo = cookies.Any((CoreWebView2Cookie c) => c.Name == "LOGIN_INFO");
		if (num && hasLoginInfo && loggedInStr == "true")
		{
			StatusTextBlock.Text = "Login successful! Saving session...";
			string cookieString = string.Join("; ", cookies.Select((CoreWebView2Cookie c) => c.Name + "=" + c.Value));
			CoreWebView2Cookie sapisidCookie = cookies.FirstOrDefault((CoreWebView2Cookie c) => c.Name == "SAPISID");
			if (sapisidCookie != null)
			{
				string json = JsonSerializer.Serialize(new Dictionary<string, string>
				{
					{ "cookie", cookieString },
					{ "sapisid", sapisidCookie.Value },
					{
						"userAgent",
						webView.CoreWebView2.Settings.UserAgent
					},
					{ "authUser", _capturedAuthUser }
				}, new JsonSerializerOptions { WriteIndented = true });
				Directory.CreateDirectory(Path.GetDirectoryName(BackendService.AuthFilePath));
				BackendService.SaveAuthData(json);
				IsLoginSuccessful = true;
				Close();
			}
			else
			{
				StatusTextBlock.Text = "Failed to extract session data. Please try logging in again.";
				webView.Visibility = Visibility.Visible;
				DoneButton.Visibility = Visibility.Visible;
			}
		}
		else
		{
			StatusTextBlock.Text = "You don't appear to be logged in. Please log in first.";
			await Task.Delay(2000);
			StatusTextBlock.Visibility = Visibility.Collapsed;
			webView.Visibility = Visibility.Visible;
			DoneButton.Visibility = Visibility.Visible;
		}
	}
}
