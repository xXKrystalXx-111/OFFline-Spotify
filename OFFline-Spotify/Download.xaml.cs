using Microsoft.Maui.ApplicationModel;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using SeleniumExtras.WaitHelpers;
using SpotifyAPI.Web;
using SQLite;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OFFline_Spotify;

public partial class Download : ContentPage
{
    private readonly Random _random = new Random();
    
    private bool _isDownloading = false; // Add this flag
    private bool _isDisposed = false; // Add at the class level
    private bool _isNavigating = false; // Add at the class level
    private IWebDriver? _activeDriver = null;

    public Download()
    {
        InitializeComponent();
    }
    private async void CopyToClipboard(object? sender, EventArgs e)
    {
        await Clipboard.Default.SetTextAsync("https://accounts.spotify.com/authorize?\r\nclient_id=8b7005e57c154538abd4ebfe3cf00ed3&response_type=code&\r\nredirect_uri=https%3A%2F%2Foauth.pstmn.io%2Fv1%2Fcallback&\r\n&scope=playlist-read-private%20playlist-read-collaborative");
        await DisplayAlert("Copied", "Now paste this link in browser url and obtain authorisation code!(login to spotify may be needed)", "OK");
    }
    private async void OnDownloadbuttonnClicked(object sender, EventArgs e)
    {
        // Validate required fields FIRST
        if (!ValidateInputs())
        {
            return; // Stop if validation fails
        }

        // Prevent multiple simultaneous downloads
        if (_isDownloading)
        {
            await DisplayAlert("Download in Progress", "A download is already in progress. Please wait.", "OK");
            return;
        }

        _isDownloading = true; // Set flag to prevent multiple downloads
        await OnDownload();
        _isDownloading = false; // Reset flag when done
    }

    // Add this new validation method
    private bool ValidateInputs()
    {
        bool isValid = true;
        string errorMessage = "";

        // Clear previous error styling
        PlaylistsLink.BackgroundColor = Colors.White;
        SpotiAuth.BackgroundColor = Colors.White;

        // Validate Playlist Link
        if (string.IsNullOrWhiteSpace(PlaylistsLink.Text))
        {
            PlaylistsLink.BackgroundColor = Color.FromArgb("#FFE0E0"); // Light red
            errorMessage += "• Playlist link is required\n";
            isValid = false;
        }

        // Validate Spotify Auth Code
        if (string.IsNullOrWhiteSpace(SpotiAuth.Text))
        {
            SpotiAuth.BackgroundColor = Color.FromArgb("#FFE0E0"); // Light red
            errorMessage += "• Spotify authorization code is required\n";
            isValid = false;
        }

        // Show error message if validation failed
        if (!isValid)
        {
            ErrorLabel.Text = errorMessage;
            ErrorLabel.TextColor = Colors.Red;
            DisplayAlert("Required Fields", "Please fill in all required fields before downloading.", "OK");
        }
        else
        {
            // Clear error message if validation passed
            ErrorLabel.Text = "";
        }

        return isValid;
    }

    private async Task RandomDelay(int minMs = 500, int maxMs = 3000)
    {
        await Task.Delay(_random.Next(minMs, maxMs));
    }

    public async Task FuckAds(IWebDriver driver)
    {
        var iframes = driver.FindElements(By.CssSelector("iframe[style*='width: 100%'][style*='z-index: 2147483647']"));
        if (iframes.Count > 0)
        {
            driver.SwitchTo().Frame(iframes[0]);
            string style = "align-items: center !important; border-color: rgb(229, 229, 229) !important; border-radius: 0.8em !important; border-style: solid !important; border-width: 1px !important; display: flex !important; flex-direction: column !important; font-size: 1.6em !important; font-weight: bold !important; justify-content: center !important; line-height: 113% !important; padding: 0.8em 0px !important; cursor: pointer !important; white-space: nowrap !important; max-width: 114px !important; width: 35% !important; bottom: 0px !important; left: 0px !important; position: absolute !important; opacity: 1 !important;";
            var adSpans = driver.FindElements(By.CssSelector($"span[style=\"{style}\"]"));
            if (adSpans.Count > 0)
            {
                adSpans[0].Click();
                await RandomDelay(500, 1500);
            }
            driver.SwitchTo().DefaultContent();
        }
    }

