namespace EarthWatch3D.App.Controllers;

/// <summary>
/// A layer built the first time it is wanted, then kept. A failed build is reported once and remembered, so it is
/// not tried again on every compose; <see cref="ForgetFailure"/> (called when the user switches the layer off)
/// allows one more try the next time it is wanted. Used from one thread (the UI thread's compose loop).
/// </summary>
public sealed class LazyLayer<T>(Func<Task<T>> build) where T : class
{
    private T? _value;

    /// <summary>True after a failed build, until <see cref="ForgetFailure"/>.</summary>
    public bool HasFailed { get; private set; }

    /// <summary>The layer, building it if needed; null if the build failed (now or before).</summary>
    public async Task<T?> GetAsync(Action<Exception> onFailure)
    {
        if (_value is not null || HasFailed) return _value;
        try { _value = await build(); }
        catch (Exception ex)
        {
            HasFailed = true;
            onFailure(ex);
        }
        return _value;
    }

    public void ForgetFailure() => HasFailed = false;
}
