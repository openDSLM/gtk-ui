using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

public static class GirNativeResolver
{
    private static readonly Dictionary<string, string> Map = new()
    {
        ["Gst"]        = "libgstreamer-1.0.so.0",
        ["GstBase"]    = "libgstbase-1.0.so.0",
        ["GstVideo"]   = "libgstvideo-1.0.so.0",
        ["GstApp"]     = "libgstapp-1.0.so.0",
        ["GstPbutils"] = "libgstpbutils-1.0.so.0",
        ["Gdk"]        = "libgdk-4.so.1",
        ["Gsk"]        = "libgsk-4.so.1",
        ["GdkPixbuf"]  = "libgdk_pixbuf-2.0.so.0",
    };

    public static void RegisterFor(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            NativeLibrary.SetDllImportResolver(assembly, Resolve);
        }
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? path)
    {
        return Map.TryGetValue(libraryName, out var mapped)
            ? NativeLibrary.Load(mapped)
            : IntPtr.Zero;
    }
}
