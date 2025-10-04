public readonly record struct ToggleAutoExposurePayload(bool Enabled);
public readonly record struct SelectIndexPayload(int Index);
public readonly record struct AdjustZoomPayload(double Zoom);
public readonly record struct AdjustPanPayload(double X, double Y);
public readonly record struct UpdateOutputDirectoryPayload(string Path);