    public async Task OnDownload()
    {
        if (DeviceInfo.Platform == DevicePlatform.Android)
        {
            var appiumOptions = new AppiumOptions();
            appiumOptions.PlatformName = "Android";
            appiumOptions.AddAdditionalOption("deviceName", "Android Emulator");
            appiumOptions.AddAdditionalOption("browserName", "Chrome");
            appiumOptions.AddAdditionalOption("automationName", "UiAutomator2");
            appiumOptions.AddAdditionalOption("chromedriverExecutable", "path/to/chromedriver");

            // Anti-detection capabilities
            appiumOptions.AddAdditionalOption("excludeSwitches", new[] { "enable-automation" });
            appiumOptions.AddAdditionalOption("useAutomationExtension", false);

            var mobileUserAgent = "Mozilla/5.0 (Linux; Android 13; Pixel 6) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/116.0.0.0 Mobile Safari/537.36";
            appiumOptions.AddAdditionalOption("userAgent", mobileUserAgent);

            try
            {
                using var driver = new AndroidDriver(new Uri("http://127.0.0.1:4723/wd/hub"), appiumOptions);

                // Execute anti-detection script
                var script = @"
                    Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
                    Object.defineProperty(navigator, 'languages', { get: () => ['en-US', 'en'] });
                    Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3, 4, 5] });
                ";
                ((IJavaScriptExecutor)driver).ExecuteScript(script);

                await RandomDelay();
                driver.Navigate().GoToUrl("https://soundloaders.app");

                // Simulate human-like behavior
                for (int i = 0; i < 3; i++)
                {
                    await RandomDelay(1000, 3000);
                    ((IJavaScriptExecutor)driver).ExecuteScript($"window.scrollTo(0, {_random.Next(300, 1000)})");
                }

