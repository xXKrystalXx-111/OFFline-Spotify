using Android.App;
using Android.Content;
using Android.OS;
using Android.Media;
using AndroidX.Core.App;

namespace OFFline_Spotify
{
    [Service(ForegroundServiceType = Android.Content.PM.ForegroundService.TypeMediaPlayback)]
    public class AudioPlayerService : Service
    {
        private const int NOTIFICATION_ID = 1001;
        private const string CHANNEL_ID = "audio_playback_channel";

        public override IBinder? OnBind(Intent? intent) => null;

        public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        {
            CreateNotificationChannel();
            var notification = BuildNotification();
            StartForeground(NOTIFICATION_ID, notification);
            return StartCommandResult.Sticky;
        }

        private void CreateNotificationChannel()
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var channel = new NotificationChannel(
                    CHANNEL_ID,
                    "Music Playback",
                    NotificationImportance.Low)
                {
                    Description = "Shows while music is playing in the background"
                };
                var manager = (NotificationManager?)GetSystemService(NotificationService);
                manager?.CreateNotificationChannel(channel);
            }
        }

        private Notification BuildNotification()
        {
            var intent = new Intent(this, typeof(MainActivity));
            intent.SetFlags(ActivityFlags.SingleTop);
            var pendingIntent = PendingIntent.GetActivity(
                this, 0, intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            return new NotificationCompat.Builder(this, CHANNEL_ID)
                .SetContentTitle("OFFline Spotify")
                .SetContentText("Playing music...")
                .SetSmallIcon(Android.Resource.Drawable.IcMediaPlay)
                .SetContentIntent(pendingIntent)
                .SetOngoing(true)
                .SetPriority(NotificationCompat.PriorityLow)
                .Build();
        }

        public override void OnDestroy()
        {
            StopForeground(StopForegroundFlags.Remove);
            base.OnDestroy();
        }
    }

    public class AudioPlayerServiceImpl : IAudioPlayerService
    {
        public void Start()
        {
            var context = Android.App.Application.Context;
            var intent = new Intent(context, typeof(AudioPlayerService));
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
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