using SQLite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.ComponentModel;

namespace OFFline_Spotify
{
    // Database models
    [Table("Playlists")]
    public class PlaylistEntity
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        
        [Unique]
        public string? SpotifyId { get; set; }
        
        [MaxLength(500)]
        public string? Name { get; set; }
        
        public string? ImagePath { get; set; }
        
        public int TotalTracks { get; set; }
        
        public DateTime CreatedAt { get; set; }
    }

    [Table("Songs")]
    public class SongEntity : INotifyPropertyChanged
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        
        public int PlaylistId { get; set; }
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Mp3FilePath { get; set; }
        public int DurationMs { get; set; }
        public DateTime CreatedAt { get; set; }
        
        private bool _isCurrentlyPlaying;
        
        [Ignore]
        public bool IsCurrentlyPlaying 
        { 
            get => _isCurrentlyPlaying;
            set
            {
                if (_isCurrentlyPlaying != value)
                {
                    _isCurrentlyPlaying = value;
                    OnPropertyChanged(nameof(IsCurrentlyPlaying));
                }
            }
        }
        
        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        
        public string? SpotifyLink { get; set; }
    }

    // Database service
    public class Database
    {
        private SQLiteAsyncConnection? _database;
        private readonly string _databasePath;
        private readonly SemaphoreSlim _initializationSemaphore = new SemaphoreSlim(1, 1);
        private bool _isInitialized = false;

        public Database(string databasePath)
        {
            _databasePath = databasePath;
            Debug.WriteLine($"Database constructor called with path: {databasePath}");
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized) 
                return;

            await _initializationSemaphore.WaitAsync();
            
            try
            {
                if (_isInitialized) 
                    return;

                Debug.WriteLine($"Starting database initialization for: {_databasePath}");
                
                if (File.Exists(_databasePath))
                {
                    Debug.WriteLine("Database file exists, checking integrity");
                    
                    try
                    {
                        _database = new SQLiteAsyncConnection(_databasePath, SQLiteOpenFlags.ReadWrite);
                        await _database.ExecuteScalarAsync<int>("SELECT 1");
                        Debug.WriteLine("Existing database verified successfully");
                    }
                    catch (SQLiteException ex)
                    {
                        Debug.WriteLine($"Database corruption detected: {ex.Message}");
                        
                        if (_database != null)
                        {
                            await _database.CloseAsync();
                            _database = null;
                        }
                        
                        try 
                        {
                            File.Delete(_databasePath);
                            Debug.WriteLine("Corrupted database deleted");
                        }
                        catch (Exception fileEx)
                        {
                            Debug.WriteLine($"Failed to delete corrupted database: {fileEx.Message}");
                        }
                    }
                }
                
                if (_database == null)
                {
                    Debug.WriteLine("Creating new database");
                    _database = new SQLiteAsyncConnection(_databasePath, SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite);
                    
                    await _database.CreateTableAsync<PlaylistEntity>();
                    await _database.CreateTableAsync<SongEntity>();
                    Debug.WriteLine("Database tables created successfully");
                }
                
                _isInitialized = true;
                Debug.WriteLine("Database initialization completed successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Database initialization failed with error: {ex.Message}");
                throw;
            }
            finally
            {
                _initializationSemaphore.Release();
            }
        }

        public bool IsInitialized() => _isInitialized;

        private async Task EnsureInitialized()
        {
            if (!_isInitialized)
            {
                await InitializeAsync();
            }
        }

        // Playlist operations
        public async Task<int> SavePlaylistAsync(PlaylistEntity playlist)
        {
            await EnsureInitialized();
            
            playlist.CreatedAt = DateTime.UtcNow;
            
            var existingPlaylist = await _database!.Table<PlaylistEntity>()
                .Where(p => p.SpotifyId == playlist.SpotifyId)
                .FirstOrDefaultAsync();
            
            if (existingPlaylist != null)
            {
                playlist.Id = existingPlaylist.Id;
                await _database!.UpdateAsync(playlist);
                Debug.WriteLine($"Updated existing playlist with ID: {playlist.Id}");
                return playlist.Id;
            }
            else
            {
                await _database!.InsertAsync(playlist);
                Debug.WriteLine($"Created new playlist with ID: {playlist.Id}");
                return playlist.Id;
            }
        }

        public async Task<List<PlaylistEntity>> GetAllPlaylistsAsync()
        {
            await EnsureInitialized();
            return await _database!.Table<PlaylistEntity>().ToListAsync();
        }

        public async Task<PlaylistEntity?> GetPlaylistByIdAsync(int id)
        {
            await EnsureInitialized();
            return await _database!.Table<PlaylistEntity>()
                .Where(p => p.Id == id)
                .FirstOrDefaultAsync();
        }

        public async Task<PlaylistEntity?> GetPlaylistBySpotifyIdAsync(string? spotifyId)
        {
            if (string.IsNullOrEmpty(spotifyId))
                return null;
                
            await EnsureInitialized();
            return await _database!.Table<PlaylistEntity>()
                .Where(p => p.SpotifyId == spotifyId)
                .FirstOrDefaultAsync();
        }

        public async Task<int> DeletePlaylistAsync(int playlistId)
        {
            await EnsureInitialized();
            
            await _database!.Table<SongEntity>()
                .Where(s => s.PlaylistId == playlistId)
                .DeleteAsync();
            
            return await _database!.Table<PlaylistEntity>()
                .Where(p => p.Id == playlistId)
                .DeleteAsync();
        }

        public async Task DeletePlaylistBySpotifyIdAsync(string? spotifyId)
        {
            if (string.IsNullOrEmpty(spotifyId))
                return;
                
            await EnsureInitialized();
            
            var playlist = await _database!.Table<PlaylistEntity>()
                .Where(p => p.SpotifyId == spotifyId)
                .FirstOrDefaultAsync();
            
            if (playlist != null)
            {
                Debug.WriteLine($"Deleting playlist with SpotifyId: {spotifyId}, DbId: {playlist.Id}");
                
                await _database!.Table<SongEntity>()
                    .Where(s => s.PlaylistId == playlist.Id)
                    .DeleteAsync();
                
                await _database!.Table<PlaylistEntity>()
                    .Where(p => p.Id == playlist.Id)
                    .DeleteAsync();
                    
                Debug.WriteLine($"Playlist {spotifyId} deleted successfully");
            }
        }

        // Song operations
        public async Task<int> SaveSongAsync(SongEntity song)
        {
            await EnsureInitialized();

            song.CreatedAt = DateTime.UtcNow;
            song.SpotifyLink = string.IsNullOrWhiteSpace(song.SpotifyLink) ? null : song.SpotifyLink.Trim();

            SongEntity? existingSong;

            if (!string.IsNullOrEmpty(song.SpotifyLink))
            {
                existingSong = await _database!.Table<SongEntity>()
                    .Where(s => s.PlaylistId == song.PlaylistId &&
                                s.SpotifyLink == song.SpotifyLink)
                    .FirstOrDefaultAsync();
            }
            else
            {
                existingSong = await _database!.Table<SongEntity>()
                    .Where(s => s.PlaylistId == song.PlaylistId &&
                                s.Title == song.Title &&
                                s.Artist == song.Artist)
                    .FirstOrDefaultAsync();
            }

            if (existingSong != null)
            {
                song.Id = existingSong.Id;
                await _database!.UpdateAsync(song);
                return song.Id;
            }

            return await _database!.InsertAsync(song);
        }

        public async Task<List<SongEntity>> GetSongsByPlaylistIdAsync(int playlistId)
        {
            await EnsureInitialized();
            return await _database!.Table<SongEntity>()
                .Where(s => s.PlaylistId == playlistId)
                .ToListAsync();
        }

        public async Task<SongEntity?> GetSongByIdAsync(int id)
        {
            await EnsureInitialized();
            return await _database!.Table<SongEntity>()
                .Where(s => s.Id == id)
                .FirstOrDefaultAsync();
        }

        public async Task<int> DeleteSongAsync(int songId)
        {
            await EnsureInitialized();
            return await _database!.Table<SongEntity>()
                .Where(s => s.Id == songId)
                .DeleteAsync();
        }

        public async Task SaveSongsAsync(List<SongEntity> songs)
        {
            await EnsureInitialized();
            
            foreach (var song in songs)
            {
                song.CreatedAt = DateTime.UtcNow;
            }
            
            await _database!.InsertAllAsync(songs);
        }

        // Helper method to save complete playlist with songs from SpotifyService
        
        private string? FindMp3File(string? folderPath, string? trackName, string? artist)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return null;

            if (string.IsNullOrEmpty(trackName))
                return null;

            try
            {
                var mp3Files = Directory.GetFiles(folderPath, "*.mp3", SearchOption.AllDirectories);
                
                // Clean up track name and artist for better matching
                string cleanTrackName = CleanForMatching(trackName);
                string cleanArtist = CleanForMatching(artist ?? "");
                
                Debug.WriteLine($"Looking for track: '{trackName}' by '{artist}' in {mp3Files.Length} files");
                
                // Priority 1: Exact track name + artist match
                foreach (var file in mp3Files)
                {
                    var fileName = CleanForMatching(Path.GetFileNameWithoutExtension(file));
                    Debug.WriteLine($"Checking file: {Path.GetFileName(file)} -> cleaned: {fileName}");
                    
                    if (fileName.Contains(cleanTrackName) && !string.IsNullOrEmpty(cleanArtist) && fileName.Contains(cleanArtist))
                    {
                        Debug.WriteLine($"MATCH (track+artist): {file}");
                        return file;
                    }
                }
                
                // Priority 2: Track name match only (most common case for spotdown.app)
                foreach (var file in mp3Files)
                {
                    var fileName = CleanForMatching(Path.GetFileNameWithoutExtension(file));
                    
                    if (fileName.Contains(cleanTrackName))
                    {
                        Debug.WriteLine($"MATCH (track only): {file}");
                        return file;
                    }
                }
                
                // Priority 3: Artist match only (fallback)
                if (!string.IsNullOrEmpty(cleanArtist))
                {
                    foreach (var file in mp3Files)
                    {
                        var fileName = CleanForMatching(Path.GetFileNameWithoutExtension(file));
                        
                        if (fileName.Contains(cleanArtist))
                        {
                            Debug.WriteLine($"MATCH (artist only): {file}");
                            return file;
                        }
                    }
                }
                
                // Priority 4: Fuzzy matching for special characters
                foreach (var file in mp3Files)
                {
                    var fileName = CleanForMatching(Path.GetFileNameWithoutExtension(file));
                    
                    if (IsFuzzyMatch(cleanTrackName, fileName))
                    {
                        Debug.WriteLine($"MATCH (fuzzy): {file}");
                        return file;
                    }
                }
                
                Debug.WriteLine($"No match found for '{trackName}' by '{artist}'");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error finding MP3 file: {ex.Message}");
            }

            return null;
        }

        // Helper method to clean strings for matching
        private string CleanForMatching(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;
                
            return input.ToLower()
                .Replace("?", "")           // Remove question marks
                .Replace("!", "")           // Remove exclamation marks
                .Replace("'", "")           // Remove apostrophes
                .Replace("\"", "")          // Remove quotes
                .Replace("(", "")           // Remove parentheses
                .Replace(")", "")
                .Replace("[", "")           // Remove brackets
                .Replace("]", "")
                .Replace("__spotdown.app", "") // Remove spotdown suffix
                .Replace("-", " ")          // Replace dashes with spaces
                .Replace("_", " ")          // Replace underscores with spaces
                .Trim();
        }

        // Helper method for fuzzy matching
        private bool IsFuzzyMatch(string? trackName, string? fileName)
        {
            if (string.IsNullOrEmpty(trackName) || string.IsNullOrEmpty(fileName))
                return false;
                
            // Remove spaces and compare
            string trackNoSpaces = trackName.Replace(" ", "");
            string fileNoSpaces = fileName.Replace(" ", "");
            
            return fileNoSpaces.Contains(trackNoSpaces) || trackNoSpaces.Contains(fileNoSpaces);
        }

        public async Task CloseAsync()
        {
            if (_database != null)
            {
                await _database.CloseAsync();
                _database = null;
            }
        }
    }
}
