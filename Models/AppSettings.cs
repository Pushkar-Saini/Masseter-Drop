using System;

namespace MasseterDrop.Models
{
    public class AppSettings
    {
        public bool LaunchAtStartup { get; set; } = false;
        public bool StartMinimized { get; set; } = false;
        public bool KeepRunningOnClose { get; set; } = true;
        public int MaxRecentFiles { get; set; } = 100;
        public string Theme { get; set; } = "Dark";
        public string? CustomDownloadsFolder { get; set; } = null;
        public bool AutoDismissOnDeactivate { get; set; } = true;
    }
}
