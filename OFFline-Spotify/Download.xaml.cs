using OpenQA.Selenium;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Compression;

namespace OFFline_Spotify;

public partial class Download : ContentPage
{
    private readonly Random _random = new Random();
    
    private bool _isDownloading = false;
    private bool _isDisposed = false;
    private bool _isNavigating = false;
    private IWebDriver? _activeDriver = null;

    // Configuration for Python server
    private const string SERVER_HOST = "192.168.1.49"; // Change to remote PC IP if needed
    private const int SERVER_PORT = 9999;
    private const long BUFFER_SIZE = 3000000000;

    public Download()
    {
        InitializeComponent();
    }
    
    private async void OnDownloadbuttonnClicked(object sender, EventArgs e)
    {
        if (!ValidateInputs())
            return;

        if (_isDownloading)
        {
            await DisplayAlert("Download in Progress", "Please wait for the current download to complete.", "OK");
            return;
        }

        await OnDownload();
    }

    // Add this new validation method
    private bool ValidateInputs()
    {
        bool isValid = true;
        string errorMessage = "";

        // Clear previous error styling
        PlaylistsLink.BackgroundColor = Colors.White;
        

        // Validate Playlist Link
        if (string.IsNullOrWhiteSpace(PlaylistsLink.Text))
        {
            PlaylistsLink.BackgroundColor = Color.FromArgb("#FFE0E0"); // Light red
            errorMessage += "• Playlist link is required\n";
            isValid = false;
        }

       

        // Show error message if validation failed
        if (!isValid)
        {
            ErrorLabel.Text = errorMessage;
            ErrorLabel.TextColor = Colors.Red;
            DisplayAlert("Required Fields", "Please fill in required field before downloading.", "OK");
        }
        else
        {
            // Clear error message if validation passed
            ErrorLabel.Text = "";
        }

        return isValid;
    }

    private async Task RandomDelay(int minMs = 500, int maxMs = 3000)
    {
        await Task.Delay(_random.Next(minMs, maxMs));
    }

    public async Task OnDownload()
    {
        _isDownloading = true;
        
        try
        {
            // Update UI
            ErrorLabel.Text = "Connecting to server...";
            ErrorLabel.TextColor = Colors.Blue;
            
            // Get playlist link(s) - split by newline if multiple
            string input = PlaylistsLink.Text?.Trim() ?? "";
            List<string> links = input.Split('\n')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();
            
            if (links.Count == 0)
            {
                await DisplayAlert("Error", "No valid playlist links found.", "OK");
                return;
            }
            
            // Send request to Python server
            ErrorLabel.Text = $"Downloading {links.Count} playlist(s)...";
            var result = await SendDownloadRequestAsync(links);
            
            if (result.Success)
            {
                ErrorLabel.Text = "Download completed! Saving file...";
                ErrorLabel.TextColor = Colors.Green;
                
                // Save the zip file
                string zipPath = await SaveZipFileAsync(result.ZipData, result.FileName);
                
                // Process the downloaded zip file
                await ProcessDownloadedPlaylist(zipPath);
                
                await DisplayAlert("Success", 
                    $"Download and processing completed!\nFile: {result.FileName}\nSize: {result.ZipData.Length / 1024 / 1024:F2} MB", 
                    "OK");
                
                ErrorLabel.Text = "✓ Download and processing completed successfully!";
            }
            else
            {
                ErrorLabel.Text = $"Error: {result.ErrorMessage}";
                ErrorLabel.TextColor = Colors.Red;
                
                await DisplayAlert("Download Failed", result.ErrorMessage, "OK");
            }
        }
        catch (Exception ex)
        {
            ErrorLabel.Text = $"Error: {ex.Message}";
            ErrorLabel.TextColor = Colors.Red;
            
            await DisplayAlert("Error", $"An error occurred: {ex.Message}", "OK");
            Debug.WriteLine($"Download error: {ex}");
        }
        finally
        {
            _isDownloading = false;
        }
    }

