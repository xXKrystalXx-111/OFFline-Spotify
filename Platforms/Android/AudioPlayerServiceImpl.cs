using Android.Content;

namespace OFFline_Spotify
{
    public class AudioPlayerServiceImpl : IAudioPlayerService
    {
        public void Start()
        {
            var context = Android.App.Application.Context;
            var intent = new Intent(context, typeof(AudioPlayerService));
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
                context.StartForegroundService(intent);
            else
                context.StartService(intent);
        }

        public void Stop()
        {
            var context = Android.App.Application.Context;
            var intent = new Intent(context, typeof(AudioPlayerService));
            context.StopService(intent);
        }
    }
}