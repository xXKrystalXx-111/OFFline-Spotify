using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Microsoft.Maui.Controls;

namespace OFFline_Spotify;

public partial class Playlists : ContentPage
{
    public ObservableCollection<PlaylistEntity> PlaylistsCollection { get; set; } = new();

    public ICommand PlaylistTappedCommand { get; }
    public ICommand DeletePlaylistCommand { get; }
    public ICommand PlayPlaylistCommand { get; }

    public Playlists()
    {
        InitializeComponent();
        BindingContext = this;
        PlaylistTappedCommand = new Command<PlaylistEntity>(OnPlaylistTapped);
        DeletePlaylistCommand = new Command<PlaylistEntity>(async (playlist) => await OnDeletePlaylist(playlist));
        PlayPlaylistCommand = new Command<PlaylistEntity>(async (playlist) => await OnPlayPlaylist(playlist));
    }

    // Add OnAppearing override to refresh playlists when page appears
    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadPlaylists();
    }

    private async void OnPlaylistTapped(PlaylistEntity playlist)
    {
        if (playlist != null)
        {
            await Shell.Current.GoToAsync($"{nameof(PlaylistDetails)}?playlistId={playlist.Id}");
        }
    }

    private async void LoadPlaylists()
    {
        if (App.Database != null)
        {
            var playlists = await App.Database.GetAllPlaylistsAsync();
            PlaylistsCollection.Clear();
            foreach (var playlist in playlists)
            {
                if (!string.IsNullOrEmpty(playlist.ImagePath) && File.Exists(playlist.ImagePath))
                {
                    // Normalize for MAUI
                    playlist.ImagePath = new Uri(playlist.ImagePath).AbsoluteUri;
                }
                PlaylistsCollection.Add(playlist);
                Debug.WriteLine($"ImagePath: {playlist.ImagePath} Exists: {File.Exists(playlist.ImagePath)}");
            }
        }
    }

    private async void OnPlaylistPointerEntered(object sender, PointerEventArgs e)
    {
        if (sender is Grid grid)
        {
            var deleteButton = grid.Children.OfType<ImageButton>().FirstOrDefault();
            var playButton = grid.Children.OfType<Button>().FirstOrDefault();
            
            if (deleteButton != null)
            {
                deleteButton.IsVisible = true;
                await Task.WhenAll(
                    deleteButton.FadeTo(1, 200, Easing.CubicOut),
                    deleteButton.ScaleTo(1, 200, Easing.CubicOut)
                );
            }
            
            if (playButton != null)
            {
                playButton.IsVisible = true;
                await Task.WhenAll(
                    playButton.FadeTo(0.9, 200, Easing.CubicOut),
                    playButton.ScaleTo(1, 200, Easing.CubicOut)
                );
            }
        }
    }

    private async void OnPlaylistPointerExited(object sender, PointerEventArgs e)
    {
        if (sender is Grid grid)
        {
            var deleteButton = grid.Children.OfType<ImageButton>().FirstOrDefault();
            var playButton = grid.Children.OfType<Button>().FirstOrDefault();
            
            if (deleteButton != null)
            {
                await Task.WhenAll(
                    deleteButton.FadeTo(0, 200, Easing.CubicIn),
                    deleteButton.ScaleTo(0.5, 200, Easing.CubicIn)
                );
                deleteButton.IsVisible = false;
            }
            
            if (playButton != null)
            {
                await Task.WhenAll(
                    playButton.FadeTo(0, 200, Easing.CubicIn),
                    playButton.ScaleTo(0.5, 200, Easing.CubicIn)
                );
                playButton.IsVisible = false;
            }
        }
    }

    private async void DeletePlaylist_Clicked(object sender, EventArgs e)
    {
        if (sender is ImageButton button && button.CommandParameter is PlaylistEntity playlist)
        {
            await OnDeletePlaylist(playlist);
        }
    }

    private async void PlayPlaylist_Clicked(object sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is PlaylistEntity playlist)
        {
            await OnPlayPlaylist(playlist);
        }
    }

    private async Task OnPlayPlaylist(PlaylistEntity playlist)
    {
        if (playlist == null) return;

        try
        {
            if (App.Database == null) return;

            // Get all songs in the playlist
            var songs = await App.Database.GetSongsByPlaylistIdAsync(playlist.Id);
            
            if (songs.Count == 0)
            {
                await DisplayAlert("Empty Playlist", "This playlist has no songs.", "OK");
                return;
            }

            // Find the first available song (one that has a valid MP3 file)
            SongEntity? firstAvailableSong = null;
            foreach (var song in songs)
            {
                if (!string.IsNullOrEmpty(song.Mp3FilePath) && File.Exists(song.Mp3FilePath))
                {
                    firstAvailableSong = song;
                    break;
                }
            }

            if (firstAvailableSong == null)
            {
                await DisplayAlert("No Available Songs", 
                    "No songs in this playlist have downloaded audio files.", 
                    "OK");
                return;
            }

            // Navigate to PlaylistDetails page with autoplay parameter
            await Shell.Current.GoToAsync($"{nameof(PlaylistDetails)}?playlistId={playlist.Id}&autoplay=true");
            
            Debug.WriteLine($"Starting playback of playlist: {playlist.Name}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error playing playlist: {ex.Message}");
            await DisplayAlert("Error", $"Failed to start playback: {ex.Message}", "OK");
        }
    }

    private async Task OnDeletePlaylist(PlaylistEntity playlist)
    {
        if (playlist == null) return;

        bool confirm = await DisplayAlert(
            "Delete Playlist", 
            $"Are you sure you want to delete the playlist '{playlist.Name}'? This action cannot be undone.", 
            "Delete", "Cancel");

        if (!confirm) return;

        try
        {
            if (App.Database != null)
            {
                // Delete playlist and its songs from database
                await App.Database.DeletePlaylistAsync(playlist.Id);
                
                // Delete playlist folder and MP3 files
                await DeletePlaylistFiles(playlist);
                
                // Remove from collection
                PlaylistsCollection.Remove(playlist);
                
                await DisplayAlert("Success", $"Playlist '{playlist.Name}' has been deleted.", "OK");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error deleting playlist: {ex.Message}");
            await DisplayAlert("Error", $"Failed to delete playlist: {ex.Message}", "OK");
        }
    }

    private Task DeletePlaylistFiles(PlaylistEntity playlist)
    {
        try
        {
            if (string.IsNullOrEmpty(playlist.Name)) return Task.CompletedTask;

            string folderName = playlist.Name;
            string sanitizedFolderName = string.Join("_", folderName.Split(Path.GetInvalidFileNameChars()));
            string playlistFolderPath = Path.Combine(FileSystem.AppDataDirectory, sanitizedFolderName);

            if (Directory.Exists(playlistFolderPath))
            {
                Directory.Delete(playlistFolderPath, true);
                Debug.WriteLine($"Deleted playlist folder: {playlistFolderPath}");
            }

            // Also delete playlist image if it exists
            if (!string.IsNullOrEmpty(playlist.ImagePath))
            {
                // Extract the actual file path from the URI if needed
                string actualPath = playlist.ImagePath;
                if (playlist.ImagePath.StartsWith("file://"))
                {
                    actualPath = new Uri(playlist.ImagePath).LocalPath;
                }
                
                if (File.Exists(actualPath))
                {
                    File.Delete(actualPath);
                    Debug.WriteLine($"Deleted playlist image: {actualPath}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error deleting playlist files: {ex.Message}");
        }
        
        return Task.CompletedTask;
    }
}