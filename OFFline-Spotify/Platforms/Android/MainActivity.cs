using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace OFFline_Spotify
{
    [Activity(
        Theme = "@style/Maui.SplashTheme", 
        MainLauncher = true, 
        LaunchMode = LaunchMode.SingleTop, 
        ConfigurationChanges = ConfigChanges.ScreenSize | 
                              ConfigChanges.Orientation | 
                              ConfigChanges.UiMode | 
                              ConfigChanges.ScreenLayout | 
                              ConfigChanges.SmallestScreenSize | 
                              ConfigChanges.Density | 
                              ConfigChanges.Keyboard | 
                              ConfigChanges.KeyboardHidden,
        ScreenOrientation = ScreenOrientation.Portrait, // Lock to portrait or remove for both orientations
        WindowSoftInputMode = SoftInput.AdjustResize)] // Adjust layout when keyboard appears
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            
            // Enable edge-to-edge display for modern Android devices
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                Window?.SetDecorFitsSystemWindows(false);
            }
        }
    }
}