    private async Task<DownloadResult> SendDownloadRequestAsync(List<string> links)
    {
        TcpClient? client = null;
        NetworkStream? stream = null;
        
        try
        {
            // Connect to server
            client = new TcpClient();
            await client.ConnectAsync(SERVER_HOST, SERVER_PORT);
            stream = client.GetStream();
            
            Debug.WriteLine($"Connected to {SERVER_HOST}:{SERVER_PORT}");
            
            // Prepare data to send (newline-separated links)
            string dataToSend = string.Join("\n", links);
            byte[] sendData = Encoding.UTF8.GetBytes(dataToSend);
            
            // Send data
            await stream.WriteAsync(sendData, 0, sendData.Length);
            Debug.WriteLine($"Sent {sendData.Length} bytes to server");
            
            // Read response header (4 bytes = header length)
            byte[] headerLengthBytes = new byte[4];
            int bytesRead = await stream.ReadAsync(headerLengthBytes, 0, 4);
            
            if (bytesRead != 4)
            {
                return new DownloadResult 
                { 
                    Success = false, 
                    ErrorMessage = "Failed to read response header" 
                };
            }
            
            int headerLength = BitConverter.ToInt32(headerLengthBytes, 0);
            if (BitConverter.IsLittleEndian)
            {
                headerLength = System.Net.IPAddress.NetworkToHostOrder(headerLength);
            }
            
            // Read header JSON
            byte[] headerBytes = new byte[headerLength];
            bytesRead = await ReadExactlyAsync(stream, headerBytes, headerLength);
            
            if (bytesRead != headerLength)
            {
                return new DownloadResult 
                { 
                    Success = false, 
                    ErrorMessage = "Failed to read complete header" 
                };
            }
            
            string headerJson = Encoding.UTF8.GetString(headerBytes);
            Debug.WriteLine($"Received header: {headerJson}");
            
            var header = JsonSerializer.Deserialize<ServerResponse>(headerJson);
            
            if (header == null)
            {
                return new DownloadResult 
                { 
                    Success = false, 
                    ErrorMessage = "Invalid server response" 
                };
            }
            
            if (header.status != "success")
            {
                return new DownloadResult 
                { 
                    Success = false, 
                    ErrorMessage = header.message ?? "Unknown error" 
                };
            }
            
            // Read file content
            long fileSize = header.size;
            string fileName = header.filename ?? "downloads.zip";
            
            Debug.WriteLine($"Receiving file: {fileName} ({fileSize / 1024 / 1024:F2} MB)");
            
            byte[] fileData = new byte[fileSize];
            long totalRead = 0;
            int lastProgress = 0;
            
            while (totalRead < fileSize)
            {
                int toRead = (int)Math.Min(BUFFER_SIZE, fileSize - totalRead);
                bytesRead = await stream.ReadAsync(fileData, (int)totalRead, toRead);
                
                if (bytesRead == 0)
                    break;
                
                totalRead += bytesRead;
                
                // Update progress every 10%
                int progress = (int)((totalRead * 100) / fileSize);
                if (progress >= lastProgress + 10)
                {
                    lastProgress = progress;
                    Debug.WriteLine($"Download progress: {progress}%");
                    
                    await MainThread.InvokeOnMainThreadAsync(() => 
                    {
                        ErrorLabel.Text = $"Downloading... {progress}%";
                    });
                }
            }
            
            Debug.WriteLine($"Received {totalRead} bytes");
            
            return new DownloadResult 
            { 
                Success = true, 
                ZipData = fileData,
                FileName = fileName
            };
        }
        catch (SocketException ex)
        {
            Debug.WriteLine($"Socket error: {ex.Message}");
            return new DownloadResult 
            { 
                Success = false, 
                ErrorMessage = $"Connection error: {ex.Message}\nMake sure the server is running on {SERVER_HOST}:{SERVER_PORT}" 
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error: {ex}");
            return new DownloadResult 
            { 
                Success = false, 
                ErrorMessage = ex.Message 
            };
        }
        finally
        {
            stream?.Close();
            client?.Close();
        }
    }

    private async Task<int> ReadExactlyAsync(NetworkStream stream, byte[] buffer, int length)
    {
        int totalRead = 0;
        while (totalRead < length)
        {
            int bytesRead = await stream.ReadAsync(buffer, totalRead, length - totalRead);
            if (bytesRead == 0)
                break;
            totalRead += bytesRead;
        }
        return totalRead;
    }

    private async Task<string> SaveZipFileAsync(byte[] zipData, string fileName)
    {
        try
        {
            // Use app's local data directory (accessible by app, survives app restarts)
            string downloadFolder = Path.Combine(FileSystem.AppDataDirectory, "Downloads");
            Directory.CreateDirectory(downloadFolder);
            
            string filePath = Path.Combine(downloadFolder, fileName);
            
            // Make sure filename is unique
            int counter = 1;
            while (File.Exists(filePath))
            {
                string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                string extension = Path.GetExtension(fileName);
                filePath = Path.Combine(downloadFolder, $"{nameWithoutExt}_{counter}{extension}");
                counter++;
            }
            
            await File.WriteAllBytesAsync(filePath, zipData);
            Debug.WriteLine($"File saved: {filePath}");
            Debug.WriteLine($"AppDataDirectory: {FileSystem.AppDataDirectory}");
            
            await MainThread.InvokeOnMainThreadAsync(() => 
            {
                ErrorLabel.Text = $"✓ Saved: {Path.GetFileName(filePath)}";
            });
            
            return filePath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving file: {ex.Message}");
            throw;
        }
    }

    private async Task ProcessDownloadedPlaylist(string zipFilePath)
    {
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() => 
            {
                ErrorLabel.Text = "Processing downloaded playlist...";
                ErrorLabel.TextColor = Colors.Blue;
            });

            Debug.WriteLine($"Starting to process zip file: {zipFilePath}");

            // Get playlist name from zip file name (without .zip extension)
            string playlistName = Path.GetFileNameWithoutExtension(zipFilePath);
            Debug.WriteLine($"Playlist name from zip file: {playlistName}");

            // 1. Extract the zip file directly to the target location
            string targetPlaylistPath = Path.Combine(FileSystem.AppDataDirectory, playlistName);
            
            // If target already exists, delete it
            if (Directory.Exists(targetPlaylistPath))
            {
                Debug.WriteLine($"Removing existing playlist folder: {targetPlaylistPath}");
                Directory.Delete(targetPlaylistPath, true);
            }
            
            await MainThread.InvokeOnMainThreadAsync(() => 
            {
                ErrorLabel.Text = "Extracting files...";
            });
            
            // Extract directly to target location
            ZipFile.ExtractToDirectory(zipFilePath, targetPlaylistPath);
            Debug.WriteLine($"Extracted to: {targetPlaylistPath}");

            // 2. Move all MP3 files from songs folder to playlist root
            string songsFolder = Path.Combine(targetPlaylistPath, "songs");
            if (Directory.Exists(songsFolder))
            {
                var mp3Files = Directory.GetFiles(songsFolder, "*.mp3");
                Debug.WriteLine($"Found {mp3Files.Length} MP3 files in songs folder");
                
                foreach (var mp3File in mp3Files)
                {
                    string fileName = Path.GetFileName(mp3File);
                    string targetPath = Path.Combine(targetPlaylistPath, fileName);
                    
                    // Handle duplicate filenames
                    int counter = 1;
                    while (File.Exists(targetPath))
                    {
                        string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                        string extension = Path.GetExtension(fileName);
                        targetPath = Path.Combine(targetPlaylistPath, $"{nameWithoutExt}_{counter}{extension}");
                        counter++;
                    }
                    
                    File.Move(mp3File, targetPath);
                    Debug.WriteLine($"Moved MP3: {fileName} -> {Path.GetFileName(targetPath)}");
                }
                
                // Delete the now-empty songs folder
                Directory.Delete(songsFolder, true);
                Debug.WriteLine("Deleted songs folder");
            }
            else
            {
                Debug.WriteLine("No songs folder found - playlist may be empty");
            }

            // 3. Integrate with database (using zip file name as playlist name)
            await MainThread.InvokeOnMainThreadAsync(() => 
            {
                ErrorLabel.Text = "Integrating with database...";
            });
            
            await IntegratePlaylistWithDatabase(targetPlaylistPath, playlistName);

            // 4. Keep the ZIP file (no cleanup)
            Debug.WriteLine($"ZIP file preserved at: {zipFilePath}");

            await MainThread.InvokeOnMainThreadAsync(() => 
            {
                ErrorLabel.Text = "✓ Processing completed!";
                ErrorLabel.TextColor = Colors.Green;
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error processing playlist: {ex.Message}");
            Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            throw new Exception($"Failed to process playlist: {ex.Message}", ex);
        }
    }

    // Helper method to recursively log directory contents
    private void LogDirectoryContents(string path, string indent)
    {
        try
        {
            // Log files in current directory
            var files = Directory.GetFiles(path);
            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                Debug.WriteLine($"{indent}📄 {Path.GetFileName(file)} ({fileInfo.Length / 1024.0:F2} KB)");
            }

            // Log subdirectories recursively
            var directories = Directory.GetDirectories(path);
            foreach (var directory in directories)
            {
                Debug.WriteLine($"{indent}📁 {Path.GetFileName(directory)}/");
                LogDirectoryContents(directory, indent + "  ");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"{indent}⚠ Error reading directory: {ex.Message}");
        }
    }

