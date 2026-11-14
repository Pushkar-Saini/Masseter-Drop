using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace MasseterDrop.Services
{
    /// <summary>
    /// Registers Masseter Drop to launch silently (in the system tray) when the user signs in to Windows.
    ///
    /// Uses the per-user Run key (HKCU\Software\Microsoft\Windows\CurrentVersion\Run), so no admin
    /// rights are needed. Also respects/updates the "StartupApproved" key that Task Manager's
    /// Startup tab and Windows Settings > Apps > Startup use to enable or disable an entry.
    /// </summary>
    public static class StartupManager
    {
        public const string StartupArgument = "--minimized";

        public const string DefaultRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string DefaultApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        public const string ValueName = "MasseterDrop";

        // Allows test suites to redirect registry writes to safe isolated test keys
        internal static string RunKeyPath { get; set; } = DefaultRunKeyPath;
        internal static string ApprovedKeyPath { get; set; } = DefaultApprovedKeyPath;

        // Allows test suites to test behavior with arbitrary virtual exe paths
        internal static string? OverrideExecutablePath { get; set; }

        /// <summary>
        /// Full path of the running MasseterDrop.exe, or null when we are not running as the real app
        /// (e.g. inside the test runner or via "dotnet run" host). In that case we never touch
        /// the registry, so running tests or development hosts won't pollute or break startup.
        /// </summary>
        public static string? ExecutablePath
        {
            get
            {
                if (!string.IsNullOrEmpty(OverrideExecutablePath))
                {
                    return OverrideExecutablePath;
                }

                try
                {
                    string? path = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(path)) return null;

                    string fileName = Path.GetFileNameWithoutExtension(path);
                    // Match "MasseterDrop" or "Masseter Drop", but exclude test runners or dotnet CLI host
                    bool isMasseter = string.Equals(fileName, "MasseterDrop", StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(fileName, "Masseter Drop", StringComparison.OrdinalIgnoreCase);

                    if (!isMasseter) return null;
                    return Path.GetFullPath(path);
                }
                catch
                {
                    return null;
                }
            }
        }

        public static bool CanManage => ExecutablePath != null;

        public static string BuildCommand(string exePath) => $"\"{exePath}\" {StartupArgument}";

        /// <summary>
        /// Extracts the executable path component from a command line string.
        /// </summary>
        public static string? ExtractExePath(string? command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            command = command.Trim();
            if (command.StartsWith("\""))
            {
                int nextQuote = command.IndexOf('"', 1);
                if (nextQuote > 1)
                {
                    return command.Substring(1, nextQuote - 1);
                }
            }
            int spaceIdx = command.IndexOf(' ');
            return spaceIdx > 0 ? command.Substring(0, spaceIdx) : command;
        }

        /// <summary>
        /// True when the Run entry exists, points at this executable, and isn't disabled in Task Manager.
        /// </summary>
        public static bool IsEnabled()
        {
            string? exe = ExecutablePath;
            if (exe == null) return false;

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
                if (key?.GetValue(ValueName) is not string command) return false;

                bool matches = command.IndexOf(exe, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!matches)
                {
                    string? extracted = ExtractExePath(command);
                    if (!string.IsNullOrEmpty(extracted))
                    {
                        try
                        {
                            matches = string.Equals(Path.GetFullPath(extracted), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase);
                        }
                        catch { }
                    }
                }

                if (!matches) return false;
                return !IsDisabledInTaskManager();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"StartupManager.IsEnabled failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// True when the user switched the entry off in Task Manager / Settings > Startup.
        /// </summary>
        public static bool IsDisabledInTaskManager()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, false);
                if (key?.GetValue(ValueName) is byte[] data && data.Length > 0)
                {
                    // Even first byte (0x02/0x06) = enabled, odd (0x03/0x07) = disabled.
                    return (data[0] & 0x1) == 1;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"StartupManager.IsDisabledInTaskManager failed: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Enables or disables launch at sign-in.
        /// </summary>
        /// <param name="enable">Desired state.</param>
        /// <param name="overrideTaskManager">
        /// When enabling, also clear a "Disabled" flag set in Task Manager. Pass true only when the
        /// user explicitly asked for it (Settings checkbox / tray menu), never on a silent sync.
        /// </param>
        /// <returns>True when the registry now matches the request.</returns>
        public static bool SetEnabled(bool enable, bool overrideTaskManager)
        {
            string? exe = ExecutablePath;
            if (exe == null) return false;

            try
            {
                using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
                if (runKey == null) return false;

                if (enable)
                {
                    string command = BuildCommand(exe);
                    if (!string.Equals(runKey.GetValue(ValueName) as string, command, StringComparison.OrdinalIgnoreCase))
                    {
                        // Also repairs a stale path if the app was moved since it was registered.
                        runKey.SetValue(ValueName, command, RegistryValueKind.String);
                    }

                    if (overrideTaskManager && IsDisabledInTaskManager())
                    {
                        MarkApproved();
                    }
                }
                else
                {
                    runKey.DeleteValue(ValueName, false);
                    using var approvedKey = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, true);
                    approvedKey?.DeleteValue(ValueName, false);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"StartupManager.SetEnabled({enable}) failed: {ex.Message}");
                return false;
            }
        }

        public static void MarkApproved()
        {
            try
            {
                using var approvedKey = Registry.CurrentUser.CreateSubKey(ApprovedKeyPath, true);
                // 0x02 followed by 11 zero bytes = "Enabled" (standard format Windows Explorer/Task Manager writes).
                var enabled = new byte[12];
                enabled[0] = 0x02;
                approvedKey?.SetValue(ValueName, enabled, RegistryValueKind.Binary);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"StartupManager.MarkApproved failed: {ex.Message}");
            }
        }
    }
}
