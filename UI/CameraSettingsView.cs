using Gtk;

public sealed record CameraSettingsView(
    Box Root,
    Button CloseButton,
    ComboBoxText ResolutionCombo,
    Entry OutputDirectoryEntry,
    Button OutputDirectoryApplyButton
);
