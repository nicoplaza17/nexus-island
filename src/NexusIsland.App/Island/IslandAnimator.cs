using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NexusIsland.Core.Animation;
using NexusIsland.Core.Island;

namespace NexusIsland.App.Island;

/// <summary>
/// Anima la forma de la isla (ancho, alto, redondeo) y el fundido entre los contenidos de cada estado
/// usando resortes físicos. Solo se engancha al ciclo de dibujo de WPF mientras algo se mueve:
/// en reposo no consume CPU.
/// </summary>
public sealed class IslandAnimator
{
    // Forma: un poco de rebote al crecer, más firme al achicarse.
    private const double GrowStiffness = 420, GrowDamping = 0.74;
    private const double ShrinkStiffness = 520, ShrinkDamping = 0.9;
    // Contenido: lo que entra aparece más despacio que lo que sale.
    private const double FadeInStiffness = 200, FadeOutStiffness = 900;

    private readonly FrameworkElement _frame;
    private readonly Border _shape, _shadowInner, _shadowOuter;
    private readonly FrameworkElement _contentHost;
    private readonly (FrameworkElement Layer, ScaleTransform Scale, Spring Opacity, IslandState State)[] _layers;
    private readonly Spring _width, _height, _radius;
    private readonly RectangleGeometry _clip = new();
    private bool _attached;
    private TimeSpan _lastFrame;

    public IslandAnimator(FrameworkElement frame, Border shape, Border shadowInner, Border shadowOuter,
        FrameworkElement contentHost,
        FrameworkElement collapsedLayer, FrameworkElement peekLayer, FrameworkElement expandedLayer)
    {
        _frame = frame;
        _shape = shape;
        _shadowInner = shadowInner;
        _shadowOuter = shadowOuter;
        _contentHost = contentHost;
        _contentHost.Clip = _clip;

        var start = IslandLayout.Collapsed;
        _width = new Spring(start.Width, restThreshold: 0.1);
        _height = new Spring(start.Height, restThreshold: 0.1);
        _radius = new Spring(start.CornerRadius, restThreshold: 0.1);

        _layers =
        [
            Prepare(collapsedLayer, IslandState.Collapsed, 1),
            Prepare(peekLayer, IslandState.Peek, 0),
            Prepare(expandedLayer, IslandState.Expanded, 0),
        ];
        Apply();
    }

    /// <summary>"Reducir movimiento": si está activo, los cambios son instantáneos.</summary>
    public bool ReduceMotion { get; set; }

    public bool IsAnimating => _attached;

    public void AnimateTo(IslandState state)
    {
        var spec = IslandLayout.For(state);
        var growing = spec.Height >= _height.Target;
        foreach (var s in new[] { _width, _height, _radius })
        {
            s.Stiffness = growing ? GrowStiffness : ShrinkStiffness;
            s.DampingRatio = growing ? GrowDamping : ShrinkDamping;
        }
        _width.Target = spec.Width;
        _height.Target = spec.Height;
        _radius.Target = spec.CornerRadius;

        foreach (var (_, _, opacity, layerState) in _layers)
        {
            var visible = layerState == state;
            opacity.Target = visible ? 1 : 0;
            opacity.Stiffness = visible ? FadeInStiffness : FadeOutStiffness;
        }

        if (ReduceMotion)
        {
            _width.SnapTo(_width.Target);
            _height.SnapTo(_height.Target);
            _radius.SnapTo(_radius.Target);
            foreach (var layer in _layers) layer.Opacity.SnapTo(layer.Opacity.Target);
            Apply();
            return;
        }

        if (!_attached)
        {
            _attached = true;
            _lastFrame = TimeSpan.Zero;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // WPF puede llamar más de una vez por cuadro; se avanza solo cuando cambia el tiempo.
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame) return;
        var dt = _lastFrame == TimeSpan.Zero ? 1.0 / 60 : (now - _lastFrame).TotalSeconds;
        _lastFrame = now;

        var moving = false;
        moving |= _width.Step(dt);
        moving |= _height.Step(dt);
        moving |= _radius.Step(dt);
        foreach (var layer in _layers) moving |= layer.Opacity.Step(dt);

        Apply();
        _frames++;

        if (!moving)
        {
            CompositionTarget.Rendering -= OnRendering;
            _attached = false;
            Settled?.Invoke(_frames);
            _frames = 0;
        }
    }

    private int _frames;

    /// <summary>Diagnóstico: la animación terminó, con la cantidad de cuadros que usó.</summary>
    public event Action<int>? Settled;

    private void Apply()
    {
        // El rebote puede llevar valores apenas por debajo del mínimo: se recortan para que nunca sea negativo.
        var w = Math.Max(1, _width.Value);
        var h = Math.Max(1, _height.Value);
        var r = Math.Clamp(_radius.Value, 0, h / 2);

        _frame.Width = w;
        _frame.Height = h;
        _shape.CornerRadius = new CornerRadius(r);
        _shadowInner.CornerRadius = new CornerRadius(r + 3);
        _shadowOuter.CornerRadius = new CornerRadius(r + 7);

        // Recorte redondeado del contenido (descontando el borde de 1 px).
        _clip.Rect = new Rect(0, 0, Math.Max(0, w - 2), Math.Max(0, h - 2));
        _clip.RadiusX = _clip.RadiusY = Math.Max(0, r - 1);

        foreach (var (layer, scale, opacity, _) in _layers)
        {
            var o = Math.Clamp(opacity.Value, 0, 1);
            layer.Opacity = o;
            layer.Visibility = o <= 0.001 ? Visibility.Hidden : Visibility.Visible;
            scale.ScaleX = scale.ScaleY = 0.94 + 0.06 * o; // el contenido "asienta" mientras aparece
        }
    }

    private static (FrameworkElement, ScaleTransform, Spring, IslandState) Prepare(
        FrameworkElement layer, IslandState state, double initialOpacity)
    {
        var scale = new ScaleTransform(1, 1);
        layer.RenderTransformOrigin = new Point(0.5, 0);
        layer.RenderTransform = scale;
        return (layer, scale, new Spring(initialOpacity, FadeInStiffness, 1.0, restThreshold: 0.002), state);
    }
}
