using System;
using Adw;
using Gtk;

/// <summary>
/// Coordinates app state, Gtk application lifetime, and controller/view wiring.
/// </summary>
public sealed class CameraApp : IDisposable
{
    private readonly Adw.Application _application;
    private readonly CameraState _state;
    private readonly ActionDispatcher _dispatcher;
    private readonly CameraController _controller;
    private readonly CameraAppOptions _options;
    private CameraWindow? _window;

    /// <summary>
    /// Creates a new camera UI host.
    /// </summary>
    public CameraApp(CameraAppOptions options)
    {
        _options = options;
        _application = Adw.Application.New("ui.opendslm.main", Gio.ApplicationFlags.FlagsNone);
        _state = new CameraState();
        _dispatcher = new ActionDispatcher();
        _controller = new CameraController(_state, _dispatcher);

        _application.OnActivate += (_, __) => OnActivate();
    }

    public int Run(string[] args)
    {
        return _application.Run(args.Length, args);
    }

    private void OnActivate()
    {
        IconThemeHelper.EnsureCustomIcons();

        var builder = new MainWindowBuilder(_state, _dispatcher, _controller);
        _window = builder.Build(_application, _options.Fullscreen);
        _controller.AttachView(_window.Picture, _window.Hud);
        if (_options.Fullscreen)
        {
            _window.Window.SetDecorated(false);
            _window.Window.Fullscreen();
        }
        _window.Window.Present();

        _dispatcher.FireAndForget(AppActionId.InitializePreview);
    }

    public void Dispose()
    {
        _controller.Dispose();
        _application.Dispose();
    }
}
