using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Spectre.Views;

public partial class SearchOverlayControl : UserControl
{
	public event EventHandler<string>? RecentSearchClicked;

	public event EventHandler<string>? RecentSearchRemoved;

	public SearchOverlayControl()
	{
		InitializeComponent();
	}

	public void UpdateRecentSearches(List<string> searches)
	{
		RecentSearchesWrapPanel.Children.Clear();
		if (searches.Count == 0)
		{
			RecentSearchesPanel.Visibility = Visibility.Collapsed;
			return;
		}
		RecentSearchesPanel.Visibility = Visibility.Visible;
		foreach (string s in searches)
		{
			Border border = new Border
			{
				Background = new SolidColorBrush(Color.FromArgb(20, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
				CornerRadius = new CornerRadius(16.0),
				Padding = new Thickness(15.0, 8.0, 15.0, 8.0),
				Margin = new Thickness(0.0, 0.0, 10.0, 10.0),
				Cursor = Cursors.Hand
			};
			StackPanel sp = new StackPanel
			{
				Orientation = Orientation.Horizontal
			};
			TextBlock tb = new TextBlock
			{
				Text = s,
				Foreground = (SolidColorBrush)Application.Current.MainWindow.Resources["TextBrush"],
				VerticalAlignment = VerticalAlignment.Center
			};
			sp.Children.Add(tb);
			TextBlock removeBtn = new TextBlock
			{
				Text = "✕",
				Foreground = (SolidColorBrush)Application.Current.MainWindow.Resources["TextSecondaryBrush"],
				FontSize = 10.0,
				Margin = new Thickness(10.0, 0.0, 0.0, 0.0),
				VerticalAlignment = VerticalAlignment.Center,
				Cursor = Cursors.Hand
			};
			removeBtn.MouseEnter += delegate
			{
				removeBtn.Foreground = Brushes.White;
			};
			removeBtn.MouseLeave += delegate
			{
				removeBtn.Foreground = (SolidColorBrush)Application.Current.MainWindow.Resources["TextSecondaryBrush"];
			};
			removeBtn.MouseLeftButtonDown += delegate(object sdr, MouseButtonEventArgs ev)
			{
				ev.Handled = true;
				this.RecentSearchRemoved?.Invoke(this, s);
			};
			sp.Children.Add(removeBtn);
			border.Child = sp;
			border.MouseEnter += delegate
			{
				border.Background = new SolidColorBrush(Color.FromArgb(40, byte.MaxValue, byte.MaxValue, byte.MaxValue));
			};
			border.MouseLeave += delegate
			{
				border.Background = new SolidColorBrush(Color.FromArgb(20, byte.MaxValue, byte.MaxValue, byte.MaxValue));
			};
			border.MouseLeftButtonDown += delegate
			{
				this.RecentSearchClicked?.Invoke(this, s);
			};
			RecentSearchesWrapPanel.Children.Add(border);
		}
	}

	public void Show()
	{
		if (base.Visibility != Visibility.Visible)
		{
			base.Visibility = Visibility.Visible;
			DoubleAnimation anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150.0));
			BeginAnimation(UIElement.OpacityProperty, anim);
		}
	}

	public void Hide(bool force = false)
	{
		if (base.Visibility != Visibility.Collapsed || force)
		{
			DoubleAnimation anim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150.0));
			anim.Completed += delegate
			{
				base.Visibility = Visibility.Collapsed;
			};
			BeginAnimation(UIElement.OpacityProperty, anim);
		}
	}

	private void SearchOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		Keyboard.ClearFocus();
		Hide();
	}
}
