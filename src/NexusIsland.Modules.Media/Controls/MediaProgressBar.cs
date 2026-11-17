using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace NexusIsland.Modules.Media.Controls;

/// <summary>
/// Barra de progreso fina y redondeada. Si la app permite adelantar (CanSeek),
/// un clic salta a esa posición. Se dibuja a mano: es más liviano que un Slider con plantilla.
/// </summary>
public sealed class MediaProgressBar : FrameworkElement
{
    private const double TrackHeight = 4;

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(MediaProgressBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CanSeekProperty = DependencyProperty.Register(
        nameof(CanSeek), typeof(bool), typeof(MediaProgressBar),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender,
            (d, e) => ((MediaProgressBar)d).Cursor = (bool)e.NewValue ? Cursors.Hand : null));

    public static readonly DependencyProperty SeekCommandProperty = DependencyProperty.Register(
        nameof(SeekCommand), typeof(ICommand), typeof(MediaProgressBar));

    private static readonly Brush TrackBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush FillBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)));

    public MediaProgressBar()
    {
        // Área de clic más alta que la barra visible: más fácil de acertar.
        MinHeight = 16;
        MouseEnter += (_, _) => InvalidateVisual();
        MouseLeave += (_, _) => InvalidateVisual();
    }

    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public bool CanSeek { get => (bool)GetValue(CanSeekProperty); set => SetValue(CanSeekProperty, value); }
    public ICommand? SeekCommand { get => (ICommand?)GetValue(SeekCommandProperty); set => SetValue(SeekCommandProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        if (width <= 0) return;
        var y = (ActualHeight - TrackHeight) / 2;
        var radius = TrackHeight / 2;

        // Fondo transparente para que toda el área reciba el clic.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, ActualHeight));
        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0, y, width, TrackHeight), radius, radius);

        var fill = Math.Clamp(Progress, 0, 1) * width;
        if (fill > 0)
            dc.DrawRoundedRectangle(FillBrush, null, new Rect(0, y, Math.Max(fill, TrackHeight), TrackHeight), radius, radius);

        if (CanSeek && IsMouseOver)
            dc.DrawEllipse(FillBrush, null, new Point(Math.Clamp(fill, 5, width - 5), ActualHeight / 2), 5, 5);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!CanSeek || ActualWidth <= 0) return; // sin "adelantar": el clic sigue de largo (cierra la isla)
        var fraction = Math.Clamp(e.GetPosition(this).X / ActualWidth, 0, 1);
        if (SeekCommand?.CanExecute(fraction) == true) SeekCommand.Execute(fraction);
        e.Handled = true;
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
