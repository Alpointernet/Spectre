namespace Spectre.ViewModels;

public class PlayQueueItemMessage
{
	public int TargetIndex { get; set; }

	public string VideoId { get; set; } = string.Empty;

	public string Title { get; set; } = string.Empty;

	public string Artist { get; set; } = string.Empty;

	public string ThumbnailUrl { get; set; } = string.Empty;
}
