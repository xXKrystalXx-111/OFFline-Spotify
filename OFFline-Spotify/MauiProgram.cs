using Microsoft.Extensions.Logging;
using System.Diagnostics;
using CommunityToolkit.Maui;

namespace OFFline_Spotify
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .UseMauiCommunityToolkitMediaElement() // Add MediaElement-specific registration
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // Enable extended error logging in debug mode
#if DEBUG
            builder.Logging.AddDebug();
            builder.Services.AddLogging(configure =>
            {
                configure.AddDebug();
                configure.SetMinimumLevel(LogLevel.Trace);
            });
            
            // Add exception handlers
            AppDomain.CurrentDomain.FirstChanceException += (sender, args) =>
            {
                Debug.WriteLine($"First chance exception: {args.Exception.Message}");
            };
#endif

            return builder.Build();
        }
    }
}
