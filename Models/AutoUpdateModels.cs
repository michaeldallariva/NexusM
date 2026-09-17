namespace NexusM.Models;

public class UpdateManifest
{
    public string Version { get; set; } = "";
    public string ReleaseDate { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public string MinVersion { get; set; } = "";
    /// <summary>Base URL for all files, e.g. https://nexusm.org/releases/2026.21</summary>
    public string BaseUrl { get; set; } = "";
    public Dictionary<string, UpdatePackage> Packages { get; set; } = new();
    /// <summary>Relative paths of shared files to download (wwwroot + assets/lang).</summary>
    public List<string> Files { get; set; } = new();
}

public class UpdatePackage
{
    /// <summary>Filename of the platform binary, e.g. "NexusM.exe" or "NexusM".</summary>
    public string Binary { get; set; } = "";
    /// <summary>SHA256 of the binary only (lowercase hex). Empty = skip verification.</summary>
    public string BinarySha256 { get; set; } = "";
    /// <summary>Size of the binary in bytes - used for display and disk-space check.</summary>
    public long SizeBytes { get; set; }
}

public class UpdateStatus
{
    public string CurrentVersion { get; set; } = "";
    public string? AvailableVersion { get; set; }
    public bool UpdateAvailable { get; set; }
    public bool IsDownloading { get; set; }
    public int DownloadProgress { get; set; }
    /// <summary>idle | downloading | verifying | ready | applying</summary>
    public string DownloadState { get; set; } = "idle";
    public string DownloadLabel { get; set; } = "";
    public bool StagingReady { get; set; }
    public bool HasBackup { get; set; }
    public string? BackupVersion { get; set; }
    public bool IsDocker { get; set; }
    public bool Enabled { get; set; }
    public int CheckIntervalHours { get; set; }
    public string Channel { get; set; } = "stable";
    public string? LastChecked { get; set; }
    public string? ReleaseNotes { get; set; }
    public string? ReleaseDate { get; set; }
    public long? PackageSizeBytes { get; set; }
    public string? ErrorMessage { get; set; }
}
