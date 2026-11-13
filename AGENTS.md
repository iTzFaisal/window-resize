# Repository guidance

- This is a Windows-only .NET 10 WPF app. The UI is `MainWindow.xaml` / `MainWindow.xaml.cs`; Win32 window enumeration, monitor lookup, and resizing live in `Services/WindowService.cs`.
- Preset and percentage sizes are client-area pixels. Percentage mode scales both dimensions of the selected window monitor's current resolution equally; resizing preserves the current top-left position.
- Keep `app.manifest` PerMonitorV2 DPI awareness: the resize calculations depend on physical pixel coordinates.
- There is no test project. On Windows, restore with the command below, run `dotnet build WindowSizer.csproj --no-restore`, then smoke-test against a normal visible window.
- This workspace has no NuGet sources configured. Restore with the official feed explicitly; do not edit global NuGet settings just to build:
  `dotnet restore WindowSizer.csproj --source https://api.nuget.org/v3/index.json`
- For a GitHub/test executable, publish framework-dependent and single-file; do not bundle the .NET/WPF runtime. It requires the .NET 10 Desktop Runtime on the target PC:
  `dotnet restore WindowSizer.csproj --runtime win-x64 --source https://api.nuget.org/v3/index.json`
  `dotnet publish WindowSizer.csproj -c Release -r win-x64 --self-contained false --no-restore -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o bin/Release/net10.0-windows/win-x64/framework-dependent/publish`
- Do not commit build output from `bin/` or `obj/`; `obj/` already contains tracked generated files, so those may still show diffs after builds. The framework-dependent executable is under the publish path above.
