namespace Planer_App.Views;

internal readonly record struct SnapPoint(double X, double Y)
{
    public double DistanceSquared(SnapPoint other) => (X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y);
}
internal readonly record struct SnapTarget(int Key, SnapPoint Position, double HorizontalRange);

/// <summary>Acquire by slot size; release horizontally from the pointer's engagement position.</summary>
internal sealed class DragSnapTracker
{
    private int? _held;
    private SnapPoint _engagedAt;
    private readonly HashSet<int> _released = [];
    public int? Update(SnapPoint freeOrigin, IReadOnlyList<SnapTarget> targets, double verticalRange, double horizontalReleaseRange)
    {
        bool Within(SnapTarget target) => Math.Abs(target.Position.X - freeOrigin.X) <= target.HorizontalRange &&
            Math.Abs(target.Position.Y - freeOrigin.Y) <= verticalRange;
        // A broad capture zone can still contain the pointer after release. Do not immediately
        // capture that same slot again; rearm it after leaving its capture zone.
        _released.RemoveWhere(key => !targets.Any(target => target.Key == key && Within(target)));
        foreach (var target in targets)
        {
            if (target.Key != _held) continue;
            if (Math.Abs(freeOrigin.X - _engagedAt.X) <= horizontalReleaseRange &&
                Math.Abs(freeOrigin.Y - target.Position.Y) <= verticalRange) return _held;
            if (Within(target)) _released.Add(target.Key);
            break;
        }
        _held = null;
        double nearest = double.PositiveInfinity;
        foreach (var target in targets)
        {
            double distance = target.Position.DistanceSquared(freeOrigin);
            if (!_released.Contains(target.Key) && Within(target) && distance < nearest)
            { nearest = distance; _held = target.Key; }
        }
        if (_held is not null) _engagedAt = freeOrigin;
        return _held;
    }
    public void Reset() { _held = null; _released.Clear(); }
    public static SnapPoint GrabOffset(SnapPoint localPoint, double localWidth, double localHeight, double renderedWidth, double renderedHeight) =>
        new(localPoint.X * renderedWidth / localWidth, localPoint.Y * renderedHeight / localHeight);
}
