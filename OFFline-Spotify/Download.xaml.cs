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

    // Configuration for Python server
    private const string DEFAULT_SERVER_HOST = "192.168.1.49"; // Change default if needed
    private string _serverHost = ServerEndpointSettings.GetHost();
    private const int SERVER_PORT = ServerEndpointSettings.ServerPort;
    private const long BUFFER_SIZE = 3000000000;

    public Download()
    {
        InitializeComponent();
        _serverHost = ServerEndpointSettings.GetHost();
    }

    // Helper to update the status label and show/hide its frame
    private void SetStatus(string message, Color color)
    {

        ErrorLabel.Text = message;
        ErrorLabel.TextColor = color;
        ErrorFrame.IsVisible = !string.IsNullOrEmpty(message);
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

    private bool ValidateInputs()
    {
        bool isValid = true;
        string errorMessage = "";

        // Clear previous error styling
        PlaylistsLink.BackgroundColor = Colors.White;

        // Validate Playlist Link
        if (string.IsNullOrWhiteSpace(PlaylistsLink.Text))
        {
            PlaylistsLink.BackgroundColor = Color.FromArgb("#FFE0E0");
            errorMessage += "• Playlist link is required\n";
            isValid = false;
        }

        if (!isValid)
        {
            SetStatus(errorMessage, Colors.Red);
            DisplayAlert("Required Fields", "Please fill in required field before downloading.", "OK");
        }
        else
        {
            SetStatus("", Colors.White);
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
            SetStatus("Connecting to server...", Colors.White);

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

            // NEW: extract Spotify playlist id from first entered link
            string? spotifyPlaylistId = ExtractSpotifyPlaylistId(links[0]);

            SetStatus($"Downloading {links.Count} playlist(s)...", Colors.White);
            var result = await SendDownloadRequestAsync(links);

            if (result.Success)
            {
                SetStatus("Download completed! Saving file...", Colors.Green);

                string safeFileName = string.IsNullOrWhiteSpace(result.FileName) ? "downloads.zip" : result.FileName;
                string zipPath = await SaveZipFileAsync(result.ZipData!, safeFileName);

                await ProcessDownloadedPlaylist(zipPath, spotifyPlaylistId);

                await DisplayAlert("Success",
                    $"Download and processing completed!\nFile: {safeFileName}\nSize: {result.ZipData!.Length / 1024 / 1024:F2} MB",
                    "OK");

                SetStatus("✓ Download and processing completed successfully!", Colors.Green);
            }
            else
            {
                SetStatus($"Error: {result.ErrorMessage}", Colors.Red);
                await DisplayAlert("Download Failed", result.ErrorMessage, "OK");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", Colors.Red);
            await DisplayAlert("Error", $"An error occurred: {ex.Message}", "OK");
            Debug.WriteLine($"Download error: {ex}");
        }
        finally
        {
            _isDownloading = false;
        }
    }

    // NEW
    private static string? ExtractSpotifyPlaylistId(string? link)
    {
        if (string.IsNullOrWhiteSpace(link))
            return null;

        const string marker = "playlist/";
        int startIndex = link.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
            return null;

        startIndex += marker.Length;
        if (startIndex >= link.Length)
            return null;

        int questionMarkIndex = link.IndexOf('?', startIndex);
        string id = questionMarkIndex >= 0
            ? link[startIndex..questionMarkIndex]
            : link[startIndex..];

        id = id.Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    private async Task<DownloadResult> SendDownloadRequestAsync(List<string> links)
    {
        TcpClient? client = null;
        NetworkStream? stream = null;

        try
        {
            client = new TcpClient();
            await client.ConnectAsync(_serverHost, SERVER_PORT);
            stream = client.GetStream();

            Debug.WriteLine($"Connected to {_serverHost}:{SERVER_PORT}");

            string dataToSend = string.Join("\n", links);
            byte[] sendData = Encoding.UTF8.GetBytes(dataToSend);

            await stream.WriteAsync(sendData, 0, sendData.Length);
            Debug.WriteLine($"Sent {sendData.Length} bytes to server");

            byte[] headerLengthBytes = new byte[4];
            int headerBytesRead = await stream.ReadAsync(headerLengthBytes, 0, 4);

            if (headerBytesRead != 4)
                return new DownloadResult { Success = false, ErrorMessage = "Failed to read response header" };

            int headerLength = BitConverter.ToInt32(headerLengthBytes, 0);
            if (BitConverter.IsLittleEndian)
                headerLength = System.Net.IPAddress.NetworkToHostOrder(headerLength);

            byte[] headerBytes = new byte[headerLength];
            int headerJsonBytesRead = await ReadExactlyAsync(stream, headerBytes, headerLength);

            if (headerJsonBytesRead != headerLength)
                return new DownloadResult { Success = false, ErrorMessage = "Failed to read complete header" };

            string headerJson = Encoding.UTF8.GetString(headerBytes);
            Debug.WriteLine($"Received header: {headerJson}");

            var header = JsonSerializer.Deserialize<ServerResponse>(headerJson);

            if (header == null)
                return new DownloadResult { Success = false, ErrorMessage = "Invalid server response" };

            if (header.status != "success")
                return new DownloadResult { Success = false, ErrorMessage = header.message ?? "Unknown error" };

            long fileSize = header.size;
            string fileName = header.filename ?? "downloads.zip";

            Debug.WriteLine($"Receiving file: {fileName} ({fileSize / 1024 / 1024:F2} MB)");

            const long CHUNK_SIZE = 8192;
            byte[] fileData = new byte[fileSize];
            long totalRead = 0;
            int lastProgress = 0;
            byte[] buffer = new byte[CHUNK_SIZE];

            while (totalRead < fileSize)
            {
                int toRead = (int)Math.Min(CHUNK_SIZE, fileSize - totalRead);
                int chunkBytesRead = await stream.ReadAsync(buffer, 0, toRead);

                if (chunkBytesRead == 0)
                    break;

                Array.Copy(buffer, 0, fileData, totalRead, chunkBytesRead);
                totalRead += chunkBytesRead;

                int progress = (int)((totalRead * 100) / fileSize);
                if (progress >= lastProgress + 5)
                {
                    lastProgress = progress;
                    Debug.WriteLine($"Download progress: {progress}% ({totalRead}/{fileSize} bytes)");

                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        SetStatus($"Downloading... {progress}%", Colors.White);
                    });
                }
            }

            Debug.WriteLine($"✓ Received {totalRead} bytes (expected {fileSize})");

            if (totalRead != fileSize)
                return new DownloadResult { Success = false, ErrorMessage = $"Incomplete download: received {totalRead}/{fileSize} bytes" };

            return new DownloadResult { Success = true, ZipData = fileData, FileName = fileName };
        }
        catch (SocketException ex)
        {
            Debug.WriteLine($"Socket error: {ex.Message}");
            return new DownloadResult
            {
                Success = false,
                ErrorMessage = $"Connection error: {ex.Message}\nMake sure the server is running on {_serverHost}:{SERVER_PORT}"
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error: {ex}");
            return new DownloadResult { Success = false, ErrorMessage = ex.Message };
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
            string downloadFolder = Path.Combine(FileSystem.AppDataDirectory, "Downloads");
            Directory.CreateDirectory(downloadFolder);

            string filePath = Path.Combine(downloadFolder, fileName);

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
            Debug.WriteLine($"File size: {zipData.Length} bytes");

            try
            {
                using (var zip = ZipFile.OpenRead(filePath))
                {
                    var entryCount = zip.Entries.Count;
                    Debug.WriteLine($"✓ ZIP validation passed: {entryCount} entries");
                }
            }
            catch (InvalidDataException ex)
            {
                Debug.WriteLine($"✗ ZIP validation failed: {ex.Message}");
                File.Delete(filePath);
                throw new Exception("Downloaded ZIP file is corrupted. Please try again.");
            }

            return filePath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving file: {ex.Message}");
            throw;
        }
    }

    private async Task ProcessDownloadedPlaylist(string zipFilePath, string? spotifyPlaylistId)
    {
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                SetStatus("Processing downloaded playlist...", Colors.Blue);
            });

            Debug.WriteLine($"Starting to process zip file: {zipFilePath}");

            string playlistName = Path.GetFileNameWithoutExtension(zipFilePath);
            Debug.WriteLine($"Playlist name from zip file: {playlistName}");

            string targetPlaylistPath = Path.Combine(FileSystem.AppDataDirectory, playlistName);

            if (Directory.Exists(targetPlaylistPath))
            {
                Debug.WriteLine($"Removing existing playlist folder: {targetPlaylistPath}");
                Directory.Delete(targetPlaylistPath, true);
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                SetStatus("Extracting files...", Colors.White);
            });

            ZipFile.ExtractToDirectory(zipFilePath, targetPlaylistPath);
            Debug.WriteLine($"Extracted to: {targetPlaylistPath}");

            string songsFolder = Path.Combine(targetPlaylistPath, "songs");
            if (Directory.Exists(songsFolder))
            {
                var mp3Files = Directory.GetFiles(songsFolder, "*.mp3");
                Debug.WriteLine($"Found {mp3Files.Length} MP3 files in songs folder");

                foreach (var mp3File in mp3Files)
                {
                    string fileName = Path.GetFileName(mp3File);
                    string targetPath = Path.Combine(targetPlaylistPath, fileName);

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

                Directory.Delete(songsFolder, true);
                Debug.WriteLine("Deleted songs folder");
            }
            else
            {
                Debug.WriteLine("No songs folder found - playlist may be empty");
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                SetStatus("Integrating with database...", Colors.White);
            });

            await IntegratePlaylistWithDatabase(targetPlaylistPath, playlistName, spotifyPlaylistId);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                SetStatus("✓ Processing completed!", Colors.Green);
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error processing playlist: {ex.Message}");
            Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            throw new Exception($"Failed to process playlist: {ex.Message}", ex);
        }
        finally
        {
            ClearDownloadsFolder();
        }
    }

    private void ClearDownloadsFolder()
    {
        try
        {
            string downloadFolder = Path.Combine(FileSystem.AppDataDirectory, "Downloads");
            if (!Directory.Exists(downloadFolder))
                return;

            foreach (var file in Directory.GetFiles(downloadFolder))
            {
                try { File.Delete(file); }
                catch (Exception ex) { Debug.WriteLine($"Failed deleting file '{file}': {ex.Message}"); }
            }

            foreach (var dir in Directory.GetDirectories(downloadFolder))
            {
                try { Directory.Delete(dir, true); }
                catch (Exception ex) { Debug.WriteLine($"Failed deleting directory '{dir}': {ex.Message}"); }
            }

            Debug.WriteLine("Downloads folder cleared.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error clearing Downloads folder: {ex.Message}");
        }
    }

    private async Task IntegratePlaylistWithDatabase(string playlistPath, string playlistName, string? spotifyPlaylistId)
    {
        try
        {
            if (App.Database == null)
                throw new Exception("Database not initialized");

            string songsDataPath = Path.Combine(playlistPath, "SONG_DATA.txt");
            if (!File.Exists(songsDataPath))
                songsDataPath = Path.Combine(playlistPath, "SONGS_DATA.txt");

            var songInfoList = new List<(int order, string title, string artist, string? link)>();

            if (File.Exists(songsDataPath))
            {
                var lines = await File.ReadAllLinesAsync(songsDataPath);
                Debug.WriteLine($"Read {lines.Length} lines from SONGS_DATA.txt");

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var match = System.Text.RegularExpressions.Regex.Match(line, @"^(\d+)\.\s+(.+)$");
                    if (!match.Success) continue;

                    int order = int.Parse(match.Groups[1].Value);
                    string payload = match.Groups[2].Value.Trim();

                    var parts = payload.Split(" - ", StringSplitOptions.TrimEntries);
                    string title = payload;
                    string artist = "";
                    string? link = null;

                    if (parts.Length >= 3)
                    {
                        title = parts[0];
                        artist = parts[1];
                        link = parts[2];
                    }
                    else if (parts.Length == 2)
                    {
                        title = parts[0];
                        artist = parts[1];
                    }

                    songInfoList.Add((order, title, artist, link));
                    Debug.WriteLine($"Parsed: Order={order}, Title='{title}', Artist='{artist}'");
                }

                songInfoList = songInfoList.OrderBy(s => s.order).ToList();
            }
            else
            {
                Debug.WriteLine("Warning: SONGS_DATA.txt not found - creating empty playlist");
            }

            string? imagePath = FindPlaylistImage(playlistPath);
            if (imagePath != null)
                Debug.WriteLine($"Found playlist image: {imagePath}");

            var playlist = new PlaylistEntity
            {
                Name = playlistName,
                SpotifyId = !string.IsNullOrWhiteSpace(spotifyPlaylistId)
    ? spotifyPlaylistId
    : $"downloaded_{Guid.NewGuid():N}",
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

            var mp3Files = Directory.GetFiles(playlistPath, "*.mp3");
            Debug.WriteLine($"Found {mp3Files.Length} MP3 files in playlist folder");

            foreach (var songInfo in songInfoList)
            {
                string? mp3Path = FindBestMatchingMp3(mp3Files, songInfo.title, songInfo.artist);

                var song = new SongEntity
                {
                    PlaylistId = playlistId,
                    Title = songInfo.title,
                    Artist = string.IsNullOrEmpty(songInfo.artist) ? "Unknown Artist" : songInfo.artist,
                    Mp3FilePath = mp3Path,
                    DurationMs = 0,
                    CreatedAt = DateTime.UtcNow,
                    SpotifyLink = songInfo.link,
                };

                await App.Database.SaveSongAsync(song);

                if (mp3Path != null)
                    Debug.WriteLine($"Saved song: '{song.Title}' by '{song.Artist}' -> {Path.GetFileName(mp3Path)}");
                else
                    Debug.WriteLine($"Saved song WITHOUT MP3: '{song.Title}' by '{song.Artist}'");
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
            string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

            foreach (var ext in imageExtensions)
            {
                var imageFiles = Directory.GetFiles(playlistPath, $"*{ext}");
                if (imageFiles.Length > 0)
                {
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

        foreach (var file in mp3Files)
        {
            string fileName = CleanForMatching(Path.GetFileNameWithoutExtension(file));
            if (fileName.Contains(cleanTitle) && !string.IsNullOrEmpty(cleanArtist) && fileName.Contains(cleanArtist))
                return file;
        }

        foreach (var file in mp3Files)
        {
            string fileName = CleanForMatching(Path.GetFileNameWithoutExtension(file));
            if (fileName.Contains(cleanTitle))
                return file;
        }

        if (!string.IsNullOrEmpty(cleanArtist))
        {
            foreach (var file in mp3Files)
            {
                string fileName = CleanForMatching(Path.GetFileNameWithoutExtension(file));
                if (fileName.Contains(cleanArtist))
                    return file;
            }
        }

        foreach (var file in mp3Files)
        {
            string fileName = CleanForMatching(Path.GetFileNameWithoutExtension(file));
            if (IsFuzzyMatch(cleanTitle, fileName))
                return file;
        }

        return null;
    }

    private string CleanForMatching(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        return input.ToLower()
            // Remove common downloader app prefixes
            .Replace("soundloaders.app", "")
            .Replace("soundloaders app", "")
            .Replace("soundloaders_app", "")
            .Replace("__spotdown.app", "")
            .Replace("spotdown.app", "")
            .Replace("spotdown app", "")
            .Replace("spotdown_app", "")
            .Replace("music.download", "")
            .Replace("music download", "")
            .Replace("music_download", "")
            .Replace(".app ", "")
            .Replace(".app-", "")
            .Replace("_app_", "")
            .Replace("_app ", "")
            .Replace("-(from", "")
            // Remove ALL special characters
            .Replace(".", "")      // <-- ADD THIS
            .Replace("&", "")
            .Replace("|", "")
            .Replace("*", "")
            .Replace(":", "")
            .Replace(";", "")
            .Replace("<", "")
            .Replace(">", "")
            .Replace("/", "")
            .Replace("\\", "")
            .Replace("?", "")
            .Replace("!", "")
            .Replace("'", "")
            .Replace("\"", "")
            .Replace(",", "")      // <-- ADD THIS too
            .Replace("(", "")
            .Replace(")", "")
            .Replace("[", "")
            .Replace("]", "")
            .Replace("-", " ")
            .Replace("_", " ")
            .Trim()
            .Replace("  ", " ")
            .Replace("  ", " ");
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

        Debug.WriteLine("Download page cleanup completed");
    }

    protected override bool OnBackButtonPressed()
    {
        if (_isDownloading)
        {
            MainThread.BeginInvokeOnMainThread(async () => {
                await DisplayAlert("Download in Progress", "Please wait for the download to complete.", "OK");
            });
            return true;
        }

        return base.OnBackButtonPressed();
    }

    private async void OnServerInfoClicked(object sender, EventArgs e)
    {
        string? enteredHost = await DisplayPromptAsync(
            "Server IP",
            "Enter server IP (or host name):",
            accept: "Save",
            cancel: "Cancel",
            placeholder: "e.g. 192.168.1.49",
            initialValue: _serverHost,
            keyboard: Keyboard.Text);

        if (string.IsNullOrWhiteSpace(enteredHost))
            return;

        enteredHost = enteredHost.Trim();

        if (!ServerEndpointSettings.IsValidHost(enteredHost))
        {
            await DisplayAlert("Invalid value", "Please enter a valid IP address or host name.", "OK");
            return;
        }

        _serverHost = enteredHost;
        ServerEndpointSettings.SetHost(_serverHost);

        SetStatus($"Server set to: {_serverHost}:{SERVER_PORT}", Colors.LightGreen);
    }

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

    private static bool IsValidServerHost(string host)
    {
        // Accept both IPv4/IPv6 and DNS names
        return System.Net.IPAddress.TryParse(host, out _)
               || Uri.CheckHostName(host) != UriHostNameType.Unknown;
    }
}
