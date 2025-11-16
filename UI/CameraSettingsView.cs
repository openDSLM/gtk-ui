using Gtk;

public sealed record CameraSettingsView(
    Box Root,
    Button CloseButton,
    ComboBoxText ResolutionCombo,
    Entry OutputDirectoryEntry,
    Button OutputDirectoryApplyButton,
    CheckButton GalleryColorToggle,
    SpinButton GalleryPageSizeSpin,
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
