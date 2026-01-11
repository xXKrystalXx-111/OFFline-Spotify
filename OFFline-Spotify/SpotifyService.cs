using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using System.IO;

namespace OFFline_Spotify
{
    internal class SpotifyService
    {
        private readonly HttpClient _httpClient;
        private string? _accessToken;
        private readonly Database? _database;

        public SpotifyService(Database? database = null)
        {
            _httpClient = new HttpClient();
            _database = database;
        }

        public void SetAccessToken(string accessToken)
        {
            _accessToken = accessToken;
            _httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        }

        public async Task<PlaylistInfo?> GetPlaylistInfoAsync(string? playlistId)
        {
            if (string.IsNullOrEmpty(_accessToken))
                throw new InvalidOperationException("Access token not set. Call SetAccessToken() first.");

            if (string.IsNullOrEmpty(playlistId))
                throw new ArgumentException("Playlist ID cannot be null or empty", nameof(playlistId));

            try
            {
                var url = $"https://api.spotify.com/v1/playlists/{playlistId}";
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var playlistData = JsonSerializer.Deserialize<SpotifyPlaylistResponse>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (playlistData == null) return null;

                    // ✅ FETCH ALL TRACKS WITH PAGINATION
                    var allTracks = new List<SpotifyTrackItem>();
                    if (playlistData.tracks?.items != null)
                    {
                        allTracks.AddRange(playlistData.tracks.items);
                    }

                    // ✅ FOLLOW PAGINATION TO GET ALL TRACKS
                    string? nextUrl = playlistData.tracks?.next;
                    while (!string.IsNullOrEmpty(nextUrl))
                    {
                        System.Diagnostics.Debug.WriteLine($"Fetching next page: {nextUrl}");
                        
                        var nextResponse = await _httpClient.GetAsync(nextUrl);
                        if (nextResponse.IsSuccessStatusCode)
                        {
                            var nextJson = await nextResponse.Content.ReadAsStringAsync();
                            var nextPage = JsonSerializer.Deserialize<SpotifyTracksResponse>(nextJson, new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });

                            if (nextPage?.items != null)
                            {
                                allTracks.AddRange(nextPage.items);
                                System.Diagnostics.Debug.WriteLine($"Added {nextPage.items.Length} more tracks. Total: {allTracks.Count}");
                            }

                            nextUrl = nextPage?.next;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to fetch next page: {nextResponse.StatusCode}");
                            break;
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"✅ Fetched ALL {allTracks.Count} tracks from playlist");

                    return new PlaylistInfo
                    {
                        Id = playlistData.id ?? "",
                        Name = playlistData.name ?? "",
                        Description = playlistData.description ?? "",
                        ImageUrl = playlistData.images?.FirstOrDefault()?.url,
                        Owner = playlistData.owner?.display_name ?? "",
                        IsPublic = playlistData.@public ?? false,
                        TotalTracks = allTracks.Count, // ✅ Use actual count
                        Tracks = ConvertTracks(allTracks.ToArray()) // ✅ Convert all tracks
                    };
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException($"Spotify API error: {response.StatusCode} - {error}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting playlist: {ex.Message}");
                throw;
            }
        }

