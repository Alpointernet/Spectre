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
	private void ToggleLogOverlay()
	{
		if (_logVisible)
		{
			HideLogOverlay();
		}
		else
		{
			ShowLogOverlay();
		}
	}

	private void ShowLogOverlay()
	{
		_logVisible = true;
		MainOverlayControl.LogOverlayRef.Visibility = Visibility.Visible;
		MainOverlayControl.LogOverlayRef.Opacity = 1.0;
		MainOverlayControl.LogRichTextBoxRef.ScrollToEnd();
	}

	private void HideLogOverlay()
	{
		_logVisible = false;
		MainOverlayControl.LogOverlayRef.Visibility = Visibility.Collapsed;
	}

	private void OnLogReceived(LogMessage entry)
	{
		base.Dispatcher.InvokeAsync(delegate
		{
			AddLogToRichTextBox(entry);
		});
	}

	private void AddLogToRichTextBox(LogMessage entry)
	{
		var paragraph = new Paragraph();
		var run = new Run(entry.Formatted);
		
		switch (entry.Level)
		{
			case LogLevel.Warning:
				run.Foreground = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString("#FFD166"));
				break;
			case LogLevel.Error:
				run.Foreground = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString("#EF476F"));
				break;
			case LogLevel.Success:
				run.Foreground = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString("#06D6A0"));
				break;
			case LogLevel.Info:
			default:
				run.Foreground = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString("#E8E8E8"));
				break;
		}

		paragraph.Inlines.Add(run);
		var doc = MainOverlayControl.LogRichTextBoxRef.Document;
		doc.Blocks.Add(paragraph);

		if (doc.Blocks.Count > 300)
		{
			doc.Blocks.Remove(doc.Blocks.FirstBlock);
		}

		if (_logVisible)
		{
			MainOverlayControl.LogRichTextBoxRef.ScrollToEnd();
		}
	}

	private void CloseLogButton_Click(object sender, RoutedEventArgs e)
	{
		HideLogOverlay();
	}

	private void CheckLoginStatus()
	{
		if (System.IO.File.Exists(BackendService.AuthFilePath))
		{
			MainTopbarControl.LoginBtnRef.ToolTip = "Logged In";
		}
	}

	private async void LoginBtn_Click(object sender, RoutedEventArgs e)
	{
		await OpenAccountPageAsync();
	}

	private string? ShowColorPickerDialog(string currentColor)
	{
		ColorDialog dlg = new ColorDialog
		{
			FullOpen = true
		};
		try
		{
			System.Windows.Media.Color c = (System.Windows.Media.Color)ColorConverter.ConvertFromString(currentColor);
			dlg.Color = System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
		}
		catch
		{
		}
		if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
		{
			string hex = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
			if (currentColor.Length > 7)
			{
				try
				{
					System.Windows.Media.Color oldC = (System.Windows.Media.Color)ColorConverter.ConvertFromString(currentColor);
					hex = $"#{oldC.A:X2}{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
				}
				catch
				{
				}
			}
			return hex;
		}
		return null;
	}
}


