using CommunityToolkit.Maui.Core.Primitives;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Text;

namespace OFFline_Spotify
{
    [QueryProperty(nameof(PlaylistId), "playlistId")]
    [QueryProperty(nameof(Autoplay), "autoplay")]
    public partial class PlaylistDetails : ContentPage
    {
        public int PlaylistId { get; set; }
        public bool Autoplay { get; set; }

        private ObservableCollection<SongEntity> _playlist = new();
        private int _currentSongIndex = -1;
        private bool _isUserDraggingSlider = false;
        private bool _isPlaying = false;
        private bool _isTransitioning = false;
        private SongEntity? _currentlyPlayingSong;

        public PlaylistDetails()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            LoadPlaylistSongs();
            await DiagnoseSongLoadingIssues();

            // Initialize volume for both desktop and mobile
            if (Player != null)
            {
                Player.Volume = 1.0;
                VolumeSlider.Value = 100;
                
                // Initialize mobile volume controls if they exist
                if (VolumeSliderMobile != null)
                {
                    VolumeSliderMobile.Value = 100;
                }
                if (VolumeLabelMobile != null)
                {
                    VolumeLabelMobile.Text = "100%";
                }
            }

            // Auto-play if requested
            if (Autoplay && _playlist.Count > 0)
            {
                await StartAutoplay();
            }
        }

