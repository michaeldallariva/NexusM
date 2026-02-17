using Microsoft.Data.Sqlite;

namespace NexusM.Services;

/// <summary>
/// Manages per-user favourites and play counts stored in users/{username}.db SQLite databases.
/// Each user has their own database with:
///   Favourites(Id, MediaType, MediaId, DateAdded, UNIQUE(MediaType, MediaId))
///   PlayCounts(Id, MediaType, MediaId, Count, LastPlayed, UNIQUE(MediaType, MediaId))
/// MediaType values: "track", "musicvideo", "video", "radio"
/// </summary>
public class UserFavouritesService
{
    private readonly ILogger<UserFavouritesService> _logger;
    private readonly string _usersDir;

    public UserFavouritesService(ILogger<UserFavouritesService> logger)
    {
        _logger = logger;
        _usersDir = Path.Combine(AppContext.BaseDirectory, "users");
    }

    /// <summary>
    /// Toggle a favourite for the given user. Returns the new favourite state.
    /// </summary>
    public bool ToggleFavourite(string username, string mediaType, int mediaId)
    {
        using var conn = OpenUserDb(username);
        if (conn == null) return false;

        // Check if already favourited
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT Id FROM Favourites WHERE MediaType = @type AND MediaId = @id";
        checkCmd.Parameters.AddWithValue("@type", mediaType);
        checkCmd.Parameters.AddWithValue("@id", mediaId);
        var existing = checkCmd.ExecuteScalar();

        if (existing != null)
        {
            // Remove favourite
            using var delCmd = conn.CreateCommand();
            delCmd.CommandText = "DELETE FROM Favourites WHERE MediaType = @type AND MediaId = @id";
            delCmd.Parameters.AddWithValue("@type", mediaType);
            delCmd.Parameters.AddWithValue("@id", mediaId);
            delCmd.ExecuteNonQuery();
            return false;
        }
        else
        {
            // Add favourite
            using var insCmd = conn.CreateCommand();
            insCmd.CommandText = "INSERT OR IGNORE INTO Favourites (MediaType, MediaId) VALUES (@type, @id)";
            insCmd.Parameters.AddWithValue("@type", mediaType);
            insCmd.Parameters.AddWithValue("@id", mediaId);
            insCmd.ExecuteNonQuery();
            return true;
        }
    }

    /// <summary>
    /// Check if a specific item is favourited by the user.
    /// </summary>
    public bool IsFavourite(string username, string mediaType, int mediaId)
    {
        using var conn = OpenUserDb(username);
        if (conn == null) return false;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM Favourites WHERE MediaType = @type AND MediaId = @id LIMIT 1";
        cmd.Parameters.AddWithValue("@type", mediaType);
        cmd.Parameters.AddWithValue("@id", mediaId);
        return cmd.ExecuteScalar() != null;
    }

    /// <summary>
    /// Get all favourite media IDs of a specific type for a user.
    /// </summary>
    public HashSet<int> GetFavouriteIds(string username, string mediaType)
    {
        var ids = new HashSet<int>();
        using var conn = OpenUserDb(username);
        if (conn == null) return ids;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MediaId FROM Favourites WHERE MediaType = @type ORDER BY DateAdded DESC";
        cmd.Parameters.AddWithValue("@type", mediaType);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            ids.Add(reader.GetInt32(0));

        return ids;
    }

    // ─── Play Counts ────────────────────────────────────────────────

