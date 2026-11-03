using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using MaterialDockerImage.UI;

namespace MaterialDockerImage.Services;

/// <summary>
/// The toast stack. New toasts slide in from the right; removed toasts fade out as a snapshot drawn in the adorner
/// layer, so the notifier can remove them immediately and nothing depends on the animation finishing.
/// </summary>
public sealed class MotionToastPanel : StackPanel
{
    private readonly ConditionalWeakTable<UIElement, BitmapSource> snapshots = new();

    protected override void OnVisualChildrenChanged(DependencyObject? visualAdded, DependencyObject? visualRemoved)
    {
        base.OnVisualChildrenChanged(visualAdded, visualRemoved);
        if (visualAdded is FrameworkElement added)
        {
            Motion.SlideInFromRight(added);
            // Snapshot once the entrance has settled, for the exit fade.
            var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(700) };
            timer.Tick += (_, _) => { timer.Stop(); if (added.Parent == this) Snapshot(added); };
            if (Motion.Enabled) timer.Start();
        }
        if (visualRemoved is FrameworkElement removed && snapshots.TryGetValue(removed, out var image))
        {
            snapshots.Remove(removed);
            var slot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(removed);
            if (Motion.Enabled && slot.Width > 0 && AdornerLayer.GetAdornerLayer(this) is { } layer)
            {
                // A bottom-anchored stack moves its top edge down by the removed slot, so shift the ghost to match.
                var top = slot.Y + removed.Margin.Top - (VerticalAlignment == VerticalAlignment.Bottom ? slot.Height : 0);
                var ghost = new ToastGhost(this, image, new Rect(slot.X + removed.Margin.Left, top, removed.ActualWidth, removed.ActualHeight));
                layer.Add(ghost);
                ghost.Leave(() => layer.Remove(ghost));
            }
        }
    }

    private void Snapshot(FrameworkElement element)
    {
        var (w, h) = (element.ActualWidth, element.ActualHeight);
        if (w < 1 || h < 1) return;
        var dpi = VisualTreeHelper.GetDpi(element);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(w * dpi.DpiScaleX), (int)Math.Ceiling(h * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, w, h) }, null, new Rect(0, 0, w, h));
        bitmap.Render(visual);
        bitmap.Freeze();
        snapshots.AddOrUpdate(element, bitmap);
    }

    private sealed class ToastGhost : Adorner
    {
        private readonly BitmapSource image;
        private readonly Rect bounds;
        private readonly TranslateTransform move = new();
        public ToastGhost(UIElement adorned, BitmapSource image, Rect bounds) : base(adorned)
        {
            this.image = image; this.bounds = bounds;
            IsHitTestVisible = false;
            RenderTransform = move;
        }
        protected override void OnRender(DrawingContext dc) => dc.DrawImage(image, bounds);
        public void Leave(Action done)
        {
            var duration = Motion.Duration(AppearanceMotion.Short4);
            var fade = new DoubleAnimation(1, 0, duration) { EasingFunction = Motion.EmphasizedAccelerate };
            fade.Completed += (_, _) => done();
            BeginAnimation(OpacityProperty, fade);
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 48, duration) { EasingFunction = Motion.EmphasizedAccelerate });
        }
    }
}
