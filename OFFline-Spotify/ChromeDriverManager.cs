using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace OFFline_Spotify;

public class ChromeDriverManager
{
    private static readonly HttpClient _httpClient = new HttpClient();
    private const string CHROME_FOR_TESTING_API = "https://googlechromelabs.github.io/chrome-for-testing/known-good-versions-with-downloads.json";
    
    /// <summary>
    /// Gets the ChromeDriver path, downloading/updating if necessary
    /// </summary>
    public static async Task<string> GetChromeDriverPathAsync()
    {
        try
        {
            // Try to find Brave's bundled ChromeDriver first
            string? braveDriver = FindBraveChromeDriver();
            if (braveDriver != null)
            {
                Debug.WriteLine($"Using Brave's ChromeDriver: {braveDriver}");
                return braveDriver;
            }

            // Fallback to managed ChromeDriver
            string managedDriverPath = Path.Combine(FileSystem.AppDataDirectory, "chromedriver", "chromedriver.exe");
            
            // Check if we need to update
            bool needsUpdate = await ShouldUpdateChromeDriver(managedDriverPath);
            
            if (needsUpdate)
            {
                Debug.WriteLine("ChromeDriver needs update, downloading...");
                await DownloadChromeDriverAsync(managedDriverPath);
            }
            else
            {
                Debug.WriteLine($"Using existing ChromeDriver: {managedDriverPath}");
            }

            return managedDriverPath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error managing ChromeDriver: {ex.Message}");
            // Fallback to default location
            return @"C:\chromedriver.exe";
        }
    }