                await RandomDelay(8000, 12000);
                driver.Quit();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to initialize driver: {ex.Message}", "OK");
            }
        }
        else if (DeviceInfo.Platform == DevicePlatform.WinUI)
        {
            var options = new ChromeOptions();
            options.BinaryLocation = @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe";
            
            // Flagi anty-detekcyjne
            options.AddArgument("--disable-blink-features=AutomationControlled");
            options.AddArgument("--disable-features=site-per-process");
            options.AddArgument("--disable-web-security");
            options.AddArgument("--start-maximized");
            options.AddArgument("--disable-infobars");
            options.AddArgument("--disable-notifications");
            options.AddArgument("--disable-popup-blocking");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--headless");
            // Usuniêcie flag automatyzacji
            options.AddExcludedArgument("enable-automation");
            options.AddAdditionalOption("useAutomationExtension", false);
            
            // User-Agent
            options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36");
            
            // Use a persistent Brave Selenium profile directory
            string seleniumProfilePath = Path.Combine(FileSystem.AppDataDirectory, "BraveSeleniumProfile");
            Directory.CreateDirectory(seleniumProfilePath); // Ensure the directory exists
            options.AddArgument($"--user-data-dir={seleniumProfilePath}");
            
            // Preferencje u¿ytkownika
            options.AddUserProfilePreference("credentials_enable_service", false);
            options.AddUserProfilePreference("profile.password_manager_enabled", false);
            options.AddUserProfilePreference("profile.default_content_setting_values.notifications", 2);
            options.AddUserProfilePreference("profile.default_content_setting_values.popups", 2);
            options.AddUserProfilePreference("profile.default_content_setting_values.automatic_downloads", 2);
            options.AddUserProfilePreference("download.prompt_for_download", false);
            options.AddUserProfilePreference("download.directory_upgrade", true);
            options.AddUserProfilePreference("download.default_directory", Path.Combine(FileSystem.AppDataDirectory, "Downloads"));
            options.AddUserProfilePreference("safebrowsing.enabled", false);
            
            // Get the managed ChromeDriver path
            string managedDriverPath = await ChromeDriverManager.GetChromeDriverPathAsync();
            string chromedriverDir = Path.GetDirectoryName(managedDriverPath);
ChromeDriverService service = ChromeDriverService.CreateDefaultService(chromedriverDir);

service.HideCommandPromptWindow = true;

IWebDriver? driver = null;
try
{
    Debug.WriteLine("Uruchamiam ChromeDriver...");
    driver = new ChromeDriver(service, options);
    Debug.WriteLine("ChromeDriver uruchomiony!");
    
    
    if (App.Database == null)
    {
        await DisplayAlert("Error", "Database not initialized", "OK");
        return;
    }
    
    string playlistUrl = PlaylistsLink.Text;
    string? playlistId = SpotifyService.ExtractPlaylistIdFromUrl(playlistUrl);
    
    if (string.IsNullOrEmpty(playlistId))
    {
        await DisplayAlert("Error", "Invalid playlist URL", "OK");
                    driver.Quit();
        return;
    }
    
    Debug.WriteLine($"Pobieram id playliœcie: {playlistId}");
    
    string clientId = "8b7005e57c154538abd4ebfe3cf00ed3";
    string clientSecret = "ecc499129c1c49169853acadedfa2a3c";
    var redirectUri = new Uri("https://oauth.pstmn.io/v1/callback");

    var oAuthClient = new OAuthClient();
    var tokenResponse = await oAuthClient.RequestToken(
        new AuthorizationCodeTokenRequest(clientId, clientSecret, SpotiAuth.Text, redirectUri)
    );

    var spotifyService = new SpotifyService(App.Database);
    spotifyService.SetAccessToken(tokenResponse.AccessToken);

    var playlistInfo = await spotifyService.GetPlaylistInfoAsync(playlistId);
    
    if (playlistInfo == null)
    {
        await DisplayAlert("Error", "Failed to get playlist information", "OK");
        return;
    }
    
    string sanitizedPlaylistName = string.Join("_", playlistInfo.Name.Split(Path.GetInvalidFileNameChars()));
    string playlistFolderPath = Path.Combine(FileSystem.AppDataDirectory, sanitizedPlaylistName);
    Directory.CreateDirectory(playlistFolderPath);
    
    // Download playlist image
    string? localImagePath = null;
    if (!string.IsNullOrEmpty(playlistInfo.ImageUrl))
    {
        localImagePath = await spotifyService.DownloadPlaylistImageAsync(playlistInfo.ImageUrl, playlistId, playlistFolderPath);
        Debug.WriteLine($"Playlist image downloaded to: {localImagePath}");
    }
    
    // Set download directory at runtime using DevTools Protocol
    var cdpSession = ((ChromeDriver)driver).GetDevToolsSession();
    ((ChromeDriver)driver).ExecuteCdpCommand("Page.setDownloadBehavior", new Dictionary<string, object>
    {
        { "behavior", "allow" },
        { "downloadPath", playlistFolderPath }
    });
    
    // Wykonaj skrypt anty-detekcji
    Debug.WriteLine("Wykonujê skrypt anty-detekcyjny...");
    ((IJavaScriptExecutor)driver).ExecuteScript(@"
        Object.defineProperty(navigator, 'webdriver', { get: () => false });
        Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3, 4, 5] });
        window.chrome = { runtime: {}, loadTimes: function() {}, csi: function() {}, app: {} };
    ");
    
    // Wykonaj pocz¹tkowe operacje z w³¹czon¹ tarcz¹
    Debug.WriteLine("Nawigacja do spotdown.app");
    ErrorLabel.Text = "Starting download procedure";
    driver.Navigate().GoToUrl("https://spotdown.app/");
    await RandomDelay(3000, 5000);

    // Po kilku sekundach wy³¹cz tarczê
    await SetBraveShield((ChromeDriver)driver, false);
    Debug.WriteLine("Tarcza Brave wy³¹czona");

    // Kontynuuj normalne operacje
    // U¿yj WebDriverWait z poprawn¹ obs³ug¹ b³êdów
    WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
    
    
    // ZnajdŸ pole wyszukiwania
    try {
        ErrorLabel.Text = "Interacting with search bar...";
                    Debug.WriteLine("Szukam pola wyszukiwania");
        IWebElement inputElement = wait.Until(driver => {
            try {
                var element = driver.FindElement(By.Id("search-form-input"));
                return element.Displayed ? element : null;
            } catch {
                return null;
            }
        });
        
        if (inputElement != null && inputElement.Displayed) {
            // Wpisz URL playlisty z ludzkimi opóŸnieniami
            ErrorLabel.Text = "Entering playlist URL...";
                        Debug.WriteLine("Wpisujê URL playlisty");
            inputElement.Clear();
            
            foreach (char c in playlistUrl) {
                inputElement.SendKeys(c.ToString());
                await Task.Delay(_random.Next(10, 100));  // Ludzkie opóŸnienia
            }
            var errors = driver.FindElement(By.Id("error-message"));

            if(errors.Text != "")
            {
                if (ErrorLabel != null)
                    ErrorLabel.Text = errors.Text;
                driver.Quit();
                return;
            }
            
            await RandomDelay(1000, 2000);
            
            // ZnajdŸ i kliknij przycisk wyszukiwania
            Debug.WriteLine("Szukam przycisku wyszukiwania");
            IWebElement submitButton = wait.Until(driver => {
                try {
                    var element = driver.FindElement(By.Id("search-form__button"));
                    return element.Displayed && element.Enabled ? element : null;
                } catch {
                    return null;
                }
            });
            ErrorLabel.Text = "Submitting search...";
                        Debug.WriteLine("Clicking search button");
            submitButton.Click();
            
            // Wait for results
            await RandomDelay(5000, 8000);
            
            // Try to find download button
            try {
                Debug.WriteLine("Looking for download button");
                IWebElement downloadButton = wait.Until(driver => {
                    try {
                        var element = driver.FindElement(By.Id("download-all-button"));
                        return element.Displayed && element.Enabled ? element : null;
                    } catch {
                        return null;
                    }
                });
                
                if(downloadButton != null) {
                    Debug.WriteLine("Clicking download button");
                    downloadButton.Click();
                    
                    // Wait for download to start
                    await RandomDelay(10000, 15000);
                    Debug.WriteLine("Download started");
                    ErrorLabel.Text = "Downloading songs started";
                            }
                
                // *** FIX FOR THE ENDLESS LOOP ***
                bool downloadCompleted = false; // Declare the variable once at the top
                 // Track the start time of the download
                 await RandomDelay(5000, 5000);
                while (!downloadCompleted)
                {
                    try
                    {
                        // Check if the download has exceeded the 10-minute timeout
                        

                        // Re-check for completion message each iteration
                        var allDoneElements = driver.FindElements(By.Id("allDownloadedMessage"));

                        if (allDoneElements.Count > 0)
                        {
                            downloadCompleted = true;
                            Debug.WriteLine("Download completed - found allDownloadedMessage element!");
                            break; // Exit the loop
                        }

                        // Update progress if available
                        try
                        {
                            var progressDiv = driver.FindElement(By.Id("downloadProgress"));
                            string currentProgress = progressDiv.Text;
                            Debug.WriteLine($"Download progress: {currentProgress}");

                            // Update UI on main thread
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                if (ErrorLabel != null)
                                {
                                    if(currentProgress.Contains("/"))
                                    {
                                        ErrorLabel.Text = currentProgress;
                                    }
                                    else
                                    {
                                        downloadCompleted = true;
                                    }
                                }
                            });
                            if(currentProgress == null)
                            {
                               Debug.WriteLine("Brak postêpu pobierania, przerywam pêtlê.");
                                break;
                            }
                        }
                        catch (Exception progressEx)
                        {
                            Debug.WriteLine($"Could not read progress: {progressEx.Message}");
                        }

                        // Wait 1 second before checking again
                        await Task.Delay(1000);
                    }
                    catch (Exception loopEx)
                    {
                        Debug.WriteLine($"Error in download monitoring loop: {loopEx.Message}");
                        await Task.Delay(1000);
                    }
                }

                if (!downloadCompleted)
                {
                    Debug.WriteLine("Download timed out or failed");
                    await DisplayAlert("Warning", "Download may have timed out. Check the download folder.", "OK");
                }
                else
                {
                    Debug.WriteLine("Download completed! Saving to database...");
                }
                
                // *** DATABASE INTEGRATION: Save playlist and songs to database after download ***
try
{
    // Show loading indicator
    await MainThread.InvokeOnMainThreadAsync(() => {
        if (ErrorLabel != null)
            ErrorLabel.Text = "Saving playlist to database...";
    });
    
    Debug.WriteLine("Starting database save operation");
    
    // Save complete playlist with downloaded files to database
    var dbPlaylistId = await App.Database!.SavePlaylistWithSongsAsync(playlistInfo, localImagePath, playlistFolderPath);
    Debug.WriteLine($"Playlist saved to database with ID: {dbPlaylistId}");
    
    // CRITICAL: Store driver reference
    _activeDriver = driver;
    
    // Dispose driver completely
    if (_activeDriver != null)
    {
        try 
        {
            Debug.WriteLine("Disposing ChromeDriver");
            _activeDriver.Quit();
            _activeDriver.Dispose();
            _activeDriver = null;
            driver = null;
        }
        catch (Exception driverEx)
        {
            Debug.WriteLine($"Error disposing driver: {driverEx.Message}");
        }
    }
    
    // Force cleanup
    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
    GC.WaitForPendingFinalizers();
    GC.Collect();
    
    await Task.Delay(1000);
    
    Debug.WriteLine("Preparing to show dialog");
    
    // WINDOWS FIX: Use Dispatcher to ensure proper threading
    await Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(async () => {
        try
        {
            Debug.WriteLine("Showing success alert");
            await DisplayAlert("Success", "Playlist downloaded and saved successfully!", "OK");
            Debug.WriteLine("Alert dismissed");
        }
        catch (Exception alertEx)
        {
            Debug.WriteLine($"Alert error: {alertEx.Message}");
        }
    });
    
    // CRITICAL FIX: Instead of navigating, close this page and let MainPage refresh
    await Task.Delay(1000);
    
    Debug.WriteLine("Closing download page");
    
    // Method 1: Just pop the current page (safest for Windows)
    await Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(async () => {
        try
        {
            // Simple pop without animation
            if (Navigation != null && Navigation.NavigationStack.Count > 1)
            {
                await Navigation.PopAsync(false);
                Debug.WriteLine("Page popped successfully");
            }
        }
        catch (Exception navEx)
        {
            Debug.WriteLine($"Navigation error: {navEx.Message}");
            Debug.WriteLine($"Stack trace: {navEx.StackTrace}");
            
            // If navigation fails, try to at least clear the page
            try
            {
                // Clear all page resources
                Content = new Label { Text = "Returning to main page..." };
                Debug.WriteLine("Content cleared");
            }
            catch (Exception clearEx)
            {
                Debug.WriteLine($"Clear error: {clearEx.Message}");
            }
        }
    });
}
catch (Exception dbEx)
{
    Debug.WriteLine($"Error saving to database: {dbEx.Message}");
    Debug.WriteLine($"Stack trace: {dbEx.StackTrace}");
    
    await Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(async () => {
        try
        {
            if (ErrorLabel != null)
                ErrorLabel.Text = "Database error occurred";
            await DisplayAlert("Warning", $"Error: {dbEx.Message}", "OK");
        }
        catch (Exception uiEx)
        {
            Debug.WriteLine($"UI error: {uiEx.Message}");
        }
    });
}
finally
{
    _isNavigating = false;
    _isDownloading = false;
    Debug.WriteLine("Finally block completed");
}
                        } catch (Exception downloadEx) {
                            Debug.WriteLine($"Error during download: {downloadEx.Message}");
                            await DisplayAlert("Error", $"Download failed: {downloadEx.Message}", "OK");
                        }
                    }
                } catch (Exception interactEx) {
                    Debug.WriteLine($"B³¹d interakcji: {interactEx.Message}");
                    await DisplayAlert("Error", $"Interaction failed: {interactEx.Message}", "OK");
                }
                
                // Zamknij przegl¹darkê
                await RandomDelay(5000, 10000);
                Debug.WriteLine("Zamykam przegl¹darkê");
                driver?.Quit();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"B³¹d g³ówny: {ex.Message}");
                Debug.WriteLine($"Stack: {ex.StackTrace}");

                try {
                    driver?.Quit();
                } catch {}
                
                // Wyœwietl b³¹d na UI jeœli ErrorLabel istnieje w twoim XAML
                if (ErrorLabel != null) {
                    ErrorLabel.Text = $"Wyst¹pi³ b³¹d: {ex.Message}";
                }
                
                await DisplayAlert("Error", $"Download failed: {ex.Message}", "OK");
            }
            finally
            {
                _isDownloading = false;
            }
        }
    }
    
    private async Task SetBraveShield(ChromeDriver driver, bool enable)
    {
        try {
            Debug.WriteLine($"Zmiana ustawieñ tarczy Brave przez CDP...");
            
            // Przygotuj skrypt zmieniaj¹cy ustawienia tarczy
            string script = @"
                // Bezpoœredni dostêp do ustawieñ tarczy przez localStorage
                localStorage.setItem('brave-site-specific-shields', JSON.stringify({
                    'https://spotdown.app': {
                        'braveShields': " + (enable ? "'enabled'" : "'disabled'") + @",
                        'ads': 'block',
                        'trackers': 'block',
                        'httpUpgradable': 'block',
                        'javascript': 'allow',
                        'fingerprinting': 'block'
                    }
                }));
            ";
            
            ((IJavaScriptExecutor)driver).ExecuteScript(script);
            
            // Opcjonalnie: prze³aduj stronê aby zmiany zosta³y zastosowane
            if (driver.Url.Contains("spotdown.app")) {
                driver.Navigate().Refresh();
                await RandomDelay(2000, 3000);
            }
            
            Debug.WriteLine($"Ustawienia tarczy zosta³y zmienione na {(enable ? "w³¹czone" : "wy³¹czone")}");
        }
        catch (Exception ex) {
            Debug.WriteLine($"B³¹d podczas zmiany ustawieñ tarczy: {ex.Message}");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        
        Debug.WriteLine($"OnDisappearing called - _isNavigating: {_isNavigating}, _isDisposed: {_isDisposed}");
        
        // CRITICAL: Don't cleanup during navigation
        if (_isNavigating)
        {
            Debug.WriteLine("Skipping cleanup - navigation in progress");
            return;
        }
        
        if (_isDisposed)
        {
            Debug.WriteLine("Already disposed");
            return;
        }
        
        _isDisposed = true;
        
        // Additional driver cleanup if somehow still active
        if (_activeDriver != null)
        {
            try
            {
                Debug.WriteLine("Emergency driver cleanup in OnDisappearing");
                _activeDriver.Quit();
                _activeDriver.Dispose();
                _activeDriver = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Emergency cleanup error: {ex.Message}");
            }
        }
        
        Debug.WriteLine("Download page cleanup completed");
    }

    // Also add this to handle when app is being closed
    protected override bool OnBackButtonPressed()
    {
        if (_isDownloading)
        {
            MainThread.BeginInvokeOnMainThread(async () => {
                await DisplayAlert("Download in Progress", "Please wait for the download to complete.", "OK");
            });
            return true; // Prevent back navigation
        }
        
        return base.OnBackButtonPressed();
    }

    // Add this method to handle downloading a single song with a timeout
    private async Task<bool> DownloadSongWithTimeout(Func<Task> downloadTask, int timeoutMs)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            // Start the download task with the cancellation token
            await Task.Run(async () => await downloadTask(), cts.Token);
            return true; // Download succeeded
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("Download timed out.");
            return false; // Download timed out
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error downloading song: {ex.Message}");
            return false; // Download failed
        }
    }

    private async void OnUpdateChromeDriverClicked(object sender, EventArgs e)
    {
        try
        {
            ErrorLabel.Text = "Updating ChromeDriver...";
            ErrorLabel.TextColor = Colors.Orange;
            await ChromeDriverManager.ForceUpdateAsync();
            ErrorLabel.Text = "ChromeDriver updated successfully!";
            ErrorLabel.TextColor = Colors.LightGreen;
        }
        catch (Exception ex)
        {
            ErrorLabel.Text = $"Failed to update ChromeDriver: {ex.Message}";
            ErrorLabel.TextColor = Colors.Red;
            Debug.WriteLine($"ChromeDriver update error: {ex}");
            await DisplayAlert("Error", $"Failed to update ChromeDriver: {ex.Message}", "OK");
        }
    }
}