        public async Task<string?> DownloadPlaylistImageAsync(string? imageUrl, string? playlistId, string? path)
        {
            if (string.IsNullOrEmpty(imageUrl) || string.IsNullOrEmpty(playlistId) || string.IsNullOrEmpty(path))
                return null;

            try
            {
                var response = await _httpClient.GetAsync(imageUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    var imageBytes = await response.Content.ReadAsByteArrayAsync();
                    var fileName = $"playlist_{playlistId}.jpg";
                    var imagesDir = Path.Combine(FileSystem.AppDataDirectory, path);
                    
                    Directory.CreateDirectory(imagesDir);
                    
                    var filePath = Path.Combine(imagesDir, fileName);
                    await File.WriteAllBytesAsync(filePath, imageBytes);
                    
                    return filePath;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error downloading image: {ex.Message}");
            }
            
            return null;
        }

        public async Task<int> DownloadAndSavePlaylistAsync(string? playlistId, string? downloadFolderPath)
        {
            if (_database == null)
                throw new InvalidOperationException("Database not initialized");

            if (string.IsNullOrEmpty(playlistId) || string.IsNullOrEmpty(downloadFolderPath))
                throw new ArgumentException("Playlist ID and download folder path cannot be null or empty");

            var playlistInfo = await GetPlaylistInfoAsync(playlistId);
            if (playlistInfo == null)
                throw new InvalidOperationException("Failed to get playlist information");
            
            string? imagePath = null;
            if (!string.IsNullOrEmpty(playlistInfo.ImageUrl))
            {
                imagePath = await DownloadPlaylistImageAsync(playlistInfo.ImageUrl, playlistId, downloadFolderPath);
            }

            var dbPlaylistId = await _database.SavePlaylistWithSongsAsync(playlistInfo, imagePath, downloadFolderPath);
            
            return dbPlaylistId;
        }

        public async Task UpdateSongMp3PathAsync(int songId, string? mp3FilePath)
        {
            if (_database == null)
                throw new InvalidOperationException("Database not initialized");

            if (string.IsNullOrEmpty(mp3FilePath))
                return;

            var song = await _database.GetSongByIdAsync(songId);
            if (song != null)
            {
                song.Mp3FilePath = mp3FilePath;
                await _database.SaveSongAsync(song);
            }
        }

        public async Task<List<PlaylistEntity>> GetSavedPlaylistsAsync()
        {
            if (_database == null)
                throw new InvalidOperationException("Database not initialized");

            return await _database.GetAllPlaylistsAsync();
        }

        public async Task<List<SongEntity>> GetPlaylistSongsAsync(int playlistId)
        {
            if (_database == null)
                throw new InvalidOperationException("Database not initialized");

            return await _database.GetSongsByPlaylistIdAsync(playlistId);
        }

        public static string? ExtractPlaylistIdFromUrl(string? url)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            try
            {
                var uri = new Uri(url);
                var pathSegments = uri.AbsolutePath.Split('/');
                
                for (int i = 0; i < pathSegments.Length; i++)
                {
                    if (pathSegments[i] == "playlist" && i + 1 < pathSegments.Length)
                    {
                        var playlistId = pathSegments[i + 1];
                        if (playlistId.Contains('?'))
                            playlistId = playlistId.Split('?')[0];
                        return playlistId;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error extracting playlist ID: {ex.Message}");
            }
            
            return null;
        }

        public async Task<List<PlaylistInfo>> SearchPlaylistsAsync(string? query, int limit = 20)
        {
            if (string.IsNullOrEmpty(_accessToken))
                throw new InvalidOperationException("Access token not set. Call SetAccessToken() first.");

            if (string.IsNullOrEmpty(query))
                return new List<PlaylistInfo>();

            try
            {
                var encodedQuery = Uri.EscapeDataString(query);
                var url = $"https://api.spotify.com/v1/search?q={encodedQuery}&type=playlist&limit={limit}";
                
                var response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var searchData = JsonSerializer.Deserialize<SpotifySearchResponse>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (searchData?.playlists?.items == null)
                        return new List<PlaylistInfo>();
                    
                    var playlists = new List<PlaylistInfo>();
                    
                    foreach (var item in searchData.playlists.items)
                    {
                        if (item != null)
                        {
                            playlists.Add(new PlaylistInfo
                            {
                                Id = item.id ?? "",
                                Name = item.name ?? "",
                                Description = item.description ?? "",
                                ImageUrl = item.images?.FirstOrDefault()?.url,
                                Owner = item.owner?.display_name ?? "",
                                IsPublic = item.@public ?? false,
                                TotalTracks = item.tracks?.total ?? 0
                            });
                        }
                    }
                    
                    return playlists;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error searching playlists: {ex.Message}");
            }
            
            return new List<PlaylistInfo>();
        }

        private List<TrackInfo> ConvertTracks(SpotifyTrackItem[]? trackItems)
        {
            var tracks = new List<TrackInfo>();
            
            if (trackItems == null) return tracks;
            
            foreach (var item in trackItems)
            {
                if (item?.track != null)
                {
                    tracks.Add(new TrackInfo
                    {
                        Id = item.track.id ?? "",
                        Name = item.track.name ?? "",
                        Artists = item.track.artists?.Where(a => !string.IsNullOrEmpty(a?.name))
                                                    .Select(a => a.name!)
                                                    .ToList() ?? new List<string>(),
                        Album = item.track.album?.name ?? "",
                        DurationMs = item.track.duration_ms,
                        AlbumImageUrl = item.track.album?.images?.FirstOrDefault()?.url,
                        PreviewUrl = item.track.preview_url
                    });
                }
            }
            
            return tracks;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    // Data models with nullable properties
    public class SpotifyPlaylistResponse
    {
        public string? id { get; set; }
        public string? name { get; set; }
        public string? description { get; set; }
        public bool? @public { get; set; }
        public SpotifyImage[]? images { get; set; }
        public SpotifyOwner? owner { get; set; }
        public SpotifyTracksResponse? tracks { get; set; }
    }

    public class SpotifyTracksResponse
    {
        public int total { get; set; }
        public SpotifyTrackItem[]? items { get; set; }
        public string? next { get; set; }
    }

    public class SpotifyTrackItem
    {
        public SpotifyTrack? track { get; set; }
    }

    public class SpotifyTrack
    {
        public string? id { get; set; }
        public string? name { get; set; }
        public SpotifyArtist[]? artists { get; set; }
        public SpotifyAlbum? album { get; set; }
        public int duration_ms { get; set; }
        public string? preview_url { get; set; }
    }

    public class SpotifyArtist
    {
        public string? name { get; set; }
    }

    public class SpotifyAlbum
    {
        public string? name { get; set; }
        public SpotifyImage[]? images { get; set; }
    }

    public class SpotifyImage
    {
        public string? url { get; set; }
        public int? height { get; set; }  // Make nullable
        public int? width { get; set; }   // Make nullable
    }

    public class SpotifyOwner
    {
        public string? display_name { get; set; }
    }

    public class SpotifySearchResponse
    {
        public SpotifyPlaylistsResponse? playlists { get; set; }
    }

    public class SpotifyPlaylistsResponse
    {
        public SpotifyPlaylistResponse[]? items { get; set; }
    }

    // Application models
    public class PlaylistInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string? ImageUrl { get; set; }
        public string Owner { get; set; } = "";
        public bool IsPublic { get; set; }
        public int TotalTracks { get; set; }
        public List<TrackInfo> Tracks { get; set; } = new();
    }

    public class TrackInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<string> Artists { get; set; } = new();
        public string Album { get; set; } = "";
        public int DurationMs { get; set; }
        public string? AlbumImageUrl { get; set; }
        public string? PreviewUrl { get; set; }
        
        public string ArtistsString => string.Join(", ", Artists);
        public TimeSpan Duration => TimeSpan.FromMilliseconds(DurationMs);
        public string DurationString => Duration.ToString(@"mm\:ss");
    }

    public class ImageInfo 
    {
        public int? Height { get; set; }  // Make nullable
        public int? Width { get; set; }   // Make nullable  
        public string Url { get; set; } = "";
    }
}
