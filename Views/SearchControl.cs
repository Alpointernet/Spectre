using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Spectre.Views;

public partial class SearchControl : UserControl
{
	public event EventHandler<string>? SearchRequested;

	public event EventHandler<EventArgs>? SearchBoxGotFocus;

	public event EventHandler<EventArgs>? SearchBoxLostFocus;

	public SearchControl()
	{
		InitializeComponent();
	}

	public void ClearFocus()
	{
		Keyboard.ClearFocus();
		(TryFindResource("LostFocusAnim") as Storyboard)?.Begin();
		this.SearchBoxLostFocus?.Invoke(this, EventArgs.Empty);
	}

	public void SetText(string text)
	{
		SearchBox.Text = text;
		SearchBox.SelectionStart = text.Length;
	}

	private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
	{
		(TryFindResource("FocusAnim") as Storyboard)?.Begin();
		this.SearchBoxGotFocus?.Invoke(this, EventArgs.Empty);
	}

	private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
	{
		(TryFindResource("LostFocusAnim") as Storyboard)?.Begin();
		this.SearchBoxLostFocus?.Invoke(this, EventArgs.Empty);
	}

	private void SearchBox_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			string query = SearchBox.Text;
			if (!string.IsNullOrWhiteSpace(query) && !(query == "Search"))
			{
				ClearFocus();
				this.SearchRequested?.Invoke(this, query);
			}
		}
	}

	private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (string.IsNullOrEmpty(SearchBox.Text))
		{
			SearchPlaceholder.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.5, TimeSpan.FromMilliseconds(150.0)));
		}
		else
		{
			SearchPlaceholder.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150.0)));
		}
	}
}
