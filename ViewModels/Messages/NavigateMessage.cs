namespace Spectre.ViewModels;

public class NavigateMessage
{
	public string PageId { get; set; }

	public NavigateMessage(string pageId)
	{
		PageId = pageId;
	}
}
