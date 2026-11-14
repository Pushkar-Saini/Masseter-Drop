using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MasseterDrop.Models;
using PostHog;

namespace MasseterDrop.Services
{
    /// <summary>
    /// Implements lightweight, privacy-focused, anonymous product analytics using PostHog.
    /// Operates completely asynchronously and never blocks the UI thread or startup lifecycle.
    /// Never collects file names, file paths, file contents, URLs, usernames, or IPs.
    /// </summary>
    public class AnalyticsService : IAnalyticsService
    {
        private readonly SettingsService _settingsService;
        private readonly string _projectToken;
        private readonly string _hostUrl;
        private readonly object _clientLock = new();

        private PostHogClient? _client;
        private bool _isDisposed = false;

        public bool IsEnabled
        {
            get
            {
                lock (_clientLock)
                {
                    return _client != null && _settingsService.CurrentSettings.AnalyticsConsent == true;
                }
            }
        }

        public string InstallationId => _settingsService.CurrentSettings.AnonymousInstallationId ?? string.Empty;

        public string AppVersion { get; }

        public AnalyticsService(
            SettingsService settingsService,
            string? projectToken = null,
            string? hostUrl = null)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _projectToken = !string.IsNullOrWhiteSpace(projectToken) ? projectToken : AnalyticsConfig.ProjectToken;
            _hostUrl = !string.IsNullOrWhiteSpace(hostUrl) ? hostUrl : AnalyticsConfig.IngestionHost;

            AppVersion = ResolveAppVersion();

            // Initialize client only if user has previously given explicit consent
            if (_settingsService.CurrentSettings.AnalyticsConsent == true)
            {
                InitializeClient();
            }
        }

        private static string ResolveAppVersion()
        {
            try
            {
                var assembly = typeof(AnalyticsService).Assembly;
                var infoVer = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(infoVer))
                {
                    // Clean git sha metadata if present (e.g. 1.0.0+abc1234 -> 1.0.0)
                    int plusIdx = infoVer.IndexOf('+');
                    return plusIdx > 0 ? infoVer.Substring(0, plusIdx) : infoVer;
                }

                var ver = assembly.GetName().Version;
                if (ver != null)
                {
                    return $"{ver.Major}.{ver.Minor}.{ver.Build}";
                }
            }
            catch { }

