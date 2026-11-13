# WindowSizer

WindowSizer is a small Windows desktop utility for resizing visible application windows while keeping them windowed.

## Features

- Lists visible top-level windows with their title, process name, and current client-area dimensions.
- Resizes the selected window to 720p (1280×720), 1080p (1920×1080), 1440p (2560×1440), or 2160p (3840×2160).
- Accepts a percentage of the resolution of the monitor containing the selected window and previews the resulting client-area pixel dimensions.
- Preserves the window's current top-left position.
- Handles minimized and maximized windows, display DPI, and standard window borders.

## Build and run

Requires the .NET 10 SDK and Windows Desktop targeting support.

```powershell
dotnet build
dotnet run
```

## Notes

- Preset and percentage sizes refer to the window's client area, not its outer border-to-border size.
- Percentage values may range from greater than 0 through 1000. Width and height are scaled by the same percentage.
- A selected window's monitor resolution is used for the percentage calculation. On multi-monitor systems, moving the target window to another display and refreshing updates the detected display.
- Some applications enforce their own minimum/maximum window sizes. Elevated applications can reject resize requests from a non-elevated utility, and exclusive-fullscreen windows cannot be resized as ordinary windowed windows.
