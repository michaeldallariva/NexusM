using System.ComponentModel.DataAnnotations;

namespace NexusM.Models;

/// <summary>
/// Represents a network share with encrypted credentials stored in shares.db.
/// </summary>
public class NetworkShare
{
    [Key]
    public int Id { get; set; }

    /// <summary>UNC path: \\server\share (Windows) or //server/share (Linux)</summary>
    [Required]
    public string SharePath { get; set; } = "";

    /// <summary>Media type this share is for: music, moviestv, musicvideos, pictures, ebooks</summary>
    public string FolderType { get; set; } = "";

    /// <summary>Local mount point. Windows: drive letter (Z:). Linux: /mnt/nexusm/sharename</summary>
    public string MountPoint { get; set; } = "";

    /// <summary>Share protocol: smb, nfs</summary>
    public string ShareType { get; set; } = "smb";

    /// <summary>Username for authentication</summary>
    public string Username { get; set; } = "";

    /// <summary>AES-256-GCM encrypted password, base64-encoded (nonce + ciphertext + tag)</summary>
    public string EncryptedPassword { get; set; } = "";

    /// <summary>Optional domain for SMB auth</summary>
    public string Domain { get; set; } = "";

    /// <summary>Additional mount options (e.g., vers=3.0)</summary>
    public string MountOptions { get; set; } = "";

    /// <summary>Whether this share should be auto-mounted on startup</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether the share is currently mounted</summary>
    public bool IsMounted { get; set; } = false;

    /// <summary>Last error message from mount attempt</summary>
    public string LastError { get; set; } = "";

    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    public DateTime? LastMounted { get; set; }
}
