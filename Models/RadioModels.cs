namespace NexusM.Models;

public class RadioStation
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Country { get; set; } = "";
    public string Genre { get; set; } = "";
    public string StreamUrl { get; set; } = "";
    public string Description { get; set; } = "";
    public string Logo { get; set; } = "";
    public bool IsFavourite { get; set; }
    public int PlayCount { get; set; }
}
