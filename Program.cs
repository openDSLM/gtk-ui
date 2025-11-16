using System;
using Adw;
using Gtk;
using Gst;

/// <summary>
/// Application entry point that wires up GTK/Adwaita and launches <see cref="CameraApp"/>.
/// </summary>
public static class Program
{
    /// <summary>
    /// Initializes libraries, parses CLI arguments, and starts the camera UI loop.
    /// </summary>
    public static int Main(string[] args)
    {
        Environment.SetEnvironmentVariable("GTK_A11Y", "none");
        Environment.SetEnvironmentVariable("NO_AT_BRIDGE", "1");
        Environment.SetEnvironmentVariable("LIBCAMERA_LOG_LEVELS", "*:2");

        GirNativeResolver.RegisterFor(
            typeof(Gst.Application).Assembly,
            typeof(GstVideo.VideoInfo).Assembly
        );
        Gst.Application.Init();
        AdwNativeHelper.EnsureLibAdwAlias();
        Adw.Functions.Init();

        bool fullscreen = false;
        var remainingArgs = new System.Collections.Generic.List<string>(args.Length);
        foreach (var arg in args)
        {
            if (arg == "--fullscreen")
            {
                fullscreen = true;
                continue;
            }

            remainingArgs.Add(arg);
        }

        using var app = new CameraApp(new CameraAppOptions(fullscreen));
        return app.Run(remainingArgs.ToArray());
    }
}
