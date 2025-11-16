public sealed record MetadataOverrides(
    string? Make,
    string? Model,
    string? UniqueModel,
    string? Software,
    string? Artist,
    string? Copyright)
{
    public static readonly MetadataOverrides Empty = new(null, null, null, null, null, null);

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Make) &&
        string.IsNullOrWhiteSpace(Model) &&
        string.IsNullOrWhiteSpace(UniqueModel) &&
        string.IsNullOrWhiteSpace(Software) &&
        string.IsNullOrWhiteSpace(Artist) &&
        string.IsNullOrWhiteSpace(Copyright);
}

public sealed record CameraMetadataSnapshot(
    string? MakeOverride,
    string? ModelOverride,
    string? UniqueModelOverride,
    string? SoftwareOverride,
    string? ArtistOverride,
    string? CopyrightOverride,
    string? EffectiveMake,
    string? EffectiveModel,
    string? EffectiveUniqueModel,
    string? EffectiveSoftware,
    string? EffectiveArtist,
    string? EffectiveCopyright)
{
    public static readonly CameraMetadataSnapshot Empty = new(
        null, null, null, null, null, null,
        null, null, null, null, null, null);
}
