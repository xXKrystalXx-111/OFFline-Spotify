using SQLite;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;

public class DatabaseService
{
    private readonly SQLiteAsyncConnection _db;

    public DatabaseService(string dbPath)
    {
        _db = new SQLiteAsyncConnection(dbPath);
        _db.CreateTableAsync<Playlist>().Wait();
        _db.CreateTableAsync<Song>().Wait();
        _db.CreateTableAsync<PlaylistSong>().Wait();
    }
    public async Task CreatePlaylistTableAsync(string playlistName)
    {
        string tableName = playlistName.Replace(" ", "_").Replace("-", "_");
        string sql = $"CREATE TABLE IF NOT EXISTS [{tableName}] (Id INTEGER PRIMARY KEY AUTOINCREMENT, Title TEXT, Artist TEXT)";
        await _db.ExecuteAsync(sql);
    }

    public async Task InsertSongAsync(string playlistName, string title, string artist)
    {
        string tableName = playlistName.Replace(" ", "_").Replace("-", "_");
        string sql = $"INSERT INTO [{tableName}] (Title, Artist) VALUES (?, ?)";
        await _db.ExecuteAsync(sql, title, artist);
    }
    public async Task CreatePlaylistTableAsync(SQLiteAsyncConnection db, string playlistName)
    {
        // Sanitize playlistName to be a valid table name
        string tableName = playlistName.Replace(" ", "_").Replace("-", "_");
        string sql = $"CREATE TABLE IF NOT EXISTS [{tableName}] (Id INTEGER PRIMARY KEY AUTOINCREMENT, Title TEXT, Artist TEXT)";
        await db.ExecuteAsync(sql);
    }

    public async Task InsertSongAsync(SQLiteAsyncConnection db, string playlistName, string title, string artist)
    {
        string tableName = playlistName.Replace(" ", "_").Replace("-", "_");
        string sql = $"INSERT INTO [{tableName}] (Title, Artist) VALUES (?, ?)";
        await db.ExecuteAsync(sql, title, artist);
    }
    public string ExtractPlaylistId(string url)
    {
        var match = System.Text.RegularExpressions.Regex.Match(url, @"playlist/([a-zA-Z0-9]+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    // Example: Assign files to songs
    public void AssignFilesToSongs(List<Song> songs, string mp3Directory)
    {
        var files = Directory.GetFiles(mp3Directory, "*.mp3");

        foreach (var song in songs)
        {
            // Normalize title for matching (remove punctuation, lower case)
            string normalizedTitle = Regex.Replace(song.Title, @"[^\w\s]", "").ToLower();

            var match = files.FirstOrDefault(f =>
                Regex.Replace(Path.GetFileNameWithoutExtension(f), @"[^\w\s]", "").ToLower()
                .Contains(normalizedTitle)
            );

            if (match != null)
            {
                song.FileName = Path.GetFileName(match); // or use full path if preferred
                // Optionally, update in database here
            }
        }
    }
    public async Task UpdateSongFileNameAsync(int songId, string fileName)
    {
        var song = await _db.Table<Song>().Where(s => s.SongId == songId).FirstOrDefaultAsync();
        if (song != null)
        {
            song.FileName = fileName;
            await _db.UpdateAsync(song);
        }
    }


}

public class Playlist
{
    [PrimaryKey, AutoIncrement]
    public int PlaylistId { get; set; }
    public string Name { get; set; }
}

public class Song
{
    [PrimaryKey, AutoIncrement]
    public int SongId { get; set; }
    public string Title { get; set; }
    public string Artist { get; set; }
    public string FileName { get; set; } // Add this property
}

public class PlaylistSong
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public int PlaylistId { get; set; }
    public int SongId { get; set; }
    public int Position { get; set; }
}

