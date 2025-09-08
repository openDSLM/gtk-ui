// .NET 8 + GirCore 0.6.x
// Live preview via gtk4paintablesink + overlay "Capture DNG" button.
// NuGet: GirCore.Gtk-4.0, GirCore.Gdk-4.0, GirCore.GLib-2.0, GirCore.Gst-1.0, GirCore.GstVideo-1.0
// OS: gstreamer1.0-gtk4, plus libcamera-still or rpicam-still.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using SysTask = System.Threading.Tasks.Task; // avoid clash with Gst.Task
using Gtk;
using Gdk;
using Gst;
using GObject; // for Value
using GLib;

// -------- Minimal native resolver so GirCore maps DllImport names -> system sonames --------
// IMPORTANT: Register ONLY for GStreamer assemblies. GirCore sets its own resolvers for Gtk/GLib.
static class GirNativeResolver
{
    static readonly Dictionary<string, string> Map = new()
    {
        // GStreamer
        ["Gst"]        = "libgstreamer-1.0.so.0",
        ["GstBase"]    = "libgstbase-1.0.so.0",
        ["GstVideo"]   = "libgstvideo-1.0.so.0",
        ["GstApp"]     = "libgstapp-1.0.so.0",
        ["GstPbutils"] = "libgstpbutils-1.0.so.0",
    };

    static IntPtr Resolve(string name, Assembly asm, DllImportSearchPath? path)
        => Map.TryGetValue(name, out var real) ? NativeLibrary.Load(real) : IntPtr.Zero;

    public static void RegisterFor(params Assembly[] assemblies)
    {
        foreach (var a in assemblies)
            NativeLibrary.SetDllImportResolver(a, Resolve);
    }
}
// ------------------------------------------------------------------------------------------

public static class Program
{
    public static int Main(string[] args)
    {
        // Register resolver ONLY for GStreamer assemblies (avoid Gtk/GLib to prevent conflicts)
        GirNativeResolver.RegisterFor(
            typeof(Gst.Application).Assembly,    // Gst core
            typeof(GstVideo.VideoInfo).Assembly  // GstVideo
        );

        // Init GStreamer
        Gst.Application.Init();

        // GTK application (Gio flags)
        var app = Gtk.Application.New("dev.poc.gtk4.libcamera", Gio.ApplicationFlags.FlagsNone);
        app.OnActivate += (_, __) => OnActivate(app, args);
        return app.Run(args.Length, args);
    }

    private static void OnActivate(Gtk.Application app, string[] args)
    {
        var win = ApplicationWindow.New(app);
        win.Title = "GTK4 Live Preview + DNG";
        win.SetDefaultSize(1280, 720);

        var overlay = Overlay.New();
        var picture = Picture.New();  // we’ll set a Gdk.Paintable from gtk4paintablesink
        overlay.SetChild(picture);

        var btn = Button.NewWithLabel("📷 Capture DNG");
        btn.MarginTop = 12; btn.MarginEnd = 12;
        btn.Halign = Align.End; btn.Valign = Align.Start;
        btn.OnClicked += async (_, __) =>
        {
            btn.Sensitive = false;
            try { await CaptureDngAsync(); }
            finally { btn.Sensitive = true; }
        };
        overlay.AddOverlay(btn);

        win.SetChild(overlay);
        win.Present();

        // ---- Build pipeline: src -> videoconvert -> gtk4paintablesink
        bool haveLibcamera = ElementFactory.Find("libcamerasrc") is not null;

        var src = ElementFactory.Make(haveLibcamera ? "libcamerasrc" : "v4l2src", "src")
                  ?? throw new Exception("Failed to create source");
        var convert = ElementFactory.Make("videoconvert", "vc")
                      ?? throw new Exception("Failed to create videoconvert");
        var sink = ElementFactory.Make("gtk4paintablesink", "psink")
                   ?? throw new Exception("gtk4paintablesink not available (install gstreamer1.0-gtk4)");

        var pipeline = Pipeline.New("p");
        pipeline.Add(src);
        pipeline.Add(convert);
        pipeline.Add(sink);

        if (!src.Link(convert)) throw new Exception("Link src->convert failed");
        if (!convert.Link(sink)) throw new Exception("Link convert->sink failed");

        // Hook the sink's Gdk.Paintable into the Picture
        void TryAssignPaintable()
        {
            Value v = new Value();                 // GObject.Value container
            sink.GetProperty("paintable", v);      // NOTE: pass 'v' (no ref/out)
            var obj = v.GetObject();               // unwrap to GObject.Object
            if (obj is Paintable p)
                picture.SetPaintable(p);
            v.Unset();                              // tidy up the GValue
        }

        TryAssignPaintable();

        if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
            throw new Exception("Pipeline failed to start");

        // Retry once after PLAYING (property often becomes non-null only then)
        GLib.Functions.IdleAdd(0, () => { TryAssignPaintable(); return false; });

        win.OnCloseRequest += (_, __) =>
        {
            pipeline.SetState(State.Null);
            return false;
        };
    }

    // Try libcamera-still first; fall back to rpicam-still (Bookworm)
    private static async SysTask CaptureDngAsync()
    {
        string outfile = $"/tmp/capture_{DateTimeOffset.Now.ToUnixTimeSeconds()}.dng";
        (string cmd, string args) = HasCmd("libcamera-still")
            ? ("libcamera-still", $"-r -o {outfile} --immediate --timeout 1")
            : ("rpicam-still", $"--raw -o {outfile} -t 100");

        var psi = new ProcessStartInfo
        {
            FileName = "bash",
            ArgumentList = { "-lc", $"{cmd} {args}" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        var p = Process.Start(psi)!;
        string so = await p.StandardOutput.ReadToEndAsync();
        string se = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        Console.WriteLine($"{cmd} exit {p.ExitCode}\nSTDOUT:\n{so}\nSTDERR:\n{se}");
    }

    private static bool HasCmd(string name)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "bash",
                ArgumentList = { "-lc", $"command -v {name}" },
                RedirectStandardOutput = true,
                UseShellExecute = false
            });
            p!.WaitForExit();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
