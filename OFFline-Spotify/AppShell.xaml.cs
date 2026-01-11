using Microsoft.Maui.Controls;

namespace OFFline_Spotify
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Routing.RegisterRoute(nameof(Download), typeof(Download));
            Routing.RegisterRoute(nameof(Playlists), typeof(Playlists));
            Routing.RegisterRoute(nameof(PlaylistDetails), typeof(PlaylistDetails));
            
            // Register the new page route
            Routing.RegisterRoute(nameof(ReplaceMp3Page), typeof(ReplaceMp3Page));
        }
    }
}
