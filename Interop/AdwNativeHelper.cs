using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

internal static class AdwNativeHelper
{
    private static readonly string[] KnownLibraryPaths =
    {
        "/lib/aarch64-linux-gnu/libadwaita-1.so.0",
        "/usr/lib/aarch64-linux-gnu/libadwaita-1.so.0",
        "/lib/arm-linux-gnueabihf/libadwaita-1.so.0",
        "/usr/lib/arm-linux-gnueabihf/libadwaita-1.so.0",
        "/lib/x86_64-linux-gnu/libadwaita-1.so.0",
        "/usr/lib/x86_64-linux-gnu/libadwaita-1.so.0",
        "/lib64/libadwaita-1.so.0",
        "/usr/lib64/libadwaita-1.so.0",
        "/usr/lib/libadwaita-1.so.0"
    };

    public static void EnsureLibAdwAlias()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string? sourceLibrary = FindSystemLibrary();
        if (string.IsNullOrEmpty(sourceLibrary) || !File.Exists(sourceLibrary))
        {
            return;
        }

        string baseDir = AppContext.BaseDirectory ?? Environment.CurrentDirectory;
        string runtimeIdentifier = RuntimeInformation.RuntimeIdentifier ?? "linux";
        string nativeDir = Path.Combine(baseDir, "runtimes", runtimeIdentifier, "native");
        Directory.CreateDirectory(nativeDir);

        string aliasPath = Path.Combine(nativeDir, "libAdw.so");
        if (File.Exists(aliasPath))
        {
            return;
        }

        try
        {
            File.Copy(sourceLibrary, aliasPath, overwrite: false);
        }
        catch (IOException)
        {
            // Another process may have created the alias concurrently; ignore.
        }
    }

    private static string? FindSystemLibrary()
    {
        foreach (var candidate in KnownLibraryPaths)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (var ldconfigPath in new[] { "ldconfig", "/sbin/ldconfig", "/usr/sbin/ldconfig" })
        {
            try
            {
                var processStartInfo = new ProcessStartInfo(ldconfigPath, "-p")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };

                using var process = Process.Start(processStartInfo);
                if (process is null)
                {
                    continue;
                }

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                using var reader = new StringReader(output);
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    if (!line.Contains("libadwaita-1.so.0", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int arrowIndex = line.IndexOf("=>", StringComparison.Ordinal);
                    if (arrowIndex < 0)
                    {
                        continue;
                    }

                    string candidate = line[(arrowIndex + 2)..].Trim();
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch
            {
                // ldconfig may be unavailable at this path; try the next option.
            }
        }

        return null;
    }
}