    private async Task IntegratePlaylistWithDatabase(string playlistPath, string playlistName)
    {
        try
        {
            if (App.Database == null)
            {
                throw new Exception("Database not initialized");
            }

            // Read SONGS_DATA.txt
            string songsDataPath = Path.Combine(playlistPath, "SONGS_DATA.txt");
            
            var songInfoList = new List<(int order, string title, string artist)>();
            
            if (File.Exists(songsDataPath))
            {
                var lines = await File.ReadAllLinesAsync(songsDataPath);
                Debug.WriteLine($"Read {lines.Length} lines from SONGS_DATA.txt");

                // Parse song data
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    
                    // Parse format: "1. Artist - Title" or "1. Title"
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"^(\d+)\.\s+(.+)$");
                    if (match.Success)
                    {
                        int order = int.Parse(match.Groups[1].Value);
                        string titleArtist = match.Groups[2].Value.Trim();
                        
                        // Split by " - " to separate artist and title
                        string artist = "";
                        string title = titleArtist;
                        
                        int dashIndex = titleArtist.IndexOf(" - ");
                        if (dashIndex > 0)
                        {
                            artist = titleArtist.Substring(0, dashIndex).Trim();
                            title = titleArtist.Substring(dashIndex + 3).Trim();
                        }
                        
                        songInfoList.Add((order, title, artist));
                        Debug.WriteLine($"Parsed: Order={order}, Artist='{artist}', Title='{title}'");
                    }
                }

                // Sort by order (ascending)
                songInfoList = songInfoList.OrderBy(s => s.order).ToList();
            }
            else
            {
                Debug.WriteLine("Warning: SONGS_DATA.txt not found - creating empty playlist");
            }

