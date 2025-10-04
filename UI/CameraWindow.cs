using Gtk;

public sealed record CameraWindow(
    ApplicationWindow Window,
    Picture Picture,
    Label Hud,
    ComboBoxText IsoBox,
    ComboBoxText ShutterBox,
    CheckButton AutoToggle,
    Scale ZoomScale,
    Scale PanXScale,
    Scale PanYScale,
    Button CaptureButton,
    Button SettingsButton,
    Stack PageStack,
    Widget LivePage,
    CameraSettingsView SettingsView
);
