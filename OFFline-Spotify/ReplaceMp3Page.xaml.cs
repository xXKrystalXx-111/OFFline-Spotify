using Microsoft.Maui.Controls;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;

namespace OFFline_Spotify
{
    [QueryProperty(nameof(SongId), "songId")]
    [QueryProperty(nameof(PlaylistId), "playlistId")]
    public partial class ReplaceMp3Page : ContentPage
    {
        public int SongId { get; set; }
        public int PlaylistId { get; set; }
        
        private SongEntity? _currentSong;
        private string? _selectedFilePath;

        public ReplaceMp3Page()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadSongInfo();
        }

        private async Task LoadSongInfo()
        {
            try
            {
                if (App.Database != null)
                {
                    _currentSong = await App.Database.GetSongByIdAsync(SongId);
                    
                    if (_currentSong != null)
                    {
                        SongTitleLabel.Text = _currentSong.Title ?? "Unknown Title";
                        SongArtistLabel.Text = _currentSong.Artist ?? "Unknown Artist";
                        
                        if (!string.IsNullOrEmpty(_currentSong.Mp3FilePath) && File.Exists(_currentSong.Mp3FilePath))
                        {
                            CurrentFileLabel.Text = $"Current file: {Path.GetFileName(_currentSong.Mp3FilePath)}";
                            CurrentFileLabel.TextColor = Colors.LightGreen;
                            RemoveButton.IsEnabled = true;
                        }
                        else
                        {
                            CurrentFileLabel.Text = "Current file: No file assigned or file missing";
                            CurrentFileLabel.TextColor = Colors.Orange;
                            RemoveButton.IsEnabled = false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading song info: {ex.Message}");
                await DisplayAlert("Error", "Failed to load song information.", "OK");
            }
        }

        private async void BrowseFile_Clicked(object sender, EventArgs e)
        {
            try
            {
                var fileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".mp3" } },
                    { DevicePlatform.macOS, new[] { "mp3" } },
                    { DevicePlatform.iOS, new[] { "public.mp3" } },
                    { DevicePlatform.Android, new[] { "audio/mpeg" } }
                });

                var result = await FilePicker.PickAsync(new PickOptions
                {
                    PickerTitle = "Select MP3 File",
                    FileTypes = fileTypes
                });

                if (result != null)
                {
                    _selectedFilePath = result.FullPath;
                    SelectedFileLabel.Text = $"Selected: {Path.GetFileName(_selectedFilePath)}";
                    SelectedFileLabel.IsVisible = true;
                    ReplaceButton.IsEnabled = true;
                    
                    Debug.WriteLine($"Selected file: {_selectedFilePath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error selecting file: {ex.Message}");
                await DisplayAlert("Error", "Failed to select file.", "OK");
            }
        }

        private async void Replace_Clicked(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedFilePath) || _currentSong == null)
                return;

            try
            {
                ReplaceButton.IsEnabled = false;
                StatusLabel.Text = "Replacing file...";
                StatusLabel.IsVisible = true;
                StatusLabel.TextColor = Colors.Orange;

                // Get the playlist folder path
                var playlist = await App.Database?.GetPlaylistByIdAsync(PlaylistId);
                if (playlist == null)
                {
                    await DisplayAlert("Error", "Playlist not found.", "OK");
                    return;
                }

                string folderName = playlist.Name ?? "Unknown";
                string sanitizedFolderName = string.Join("_", folderName.Split(Path.GetInvalidFileNameChars()));
                string playlistFolderPath = Path.Combine(FileSystem.AppDataDirectory, sanitizedFolderName);

                // Ensure the directory exists
                Directory.CreateDirectory(playlistFolderPath);

                // Create new file name
                string fileName = $"{_currentSong.Title}_{_currentSong.Artist}.mp3"
                    .Replace(" ", "_")
                    .Replace("/", "_")
                    .Replace("\\", "_");
                fileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));

                string destinationPath = Path.Combine(playlistFolderPath, fileName);

                // Copy the selected file to the playlist folder
                File.Copy(_selectedFilePath, destinationPath, true);

                // Update the database
                _currentSong.Mp3FilePath = destinationPath;
                await App.Database.SaveSongAsync(_currentSong);

                StatusLabel.Text = "File replaced successfully!";
                StatusLabel.TextColor = Colors.LightGreen;

                // Update the UI
                await LoadSongInfo();

                // Auto-close after 2 seconds
                await Task.Delay(2000);
                await Shell.Current.GoToAsync("..");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error replacing file: {ex.Message}");
                StatusLabel.Text = "Failed to replace file.";
                StatusLabel.TextColor = Colors.Red;
                await DisplayAlert("Error", $"Failed to replace file: {ex.Message}", "OK");
            }
            finally
            {
                ReplaceButton.IsEnabled = true;
            }
        }

        private async void Remove_Clicked(object sender, EventArgs e)
        {
            if (_currentSong == null)
                return;

            bool confirm = await DisplayAlert("Confirm", 
                "Are you sure you want to remove the current MP3 file assignment? This will not delete the physical file.", 
                "Yes", "No");

            if (!confirm)
                return;

            try
            {
                _currentSong.Mp3FilePath = null;
                await App.Database?.SaveSongAsync(_currentSong);

                StatusLabel.Text = "File assignment removed.";
                StatusLabel.TextColor = Colors.Orange;
                StatusLabel.IsVisible = true;

                await LoadSongInfo();

                await Task.Delay(2000);
                await Shell.Current.GoToAsync("..");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error removing file: {ex.Message}");
                await DisplayAlert("Error", $"Failed to remove file assignment: {ex.Message}", "OK");
            }
        }

        private async void Cancel_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("..");
        }
    }
}