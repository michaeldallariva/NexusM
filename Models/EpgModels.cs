namespace NexusM.Models;

public class EpgSource
{
    public int Id { get; set; }
    public string Url { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>True when the URL was auto-extracted from an M3U x-tvg-url header.</summary>
    public bool IsAuto { get; set; } = false;
    public DateTime? LastFetched { get; set; }
    public int ProgrammeCount { get; set; }
    /// <summary>pending | fetching | ok | error</summary>
    public string Status { get; set; } = "pending";
    public string? ErrorMessage { get; set; }
    public int SortOrder { get; set; } = 0;
}

/// <summary>Channel display names from XMLTV &lt;channel&gt; elements - used for name-based fallback matching.</summary>
public class EpgChannelName
{
    public int    Id              { get; set; }
    public string TvgId          { get; set; } = "";
    public string DisplayName    { get; set; } = "";
    /// <summary>Lowercase alphanumeric only - used for fuzzy name matching.</summary>
    public string DisplayNameNorm { get; set; } = "";
    public int    SourceId       { get; set; }
}

public class EpgProgramme
{
    public int Id { get; set; }
    public string ChannelTvgId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime Stop { get; set; }
    public string Category { get; set; } = "";
    public int SourceId { get; set; }
}
