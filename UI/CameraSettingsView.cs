using Gtk;

/// <summary>
/// Aggregates widgets backing the settings stack so they can be wired to state/actions.
/// </summary>
public sealed record CameraSettingsView(
    Box Root,
    Button CloseButton,
    ComboBoxText ResolutionCombo,
    Entry OutputDirectoryEntry,
    Button OutputDirectoryApplyButton,
    CheckButton GalleryColorToggle,
    Button GalleryRowsDecreaseButton,
    Label GalleryRowsValueLabel,
    Button GalleryRowsIncreaseButton,
    Label InfoVersionLabel,
    Button DebugExitButton,
    Entry MetadataMakeEntry,
    Entry MetadataModelEntry,
    Entry MetadataUniqueEntry,
    Entry MetadataSoftwareEntry,
    Entry MetadataArtistEntry,
    Entry MetadataCopyrightEntry,
    Button MetadataApplyButton
);
