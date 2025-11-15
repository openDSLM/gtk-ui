using Gtk;

public sealed record PhotoControlsView(
    Widget Root,
    CheckButton AutoToggle,
    ComboBoxText IsoBox,
    ComboBoxText ShutterBox,
    Button CaptureButton);

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