        // Add this new method to handle autoplay
        private async Task StartAutoplay()
        {
            try
            {
                // Find the first available song
                for (int i = 0; i < _playlist.Count; i++)
                {
                    var song = _playlist[i];
                    if (!string.IsNullOrEmpty(song.Mp3FilePath) && File.Exists(song.Mp3FilePath))
                    {
                        _currentSongIndex = i;
                        _isPlaying = true;
                        await PlaySongAtIndex(i);
                        Debug.WriteLine($"Autoplay started with song: {song.Title}");
                        break;
                    }
                }

                if (_currentSongIndex == -1)
                {
                    Debug.WriteLine("No available songs found for autoplay");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in autoplay: {ex.Message}");
            }
            finally
            {
                // Reset autoplay flag
                Autoplay = false;
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            try
            {
                _isPlaying = false;
                if (Player != null)
                {
                    Player.Stop();
                    Player.Source = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error stopping Player on disappearing: {ex.Message}");
            }
        }

        private async void LoadPlaylistSongs()
        {
            try
            {
                if (App.Database != null)
                {
                    // Get playlist information to set the title
                    var playlistEntity = await App.Database.GetPlaylistByIdAsync(PlaylistId);
                    if (playlistEntity != null)
                    {
                        this.Title = playlistEntity.Name ?? "Playlist Details";
                        Debug.WriteLine($"Set page title to: {this.Title}");
                    }

                    // Get songs directly from database - no SpotifyService needed
                    var songs = await App.Database.GetSongsByPlaylistIdAsync(PlaylistId);

                    Debug.WriteLine($"Loaded {songs.Count} songs");

                    // Clear and repopulate ObservableCollection
                    _playlist.Clear();
                    foreach (var song in songs)
                    {
                        _playlist.Add(song);
                    }

                    // Set ItemsSource only once
                    if (SongsListView.ItemsSource == null)
                    {
                        SongsListView.ItemsSource = _playlist;
                    }

                    // Check for missing songs (rest of your existing code)
                    var missingSongs = _playlist.Where(s => string.IsNullOrEmpty(s.Mp3FilePath) || !File.Exists(s.Mp3FilePath)).ToList();

                    if (missingSongs.Count > 0)
                    {
                        Debug.WriteLine($"Found {missingSongs.Count} songs with missing MP3 files:");
                        foreach (var song in missingSongs)
                        {
                            Debug.WriteLine($"  - {song.Title} by {song.Artist} (Path: {song.Mp3FilePath ?? "NULL"})");
                        }

                        if (missingSongs.Count == _playlist.Count && _playlist.Count > 0)
                        {
                            bool retry = await DisplayAlert(
                                "Missing Audio Files",
                                $"All {missingSongs.Count} songs are missing audio files. Would you like to attempt to locate them?",
                                "Yes", "No");

                            if (retry)
                            {
                                await AttemptToRecoverMissingFiles(missingSongs);
                            }
                        }
                        else if (missingSongs.Count > 0)
                        {
                            await DisplayAlert(
                                "Some Audio Files Missing",
                                $"{missingSongs.Count} out of {_playlist.Count} songs are missing audio files. Songs without files are shown in red.",
                                "OK");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading songs: {ex.Message}");
                await DisplayAlert("Error", $"Failed to load songs: {ex.Message}", "OK");
            }
        }

        private async Task AttemptToRecoverMissingFiles(List<SongEntity> missingSongs)
        {
            if (missingSongs.Count == 0 || App.Database == null)
                return;
                
            try
            {
                var playlist = await App.Database.GetPlaylistByIdAsync(PlaylistId);
                if (playlist == null)
                    return;
                    
                string folderName = playlist.Name ?? "Unknown";
                string sanitizedFolderName = string.Join("_", folderName.Split(Path.GetInvalidFileNameChars()));
                string playlistFolderPath = Path.Combine(FileSystem.AppDataDirectory, sanitizedFolderName);
                
                if (!Directory.Exists(playlistFolderPath))
                {
                    Debug.WriteLine($"Playlist folder not found: {playlistFolderPath}");
                    await DisplayAlert("Folder Not Found", "The playlist folder could not be found.", "OK");
                    return;
                }
                
                var mp3Files = Directory.GetFiles(playlistFolderPath, "*.mp3", SearchOption.AllDirectories);
                Debug.WriteLine($"Found {mp3Files.Length} MP3 files in {playlistFolderPath}");
                
                int matchedCount = 0;
                
                foreach (var song in missingSongs)
                {
                    string songTitle = song.Title?.ToLower() ?? "";
                    string songArtist = song.Artist?.ToLower() ?? "";
                    
                    string? matchedFile = null;
                    foreach (var file in mp3Files)
                    {
                        string fileName = Path.GetFileNameWithoutExtension(file).ToLower();
                        
                        if (!string.IsNullOrEmpty(songTitle) && 
                            fileName.Contains(songTitle) && 
                            (string.IsNullOrEmpty(songArtist) || fileName.Contains(songArtist)))
                        {
                            matchedFile = file;
                            break;
                        }
                    }
                    
                    if (matchedFile != null)
                    {
                        song.Mp3FilePath = matchedFile;
                        await App.Database.SaveSongAsync(song);
                        matchedCount++;
                        Debug.WriteLine($"Matched song '{song.Title}' to file '{Path.GetFileName(matchedFile)}'");
                    }
                }
                
                // Reload the playlist
                var updatedSongs = await App.Database.GetSongsByPlaylistIdAsync(PlaylistId);
                _playlist.Clear();
                foreach (var song in updatedSongs)
                {
                    _playlist.Add(song);
                }
                
                if (matchedCount > 0)
                {
                    await DisplayAlert("Files Located", $"Successfully located {matchedCount} out of {missingSongs.Count} missing files.", "OK");
                }
                else
                {
                    await DisplayAlert("No Matches", "Could not locate any missing audio files. Try downloading the songs again.", "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error recovering missing files: {ex.Message}");
                await DisplayAlert("Error", $"Failed to recover files: {ex.Message}", "OK");
            }
        }

        
        private async void SongsListView_ItemTapped(object sender, ItemTappedEventArgs e)
        {
            try
            {
                if (e.Item is SongEntity song)
                {
                    _currentSongIndex = _playlist.IndexOf(song);
                    _isPlaying = true;
                    await PlaySongAtIndex(_currentSongIndex);
                    ((ListView)sender).SelectedItem = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Playback error: {ex.Message}");
                await DisplayAlert("Playback Error", $"Could not play this song:\n{ex.Message}", "OK");
            }
        }

        private async Task PlaySongAtIndex(int index)
        {
            if (_isTransitioning)
            {
                Debug.WriteLine("Already transitioning between songs, ignoring request");
                return;
            }

            _isTransitioning = true;

            try
            {
                if (index < 0 || index >= _playlist.Count)
                {
                    Debug.WriteLine($"Invalid index: {index}");
                    _isTransitioning = false;
                    return;
                }

                var song = _playlist[index];
                
                // Update currently playing song - INotifyPropertyChanged will handle UI update
                if (_currentlyPlayingSong != null)
                {
                    _currentlyPlayingSong.IsCurrentlyPlaying = false;
                }
                _currentlyPlayingSong = song;
                song.IsCurrentlyPlaying = true;
                
                
                
                if (string.IsNullOrEmpty(song.Mp3FilePath))
                {
                    await DisplayAlert("No Audio File", "This song has not been downloaded yet.", "OK");
                    _isTransitioning = false;
                    await Task.Delay(500);
                    await PlayNextSong();
                    return;
                }

                if (!File.Exists(song.Mp3FilePath))
                {
                    await DisplayAlert("File Not Found", $"The audio file could not be found:\n{song.Mp3FilePath}", "OK");
                    _isTransitioning = false;
                    await Task.Delay(500);
                    await PlayNextSong();
                    return;
                }

                Player.Stop();
                await Task.Delay(100);
                
                Player.Source = MediaSource.FromFile(song.Mp3FilePath);
                Player.Play();
                
                NowPlayingLabel.Text = $"{song.Title} - {song.Artist}";
                PlayPauseButton.Text = "‖";
                
                Debug.WriteLine($"Playing song {index + 1}/{_playlist.Count}: {song.Title} from {song.Mp3FilePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error playing song: {ex.Message}");
                await DisplayAlert("Playback Error", ex.Message, "OK");
                
                await Task.Delay(500);
                _isTransitioning = false;
                await PlayNextSong();
            }
    
            _isTransitioning = false;
        }

        private async Task PlayNextSong()
        {
            if (!_isPlaying || _playlist.Count == 0)
                return;

            // Move to next song, or loop back to the first song
            if (_currentSongIndex < _playlist.Count - 1)
            {
                _currentSongIndex++;
            }
            else
            {
                // Loop back to the beginning of the playlist
                _currentSongIndex = 0;
                Debug.WriteLine("Reached end of playlist - looping back to start");
            }

            Debug.WriteLine($"Auto-playing next song at index {_currentSongIndex}");
            await PlaySongAtIndex(_currentSongIndex);

            // Force play state to ensure it starts
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (_isPlaying && Player.CurrentState != MediaElementState.Playing)
                {
                    Debug.WriteLine("Forcing play state after transition");
                    Player.Play();
                }
            });
        }

        private void PlayPause_Clicked(object sender, EventArgs e)
        {
            try
            {
                if (Player.CurrentState == MediaElementState.Playing)
                {
                    Player.Pause();
                    _isPlaying = false;
                    PlayPauseButton.Text = "►"; // Play symbol
                    Debug.WriteLine("Playback paused by user");
                }
                else if (Player.CurrentState == MediaElementState.Paused)
                {
                    Player.Play();
                    _isPlaying = true;
                    PlayPauseButton.Text = "‖"; // Pause symbol
                    Debug.WriteLine("Playback resumed by user");
                }
                else if (_currentSongIndex >= 0)
                {
                    // Resume or start playing current song
                    _isPlaying = true;
                    _ = PlaySongAtIndex(_currentSongIndex);
                    Debug.WriteLine("Starting playback");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Play/Pause error: {ex.Message}");
            }
        }

        private async void Previous_Clicked(object sender, EventArgs e)
        {
            if (_playlist.Count == 0)
                return;

            _isPlaying = true;

            if (_currentSongIndex > 0)
            {
                _currentSongIndex--;
            }
            else
            {
                // Loop back to last song
                _currentSongIndex = _playlist.Count - 1;
            }

            await PlaySongAtIndex(_currentSongIndex);

            // Ensure it's playing
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (_isPlaying && Player.CurrentState != MediaElementState.Playing)
                {
                    Player.Play();
                }
            });
        }

        private async void Next_Clicked(object sender, EventArgs e)
        {
            if (_playlist.Count == 0)
                return;

            _isPlaying = true;
            await PlayNextSong();
        }

        private async void Player_MediaEnded(object sender, EventArgs e)
        {
            Debug.WriteLine($"*** MediaEnded event fired! _isPlaying={_isPlaying}, CurrentIndex={_currentSongIndex}/{_playlist.Count}");

            try
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (!_isPlaying || _playlist.Count == 0)
                    {
                        Debug.WriteLine("Not playing or no songs, skipping auto-play");
                        return;
                    }

                    Debug.WriteLine("Waiting 200ms before playing next song...");
                    await Task.Delay(200); // Small delay to ensure clean transition

                    await PlayNextSong();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in Player_MediaEnded: {ex.Message}");
            }
        }

        private void Player_StateChanged(object sender, MediaStateChangedEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Debug.WriteLine($"*** Player state changed to: {e.NewState}");

                if (e.NewState == MediaElementState.Playing)
                {
                    PlayPauseButton.Text = "‖"; // Pause symbol
                }
                else if (e.NewState == MediaElementState.Paused || e.NewState == MediaElementState.Stopped)
                {
                    PlayPauseButton.Text = "►"; // Play symbol
                }

                // Handle case where player gets stuck in paused state during auto-play
                if (_isPlaying && e.NewState == MediaElementState.Paused && !_isTransitioning)
                {
                    Debug.WriteLine("Player unexpectedly paused during auto-play, forcing Play");
                    Player.Play();
                }
            });
        }

        private void Player_PositionChanged(object sender, MediaPositionChangedEventArgs e)
        {
            if (_isUserDraggingSlider)
                return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                var position = e.Position;
                var duration = Player.Duration;

                if (duration.TotalSeconds > 0)
                {
                    ProgressSlider.Value = (position.TotalSeconds / duration.TotalSeconds) * 100;
                    CurrentTimeLabel.Text = FormatTime(position);
                    TotalTimeLabel.Text = FormatTime(duration);

                    // Fallback: If we're within 0.5 second of the end and MediaEnded hasn't fired
                    if (_isPlaying &&
                        (duration.TotalSeconds - position.TotalSeconds) < 0.5 &&
                        (duration.TotalSeconds - position.TotalSeconds) > 0)
                    {
                        Debug.WriteLine($"Near end of song: {duration.TotalSeconds - position.TotalSeconds}s remaining");
                    }
                }
            });
        }

