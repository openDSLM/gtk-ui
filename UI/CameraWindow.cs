using Gtk;

/// <summary>
/// Exposes the capture controls assembled for the live view.
/// </summary>
public sealed record PhotoControlsView(
    Widget Root,
    CheckButton AutoToggle,
    ComboBoxText IsoBox,
    ComboBoxText ShutterBox,
    Button CaptureButton);

/// <summary>
/// Represents the root GTK widgets that make up the camera UI.
/// </summary>
public sealed record CameraWindow(
    ApplicationWindow Window,
    Picture Picture,
    Label Hud,
    Button SettingsButton,
    Button GalleryButton,
    PhotoControlsView PhotoControls,
    Stack PageStack,
    Widget LivePage,
    CameraSettingsView SettingsView,
    Widget GalleryPage,
    GalleryView GalleryView
);
