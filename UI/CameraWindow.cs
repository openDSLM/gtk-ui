using Gtk;

public sealed record PhotoControlsView(
    Widget Root,
    CheckButton AutoToggle,
    ComboBoxText IsoBox,
    ComboBoxText ShutterBox,
    Button CaptureButton);

public sealed record VideoControlsView(
    Widget Root,
    ComboBoxText FpsCombo,
    SpinButton ShutterAngleSpin,
    Button RecordButton,
    Label StatusLabel);

public sealed record TimelapseControlsView(
    Widget Root,
    SpinButton IntervalSpin,
    SpinButton FrameCountSpin,
    Button StartButton,
    Label StatusLabel);

public sealed record CameraWindow(
    ApplicationWindow Window,
    Picture Picture,
    Label Hud,
    Button SettingsButton,
    Button GalleryButton,
    ToggleButton ModeMenuToggle,
    ToggleButton ModePhotoToggle,
    ToggleButton ModeVideoToggle,
    ToggleButton ModeTimelapseToggle,
    Revealer ModeRevealer,
    Stack ModeStack,
    PhotoControlsView PhotoControls,
    VideoControlsView VideoControls,
    TimelapseControlsView TimelapseControls,
    Stack PageStack,
    Widget LivePage,
    CameraSettingsView SettingsView,
    Widget GalleryPage,
    GalleryView GalleryView
);