        private void ProgressSlider_DragStarted(object sender, EventArgs e)
        {
            _isUserDraggingSlider = true;
        }

        private void ProgressSlider_DragCompleted(object sender, EventArgs e)
        {
            _isUserDraggingSlider = false;

            // Seek to the new position
            var duration = Player.Duration;
            if (duration.TotalSeconds > 0)
            {
                var newPosition = TimeSpan.FromSeconds((ProgressSlider.Value / 100) * duration.TotalSeconds);
                Player.SeekTo(newPosition);
                Debug.WriteLine($"Seeked to {FormatTime(newPosition)}");
            }
        }

        private void ProgressSlider_ValueChanged(object sender, ValueChangedEventArgs e)
        {
            if (_isUserDraggingSlider)
            {
                // Update time label while dragging
                var duration = Player.Duration;
                if (duration.TotalSeconds > 0)
                {
                    var previewTime = TimeSpan.FromSeconds((e.NewValue / 100) * duration.TotalSeconds);
                    CurrentTimeLabel.Text = FormatTime(previewTime);
                }
            }
        }

        // Update the VolumeSlider_ValueChanged method to handle both sliders
        private void VolumeSlider_ValueChanged(object sender, ValueChangedEventArgs e)
        {
            try
            {
                if (Player != null)
                {
                    // Convert percentage (0-100) to volume (0.0-1.0)
                    double volumeLevel = e.NewValue / 100.0;
                    Player.Volume = volumeLevel;
                    
                    // Update both volume labels and sync sliders
                    string volumeText = $"{(int)e.NewValue}%";
                    VolumeLabel.Text = volumeText;
                    
                    // Update mobile volume label and slider if they exist (Android)
                    if (VolumeLabelMobile != null)
                    {
                        VolumeLabelMobile.Text = volumeText;
                    }
                    
                    // Sync the other slider
                    if (sender == VolumeSlider && VolumeSliderMobile != null && Math.Abs(VolumeSliderMobile.Value - e.NewValue) > 0.1)
                    {
                        VolumeSliderMobile.Value = e.NewValue;
                    }
                    else if (sender == VolumeSliderMobile && VolumeSlider != null && Math.Abs(VolumeSlider.Value - e.NewValue) > 0.1)
                    {
                        VolumeSlider.Value = e.NewValue;
                    }
                    
                    Debug.WriteLine($"Volume changed to: {volumeLevel} ({(int)e.NewValue}%)");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error changing volume: {ex.Message}");
            }
        }

        private string FormatTime(TimeSpan time)
        {
            if (time.TotalHours >= 1)
                return time.ToString(@"h\:mm\:ss");
            return time.ToString(@"m\:ss");
        }

        // Add this method to the class
        private async Task DiagnoseSongLoadingIssues()
        {
            Debug.WriteLine("=== SONG LOADING DIAGNOSTIC INFORMATION ===");

            // Check if App.Database is initialized
            Debug.WriteLine($"Database initialized: {App.Database != null}");

            // Check playlist ID
            Debug.WriteLine($"PlaylistId property: {PlaylistId}");

            // Check if playlist exists
            if (App.Database != null)
            {
                var playlist = await App.Database.GetPlaylistByIdAsync(PlaylistId);
                Debug.WriteLine($"Playlist found in database: {playlist != null}");
                if (playlist != null)
                {
                    Debug.WriteLine($"  Name: {playlist.Name}");
                    Debug.WriteLine($"  SpotifyId: {playlist.SpotifyId}");
                }

                // Check songs directly in database
                var songs = await App.Database.GetSongsByPlaylistIdAsync(PlaylistId);
                Debug.WriteLine($"Songs in database for this playlist: {songs.Count}");

                foreach (var song in songs)
                {
                    Debug.WriteLine($"  Song: {song.Title} by {song.Artist}");
                    Debug.WriteLine($"    ID: {song.Id}");
                    Debug.WriteLine($"    MP3 Path: {song.Mp3FilePath ?? "NULL"}");
                    Debug.WriteLine($"    MP3 File Exists: {!string.IsNullOrEmpty(song.Mp3FilePath) && File.Exists(song.Mp3FilePath)}");
                }
            }

            Debug.WriteLine("=== END DIAGNOSTIC INFORMATION ===");
        }

        // Updated method to handle ImageButton click
        private async void EditSong_Clicked(object sender, EventArgs e)
        {
            try
            {
                Debug.WriteLine("EditSong_Clicked triggered!");

                if (sender is ImageButton imageButton && imageButton.CommandParameter is SongEntity song)
                {
                    Debug.WriteLine($"Navigating to edit page for song: {song.Title} (ID: {song.Id})");
                    Debug.WriteLine($"Current PlaylistId: {PlaylistId}");

                    string route = $"{nameof(ReplaceMp3Page)}?songId={song.Id}&playlistId={PlaylistId}";
                    Debug.WriteLine($"Navigation route: {route}");

                    await Shell.Current.GoToAsync(route);
                    Debug.WriteLine("Navigation completed successfully");
                }
                else
                {
                    Debug.WriteLine("ERROR: Invalid sender or CommandParameter");
                    Debug.WriteLine($"Sender type: {sender?.GetType().Name}");
                    Debug.WriteLine($"CommandParameter type: {(sender as ImageButton)?.CommandParameter?.GetType().Name}");

                    await DisplayAlert("Error", "Invalid song selection. Please try again.", "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Navigation error: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                await DisplayAlert("Error", $"Failed to navigate to edit page: {ex.Message}", "OK");
            }
        }

        // Add this method to the PlaylistDetails class
        private async void DeletePlaylist_Clicked(object sender, EventArgs e)
        {
            try
            {
                if (App.Database == null) return;

                var playlist = await App.Database.GetPlaylistByIdAsync(PlaylistId);
                if (playlist == null)
                {
                    await DisplayAlert("Error", "Playlist not found.", "OK");
                    return;
                }

                bool confirm = await DisplayAlert(
                    "Delete Playlist",
                    $"Are you sure you want to delete the playlist '{playlist.Name}'? This will remove all songs and cannot be undone.",
                    "Delete", "Cancel");

                if (!confirm) return;

                // Stop player if it's currently playing
                if (Player != null)
                {
                    _isPlaying = false;
                    Player.Stop();
                    Player.Source = null;
                }

                // Delete playlist and its songs from database
                await App.Database.DeletePlaylistAsync(PlaylistId);

                // Delete playlist folder and MP3 files
                await DeletePlaylistFiles(playlist);

                await DisplayAlert("Success", $"Playlist '{playlist.Name}' has been deleted.", "OK");

                // Navigate back to playlists page
                await Shell.Current.GoToAsync("..");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error deleting playlist: {ex.Message}");
                await DisplayAlert("Error", $"Failed to delete playlist: {ex.Message}", "OK");
            }
        }

        private async Task DeletePlaylistFiles(PlaylistEntity playlist)
        {
            try
            {
                if (string.IsNullOrEmpty(playlist.Name)) return;

                string folderName = playlist.Name;
                string sanitizedFolderName = string.Join("_", folderName.Split(Path.GetInvalidFileNameChars()));
                string playlistFolderPath = Path.Combine(FileSystem.AppDataDirectory, sanitizedFolderName);

                if (Directory.Exists(playlistFolderPath))
                {
                    Directory.Delete(playlistFolderPath, true);
                    Debug.WriteLine($"Deleted playlist folder: {playlistFolderPath}");
                }

                // Also delete playlist image if it exists
                if (!string.IsNullOrEmpty(playlist.ImagePath) && File.Exists(playlist.ImagePath))
                {
                    File.Delete(playlist.ImagePath);
                    Debug.WriteLine($"Deleted playlist image: {playlist.ImagePath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error deleting playlist files: {ex.Message}");
            }
        }

        private async void Fix_Download(object sender, EventArgs e)
        {
            try
            {
                if (App.Database == null)
                {
                    await DisplayAlert("Error", "Database not initialized.", "OK");
                    return;
                }

                // Get all songs for this playlist
                var allSongs = await App.Database.GetSongsByPlaylistIdAsync(PlaylistId);
                
                // Find songs with missing MP3 files
                var missingSongs = allSongs
                    .Where(s => string.IsNullOrEmpty(s.Mp3FilePath) || !File.Exists(s.Mp3FilePath))
                    .ToList();

                if (missingSongs.Count == 0)
                {
                    await DisplayAlert("No Missing Files", "All songs in this playlist have valid audio files!", "OK");
                    return;
                }

                bool confirm = await DisplayAlert(
                    "Fix Download",
                    $"Found {missingSongs.Count} song(s) with missing audio files. Would you like to download them?",
                    "Yes", "Cancel");

                if (!confirm) return;

                // Show loading indicator
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    // You can add a loading spinner here if you have one
                });

                // Call the fix download method
                await FixMissingDownloads(missingSongs);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in Fix_Download: {ex.Message}");
                await DisplayAlert("Error", $"Failed to fix downloads: {ex.Message}", "OK");
            }
        }

        private async Task FixMissingDownloads(List<SongEntity> missingSongs)
        {
            try
            {
                // Format songs as "title - artist" separated by newlines
                var songRequests = missingSongs
                    .Select(s => $"{s.Title} - {s.Artist}")
                    .ToList();

                string requestData = string.Join("\n", songRequests);
                
                Debug.WriteLine($"Sending fix download request for {missingSongs.Count} songs:");
                Debug.WriteLine(requestData);

                // Send request to Python server (reusing the Download.xaml.cs logic)
                var result = await SendFixDownloadRequestAsync(requestData);

                if (result.Success)
                {
                    // Get playlist info to determine folder path
                    var playlist = await App.Database!.GetPlaylistByIdAsync(PlaylistId);
                    if (playlist == null)
                    {
                        await DisplayAlert("Error", "Playlist not found.", "OK");
                        return;
                    }

                    string folderName = playlist.Name ?? "Unknown";
                    string sanitizedFolderName = string.Join("_", folderName.Split(Path.GetInvalidFileNameChars()));
                    string playlistFolderPath = Path.Combine(FileSystem.AppDataDirectory, sanitizedFolderName);

                    // Save the zip file
                    string zipPath = await SaveFixDownloadZipAsync(result.ZipData, result.FileName);
                    
                    // Extract and match files
                    await ProcessFixDownloadZip(zipPath, playlistFolderPath, missingSongs);

                    // Reload the playlist to show updated songs
                    LoadPlaylistSongs();

                    await DisplayAlert("Success", 
                        $"Fixed download completed! Downloaded {missingSongs.Count} song(s).", 
                        "OK");
                }
                else
                {
                    await DisplayAlert("Download Failed", result.ErrorMessage, "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in FixMissingDownloads: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private async Task<DownloadResult> SendFixDownloadRequestAsync(string songRequests)
        {
            const string SERVER_HOST = "192.168.1.49"; // Use same server as Download.xaml.cs
            const int SERVER_PORT = 9999;
            const long BUFFER_SIZE = 3000000000;

            TcpClient? client = null;
            NetworkStream? stream = null;

            try
            {
                // Connect to server
                client = new TcpClient();
                await client.ConnectAsync(SERVER_HOST, SERVER_PORT);
                stream = client.GetStream();

                Debug.WriteLine($"Connected to {SERVER_HOST}:{SERVER_PORT}");

                // Send song requests
                byte[] sendData = Encoding.UTF8.GetBytes(songRequests);
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

                var header = System.Text.Json.JsonSerializer.Deserialize<ServerResponse>(headerJson);

                if (header == null || header.status != "success")
                {
                    return new DownloadResult
                    {
                        Success = false,
                        ErrorMessage = header?.message ?? "Unknown error"
                    };
                }

                // Read file content
                long fileSize = header.size;
                string fileName = header.filename ?? "fix_downloads.zip";

                Debug.WriteLine($"Receiving file: {fileName} ({fileSize / 1024 / 1024:F2} MB)");

                byte[] fileData = new byte[fileSize];
                long totalRead = 0;

                while (totalRead < fileSize)
                {
                    int toRead = (int)Math.Min(BUFFER_SIZE, fileSize - totalRead);
                    bytesRead = await stream.ReadAsync(fileData, (int)totalRead, toRead);

                    if (bytesRead == 0)
                        break;

                    totalRead += bytesRead;
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

        private async Task<string> SaveFixDownloadZipAsync(byte[] zipData, string fileName)
        {
            try
            {
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
                Debug.WriteLine($"Fix download zip saved: {filePath}");

                return filePath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving zip file: {ex.Message}");
                throw;
            }
        }

        private async Task ProcessFixDownloadZip(string zipFilePath, string playlistFolderPath, List<SongEntity> missingSongs)
        {
            try
            {
                // Create temp extraction folder
                string tempExtractPath = Path.Combine(FileSystem.AppDataDirectory, "temp_fix_download");

                if (Directory.Exists(tempExtractPath))
                {
                    Directory.Delete(tempExtractPath, true);
                }

                Directory.CreateDirectory(tempExtractPath);

                // Extract ZIP
                System.IO.Compression.ZipFile.ExtractToDirectory(zipFilePath, tempExtractPath);
                Debug.WriteLine($"Extracted fix download zip to: {tempExtractPath}");

                // Get all MP3 files from extracted content
                var extractedMp3Files = Directory.GetFiles(tempExtractPath, "*.mp3", SearchOption.AllDirectories);
                Debug.WriteLine($"Found {extractedMp3Files.Length} MP3 files in extracted content");

                // Ensure playlist folder exists
                Directory.CreateDirectory(playlistFolderPath);

                int matchedCount = 0;

                // Match and move MP3 files to playlist folder
                foreach (var song in missingSongs)
                {
                    string? matchedFile = FindBestMatchingMp3ForSong(extractedMp3Files, song.Title, song.Artist);

                    if (matchedFile != null)
                    {
                        // Generate target file path
                        string targetFileName = $"{song.Title} - {song.Artist}.mp3";
                        string sanitizedFileName = string.Join("_", targetFileName.Split(Path.GetInvalidFileNameChars()));
                        string targetPath = Path.Combine(playlistFolderPath, sanitizedFileName);

                        // Handle duplicates
                        int counter = 1;
                        while (File.Exists(targetPath))
                        {
                            string nameWithoutExt = Path.GetFileNameWithoutExtension(sanitizedFileName);
                            targetPath = Path.Combine(playlistFolderPath, $"{nameWithoutExt}_{counter}.mp3");
                            counter++;
                        }

                        // Move file
                        File.Move(matchedFile, targetPath);
                        Debug.WriteLine($"Moved: {Path.GetFileName(matchedFile)} -> {Path.GetFileName(targetPath)}");

                        // Update database
                        song.Mp3FilePath = targetPath;
                        await App.Database!.SaveSongAsync(song);
                        matchedCount++;

                        Debug.WriteLine($"Updated song '{song.Title}' with MP3 path: {targetPath}");
                    }
                    else
                    {
                        Debug.WriteLine($"No match found for: {song.Title} - {song.Artist}");
                    }
                }

                // Cleanup
                Directory.Delete(tempExtractPath, true);
                File.Delete(zipFilePath);

                Debug.WriteLine($"Fix download processing completed. Matched {matchedCount}/{missingSongs.Count} songs");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing fix download zip: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private string? FindBestMatchingMp3ForSong(string[] mp3Files, string? title, string? artist)
        {
            if (mp3Files.Length == 0 || string.IsNullOrEmpty(title))
                return null;

            string cleanTitle = CleanForMatching(title);
            string cleanArtist = CleanForMatching(artist ?? "");

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
                if (IsFuzzyMatchForSong(cleanTitle, fileName))
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

        private bool IsFuzzyMatchForSong(string? trackName, string? fileName)
        {
            if (string.IsNullOrEmpty(trackName) || string.IsNullOrEmpty(fileName))
                return false;

            string trackNoSpaces = trackName.Replace(" ", "");
            string fileNoSpaces = fileName.Replace(" ", "");

            return fileNoSpaces.Contains(trackNoSpaces) || trackNoSpaces.Contains(fileNoSpaces);
        }

        // Helper classes for server communication
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

    // Add this class at the bottom of your file, outside the PlaylistDetails class
    public class Mp3FileExistsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string? filePath = value as string;
            bool fileExists = !string.IsNullOrEmpty(filePath) && File.Exists(filePath);

            if (parameter != null)
            {
                string param = parameter.ToString() ?? "";

                // For icon display
                if (param == "icon")
                    return fileExists ? "" : "⚠️";

                // For boolean with possible inversion
                if (param == "bool-inverted")
                    return !fileExists;
            }

            // For text color - changed from Orange to Red for missing files
            return fileExists ? Colors.White : Colors.Red;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class SongBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isPlaying && isPlaying)
            {
                return Color.FromArgb("#404040"); // Gray background for currently playing
            }
            return Colors.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}