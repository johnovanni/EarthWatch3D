using System.Windows.Controls;
using GlobeCore.Wpf.Layers;
using GlobeCore.Wpf.Rendering;

namespace EarthWatch3D.App.Layers;

/// <summary>
/// The labels of M5+ quakes and named storms on the overlay canvas, for the events of <see cref="EventsSceneLayer"/>
/// on the side of the globe facing the camera. (Moved from SceneController: the labels half of UpdateEvents.)
/// </summary>
public sealed class EventLabelsOverlayLayer : OverlayLayer
{
    public const string LayerId = "event-labels";
    public const int Order = 200;

    private readonly EventsSceneLayer _events;
    private readonly LabelOverlay _labels;

    /// <param name="overlay">The canvas the labels go on. Its text blocks are added the first time they are needed.</param>
    public EventLabelsOverlayLayer(EventsSceneLayer events, Canvas overlay) : base(LayerId, "Event labels", Order)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _labels = new LabelOverlay(overlay ?? throw new ArgumentNullException(nameof(overlay)));
    }

    public override void Render(Canvas overlay, Projector projector) => _events.DrawLabels(_labels, projector);

    protected override void Clear(Canvas overlay)
    {
        _labels.Begin();
        _labels.End();
    }
}
