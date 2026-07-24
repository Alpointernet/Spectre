using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Spectre.Views;

public partial class SidebarTab : UserControl
{
	public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(SidebarTab), new PropertyMetadata(""));

	public static readonly DependencyProperty IconSourceProperty = DependencyProperty.Register("IconSource", typeof(System.Windows.Media.ImageSource), typeof(SidebarTab), new PropertyMetadata(null, OnIconSourceChanged));

	public static readonly DependencyProperty PageIdProperty = DependencyProperty.Register("PageId", typeof(string), typeof(SidebarTab), new PropertyMetadata(""));

	public static readonly DependencyProperty CommandProperty = DependencyProperty.Register("Command", typeof(ICommand), typeof(SidebarTab), new PropertyMetadata(null));

	public string Text
	{
		get
		{
			return (string)GetValue(TextProperty);
		}
		set
		{
			SetValue(TextProperty, value);
		}
	}

	public ICommand Command
	{
		get
		{
			return (ICommand)GetValue(CommandProperty);
		}
		set
		{
			SetValue(CommandProperty, value);
		}
	}

	public System.Windows.Media.ImageSource IconSource
	{
		get
		{
			return (System.Windows.Media.ImageSource)GetValue(IconSourceProperty);
		}
		set
		{
			SetValue(IconSourceProperty, value);
		}
	}

	public string PageId
	{
		get
		{
			return (string)GetValue(PageIdProperty);
		}
		set
		{
			SetValue(PageIdProperty, value);
		}
	}

	public Border GetMainBorder()
	{
		return MainBorder;
	}

	public TextBlock GetTitleText()
	{
		return TitleText;
	}

	public SidebarTab()
	{
		InitializeComponent();
	}

	private static void OnIconSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is SidebarTab tab && e.NewValue is System.Windows.Media.ImageSource source)
		{
			tab.IconViewbox.Source = source;
		}
	}

	public void SetTextOpacity(double opacity)
	{
		TitleText.BeginAnimation(UIElement.OpacityProperty, null);
		TitleText.Opacity = opacity;
	}

	public void UpdateHighlight(string currentPageId, Action<Border, Color> fadeBackground, Action<TextBlock, Color> fadeText)
	{
		if (PageId == currentPageId)
		{
			MainBorder.SetResourceReference(Border.BackgroundProperty, "CardHoverBrush");
			TitleText.Foreground = (SolidColorBrush)Application.Current.MainWindow.Resources["TextBrush"];
		}
		else
		{
			fadeBackground(MainBorder, Colors.Transparent);
			fadeText(TitleText, ((SolidColorBrush)Application.Current.MainWindow.Resources["SidebarTextBrush"]).Color);
		}
	}
}