            // Find playlist image
            string? imagePath = FindPlaylistImage(playlistPath);
            if (imagePath != null)
            {
                Debug.WriteLine($"Found playlist image: {imagePath}");
            }

            // Create or update playlist entity (even if no songs)
            var playlist = new PlaylistEntity
            {
                Name = playlistName,
                SpotifyId = $"downloaded_{Guid.NewGuid():N}", // Generate unique ID for downloaded playlists
                ImagePath = imagePath,
                TotalTracks = songInfoList.Count,
                CreatedAt = DateTime.UtcNow
            };

            int playlistId = await App.Database.SavePlaylistAsync(playlist);
            Debug.WriteLine($"Saved playlist with ID: {playlistId} (Songs: {songInfoList.Count})");

            if (songInfoList.Count == 0)
            {
                Debug.WriteLine("Empty playlist saved - can be fixed later with 'fix download' feature");
                return;
            }

            // Get all MP3 files in the playlist folder
            var mp3Files = Directory.GetFiles(playlistPath, "*.mp3");
            Debug.WriteLine($"Found {mp3Files.Length} MP3 files in playlist folder");

            // Create song entities and match with MP3 files
            foreach (var songInfo in songInfoList)
            {
                // Try to match MP3 file
                string? mp3Path = FindBestMatchingMp3(mp3Files, songInfo.title, songInfo.artist);
                
                var song = new SongEntity
                {
                    PlaylistId = playlistId,
                    Title = songInfo.title,
                    Artist = string.IsNullOrEmpty(songInfo.artist) ? "Unknown Artist" : songInfo.artist,
                    Mp3FilePath = mp3Path,
                    DurationMs = 0, // Will be updated when played
                    CreatedAt = DateTime.UtcNow
                };

                await App.Database.SaveSongAsync(song);
                
                if (mp3Path != null)
                {
                    Debug.WriteLine($"Saved song: '{song.Title}' by '{song.Artist}' -> {Path.GetFileName(mp3Path)}");
                }
                else
                {
                    Debug.WriteLine($"Saved song WITHOUT MP3: '{song.Title}' by '{song.Artist}'");
                }
            }

