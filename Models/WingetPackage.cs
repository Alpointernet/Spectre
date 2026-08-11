namespace Spectre.Models;

public class WingetPackage
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Match { get; set; } = string.Empty;
}
