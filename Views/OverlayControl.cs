using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Spectre.Views;

public partial class OverlayControl : UserControl
{
	public Grid LargeCoverOverlayRef => LargeCoverOverlay;

	public Border LargeCoverBorderRef => LargeCoverBorder;

	public ScaleTransform LargeCoverScaleRef => LargeCoverScale;

	public Rectangle LargeCoverRectRef => LargeCoverRect;

	public ProgressBar TopLoadingBarRef => TopLoadingBar;

	public Grid LoadingOverlayRef => LoadingOverlay;

	public Grid LoadingOverlayContentContainerRef => LoadingOverlayContentContainer;

	public void StopLoadingAnimations()
	{
		LoadingStoryboardBegin.Storyboard.Stop(LoadingOverlay);
	}

	public void StartLoadingAnimations()
	{
		LoadingStoryboardBegin.Storyboard.Begin(LoadingOverlay, true);
	}

	public Grid CreditsOverlayRef => CreditsOverlay;

	public Border CreditsDialogBorderRef => CreditsDialogBorder;

	public ScrollViewer CreditsScrollViewerRef => CreditsScrollViewer;

	public StackPanel CreditsPanelRef => CreditsPanel;

	public TextBlock CreditsLoadingTextRef => CreditsLoadingText;

	public Grid GlobalErrorOverlayRef => GlobalErrorOverlay;

	public TextBlock GlobalErrorTitleRef => GlobalErrorTitle;

	public TextBlock GlobalErrorDetailsRef => GlobalErrorDetails;

	public Border GlobalErrorRetryBtnRef => GlobalErrorRetryBtn;

	public Grid LogOverlayRef => LogOverlay;

	public RichTextBox LogRichTextBoxRef => LogRichTextBox;

	public Border ToastBorderRef => ToastBorder;

	public TranslateTransform ToastTransformRef => ToastTransform;

	public TextBlock ToastTextRef => ToastText;

	public Canvas ConfettiCanvasRef => ConfettiCanvas;

	public event MouseButtonEventHandler? LargeCoverOverlay_MouseLeftButtonDown_Event;

	public event MouseButtonEventHandler? CreditsOverlay_MouseLeftButtonDown_Event;

	public event MouseButtonEventHandler? CreditsBorder_MouseLeftButtonDown_Event;

	public event RoutedEventHandler? CloseCreditsBtn_Click_Event;

	public event MouseWheelEventHandler? CreditsScrollViewer_PreviewMouseWheel_Event;

	public event MouseEventHandler? GlobalErrorRetryBtn_MouseEnter_Event;

	public event MouseEventHandler? GlobalErrorRetryBtn_MouseLeave_Event;

	public event MouseButtonEventHandler? GlobalErrorRetryBtn_Click_Event;

	public event RoutedEventHandler? CloseLogButton_Click_Event;

	public OverlayControl()
	{
		InitializeComponent();
	}

	private void LargeCoverOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		this.LargeCoverOverlay_MouseLeftButtonDown_Event?.Invoke(sender, e);
	}

	private void CreditsOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		this.CreditsOverlay_MouseLeftButtonDown_Event?.Invoke(sender, e);
	}

	private void CreditsBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		this.CreditsBorder_MouseLeftButtonDown_Event?.Invoke(sender, e);
	}

	private void CloseCreditsBtn_Click(object sender, RoutedEventArgs e)
	{
		this.CloseCreditsBtn_Click_Event?.Invoke(sender, e);
	}

	private void CreditsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		this.CreditsScrollViewer_PreviewMouseWheel_Event?.Invoke(sender, e);
	}

	private void GlobalErrorRetryBtn_MouseEnter(object sender, MouseEventArgs e)
	{
		this.GlobalErrorRetryBtn_MouseEnter_Event?.Invoke(sender, e);
	}

	private void GlobalErrorRetryBtn_MouseLeave(object sender, MouseEventArgs e)
	{
		this.GlobalErrorRetryBtn_MouseLeave_Event?.Invoke(sender, e);
	}

	private void GlobalErrorRetryBtn_Click(object sender, MouseButtonEventArgs e)
	{
		this.GlobalErrorRetryBtn_Click_Event?.Invoke(sender, e);
	}

	private void CloseLogButton_Click(object sender, RoutedEventArgs e)
	{
		this.CloseLogButton_Click_Event?.Invoke(sender, e);
	}
}
