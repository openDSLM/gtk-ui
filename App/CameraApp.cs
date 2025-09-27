using System;
using Gtk;

public sealed class CameraApp : IDisposable
{
    private readonly Gtk.Application _application;
    private readonly CameraState _state;
    private readonly ActionDispatcher _dispatcher;
    private readonly CameraController _controller;
    private CameraWindow? _window;

    public CameraApp()
    {
        _application = Gtk.Application.New("dev.poc.gtk4.libcamera", Gio.ApplicationFlags.FlagsNone);
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
        var builder = new MainWindowBuilder(_state, _dispatcher, _controller);
        _window = builder.Build(_application);
        _controller.AttachView(_window.Picture, _window.Hud);
        _window.Window.Present();

        _dispatcher.FireAndForget(AppActionId.InitializePreview);
    }

    public void Dispose()
    {
        _controller.Dispose();
        _application.Dispose();
    }
}
