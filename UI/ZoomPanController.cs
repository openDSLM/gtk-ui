using System;
using System.Globalization;
using Gtk;

/// <summary>
/// Adds scroll-wheel zooming and drag panning to any widget by applying CSS transforms.
/// </summary>
public sealed class ZoomPanController : IDisposable
{
    private readonly Widget _target;
    private readonly CssProvider _provider;
    private readonly GestureDrag _dragGesture;
    private readonly EventControllerScroll _scrollController;
    private readonly EventControllerMotion _motionController;
    private readonly double _minZoom;
    private readonly double _maxZoom;

    private double _zoom = 1.0;
    private double _offsetX;
    private double _offsetY;
    private double _dragStartOffsetX;
    private double _dragStartOffsetY;
    private double _lastPointerX;
    private double _lastPointerY;

    public ZoomPanController(Widget target, double minZoom = 1.0, double maxZoom = 6.0)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _minZoom = minZoom;
        _maxZoom = Math.Max(minZoom, maxZoom);

        _provider = CssProvider.New();
        var context = _target.GetStyleContext();
        context.AddProvider(_provider, 800);
        _target.SetOverflow(Overflow.Hidden);

        _dragGesture = GestureDrag.New();
        _dragGesture.SetButton(0);
        _dragGesture.PropagationPhase = PropagationPhase.Bubble;
        _dragGesture.OnDragBegin += HandleDragBegin;
        _dragGesture.OnDragUpdate += HandleDragUpdate;
        _target.AddController(_dragGesture);

        _scrollController = EventControllerScroll.New(
            EventControllerScrollFlags.BothAxes | EventControllerScrollFlags.Discrete | EventControllerScrollFlags.Kinetic);
        _scrollController.PropagationPhase = PropagationPhase.Bubble;
        _scrollController.OnScroll += HandleScroll;
        _target.AddController(_scrollController);

        _motionController = EventControllerMotion.New();
        _motionController.PropagationPhase = PropagationPhase.Capture;
        _motionController.OnMotion += (_, args) =>
        {
            _lastPointerX = args.X;
            _lastPointerY = args.Y;
        };
        _target.AddController(_motionController);

        ApplyTransform();
    }

    public double Zoom => _zoom;

    public void Reset()
    {
        _zoom = 1.0;
        _offsetX = 0;
        _offsetY = 0;
        ApplyTransform();
    }

    public void Dispose()
    {
        _target.GetStyleContext().RemoveProvider(_provider);
        _provider.Dispose();
        _target.RemoveController(_dragGesture);
        _target.RemoveController(_scrollController);
        _target.RemoveController(_motionController);
        _dragGesture.Dispose();
        _scrollController.Dispose();
        _motionController.Dispose();
    }

    private void HandleDragBegin(GestureDrag gesture, GestureDrag.DragBeginSignalArgs args)
    {
        _dragStartOffsetX = _offsetX;
        _dragStartOffsetY = _offsetY;
    }

    private void HandleDragUpdate(GestureDrag gesture, GestureDrag.DragUpdateSignalArgs args)
    {
        if (_zoom <= 1.0)
        {
            return;
        }

        double width = Math.Max(1, _target.GetAllocatedWidth());
        double height = Math.Max(1, _target.GetAllocatedHeight());
        double maxOffsetX = Math.Max(0.0, width * _zoom - width);
        double maxOffsetY = Math.Max(0.0, height * _zoom - height);

        double deltaX = args.OffsetX * _zoom;
        double deltaY = args.OffsetY * _zoom;

        _offsetX = Math.Clamp(_dragStartOffsetX - deltaX, 0.0, maxOffsetX);
        _offsetY = Math.Clamp(_dragStartOffsetY - deltaY, 0.0, maxOffsetY);
        ApplyTransform();
    }

    private bool HandleScroll(EventControllerScroll controller, EventControllerScroll.ScrollSignalArgs args)
    {
        double delta = Math.Abs(args.Dy) > Math.Abs(args.Dx) ? -args.Dy : -args.Dx;
        if (Math.Abs(delta) < double.Epsilon)
        {
            return false;
        }

        double factor = delta > 0 ? 1.1 : 0.9;
        ZoomAroundPointer(_zoom * factor);
        return true;
    }

    private void ZoomAroundPointer(double requestedZoom)
    {
        double width = Math.Max(1, _target.GetAllocatedWidth());
        double height = Math.Max(1, _target.GetAllocatedHeight());
        double pointerX = Math.Clamp(_lastPointerX, 0, width);
        double pointerY = Math.Clamp(_lastPointerY, 0, height);

        double oldZoom = _zoom;
        double newZoom = Math.Clamp(requestedZoom, _minZoom, _maxZoom);
        if (Math.Abs(oldZoom - newZoom) < 0.001)
        {
            return;
        }

        double contentX = (_offsetX + pointerX) / oldZoom;
        double contentY = (_offsetY + pointerY) / oldZoom;

        double newOffsetX = contentX * newZoom - pointerX;
        double newOffsetY = contentY * newZoom - pointerY;

        double maxOffsetX = Math.Max(0.0, width * newZoom - width);
        double maxOffsetY = Math.Max(0.0, height * newZoom - height);

        _zoom = newZoom;
        _offsetX = Math.Clamp(newOffsetX, 0.0, maxOffsetX);
        _offsetY = Math.Clamp(newOffsetY, 0.0, maxOffsetY);
        ApplyTransform();
    }

    private void ApplyTransform()
    {
        double zoom = _zoom;
        double width = Math.Max(1, _target.GetAllocatedWidth());
        double height = Math.Max(1, _target.GetAllocatedHeight());
        double maxOffsetX = Math.Max(0.0, width * zoom - width);
        double maxOffsetY = Math.Max(0.0, height * zoom - height);
        _offsetX = Math.Clamp(_offsetX, 0.0, maxOffsetX);
        _offsetY = Math.Clamp(_offsetY, 0.0, maxOffsetY);

        string css = $"* {{ transform-origin: 0 0; transform: translate({(-_offsetX).ToString(CultureInfo.InvariantCulture)}px, {(-_offsetY).ToString(CultureInfo.InvariantCulture)}px) scale({zoom.ToString(CultureInfo.InvariantCulture)}); }}";
        _provider.LoadFromData(css, css.Length);
    }
}