            Debug.WriteLine($"Database integration completed for playlist: {playlistName}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error integrating with database: {ex.Message}");
            Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    private string? FindPlaylistImage(string playlistPath)
    {
        try
        {
            // Look for common image extensions
            string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
            
            foreach (var ext in imageExtensions)
            {
                var imageFiles = Directory.GetFiles(playlistPath, $"*{ext}");
                if (imageFiles.Length > 0)
                {
                    // Prefer files with "cover" or "playlist" in the name
                    var preferredImage = imageFiles.FirstOrDefault(f => 
                        Path.GetFileNameWithoutExtension(f).ToLower().Contains("cover") ||
                        Path.GetFileNameWithoutExtension(f).ToLower().Contains("playlist") ||
                        Path.GetFileNameWithoutExtension(f).ToLower().Contains("image"));
                    
                    return preferredImage ?? imageFiles[0];
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error finding playlist image: {ex.Message}");
            return null;
        }
    }

    private string? FindBestMatchingMp3(string[] mp3Files, string title, string artist)
    {
        if (mp3Files.Length == 0) return null;

        string cleanTitle = CleanForMatching(title);
        string cleanArtist = CleanForMatching(artist);

        // Priority 1: Match both title and artist
        foreach (var file in mp3Files)
        {
            string fileName = CleanForMatching(Path.GetFileNameWithoutExtension(file));
            if (fileName.Contains(cleanTitle) && !string.IsNullOrEmpty(cleanArtist) && fileName.Contains(cleanArtist))
            {
                return file;
            }
        }

        // Priority 2: Match title only
        foreach (var file in mp3Files)
        {
            string fileName = CleanForMatching(Path.GetFileNameWithoutExtension(file));
            if (fileName.Contains(cleanTitle))
            {
                return file;
            }
        }

        // Priority 3: Match artist only (if artist is provided)
        if (!string.IsNullOrEmpty(cleanArtist))
        {
            foreach (var file in mp3Files)
            {
                string fileName = CleanForMatching(Path.GetFileNameWithoutExtension(file));
                if (fileName.Contains(cleanArtist))
                {
                    return file;
                }
            }
        }

        // Priority 4: Fuzzy match
        foreach (var file in mp3Files)
        {
            string fileName = CleanForMatching(Path.GetFileNameWithoutExtension(file));
            if (IsFuzzyMatch(cleanTitle, fileName))
            {
                return file;
            }
        }

        return null;
    }

    private string CleanForMatching(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        return input.ToLower()
            .Replace("?", "")
            .Replace("!", "")
            .Replace("'", "")
            .Replace("\"", "")
            .Replace("(", "")
            .Replace(")", "")
            .Replace("[", "")
            .Replace("]", "")
            .Replace("__spotdown.app", "")
            .Replace("-", " ")
            .Replace("_", " ")
            .Trim();
    }

    private bool IsFuzzyMatch(string? trackName, string? fileName)
    {
        if (string.IsNullOrEmpty(trackName) || string.IsNullOrEmpty(fileName))
            return false;

        string trackNoSpaces = trackName.Replace(" ", "");
        string fileNoSpaces = fileName.Replace(" ", "");

        return fileNoSpaces.Contains(trackNoSpaces) || trackNoSpaces.Contains(fileNoSpaces);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        
        Debug.WriteLine($"OnDisappearing called - _isNavigating: {_isNavigating}, _isDisposed: {_isDisposed}");
        
        
        if (_isNavigating)
        {
            Debug.WriteLine("Skipping cleanup - navigation in progress");
            return;
        }
        
        if (_isDisposed)
        {
            Debug.WriteLine("Already disposed");
            return;
        }
        
        _isDisposed = true;
        
        // Additional driver cleanup if somehow still active
        if (_activeDriver != null)
        {
            try
            {
                Debug.WriteLine("Emergency driver cleanup in OnDisappearing");
                _activeDriver.Quit();
                _activeDriver.Dispose();
                _activeDriver = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Emergency cleanup error: {ex.Message}");
            }
        }
        
        Debug.WriteLine("Download page cleanup completed");
    }

    // Also add this to handle when app is being closed
    protected override bool OnBackButtonPressed()
    {
        if (_isDownloading)
        {
            MainThread.BeginInvokeOnMainThread(async () => {
                await DisplayAlert("Download in Progress", "Please wait for the download to complete.", "OK");
            });
            return true; // Prevent back navigation
        }
        
        return base.OnBackButtonPressed();
    }

    // Helper classes for JSON deserialization
    private class ServerResponse
    {
        public string status { get; set; } = "";
        public string? message { get; set; }
        public string? filename { get; set; }
        public long size { get; set; }
    }

    private class DownloadResult
    {
        public bool Success { get; set; }
        public byte[]? ZipData { get; set; }
        public string? FileName { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
