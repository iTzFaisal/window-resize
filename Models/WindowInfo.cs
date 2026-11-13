namespace WindowSizer.Models;

public sealed record WindowInfo(
    nint Handle,
    string Title,
    string ProcessName,
    int ClientWidth,
    int ClientHeight,
    int DisplayWidth,
    int DisplayHeight,
    string DisplayName)
{
    public string ProcessAndSize =>
        $"{ProcessName}  ·  {ClientWidth:N0} × {ClientHeight:N0} client px";
}

public sealed record WindowResizeResult(
    int RequestedWidth,
    int RequestedHeight,
    int ActualWidth,
    int ActualHeight)
{
    public bool MatchesRequest =>
        RequestedWidth == ActualWidth && RequestedHeight == ActualHeight;
}
