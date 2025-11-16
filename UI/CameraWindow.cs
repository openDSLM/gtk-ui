using Adw;
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
    Adw.ApplicationWindow Window,
    Picture Picture,
    Label Hud,
    PhotoControlsView PhotoControls,
    Adw.ViewStack PageStack,
    Widget LivePage,
    CameraSettingsView SettingsView,
    Widget GalleryPage,
    GalleryView GalleryView
);