    /// <summary>
    /// Finds Brave's bundled ChromeDriver
    /// </summary>
    private static string? FindBraveChromeDriver()
    {
        try
        {
            string bravePath = @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe";
            if (!File.Exists(bravePath))
                return null;

            string braveDir = Path.GetDirectoryName(bravePath)!;
            
            // Check main directory
            string mainDriver = Path.Combine(braveDir, "chromedriver.exe");
            if (File.Exists(mainDriver))
                return mainDriver;

            // Check version subdirectories
            var versionDirs = Directory.GetDirectories(braveDir)
                .Where(d => char.IsDigit(Path.GetFileName(d)[0]))
                .OrderByDescending(d => d);

            foreach (var versionDir in versionDirs)
            {
                string versionDriver = Path.Combine(versionDir, "chromedriver.exe");
                if (File.Exists(versionDriver))
                    return versionDriver;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error finding Brave ChromeDriver: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Gets the installed Brave browser version
    /// </summary>
    private static string? GetBraveVersion()
    {
        try
        {
            string bravePath = @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe";
            if (File.Exists(bravePath))
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(bravePath);
                return versionInfo.ProductVersion?.Split('.')[0]; // Major version
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting Brave version: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Checks if ChromeDriver needs updating
    /// </summary>
    private static async Task<bool> ShouldUpdateChromeDriver(string driverPath)
    {
        try
        {
            // If driver doesn't exist, needs download
            if (!File.Exists(driverPath))
            {
                Debug.WriteLine("ChromeDriver not found locally");
                return true;
            }

            // Check if driver is older than 7 days
            var lastModified = File.GetLastWriteTime(driverPath);
            if ((DateTime.Now - lastModified).TotalDays > 7)
            {
                Debug.WriteLine($"ChromeDriver is {(DateTime.Now - lastModified).TotalDays:F0} days old");
                return true;
            }

            // Check version compatibility
            var localVersion = GetLocalChromeDriverVersion(driverPath);
            var braveVersion = GetBraveVersion();
            
            if (braveVersion != null && localVersion != null)
            {
                if (!localVersion.StartsWith(braveVersion))
                {
                    Debug.WriteLine($"Version mismatch: ChromeDriver {localVersion}, Brave {braveVersion}");
                    return true;
                }
            }

            Debug.WriteLine("ChromeDriver is up to date");
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error checking update: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets the version of local ChromeDriver
    /// </summary>
    private static string? GetLocalChromeDriverVersion(string driverPath)
    {
        try
        {
            if (!File.Exists(driverPath))
                return null;

            var versionInfo = FileVersionInfo.GetVersionInfo(driverPath);
            return versionInfo.FileVersion;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting local driver version: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Downloads the appropriate ChromeDriver version
    /// </summary>
    private static async Task DownloadChromeDriverAsync(string targetPath)
    {
        try
        {
            string? braveVersion = GetBraveVersion();
            if (braveVersion == null)
            {
                Debug.WriteLine("Cannot determine Brave version, using latest stable");
                braveVersion = "131"; // Fallback to recent stable
            }

            Debug.WriteLine($"Downloading ChromeDriver for Brave version {braveVersion}");

            // Get available versions
            var versionsJson = await _httpClient.GetStringAsync(CHROME_FOR_TESTING_API);
            var versionsData = JsonDocument.Parse(versionsJson);
            
            // Find matching version
            string? downloadUrl = null;
            foreach (var version in versionsData.RootElement.GetProperty("versions").EnumerateArray())
            {
                var versionNumber = version.GetProperty("version").GetString();
                if (versionNumber?.StartsWith(braveVersion) == true)
                {
                    var downloads = version.GetProperty("downloads");
                    if (downloads.TryGetProperty("chromedriver", out var chromedriverArray))
                    {
                        foreach (var platform in chromedriverArray.EnumerateArray())
                        {
                            if (platform.GetProperty("platform").GetString() == "win64")
                            {
                                downloadUrl = platform.GetProperty("url").GetString();
                                Debug.WriteLine($"Found ChromeDriver {versionNumber}: {downloadUrl}");
                                break;
                            }
                        }
                    }
                    if (downloadUrl != null)
                        break;
                }
            }

            if (downloadUrl == null)
            {
                Debug.WriteLine("No matching ChromeDriver found, trying latest");
                // Get latest version as fallback
                var latestVersion = versionsData.RootElement.GetProperty("versions").EnumerateArray().Last();
                var downloads = latestVersion.GetProperty("downloads");
                if (downloads.TryGetProperty("chromedriver", out var chromedriverArray))
                {
                    foreach (var platform in chromedriverArray.EnumerateArray())
                    {
                        if (platform.GetProperty("platform").GetString() == "win64")
                        {
                            downloadUrl = platform.GetProperty("url").GetString();
                            break;
                        }
                    }
                }
            }

            if (downloadUrl == null)
            {
                throw new Exception("Could not find ChromeDriver download URL");
            }

            // Download and extract
            Debug.WriteLine($"Downloading from: {downloadUrl}");
            var zipBytes = await _httpClient.GetByteArrayAsync(downloadUrl);
            
            string tempZip = Path.Combine(Path.GetTempPath(), "chromedriver.zip");
            await File.WriteAllBytesAsync(tempZip, zipBytes);

            string extractDir = Path.GetDirectoryName(targetPath)!;
            Directory.CreateDirectory(extractDir);

            // Clear existing files
            if (Directory.Exists(extractDir))
            {
                foreach (var file in Directory.GetFiles(extractDir))
                {
                    try { File.Delete(file); } catch { }
                }
            }

            // Extract zip
            ZipFile.ExtractToDirectory(tempZip, extractDir, overwriteFiles: true);
            
            // Find chromedriver.exe in extracted files
            var extractedDriver = Directory.GetFiles(extractDir, "chromedriver.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (extractedDriver != null && extractedDriver != targetPath)
            {
                File.Move(extractedDriver, targetPath, overwrite: true);
            }

            // *** NEW: Unblock the downloaded file (Windows security) ***
            UnblockFile(targetPath);
            
            // *** NEW: Verify the file is executable ***
            if (!File.Exists(targetPath))
            {
                throw new Exception("ChromeDriver extraction failed - file not found");
            }
            
            // *** NEW: Test if ChromeDriver can run ***
            if (!TestChromeDriverExecutable(targetPath))
            {
                throw new Exception("ChromeDriver is not executable or blocked by Windows");
            }

            // Cleanup
            File.Delete(tempZip);
            
            Debug.WriteLine($"ChromeDriver downloaded and verified successfully: {targetPath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error downloading ChromeDriver: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Unblocks a file downloaded from the internet (removes Windows security warning)
    /// </summary>
    private static void UnblockFile(string filePath)
    {
        try
        {
            // Remove the "Zone.Identifier" alternate data stream that Windows adds to downloaded files
            string zoneIdentifier = filePath + ":Zone.Identifier";
            if (File.Exists(zoneIdentifier))
            {
                File.Delete(zoneIdentifier);
                Debug.WriteLine($"Unblocked file: {filePath}");
            }
            
            // Alternative method using PowerShell
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-Command \"Unblock-File -Path '{filePath}'\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
            Debug.WriteLine($"PowerShell unblock completed for: {filePath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Warning: Could not unblock file: {ex.Message}");
        }
    }

    /// <summary>
    /// Tests if ChromeDriver executable can run
    /// </summary>
    private static bool TestChromeDriverExecutable(string driverPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = driverPath,
                Arguments = "--version",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            using var process = Process.Start(psi);
            if (process == null)
            {
                Debug.WriteLine("Failed to start ChromeDriver test process");
                return false;
            }
            
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);
            
            Debug.WriteLine($"ChromeDriver test output: {output}");
            if (!string.IsNullOrEmpty(error))
            {
                Debug.WriteLine($"ChromeDriver test error: {error}");
            }
            
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ChromeDriver executable test failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Forces a ChromeDriver update check and download 
    /// </summary>
    public static async Task ForceUpdateAsync()
    {
        string managedDriverPath = Path.Combine(FileSystem.AppDataDirectory, "chromedriver", "chromedriver.exe");
        await DownloadChromeDriverAsync(managedDriverPath);
    }
}