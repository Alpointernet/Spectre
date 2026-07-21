using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Spectre.Views;

public partial class TopbarControl : UserControl
{
	public Button BackBtnRef => BackBtn;

	public Button ForwardBtnRef => ForwardBtn;

	public SearchControl MainSearchControlRef => MainSearchControl;

	public Border TopbarBackgroundRef => TopbarBackground;

	public TextBlock StatusLabelRef => StatusLabel;

	public Border LyricsOffsetPanelRef => LyricsOffsetPanel;

	public TextBlock LyricsOffsetValueTextRef => LyricsOffsetValueText;

	public Button LoginBtnRef => LoginBtn;

	public event RoutedEventHandler? BackBtn_Click_Event;

	public event RoutedEventHandler? ForwardBtn_Click_Event;

	public event RoutedEventHandler? LoginBtn_Click_Event;

	public event RoutedEventHandler? HistoryBtn_Click_Event;

	public event RoutedEventHandler? SettingsBtn_Click_Event;

	public event MouseButtonEventHandler? LyricsOffsetMinusBtn_Click_Event;

	public event MouseButtonEventHandler? LyricsOffsetPlusBtn_Click_Event;

	public event EventHandler<string>? MainSearchControl_SearchRequested_Event;

	public event EventHandler? MainSearchControl_GotFocus_Event;

	public event EventHandler? MainSearchControl_LostFocus_Event;

	public TopbarControl()
	{
		InitializeComponent();
	}

	private void BackBtn_Click(object sender, RoutedEventArgs e)
	{
		this.BackBtn_Click_Event?.Invoke(sender, e);
	}

	private void ForwardBtn_Click(object sender, RoutedEventArgs e)
	{
		this.ForwardBtn_Click_Event?.Invoke(sender, e);
	}

	private void LoginBtn_Click(object sender, RoutedEventArgs e)
	{
		this.LoginBtn_Click_Event?.Invoke(sender, e);
	}

	private void HistoryBtn_Click(object sender, RoutedEventArgs e)
	{
		this.HistoryBtn_Click_Event?.Invoke(sender, e);
	}

	private void SettingsBtn_Click(object sender, RoutedEventArgs e)
	{
		this.SettingsBtn_Click_Event?.Invoke(sender, e);
	}

	private void LyricsOffsetMinusBtn_Click(object sender, MouseButtonEventArgs e)
	{
		this.LyricsOffsetMinusBtn_Click_Event?.Invoke(sender, e);
	}

	private void LyricsOffsetPlusBtn_Click(object sender, MouseButtonEventArgs e)
	{
		this.LyricsOffsetPlusBtn_Click_Event?.Invoke(sender, e);
	}

	private void MainSearchControl_SearchRequested(object sender, string e)
	{
		this.MainSearchControl_SearchRequested_Event?.Invoke(sender, e);
	}

	private void MainSearchControl_GotFocus(object sender, EventArgs e)
	{
		this.MainSearchControl_GotFocus_Event?.Invoke(sender, e);
	}

	private void MainSearchControl_LostFocus(object sender, EventArgs e)
	{
		this.MainSearchControl_LostFocus_Event?.Invoke(sender, e);
	}
}
