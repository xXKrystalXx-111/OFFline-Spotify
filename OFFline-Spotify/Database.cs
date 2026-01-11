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
        
        public string? Description { get; set; }
        
        public string? Owner { get; set; }
        
        public int TotalTracks { get; set; }
        
        public DateTime CreatedAt { get; set; }
    }

    [Table("Songs")]
    public class SongEntity : INotifyPropertyChanged
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        
        public int PlaylistId { get; set; }
        public string? SpotifyId { get; set; }
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public string? Mp3FilePath { get; set; }
        public int DurationMs { get; set; }
        public DateTime CreatedAt { get; set; }  // Add this property
        
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
            // No blocking initialization in constructor
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized) 
                return;

            // Ensure only one initialization process at a time
            await _initializationSemaphore.WaitAsync();
            
            try
            {
                // Double-check pattern
                if (_isInitialized) 
                    return;

                Debug.WriteLine($"Starting database initialization for: {_databasePath}");
                
                // Check for file existence and handle corruption
                if (File.Exists(_databasePath))
                {
                    Debug.WriteLine("Database file exists, checking integrity");
                    
                    try
                    {
                        _database = new SQLiteAsyncConnection(_databasePath, SQLiteOpenFlags.ReadWrite);
                        // Test the connection with a simple query
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
                        
                        // Delete the corrupt file
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
                
                // Create a new database if needed
                if (_database == null)
                {
                    Debug.WriteLine("Creating new database");
                    _database = new SQLiteAsyncConnection(_databasePath, SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite);
                    
                    // Create tables
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

        // Add a method to check if database is ready
        public bool IsInitialized() => _isInitialized;

        // Ensure all public methods check for initialization
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
            
            // Check if playlist already exists
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
                // Insert the playlist and get the actual ID
                await _database!.InsertAsync(playlist);
                Debug.WriteLine($"Created new playlist with ID: {playlist.Id}");
                return playlist.Id;  // ✅ SQLite automatically sets the Id property after insert
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
            
            // Delete all songs in the playlist first
            await _database!.Table<SongEntity>()
                .Where(s => s.PlaylistId == playlistId)
                .DeleteAsync();
            
            // Delete the playlist
            return await _database!.Table<PlaylistEntity>()
                .Where(p => p.Id == playlistId)
                .DeleteAsync();
        }

        // Add this method to Database class after DeletePlaylistAsync
        public async Task DeletePlaylistBySpotifyIdAsync(string? spotifyId)
        {
            if (string.IsNullOrEmpty(spotifyId))
                return;
                
            await EnsureInitialized();
            
            // Find playlist by SpotifyId
            var playlist = await _database!.Table<PlaylistEntity>()
                .Where(p => p.SpotifyId == spotifyId)
                .FirstOrDefaultAsync();
            
            if (playlist != null)
            {
                Debug.WriteLine($"Deleting playlist with SpotifyId: {spotifyId}, DbId: {playlist.Id}");
                
                // Delete all songs in the playlist first
                await _database!.Table<SongEntity>()
                    .Where(s => s.PlaylistId == playlist.Id)
                    .DeleteAsync();
                
                // Delete the playlist
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
            
            // Check if song already exists in this playlist
            var existingSong = await _database!.Table<SongEntity>()
                .Where(s => s.PlaylistId == song.PlaylistId && s.SpotifyId == song.SpotifyId)
                .FirstOrDefaultAsync();
            
            if (existingSong != null)
            {
                song.Id = existingSong.Id;
                await _database!.UpdateAsync(song);
                return song.Id;
            }
            else
            {
                return await _database!.InsertAsync(song);
            }
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

        // Batch operations for better performance
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
        public async Task<int> SavePlaylistWithSongsAsync(PlaylistInfo? playlistInfo, string? playlistImagePath, string mp3FolderPath)
        {
            if (playlistInfo == null)
                throw new ArgumentNullException(nameof(playlistInfo));
                
            await EnsureInitialized();

            // ✅ DELETE EXISTING PLAYLIST FIRST (if it exists)
            await DeletePlaylistBySpotifyIdAsync(playlistInfo.Id);
            Debug.WriteLine($"Deleted any existing playlist with SpotifyId: {playlistInfo.Id}");

            // Create playlist entity
            var playlistEntity = new PlaylistEntity
            {
                SpotifyId = playlistInfo.Id,
                Name = playlistInfo.Name,
                ImagePath = playlistImagePath,
                Description = playlistInfo.Description,
                Owner = playlistInfo.Owner,
                TotalTracks = playlistInfo.TotalTracks
            };

            // Save playlist (will create new since we deleted the old one)
            var playlistId = await SavePlaylistAsync(playlistEntity);
            playlistEntity.Id = playlistId;
            
            Debug.WriteLine($"Created new playlist with DbId: {playlistId}");

            // Create song entities
            var songEntities = new List<SongEntity>();
            foreach (var track in playlistInfo.Tracks)
            {
                // Try to find the corresponding MP3 file
                string? mp3FilePath = FindMp3File(mp3FolderPath, track.Name, track.ArtistsString);
                
                Debug.WriteLine($"Track: '{track.Name}' by '{track.ArtistsString}' -> MP3: {mp3FilePath ?? "NOT FOUND"}");
                
                var songEntity = new SongEntity
                {
                    PlaylistId = playlistId,  // ✅ Now uses the correct new playlist ID
                    SpotifyId = track.Id,
                    Title = track.Name,
                    Artist = track.ArtistsString,
                    Album = track.Album,
                    Mp3FilePath = mp3FilePath,
                    DurationMs = track.DurationMs
                };
                
                songEntities.Add(songEntity);
            }

            // Save all songs
            foreach (var song in songEntities)
            {
                await SaveSongAsync(song);
            }
            
            Debug.WriteLine($"Saved {songEntities.Count} songs to playlist {playlistId}");

            return playlistId;
        }

        // Helper method to find MP3 file in the folder
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

        // Clean up
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
