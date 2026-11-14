using System;
using System.Threading.Tasks;
using MasseterDrop.Models;

namespace MasseterDrop.Services
{
    /// <summary>
    /// Service contract for anonymous, privacy-preserving product analytics.
    /// Shields UI and application components from direct PostHog SDK dependencies.
    /// </summary>
    public interface IAnalyticsService : IDisposable
    {
        /// <summary>
        /// Gets whether analytics capturing is actively enabled by the user.
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>
        /// Gets the persistent, anonymous UUID installation identifier.
        /// </summary>
        string InstallationId { get; }

        /// <summary>
        /// Gets the current application semantic version.
        /// </summary>
        string AppVersion { get; }

        /// <summary>
        /// Sets user consent for analytics.
        /// Enables or immediately halts event capture and persists preference.
        /// </summary>
        /// <param name="consentGiven">True to allow anonymous analytics; false to decline.</param>
        void SetConsent(bool consentGiven);

        /// <summary>
        /// Tracks the first launch event after user consent. Recorded at most once per installation.
        /// </summary>
        void TrackFirstLaunch();

        /// <summary>
        /// Tracks application startup to measure daily/weekly/monthly active installations.
        /// </summary>
        /// <param name="launchMode">Launch mode descriptor ("standard", "minimized", "startup").</param>
        void TrackAppStarted(string launchMode = "standard");

        /// <summary>
        /// Tracks application shutdown.
        /// </summary>
        /// <param name="source">Shutdown trigger ("user", "system", "exit").</param>
        void TrackAppShutdown(string source = "user");

        /// <summary>
        /// Tracks when an installed application is replaced by a newer version.
        /// </summary>
        /// <param name="previousVersion">Previous application version.</param>
        /// <param name="currentVersion">New application version.</param>
        void TrackAppUpdated(string previousVersion, string currentVersion);

        /// <summary>
        /// Tracks when the floating download shelf actually becomes visible.
        /// </summary>
        /// <param name="source">Display trigger ("automatic", "manual").</param>
        void TrackShelfDisplayed(string source);

        /// <summary>
        /// Tracks when the shelf is dismissed or hidden.
        /// </summary>
        /// <param name="reason">Dismissal reason ("auto_dismiss", "escape_key", "close_button", "deactivated", "tray_toggle").</param>
        void TrackShelfDismissed(string reason);

        /// <summary>
        /// Tracks when the user manually reopens the shelf from dismissed state.
        /// </summary>
        /// <param name="source">Reopen trigger ("tray", "shortcut", "manual").</param>
        void TrackShelfReopened(string source);

        /// <summary>
        /// Tracks when a file is successfully opened through Masseter Drop.
        /// Only safe, non-identifying file category is sent. Never file name or path.
        /// </summary>
        /// <param name="category">Categorized file type.</param>
        void TrackFileOpened(FileCategory category);

        /// <summary>
        /// Tracks when a file drag operation is initiated from the shelf.
        /// Only safe, non-identifying file category is sent. Never file name or path.
        /// </summary>
        /// <param name="category">Categorized file type.</param>
        void TrackFileDragged(FileCategory category);

        /// <summary>
        /// Flushes any pending queued events with a strict timeout to ensure shutdown never hangs.
        /// </summary>
        /// <param name="timeout">Maximum time to wait for network flush.</param>
        Task FlushAsync(TimeSpan timeout);
    }
}
