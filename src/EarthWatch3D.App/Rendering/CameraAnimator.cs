using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace EarthWatch3D.App.Rendering;

/// <summary>Drives CameraRules.OrbitStep over a duration on CompositionTarget.Rendering. A new animation cancels the running one.</summary>
public sealed class CameraAnimator
{
    private readonly Func<PerspectiveCamera?> _camera;
    private readonly Stopwatch _clock = new();
    private Vector3D _from, _to;
    private double _durationMs;
    private bool _running;

    /// <param name="camera">Returns the viewport's current camera (null if it is not a perspective camera); read at every step.</param>
    public CameraAnimator(Func<PerspectiveCamera?> camera) => _camera = camera ?? throw new ArgumentNullException(nameof(camera));

    public bool IsRunning => _running;

    public void AnimateTo(Point3D target, double durationMs = 700)
    {
        Cancel();
        if (_camera() is not { } camera) return;
        _from = (Vector3D)camera.Position;
        _to = (Vector3D)target;
        _durationMs = durationMs;
        _clock.Restart();
        _running = true;
        CompositionTarget.Rendering += OnRendering;
    }

    public void Cancel()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_camera() is not { } camera) { Cancel(); return; }
        double t = Math.Min(1, _clock.Elapsed.TotalMilliseconds / _durationMs);
        var (p, l, u) = CameraRules.OrbitStep(_from, _to, t);
        camera.Position = p; camera.LookDirection = l; camera.UpDirection = u;
        if (t >= 1) Cancel();
    }
}