    /// <summary>
    /// Increment the play count for a media item for the given user.
    /// Debounced: only increments if the last play was more than 5 minutes ago,
    /// preventing inflated counts from byte-range/HLS streaming requests.
    /// </summary>
    public void IncrementPlayCount(string username, string mediaType, int mediaId)
    {
        using var conn = OpenUserDb(username);
        if (conn == null) return;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO PlayCounts (MediaType, MediaId, Count, LastPlayed)
            VALUES (@type, @id, 1, datetime('now'))
            ON CONFLICT(MediaType, MediaId) DO UPDATE SET
                Count = CASE WHEN (julianday('now') - julianday(LastPlayed)) * 1440 > 5 THEN Count + 1 ELSE Count END,
                LastPlayed = datetime('now')";
        cmd.Parameters.AddWithValue("@type", mediaType);
        cmd.Parameters.AddWithValue("@id", mediaId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Get the most played media IDs of a specific type for a user, ordered by play count descending.
    /// Returns list of (MediaId, Count) tuples.
    /// </summary>
    public List<(int MediaId, int Count)> GetMostPlayed(string username, string mediaType, int limit = 10)
    {
        var results = new List<(int, int)>();
        using var conn = OpenUserDb(username);
        if (conn == null) return results;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MediaId, Count FROM PlayCounts WHERE MediaType = @type AND Count > 0 ORDER BY Count DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@type", mediaType);
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add((reader.GetInt32(0), reader.GetInt32(1)));

        return results;
    }

    /// <summary>
    /// Reset all play counts for a user (clears inflated data).
    /// </summary>
    public void ResetPlayCounts(string username)
    {
        using var conn = OpenUserDb(username);
        if (conn == null) return;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM PlayCounts";
        cmd.ExecuteNonQuery();
    }

    // ─── Watched Videos ────────────────────────────────────────────

    /// <summary>
    /// Mark a video as watched for the given user.
    /// </summary>
    public void MarkWatched(string username, int mediaId)
    {
        using var conn = OpenUserDb(username);
        if (conn == null) return;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO WatchedVideos (MediaId) VALUES (@id)";
        cmd.Parameters.AddWithValue("@id", mediaId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Mark a video as unwatched for the given user.
    /// </summary>
    public void MarkUnwatched(string username, int mediaId)
    {
        using var conn = OpenUserDb(username);
        if (conn == null) return;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM WatchedVideos WHERE MediaId = @id";
        cmd.Parameters.AddWithValue("@id", mediaId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Toggle watched state for a video. Returns new state (true = watched).
    /// </summary>
    public bool ToggleWatched(string username, int mediaId)
    {
        using var conn = OpenUserDb(username);
        if (conn == null) return false;

        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT 1 FROM WatchedVideos WHERE MediaId = @id LIMIT 1";
        checkCmd.Parameters.AddWithValue("@id", mediaId);
        var existing = checkCmd.ExecuteScalar();

        if (existing != null)
        {
            using var delCmd = conn.CreateCommand();
            delCmd.CommandText = "DELETE FROM WatchedVideos WHERE MediaId = @id";
            delCmd.Parameters.AddWithValue("@id", mediaId);
            delCmd.ExecuteNonQuery();
            return false;
        }
        else
        {
            using var insCmd = conn.CreateCommand();
            insCmd.CommandText = "INSERT OR IGNORE INTO WatchedVideos (MediaId) VALUES (@id)";
            insCmd.Parameters.AddWithValue("@id", mediaId);
            insCmd.ExecuteNonQuery();
            return true;
        }
    }

    /// <summary>
    /// Check if a video is watched by the user.
    /// </summary>
    public bool IsWatched(string username, int mediaId)
    {
        using var conn = OpenUserDb(username);
        if (conn == null) return false;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM WatchedVideos WHERE MediaId = @id LIMIT 1";
        cmd.Parameters.AddWithValue("@id", mediaId);
        return cmd.ExecuteScalar() != null;
    }

    /// <summary>
    /// Get all watched video IDs for a user (for bulk display).
    /// </summary>
    public HashSet<int> GetWatchedIds(string username)
    {
        var ids = new HashSet<int>();
        using var conn = OpenUserDb(username);
        if (conn == null) return ids;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MediaId FROM WatchedVideos ORDER BY WatchedAt DESC";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            ids.Add(reader.GetInt32(0));

        return ids;
    }

    // ─── Playlists ────────────────────────────────────────────────

    /// <summary>
    /// Get all playlists for a user with track counts.
    /// </summary>
    public List<Dictionary<string, object>> GetPlaylists(string username)
    {
        var results = new List<Dictionary<string, object>>();
        using var conn = OpenUserDb(username);
        if (conn == null) return results;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.Id, p.Name, p.Description, p.CoverImagePath, p.DateCreated, p.DateModified,
                   (SELECT COUNT(*) FROM PlaylistTracks WHERE PlaylistId = p.Id) AS TrackCount
            FROM Playlists p ORDER BY p.Name";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new Dictionary<string, object>
            {
                ["id"] = reader.GetInt32(0),
                ["name"] = reader.GetString(1),
                ["description"] = reader.IsDBNull(2) ? "" : reader.GetString(2),
                ["coverImagePath"] = reader.IsDBNull(3) ? "" : reader.GetString(3),
                ["dateCreated"] = reader.GetString(4),
                ["dateModified"] = reader.GetString(5),
                ["trackCount"] = reader.GetInt32(6)
            });
        }
        return results;
    }

    /// <summary>
    /// Get a single playlist with its track IDs and positions.
    /// </summary>
    public (Dictionary<string, object>? Playlist, List<Dictionary<string, object>> Tracks) GetPlaylist(string username, int playlistId)
    {
        using var conn = OpenUserDb(username);
        if (conn == null) return (null, new());

        // Get playlist info
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, Description, CoverImagePath, DateCreated, DateModified FROM Playlists WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", playlistId);

        Dictionary<string, object>? playlist = null;
        using (var reader = cmd.ExecuteReader())
        {
            if (reader.Read())
            {
                playlist = new Dictionary<string, object>
                {
                    ["id"] = reader.GetInt32(0),
                    ["name"] = reader.GetString(1),
                    ["description"] = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    ["coverImagePath"] = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    ["dateCreated"] = reader.GetString(4),
                    ["dateModified"] = reader.GetString(5)
                };
            }
        }

        if (playlist == null) return (null, new());

        // Get playlist tracks
        var tracks = new List<Dictionary<string, object>>();
        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "SELECT Id, TrackId, Position, DateAdded FROM PlaylistTracks WHERE PlaylistId = @id ORDER BY Position";
        cmd2.Parameters.AddWithValue("@id", playlistId);

        using var reader2 = cmd2.ExecuteReader();
        while (reader2.Read())
        {
            tracks.Add(new Dictionary<string, object>
            {
                ["id"] = reader2.GetInt32(0),
                ["trackId"] = reader2.GetInt32(1),
                ["position"] = reader2.GetInt32(2),
                ["dateAdded"] = reader2.GetString(3)
            });
        }

        return (playlist, tracks);
    }

    /// <summary>
    /// Create a new playlist for a user. Returns the new playlist info.
    /// </summary>
    public Dictionary<string, object>? CreatePlaylist(string username, string name, string? description)
    {
        using var conn = OpenUserDb(username);
        if (conn == null) return null;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO Playlists (Name, Description) VALUES (@name, @desc);
                            SELECT last_insert_rowid()";
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@desc", (object?)description ?? DBNull.Value);
        var id = Convert.ToInt32(cmd.ExecuteScalar());

        // Read back the created playlist
        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "SELECT Id, Name, Description, DateCreated FROM Playlists WHERE Id = @id";
        cmd2.Parameters.AddWithValue("@id", id);
        using var reader = cmd2.ExecuteReader();
        if (reader.Read())
        {
            return new Dictionary<string, object>
            {
                ["id"] = reader.GetInt32(0),
                ["name"] = reader.GetString(1),
                ["description"] = reader.IsDBNull(2) ? "" : reader.GetString(2),
                ["dateCreated"] = reader.GetString(3)
            };
        }
        return null;
    }

    /// <summary>
    /// Update a playlist's name and description.
    /// </summary>
    public Dictionary<string, object>? UpdatePlaylist(string username, int playlistId, string name, string? description)
    {
        using var conn = OpenUserDb(username);
        if (conn == null) return null;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE Playlists SET Name = @name, Description = @desc, DateModified = datetime('now')
                            WHERE Id = @id";
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@desc", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", playlistId);
        var rows = cmd.ExecuteNonQuery();
        if (rows == 0) return null;

        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "SELECT Id, Name, Description, DateModified FROM Playlists WHERE Id = @id";
        cmd2.Parameters.AddWithValue("@id", playlistId);
        using var reader = cmd2.ExecuteReader();
        if (reader.Read())
        {
            return new Dictionary<string, object>
            {
                ["id"] = reader.GetInt32(0),
                ["name"] = reader.GetString(1),
                ["description"] = reader.IsDBNull(2) ? "" : reader.GetString(2),
                ["dateModified"] = reader.GetString(3)
            };
        }
        return null;
    }

    /// <summary>
    /// Delete a playlist and all its track entries.
    /// </summary>
    public bool DeletePlaylist(string username, int playlistId)
    {
        using var conn = OpenUserDb(username);
        if (conn == null) return false;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM PlaylistTracks WHERE PlaylistId = @id";
        cmd.Parameters.AddWithValue("@id", playlistId);
        cmd.ExecuteNonQuery();

        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "DELETE FROM Playlists WHERE Id = @id";
        cmd2.Parameters.AddWithValue("@id", playlistId);
        return cmd2.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// Add a single track to a playlist. Returns the entry info or null if playlist not found.
    /// </summary>
    public Dictionary<string, object>? AddTrackToPlaylist(string username, int playlistId, int trackId)
    {
        using var conn = OpenUserDb(username);
        if (conn == null) return null;

        // Verify playlist exists
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT 1 FROM Playlists WHERE Id = @id";
        checkCmd.Parameters.AddWithValue("@id", playlistId);
        if (checkCmd.ExecuteScalar() == null) return null;

        // Get max position
        using var posCmd = conn.CreateCommand();
        posCmd.CommandText = "SELECT COALESCE(MAX(Position), 0) FROM PlaylistTracks WHERE PlaylistId = @id";
        posCmd.Parameters.AddWithValue("@id", playlistId);
        var maxPos = Convert.ToInt32(posCmd.ExecuteScalar());

        // Insert
        using var insCmd = conn.CreateCommand();
        insCmd.CommandText = @"INSERT OR IGNORE INTO PlaylistTracks (PlaylistId, TrackId, Position) VALUES (@plId, @trId, @pos);
                               SELECT last_insert_rowid()";
        insCmd.Parameters.AddWithValue("@plId", playlistId);
        insCmd.Parameters.AddWithValue("@trId", trackId);
        insCmd.Parameters.AddWithValue("@pos", maxPos + 1);
        var entryId = Convert.ToInt32(insCmd.ExecuteScalar());

        // Update playlist modified date
        using var updCmd = conn.CreateCommand();
        updCmd.CommandText = "UPDATE Playlists SET DateModified = datetime('now') WHERE Id = @id";
        updCmd.Parameters.AddWithValue("@id", playlistId);
        updCmd.ExecuteNonQuery();

        return new Dictionary<string, object>
        {
            ["id"] = entryId,
            ["playlistId"] = playlistId,
            ["trackId"] = trackId,
            ["position"] = maxPos + 1,
            ["message"] = "Track added"
        };
    }

    /// <summary>
    /// Add multiple tracks to a playlist in bulk.
    /// </summary>
    public (int added, bool found) AddTracksToPlaylist(string username, int playlistId, int[] trackIds)
    {
        using var conn = OpenUserDb(username);
        if (conn == null) return (0, false);

        // Verify playlist exists
        using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT 1 FROM Playlists WHERE Id = @id";
        checkCmd.Parameters.AddWithValue("@id", playlistId);
        if (checkCmd.ExecuteScalar() == null) return (0, false);

        // Get max position
        using var posCmd = conn.CreateCommand();
        posCmd.CommandText = "SELECT COALESCE(MAX(Position), 0) FROM PlaylistTracks WHERE PlaylistId = @id";
        posCmd.Parameters.AddWithValue("@id", playlistId);
        var maxPos = Convert.ToInt32(posCmd.ExecuteScalar());

        var added = 0;
        foreach (var trackId in trackIds)
        {
            maxPos++;
            using var insCmd = conn.CreateCommand();
            insCmd.CommandText = "INSERT OR IGNORE INTO PlaylistTracks (PlaylistId, TrackId, Position) VALUES (@plId, @trId, @pos)";
            insCmd.Parameters.AddWithValue("@plId", playlistId);
            insCmd.Parameters.AddWithValue("@trId", trackId);
            insCmd.Parameters.AddWithValue("@pos", maxPos);
            insCmd.ExecuteNonQuery();
            added++;
        }

        // Update playlist modified date
        using var updCmd = conn.CreateCommand();
        updCmd.CommandText = "UPDATE Playlists SET DateModified = datetime('now') WHERE Id = @id";
        updCmd.Parameters.AddWithValue("@id", playlistId);
        updCmd.ExecuteNonQuery();

        return (added, true);
    }

    /// <summary>
    /// Remove a specific track entry from a playlist.
    /// </summary>
    public bool RemovePlaylistTrack(string username, int playlistId, int entryId)
    {
        using var conn = OpenUserDb(username);
        if (conn == null) return false;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM PlaylistTracks WHERE Id = @entryId AND PlaylistId = @plId";
        cmd.Parameters.AddWithValue("@entryId", entryId);
        cmd.Parameters.AddWithValue("@plId", playlistId);
        var deleted = cmd.ExecuteNonQuery() > 0;

        if (deleted)
        {
            using var updCmd = conn.CreateCommand();
            updCmd.CommandText = "UPDATE Playlists SET DateModified = datetime('now') WHERE Id = @id";
            updCmd.Parameters.AddWithValue("@id", playlistId);
            updCmd.ExecuteNonQuery();
        }

        return deleted;
    }

    /// <summary>
    /// Get the total number of playlists for a user.
    /// </summary>
    public int GetPlaylistCount(string username)
    {
        using var conn = OpenUserDb(username);
        if (conn == null) return 0;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Playlists";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private SqliteConnection? OpenUserDb(string username)
    {
        try
        {
            var dbPath = Path.Combine(_usersDir, $"{username}.db");
            if (!File.Exists(dbPath))
            {
                _logger.LogWarning("User database not found: {Path}", dbPath);
                return null;
            }

            var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            // Ensure Favourites table exists (handles legacy databases without the table)
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS Favourites (Id INTEGER PRIMARY KEY AUTOINCREMENT, MediaType TEXT NOT NULL, MediaId INTEGER NOT NULL, DateAdded TEXT NOT NULL DEFAULT (datetime('now')), UNIQUE(MediaType, MediaId))";
            cmd.ExecuteNonQuery();

            // Ensure PlayCounts table exists
            using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = "CREATE TABLE IF NOT EXISTS PlayCounts (Id INTEGER PRIMARY KEY AUTOINCREMENT, MediaType TEXT NOT NULL, MediaId INTEGER NOT NULL, Count INTEGER NOT NULL DEFAULT 0, LastPlayed TEXT NOT NULL DEFAULT (datetime('now')), UNIQUE(MediaType, MediaId))";
            cmd2.ExecuteNonQuery();

            // Ensure WatchedVideos table exists
            using var cmd3 = conn.CreateCommand();
            cmd3.CommandText = "CREATE TABLE IF NOT EXISTS WatchedVideos (Id INTEGER PRIMARY KEY AUTOINCREMENT, MediaId INTEGER NOT NULL UNIQUE, WatchedAt TEXT NOT NULL DEFAULT (datetime('now')))";
            cmd3.ExecuteNonQuery();

            // Ensure Playlists table exists
            using var cmd4 = conn.CreateCommand();
            cmd4.CommandText = "CREATE TABLE IF NOT EXISTS Playlists (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Description TEXT, CoverImagePath TEXT, DateCreated TEXT NOT NULL DEFAULT (datetime('now')), DateModified TEXT NOT NULL DEFAULT (datetime('now')))";
            cmd4.ExecuteNonQuery();

            // Ensure PlaylistTracks table exists
            using var cmd5 = conn.CreateCommand();
            cmd5.CommandText = "CREATE TABLE IF NOT EXISTS PlaylistTracks (Id INTEGER PRIMARY KEY AUTOINCREMENT, PlaylistId INTEGER NOT NULL, TrackId INTEGER NOT NULL, Position INTEGER NOT NULL, DateAdded TEXT NOT NULL DEFAULT (datetime('now')), UNIQUE(PlaylistId, TrackId))";
            cmd5.ExecuteNonQuery();

            return conn;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open user database for: {Username}", username);
            return null;
        }
    }
}
