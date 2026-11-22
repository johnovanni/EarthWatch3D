using System.ComponentModel;
using System.Windows.Threading;
using EarthWatch3D.App.Rendering;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.Core.Lightning;
using GlobeCore.Wpf.Layers;

namespace EarthWatch3D.App.Layers;

/// <summary>
/// The lightning flashes (<see cref="LightningLayer"/>). Rebuilt when flashes arrive, every tick, and when the lightning
/// toggle, the window length or live mode changes; the layer itself rebuilds at most once a second. Lightning is live
/// only. (Moved from SceneController: OnFlashesChanged, OnTicked, OnLightningSettingChanged, UpdateLightning.)
/// </summary>
public sealed class LightningSceneLayer : SceneLayer
{
    public const string LayerId = "lightning";
    public const int Order = 110;

    private readonly LightningLayer _lightning;
    private readonly ILightningSource _source;
    private readonly MainViewModel _vm;
    private readonly Dispatcher _dispatcher;
    private int _flashChangePending;
    private bool _closed;

    /// <param name="dispatcher">The UI thread's dispatcher (flashes arrive on feed threads).</param>
    public LightningSceneLayer(ILightningSource source, MainViewModel vm, Dispatcher dispatcher) : base(LayerId, "Lightning", Order)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _lightning = new LightningLayer(Root);
        _source.FlashesChanged += OnFlashesChanged;
        _vm.Ticked += OnTicked;
        _vm.Layers.PropertyChanged += OnLightningSettingChanged;
        _vm.Display.PropertyChanged += OnLightningSettingChanged;
        _vm.Timeline.PropertyChanged += OnTimelineChanged;
    }

    /// <summary>Nothing to do on a scene update: the flashes are points on the globe, rebuilt from the feed and the clock.</summary>
    public override void Update(SceneContext ctx) { }

    /// <summary>Stops listening (the window has closed).</summary>
    public void Close()
    {
        _closed = true;
        _source.FlashesChanged -= OnFlashesChanged;
        _vm.Ticked -= OnTicked;
        _vm.Layers.PropertyChanged -= OnLightningSettingChanged;
        _vm.Display.PropertyChanged -= OnLightningSettingChanged;
        _vm.Timeline.PropertyChanged -= OnTimelineChanged;
    }

    // Raised on a feed thread; coalesced like store changes. The layer itself rebuilds at most once a second.
    private void OnFlashesChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _flashChangePending, 1) == 1) return;
        _dispatcher.BeginInvoke(() =>
        {
            Volatile.Write(ref _flashChangePending, 0);
            if (!_closed) UpdateLightning();
        });
    }

    private void OnTicked(object? sender, EventArgs e)
    {
        if (_closed) return;
        UpdateLightning();
        // How old each sensor's newest flash is right now (wall clock), not relative to a replayed display time; and, when
        // the window holds more flashes than are drawn, how many.
        _vm.Layers.LightningLegend = LightningLayer.LegendNote(_source.Coverage, _vm.NowUtc, flashesInWindow: _lightning.FlashCountInWindow);
    }

    private void OnLightningSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LayersViewModel.ShowLightning) or nameof(DisplayOptionsViewModel.LightningWindowMinutes)) UpdateLightning();
    }

    private void OnTimelineChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TimelineViewModel.IsLive)) return;
        if (!_closed) UpdateLightning();   // lightning is live only: hide or show it now, not at the next tick
    }

    private void UpdateLightning()
    {
        bool show = LightningLayer.IsShown(_vm.Layers.ShowLightning, _vm.Timeline.IsLive, _source.SupportsHistory);
        _lightning.Update(_source, _vm.DisplayTimeUtc, TimeSpan.FromMinutes(_vm.Display.LightningWindowMinutes), show);
    }
}
