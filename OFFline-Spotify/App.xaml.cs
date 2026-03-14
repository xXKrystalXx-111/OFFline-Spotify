using System.Diagnostics;
namespace OFFline_Spotify
{
    public partial class App : Application
    {
        // Shared database instance
        public static Database? Database { get; private set; }
        
        // Flag to track if database initialization was attempted
        private static bool _databaseInitializationAttempted = false;

        public App()
        {
            try
            {
                if (App.Database == null)
                {
                    System.Diagnostics.Debug.WriteLine("Warning: Database is not initialized in App constructor.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in App constructor: {ex.Message}");
            }
            try
            {
                InitializeComponent();
                InitializeDatabase();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing App: {ex.Message}");
            }
        }

        protected override void OnStart()
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                System.Diagnostics.Debug.WriteLine($"Unhandled exception: {args.ExceptionObject}");
            };

            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                System.Diagnostics.Debug.WriteLine($"Unobserved task exception: {args.Exception}");
                args.SetObserved();
            };
        }

        protected override void OnSleep()
        {
            // App is going to background
            Debug.WriteLine("App going to sleep - music should continue");
            // Don't stop the MediaElement here
        }

        protected override void OnResume()
        {
            // App is coming back to foreground
            Debug.WriteLine("App resumed from sleep");
        }

        private void InitializeDatabase()
        {
            if (_databaseInitializationAttempted)
                return;
                
            _databaseInitializationAttempted = true;
            
            try
            {
                string dbPath = Path.Combine(FileSystem.AppDataDirectory, "playlists.db");
                Debug.WriteLine($"Initializing database at: {dbPath}");
                
                // Create database instance without initialization
                Database = new Database(dbPath);
                
                // Start background initialization
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Database.InitializeAsync();
                        Debug.WriteLine("Database initialized successfully");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Database initialization failed: {ex.Message}");
                        
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            TryRecreateDatabase();
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create database: {ex.Message}");
                TryRecreateDatabase();
            }
        }

        private void TryRecreateDatabase()
        {
            try
            {
                string dbPath = Path.Combine(FileSystem.AppDataDirectory, "playlists_new.db");
                
                // First, delete any existing file
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                    Debug.WriteLine($"Deleted existing backup database at: {dbPath}");
                }
                
                Debug.WriteLine($"Creating backup database at: {dbPath}");
                Database = new Database(dbPath);
                
                // Background initialize
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Database.InitializeAsync();
                        Debug.WriteLine("Backup database initialized successfully");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Backup database initialization failed: {ex.Message}");
                        MainThread.BeginInvokeOnMainThread(() => 
                        {
                            Database = null;
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create backup database: {ex.Message}");
                Database = null;
            }
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            // This is the correct way to set the main page in .NET MAUI
            return new Window(new AppShell());
        }
    }
}