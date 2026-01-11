using System.Threading.Tasks;
using System.IO;
using Microsoft.Maui.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OFFline_Spotify
{
    public partial class MainPage : ContentPage, INotifyPropertyChanged
    {

        

        public MainPage()
        {
            try
            {
                InitializeComponent();
                BindingContext = this; // Ensure this is set correctly
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing MainPage: {ex.Message}");
            }
        }

       

        private async void Button_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(Playlists));
        }

        private async void Button_Clicked_1(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(Download));
        }
    }

    
}
