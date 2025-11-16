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
    Label MetadataMakeEffectiveLabel,
    Entry MetadataModelEntry,
    Label MetadataModelEffectiveLabel,
    Entry MetadataUniqueEntry,
    Label MetadataUniqueEffectiveLabel,
    Entry MetadataSoftwareEntry,
    Label MetadataSoftwareEffectiveLabel,
    Entry MetadataArtistEntry,
    Label MetadataArtistEffectiveLabel,
    Entry MetadataCopyrightEntry,
    Label MetadataCopyrightEffectiveLabel,
    Button MetadataApplyButton
);
