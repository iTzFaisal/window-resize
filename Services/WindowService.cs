using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WindowSizer.Models;

namespace WindowSizer.Services;

public sealed class WindowService
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int SwRestore = 9;
    private const int MonitorDefaultToNearest = 2;
    private const int EnumCurrentSettings = -1;
    private const uint WsExToolWindow = 0x00000080;
    private const uint DwmwaCloaked = 14;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    public IReadOnlyList<WindowInfo> GetOpenWindows()
    {
        var windows = new List<WindowInfo>();
        var currentProcessId = (uint)Environment.ProcessId;

        EnumWindowsProc callback = (handle, _) =>
        {
            try
            {
                if (!IsWindowVisible(handle) || IsCloaked(handle))
                {
                    return true;
                }

                GetWindowThreadProcessId(handle, out var processId);
                if (processId == 0 || processId == currentProcessId)
                {
                    return true;
                }

                var extendedStyle = unchecked((uint)GetWindowLongPtr(handle, GwlExStyle).ToInt64());
                if ((extendedStyle & WsExToolWindow) != 0)
                {
                    return true;
                }

                var titleLength = GetWindowTextLength(handle);
                if (titleLength <= 0)
                {
                    return true;
                }

                var title = new StringBuilder(titleLength + 1);
                if (GetWindowText(handle, title, title.Capacity) <= 0)
                {
                    return true;
                }

                if (!GetClientRect(handle, out var clientRect))
                {
                    return true;
                }

                var (displayWidth, displayHeight, displayName) = GetDisplayForWindow(handle);
                var processName = GetProcessName(processId);
                windows.Add(new WindowInfo(
                    handle,
                    title.ToString(),
                    processName,
                    Math.Max(0, clientRect.Right - clientRect.Left),
                    Math.Max(0, clientRect.Bottom - clientRect.Top),
                    displayWidth,
                    displayHeight,
                    displayName));
            }
            catch
            {
                // A window may disappear or its process may exit while it is being enumerated.
            }

            return true;
        };

        if (!EnumWindows(callback, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not be enumerated.");
        }

        return windows
            .OrderBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public WindowResizeResult ResizeClientArea(nint handle, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The requested dimensions must be positive.");
        }

        if (!IsWindow(handle))
        {
            throw new InvalidOperationException("That window is no longer open. Refresh the window list and try again.");
        }

        if (IsIconic(handle) || IsZoomed(handle))
        {
            ShowWindow(handle, SwRestore);
        }

        if (!GetWindowRect(handle, out var currentRect))
        {
            throw LastWin32Exception("The selected window's position could not be read.");
        }

        var style = unchecked((uint)GetWindowLongPtr(handle, GwlStyle).ToInt64());
        var extendedStyle = unchecked((uint)GetWindowLongPtr(handle, GwlExStyle).ToInt64());
        var dpi = GetWindowDpi(handle);
        var adjustedRect = new NativeRect { Left = 0, Top = 0, Right = width, Bottom = height };

        if (!AdjustWindowRectForDpi(
                ref adjustedRect,
                style,
                GetMenu(handle) != IntPtr.Zero,
                extendedStyle,
                dpi))
        {
            throw LastWin32Exception("The requested client size could not be converted to a window size.");
        }

        var outerWidth = adjustedRect.Right - adjustedRect.Left;
        var outerHeight = adjustedRect.Bottom - adjustedRect.Top;
        if (outerWidth <= 0 || outerHeight <= 0)
        {
            throw new InvalidOperationException("Windows calculated an invalid outer window size.");
        }

        var flags = SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder;
        if (!SetWindowPos(
                handle,
                IntPtr.Zero,
                currentRect.Left,
                currentRect.Top,
                outerWidth,
                outerHeight,
                flags))
        {
            throw LastWin32Exception(
                "Windows refused to resize the selected window. It may be elevated or have a fixed size.");
        }

        if (!GetClientRect(handle, out var actualRect))
        {
            throw LastWin32Exception("The window was resized, but its resulting client size could not be read.");
        }

        return new WindowResizeResult(
            width,
            height,
            Math.Max(0, actualRect.Right - actualRect.Left),
            Math.Max(0, actualRect.Bottom - actualRect.Top));
    }

    private static string GetProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return process.ProcessName;
        }
        catch
        {
            return "Unknown process";
        }
    }

    private static bool IsCloaked(nint handle)
    {
        try
        {
            return DwmGetWindowAttribute(
                       handle,
                       DwmwaCloaked,
                       out var cloaked,
                       (uint)Marshal.SizeOf<uint>()) == 0
                   && cloaked != 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static (int Width, int Height, string Name) GetDisplayForWindow(nint handle)
    {
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return (0, 0, "Unknown display");
        }

        var info = new MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>(),
            DeviceName = string.Empty
        };

        if (!GetMonitorInfo(monitor, ref info))
        {
            return (0, 0, "Unknown display");
        }

        var mode = new DevMode
        {
            DeviceName = string.Empty,
            FormName = string.Empty,
            Size = (ushort)Marshal.SizeOf<DevMode>()
        };

        if (EnumDisplaySettings(info.DeviceName, EnumCurrentSettings, ref mode)
            && mode.PelsWidth > 0
            && mode.PelsHeight > 0)
        {
            return (checked((int)mode.PelsWidth), checked((int)mode.PelsHeight), info.DeviceName);
        }

        var monitorWidth = info.Monitor.Right - info.Monitor.Left;
        var monitorHeight = info.Monitor.Bottom - info.Monitor.Top;
        return (Math.Max(0, monitorWidth), Math.Max(0, monitorHeight), info.DeviceName);
    }

    private static uint GetWindowDpi(nint handle)
    {
        try
        {
            var dpi = GetDpiForWindow(handle);
            return dpi == 0 ? 96u : dpi;
        }
        catch (EntryPointNotFoundException)
        {
            return 96;
        }
    }

    private static bool AdjustWindowRectForDpi(
        ref NativeRect rect,
        uint style,
        bool hasMenu,
        uint extendedStyle,
        uint dpi)
    {
        try
        {
            return AdjustWindowRectExForDpi(ref rect, style, hasMenu, extendedStyle, dpi);
        }
        catch (EntryPointNotFoundException)
        {
            return AdjustWindowRectEx(ref rect, style, hasMenu, extendedStyle);
        }
    }

    private static Win32Exception LastWin32Exception(string message)
    {
        var errorCode = Marshal.GetLastWin32Error();
        return errorCode == 0
            ? new Win32Exception(message)
            : new Win32Exception(errorCode, message);
    }

    private static nint GetWindowLongPtr(nint handle, int index)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(handle, index)
            : new IntPtr(GetWindowLong32(handle, index));
    }

    private delegate bool EnumWindowsProc(nint handle, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public ushort SpecVersion;
        public ushort DriverVersion;
        public ushort Size;
        public ushort DriverExtra;
        public uint Fields;
        public int PositionX;
        public int PositionY;
        public uint DisplayOrientation;
        public uint DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TtOption;
        public short Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FormName;
        public ushort LogPixels;
        public uint BitsPerPel;
        public uint PelsWidth;
        public uint PelsHeight;
        public uint DisplayFlags;
        public uint DisplayFrequency;
        public uint IcmMethod;
        public uint IcmIntent;
        public uint MediaType;
        public uint DitherType;
        public uint Reserved1;
        public uint Reserved2;
        public uint PanningWidth;
        public uint PanningHeight;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint handle);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(nint handle, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint handle, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint handle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern nint GetMenu(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint handle, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint handle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", ExactSpelling = true, EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint handle, int index);

    [DllImport("user32.dll", ExactSpelling = true, EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(nint handle, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint handle, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "EnumDisplaySettingsW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNumber, ref DevMode mode);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustWindowRectEx(
        ref NativeRect rect,
        uint style,
        [MarshalAs(UnmanagedType.Bool)] bool hasMenu,
        uint extendedStyle);

    [DllImport("user32.dll", ExactSpelling = true, EntryPoint = "AdjustWindowRectExForDpi", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustWindowRectExForDpi(
        ref NativeRect rect,
        uint style,
        [MarshalAs(UnmanagedType.Bool)] bool hasMenu,
        uint extendedStyle,
        uint dpi);

    [DllImport("user32.dll", ExactSpelling = true, EntryPoint = "GetDpiForWindow")]
    private static extern uint GetDpiForWindow(nint handle);

    [DllImport("dwmapi.dll", ExactSpelling = true, EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetWindowAttribute(
        nint handle,
        uint attribute,
        out uint value,
        uint valueSize);
}
