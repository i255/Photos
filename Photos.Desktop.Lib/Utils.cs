using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Photos.Desktop
{
    internal static class Utils
    {
        public static SKPoint ToSK(this Vector2 v) => new(v.X, v.Y);
        public static SKSize ToSKSize(this Vector2i v) => new(v.X, v.Y);

    }

    [SupportedOSPlatform("windows")]
    internal static class WinUtils
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DWM_BLURBEHIND blurBehind);

        public static unsafe bool PrepareWindow(NativeWindow w)
        {
            try
            {
                var par = new DWM_BLURBEHIND() { dwFlags = DWM_BB.Enable, fEnable = true };
                var err = DwmEnableBlurBehindWindow(GLFW.GetWin32Window(w.WindowPtr), ref par);
                if (err != 0)
                {
                    Core.Utils.Trace($"blur error: {err}");
                    return false;
                }
            }
            catch (Exception e)
            {
                Core.Utils.TraceError(e);
                return false;
            }

            return true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DWM_BLURBEHIND
    {
        public DWM_BB dwFlags;
        public bool fEnable;
        public IntPtr hRgnBlur;
        public bool fTransitionOnMaximized;

      
    }

    enum DWM_BB
    {
        Enable = 1,
        BlurRegion = 2,
        TransitionMaximized = 4
    }


    ///////////////////////////////////////////////////////////////////////////////////////////////////
    
    
    
    [SupportedOSPlatform("windows")]
    public class WindowsClipboardHelper
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, uint dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        private const uint CF_HDROP = 15;
        private const uint GMEM_MOVEABLE = 0x0002;

        public static void CopyFilesToClipboard(string[] filePaths)
        {
            if (filePaths == null || filePaths.Length == 0)
                throw new ArgumentException("File paths cannot be null or empty.");

            // Calculate the size of the DROPFILES structure and the file paths.
            int totalSize = Marshal.SizeOf<DROPFILES>();
            foreach (var filePath in filePaths)
            {
                totalSize += (filePath.Length + 1) * sizeof(char); // Include null terminator
            }
            totalSize += sizeof(char); // Double null terminator at the end

            IntPtr hGlobal = IntPtr.Zero;
            try
            {
                hGlobal = GlobalAlloc(GMEM_MOVEABLE, (uint)totalSize);
                if (hGlobal == IntPtr.Zero)
                    throw new Exception("Failed to allocate global memory.");

                IntPtr lockedMemory = GlobalLock(hGlobal);
                if (lockedMemory == IntPtr.Zero)
                    throw new Exception("Failed to lock global memory.");

                try
                {
                    // Write the DROPFILES structure
                    var dropFiles = new DROPFILES
                    {
                        pFiles = Marshal.SizeOf(typeof(DROPFILES)),
                        fWide = true // Unicode
                    };

                    Marshal.StructureToPtr(dropFiles, lockedMemory, false);

                    // Write the file paths
                    IntPtr filePathPtr = IntPtr.Add(lockedMemory, Marshal.SizeOf(typeof(DROPFILES)));
                    foreach (var filePath in filePaths)
                    {
                        var chars = Encoding.Unicode.GetBytes(filePath + "\0");
                        Marshal.Copy(chars, 0, filePathPtr, chars.Length);
                        filePathPtr = IntPtr.Add(filePathPtr, chars.Length);
                    }

                    // Add double null terminator
                    Marshal.Copy(new byte[2], 0, filePathPtr, 2);
                }
                finally
                {
                    GlobalUnlock(hGlobal);
                }

                if (!OpenClipboard(IntPtr.Zero))
                    throw new Exception("Failed to open clipboard.");

                try
                {
                    if (!EmptyClipboard())
                        throw new Exception("Failed to empty clipboard.");

                    if (SetClipboardData(CF_HDROP, hGlobal) == IntPtr.Zero)
                        throw new Exception("Failed to set clipboard data.");

                    // Clipboard now owns the memory; don't free it.
                    hGlobal = IntPtr.Zero;
                }
                finally
                {
                    CloseClipboard();
                }
            }
            finally
            {
                if (hGlobal != IntPtr.Zero)
                    GlobalFree(hGlobal);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DROPFILES
        {
            public int pFiles;
            public int ptX;
            public int ptY;
            public bool fNC;
            public bool fWide;
        }
    }

    [SupportedOSPlatform("linux")]
    public class LinuxClipboardHelper
    {
        public static string CopyFilesToClipboard(string[] filePaths)
        {
            if (filePaths == null || filePaths.Length == 0)
                return "No files selected for copying.";

            // Convert file paths to URI format
            var uriList = string.Join("\n", filePaths.Select(path => "file://" + path));

            // Check if we're running under Wayland
            var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = true
            };

            string clipboardTool;
            string installCommand;
            if (!string.IsNullOrEmpty(waylandDisplay))
            {
                // Use wl-copy for Wayland
                clipboardTool = "wl-copy";
                startInfo.FileName = clipboardTool;
                startInfo.Arguments = "--type text/uri-list";
                installCommand = "sudo apt-get install wl-clipboard";
            }
            else
            {
                // Use xclip for X11
                clipboardTool = "xclip";
                startInfo.FileName = clipboardTool;
                startInfo.Arguments = "-selection clipboard -t text/uri-list";
                installCommand = "sudo apt-get install xclip";
            }

            try
            {
                using var process = Process.Start(startInfo);
                using var writer = process.StandardInput;
                writer.Write(uriList);
                writer.Close();

                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new Exception();

                return null; // Success
            }
            catch
            {
                return $"Please install {clipboardTool} (like: '{installCommand}')";
            }
        }
    }
}
