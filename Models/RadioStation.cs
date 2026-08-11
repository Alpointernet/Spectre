using System.Collections.Generic;

namespace Spectre;

public class RadioStation
{
	public string Name { get; set; } = "";

	public string Description { get; set; } = "";

	public string ThumbnailUrl { get; set; } = "";

	public List<RadioStream> Streams { get; set; } = new List<RadioStream>();
}
