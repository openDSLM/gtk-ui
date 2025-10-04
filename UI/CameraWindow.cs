using Gtk;

public sealed record CameraWindow(
    ApplicationWindow Window,
    Picture Picture,
    Label Hud,
    ComboBoxText IsoBox,
    ComboBoxText ShutterBox,
    ComboBoxText ResolutionBox,
    CheckButton AutoToggle,
    Scale ZoomScale,
    Scale PanXScale,
    Scale PanYScale,
    Button CaptureButton,
    Button SettingsButton,
    Button SettingsCloseButton,
    Stack PageStack,
    Widget LivePage,
    Widget SettingsPage
);
