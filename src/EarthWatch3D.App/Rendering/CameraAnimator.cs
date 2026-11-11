using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace EarthWatch3D.App.Rendering;

/// <summary>Drives CameraRules.OrbitStep over a duration on CompositionTarget.Rendering. A new animation cancels the running one.</summary>
public sealed class CameraAnimator
{
    private readonly PerspectiveCamera _camera;
    private readonly Stopwatch _clock = new();
    private Vector3D _from, _to;
    private double _durationMs;
    private bool _running;

    public CameraAnimator(PerspectiveCamera camera) => _camera = camera;

    public bool IsRunning => _running;

    public void AnimateTo(Point3D target, double durationMs = 700)
    {
        Cancel();
        _from = (Vector3D)_camera.Position;
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
        double t = Math.Min(1, _clock.Elapsed.TotalMilliseconds / _durationMs);
        var (p, l, u) = CameraRules.OrbitStep(_from, _to, t);
        _camera.Position = p; _camera.LookDirection = l; _camera.UpDirection = u;
        if (t >= 1) Cancel();
    }
}
