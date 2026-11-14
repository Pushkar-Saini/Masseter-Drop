using System;

namespace MasseterDrop.Models
{
    public class AppSettings
    {
        public bool LaunchAtStartup { get; set; } = true;

        /// <summary>
        /// Set once the app has applied its first-run "launch at startup" default, so the
        /// user's later choice (on or off) is never overridden.
        /// </summary>
        public bool StartupDefaultApplied { get; set; } = false;
        public bool StartMinimized { get; set; } = false;
        public bool KeepRunningOnClose { get; set; } = true;
        public int MaxRecentFiles { get; set; } = 100;
        public string Theme { get; set; } = "Dark";
        public string? CustomDownloadsFolder { get; set; } = null;
        public bool AutoDismissOnDeactivate { get; set; } = true;

        /// <summary>
        /// Persistent random UUID generated on the first launch when no ID exists.
        /// Used strictly as an anonymous distinct ID for product analytics.
        /// Never contains personal or hardware-identifying data.
        /// </summary>
        public string? AnonymousInstallationId { get; set; } = null;

        /// <summary>
        /// User consent preference for anonymous product analytics.
        /// null = consent has not yet been requested or chosen (first launch experience).
        /// true = user explicitly allowed anonymous analytics.
        /// false = user explicitly declined or opted out.
        /// </summary>
        public bool? AnalyticsConsent { get; set; } = null;

        /// <summary>
        /// Indicates whether the one-time 'app_first_launch' analytics event has been recorded.
        /// </summary>
        public bool FirstLaunchEventTracked { get; set; } = false;

        /// <summary>
        /// Tracks the last application version run on this machine to detect version upgrades.
        /// </summary>
        public string? LastVersionRun { get; set; } = null;
    }
}