            return "1.0.0";
        }

        private void InitializeClient()
        {
            lock (_clientLock)
            {
                if (_client != null) return;

                try
                {
                    var options = new PostHogOptions
                    {
                        ProjectToken = _projectToken,
                        HostUrl = new Uri(_hostUrl),
                        CaptureUnhandledExceptions = false,
                        Disabled = false,
                        FlushAt = 1,
                        FlushInterval = TimeSpan.FromSeconds(5),
                        MaxRetries = 2,
                        SuperProperties = new Dictionary<string, object>
                        {
                            ["app_version"] = AppVersion,
                            ["platform"] = "Windows",
                            ["$process_person_profile"] = false
                        }
                    };

                    _client = new PostHogClient(options);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Analytics] Failed to initialize PostHogClient: {ex.Message}");
                    _client = null;
                }
            }
        }

        private void DestroyClient()
        {
            lock (_clientLock)
            {
                if (_client != null)
                {
                    try
                    {
                        _client.Dispose();
                    }
                    catch { }
                    _client = null;
                }
            }
        }

        public void SetConsent(bool consentGiven)
        {
            _settingsService.CurrentSettings.AnalyticsConsent = consentGiven;
            _settingsService.SaveSettings(_settingsService.CurrentSettings);

            if (consentGiven)
            {
                InitializeClient();
            }
            else
            {
                DestroyClient();
            }
        }

        public void TrackFirstLaunch()
        {
            CaptureEvent("app_first_launch");
        }

        public void TrackAppStarted(string launchMode = "standard")
        {
            CaptureEvent("app_started", new Dictionary<string, object?>
            {
                ["event_source"] = launchMode
            });
        }

        public void TrackAppShutdown(string source = "user")
        {
            CaptureEvent("app_shutdown", new Dictionary<string, object?>
            {
                ["event_source"] = source
            });
        }

        public void TrackAppUpdated(string previousVersion, string currentVersion)
        {
            CaptureEvent("app_updated", new Dictionary<string, object?>
            {
                ["previous_version"] = previousVersion,
                ["app_version"] = currentVersion
            });
        }

        public void TrackShelfDisplayed(string source)
        {
            CaptureEvent("shelf_displayed", new Dictionary<string, object?>
            {
                ["event_source"] = source
            });
        }

        public void TrackShelfDismissed(string reason)
        {
            CaptureEvent("shelf_dismissed", new Dictionary<string, object?>
            {
                ["event_source"] = reason
            });
        }

        public void TrackShelfReopened(string source)
        {
            CaptureEvent("shelf_reopened", new Dictionary<string, object?>
            {
                ["event_source"] = source
            });
        }

        public void TrackFileOpened(FileCategory category)
        {
            CaptureEvent("file_opened", new Dictionary<string, object?>
            {
                ["file_category"] = ToCategoryString(category)
            });
        }

        public void TrackFileDragged(FileCategory category)
        {
            CaptureEvent("file_dragged", new Dictionary<string, object?>
            {
                ["file_category"] = ToCategoryString(category)
            });
        }

        public static string ToCategoryString(FileCategory category)
        {
            return category switch
            {
                FileCategory.Pdf => "pdf",
                FileCategory.Image => "image",
                FileCategory.Video => "video",
                FileCategory.Audio => "audio",
                FileCategory.Archive => "archive",
                FileCategory.Document => "document",
                FileCategory.Executable => "executable",
                FileCategory.Code => "other",
                _ => "other"
            };
        }

        private void CaptureEvent(string eventName, Dictionary<string, object?>? properties = null)
        {
            if (!IsEnabled || string.IsNullOrWhiteSpace(InstallationId))
            {
                return;
            }

            PostHogClient? client;
            lock (_clientLock)
            {
                client = _client;
            }

            if (client == null)
            {
                return;
            }

            try
            {
                var sanitized = new Dictionary<string, object>
                {
                    ["app_version"] = AppVersion,
                    ["platform"] = "Windows",
                    ["$process_person_profile"] = false
                };

                if (properties != null)
                {
                    foreach (var kvp in properties)
                    {
                        if (kvp.Value == null) continue;

                        string keyLower = kvp.Key.ToLowerInvariant();

                        // Strict privacy gatekeeper: reject any keys containing personal, file, or path identifiers
                        if (keyLower.Contains("path") ||
                            keyLower.Contains("filename") ||
                            keyLower.Contains("file_name") ||
                            keyLower.Contains("url") ||
                            keyLower.Contains("user") ||
                            keyLower.Contains("ip") ||
                            keyLower.Contains("host") ||
                            keyLower.Contains("machine"))
                        {
                            continue;
                        }

                        // Also reject values containing full file paths or URLs
                        if (kvp.Value is string sVal && (sVal.Contains(@":\") || sVal.Contains("://") || sVal.Contains(@"/")))
                        {
                            continue;
                        }

                        sanitized[kvp.Key] = kvp.Value;
                    }
                }

                client.Capture(
                    distinctId: InstallationId,
                    eventName: eventName,
                    properties: sanitized);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Analytics] Error capturing '{eventName}': {ex.Message}");
            }
        }

        public async Task FlushAsync(TimeSpan timeout)
        {
            PostHogClient? client;
            lock (_clientLock)
            {
                client = _client;
            }

            if (client == null) return;

            try
            {
                using var cts = new CancellationTokenSource(timeout);
                await client.FlushAsync().WaitAsync(cts.Token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Analytics] Flush timeout or error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            DestroyClient();
        }
    }
}
