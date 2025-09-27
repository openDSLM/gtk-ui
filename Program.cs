using System;
using Gtk;
using Gst;

public static class Program
{
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

        using var app = new CameraApp();
        return app.Run(args);
    }
}
