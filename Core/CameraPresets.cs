public static class CameraPresets
{
    public static readonly double[] ShutterSteps =
    {
        1.0/8000, 1.0/4000, 1.0/2000, 1.0/1000, 1.0/500, 1.0/250, 1.0/125, 1.0/60, 1.0/50, 1.0/30,
        1.0/25, 1.0/20, 1.0/15, 1.0/10, 1.0/8, 1.0/6, 1.0/5, 1.0/4, 1.0/3, 1.0/2, 1.0,
        2, 4, 8, 15, 30
    };

    public static readonly int[] IsoSteps = { 100, 200, 400, 800, 1600, 3200, 6400, 12800 };

    public static readonly (string Label, int Width, int Height)[] ResolutionOptions =
    {
        ("1928 x 1090 (2K crop)", 1928, 1090),
        ("3856 x 2180 (4K full)", 3856, 2180)
    };
}
