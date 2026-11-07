using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MasseterDrop.Helpers
{
    public static class NativeMethods
    {
        #region Known Folders

        private static readonly Guid FOLDERID_Downloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
        private static extern int SHGetKnownFolderPath(
            [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
            uint dwFlags,
            IntPtr hToken,
            out IntPtr ppszPath);

        public static string GetDownloadsFolderPath()
        {
            try
            {
                IntPtr pPath;
                int hr = SHGetKnownFolderPath(FOLDERID_Downloads, 0, IntPtr.Zero, out pPath);
                if (hr == 0 && pPath != IntPtr.Zero)
                {
                    try
                    {
                        string? path = Marshal.PtrToStringUni(pPath);
                        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                        {
                            return path;
                        }
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(pPath);
                    }
                }
            }
            catch
            {
                // Fallback if Shell API fails
            }

            // Fallback 1: UserProfile\Downloads
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string downloadsPath = Path.Combine(userProfile, "Downloads");
            if (Directory.Exists(downloadsPath))
            {
                return downloadsPath;
            }

            // Fallback 2: MyDocuments\Downloads
            string myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            downloadsPath = Path.Combine(myDocs, "Downloads");
            if (Directory.Exists(downloadsPath))
            {
                return downloadsPath;
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        #endregion

        #region AppUserModelID

        [DllImport("shell32.dll", SetLastError = true)]
        public static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

        #endregion

        #region DWM Styling (Rounded corners & Dark Mode)

        public enum DWMWINDOWATTRIBUTE
        {
            DWMWA_NCRENDERING_ENABLED = 1,
            DWMWA_NCRENDERING_POLICY,
            DWMWA_TRANSITIONS_FORCEDISABLED,
            DWMWA_ALLOW_NCPAINT,
            DWMWA_CAPTION_BUTTON_BOUNDS,
            DWMWA_NONCLIENT_RTL_LAYOUT,
            DWMWA_FORCE_ICONIC_REPRESENTATION,
            DWMWA_FLIP3D_POLICY,
            DWMWA_EXTENDED_FRAME_BOUNDS,
            DWMWA_HAS_ICONIC_BITMAP,
            DWMWA_DISALLOW_PEEK,
            DWMWA_EXCLUDED_FROM_PEEK,
            DWMWA_CLOAK,
            DWMWA_CLOAKED,
            DWMWA_FREEZE_REPRESENTATION,
            DWMWA_PASSIVE_UPDATE_MODE,
            DWMWA_USE_HOSTBACKDROPBRUSH,
            DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19,
            DWMWA_USE_IMMERSIVE_DARK_MODE = 20,
            DWMWA_WINDOW_CORNER_PREFERENCE = 33,
            DWMWA_BORDER_COLOR = 34,
            DWMWA_CAPTION_COLOR = 35,
            DWMWA_TEXT_COLOR = 36,
            DWMWA_VISIBLE_FRAME_BORDER_THICKNESS = 37,
            DWMWA_SYSTEMBACKDROP_TYPE = 38
        }

        public enum DWM_WINDOW_CORNER_PREFERENCE
        {
            DWMWCP_DEFAULT = 0,
            DWMWCP_DONOTROUND = 1,
            DWMWCP_ROUND = 2,
            DWMWCP_ROUNDSMALL = 3
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, DWMWINDOWATTRIBUTE attr, ref int attrValue, int attrSize);

        public static void ApplyModernWindowStyle(Window window, bool isDarkMode = true)
        {
            try
            {
                var helper = new WindowInteropHelper(window);
                IntPtr hwnd = helper.EnsureHandle();
                if (hwnd == IntPtr.Zero) return;

                int darkMode = isDarkMode ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref darkMode, sizeof(int));

                int cornerPreference = (int)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
            }
            catch
            {
                // Graceful fallback on older Windows versions
            }
        }

        #endregion

        #region Shell File Icons

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        public const uint SHGFI_ICON = 0x000000100;
        public const uint SHGFI_LARGEICON = 0x000000000;
        public const uint SHGFI_SMALLICON = 0x000000001;
        public const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        public const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        public static ImageSource? GetFileIcon(string filePath, bool isLarge = true)
        {
            try
            {
                SHFILEINFO shinfo = new SHFILEINFO();
                uint flags = SHGFI_ICON | (isLarge ? SHGFI_LARGEICON : SHGFI_SMALLICON);

                if (!File.Exists(filePath))
                {
                    flags |= SHGFI_USEFILEATTRIBUTES;
                    SHGetFileInfo(filePath, FILE_ATTRIBUTE_NORMAL, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
                }
                else
                {
                    SHGetFileInfo(filePath, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
                }

                if (shinfo.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        ImageSource img = Imaging.CreateBitmapSourceFromHIcon(
                            shinfo.hIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        img.Freeze();
                        return img;
                    }
                    finally
                    {
                        DestroyIcon(shinfo.hIcon);
                    }
                }
            }
            catch
            {
                // Fallback to null
            }
            return null;
        }

        #endregion

        #region Taskbar and Monitor Metrics

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uCallbackMessage;
            public int uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        public const int ABM_GETTASKBARPOS = 0x00000005;
        public const int ABE_LEFT = 0;
        public const int ABE_TOP = 1;
        public const int ABE_RIGHT = 2;
        public const int ABE_BOTTOM = 3;

        [DllImport("shell32.dll")]
        public static extern IntPtr SHAppBarMessage(int dwMessage, ref APPBARDATA pData);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        public struct TaskbarInfo
        {
            public int Edge;
            public RECT Bounds;
            public bool Exists;
        }

        public static TaskbarInfo GetTaskbarInfo()
        {
            var info = new TaskbarInfo();
            var data = new APPBARDATA();
            data.cbSize = Marshal.SizeOf(typeof(APPBARDATA));

            IntPtr result = SHAppBarMessage(ABM_GETTASKBARPOS, ref data);
            if (result != IntPtr.Zero)
            {
                info.Exists = true;
                info.Edge = data.uEdge;
                info.Bounds = data.rc;
            }
            else
            {
                info.Exists = false;
                info.Edge = ABE_BOTTOM;
            }

            return info;
        }

        #endregion
    }
}
