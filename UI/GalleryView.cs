using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gtk;
using Gdk;
using GdkPixbuf;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using ImageMagick;

/// <summary>
/// Manages gallery pagination, thumbnail generation, and full-screen previewing.
/// </summary>
public sealed class GalleryView
{
    private const int ThumbnailWidth = 240;
    private const int ThumbnailHeight = 160;
    private const int FullMaxDimension = 4096;

    private readonly Widget _header;
    private readonly Widget _rowsControl;
    private readonly Stack _stack;
    private readonly Widget _emptyPage;
    private readonly Widget _gridPage;
    private readonly Widget _viewerPage;
    private readonly FlowBox _flowBox;
    private readonly Button _viewerBackButton;
    private readonly Button _viewerLiveButton;
    private readonly Button _prevButton;
    private readonly Button _nextButton;
    private readonly Label _pageLabel;
    private readonly Picture _fullPicture;
    private readonly Label _fullLabel;
    private readonly ZoomPanController _viewerZoom;
    private readonly Widget _footer;

    private readonly List<ThumbnailEntry> _thumbnails = new();
    private readonly Dictionary<FlowBoxChild, string> _pathsByChild = new();

    private Texture? _currentFullTexture;
    private bool _colorEnabled = true;

    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dng",
        ".nef",
        ".cr2",
        ".cr3",
        ".arw",
        ".raf",
        ".rw2",
        ".orf",
        ".srw",
        ".pef",
        ".raw"
    };

    public GalleryView(
        Widget root,
        Widget header,
        Widget rowsControl,
        Widget footer,
        Button backButton,
        Stack stack,
        Widget emptyPage,
        Widget gridPage,
        FlowBox flowBox,
        Widget viewerPage,
        Button viewerBackButton,
        Button viewerLiveButton,
        Button prevButton,
        Button nextButton,
        Label pageLabel,
        Picture fullPicture,
        Label fullLabel)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        _header = header ?? throw new ArgumentNullException(nameof(header));
        _rowsControl = rowsControl ?? throw new ArgumentNullException(nameof(rowsControl));
        _footer = footer ?? throw new ArgumentNullException(nameof(footer));
        BackButton = backButton ?? throw new ArgumentNullException(nameof(backButton));
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        _emptyPage = emptyPage ?? throw new ArgumentNullException(nameof(emptyPage));
        _gridPage = gridPage ?? throw new ArgumentNullException(nameof(gridPage));
        _flowBox = flowBox ?? throw new ArgumentNullException(nameof(flowBox));
        _viewerPage = viewerPage ?? throw new ArgumentNullException(nameof(viewerPage));
        _viewerBackButton = viewerBackButton ?? throw new ArgumentNullException(nameof(viewerBackButton));
        _viewerLiveButton = viewerLiveButton ?? throw new ArgumentNullException(nameof(viewerLiveButton));
        _prevButton = prevButton ?? throw new ArgumentNullException(nameof(prevButton));
        _nextButton = nextButton ?? throw new ArgumentNullException(nameof(nextButton));
        _pageLabel = pageLabel ?? throw new ArgumentNullException(nameof(pageLabel));
        _fullPicture = fullPicture ?? throw new ArgumentNullException(nameof(fullPicture));
        _fullLabel = fullLabel ?? throw new ArgumentNullException(nameof(fullLabel));
        _viewerZoom = new ZoomPanController(_fullPicture);

        _flowBox.OnChildActivated += OnThumbnailActivated;

        BackButton.OnClicked += (_, __) =>
        {
            EnsureGridVisible();
            BackRequested?.Invoke(this, EventArgs.Empty);
        };

        _viewerBackButton.OnClicked += (_, __) => ShowGrid();
        _viewerLiveButton.OnClicked += (_, __) =>
        {
            EnsureGridVisible();
            BackRequested?.Invoke(this, EventArgs.Empty);
        };
        _prevButton.OnClicked += (_, __) => PageRequested?.Invoke(this, -1);
        _nextButton.OnClicked += (_, __) => PageRequested?.Invoke(this, +1);
        _prevButton.Sensitive = false;
        _nextButton.Sensitive = false;
        _pageLabel.SetText("No photos");
        SetChromeVisible(true);
        SetGridRows(2);
    }

    public Widget Root { get; }

    public Button BackButton { get; }

    public event EventHandler? BackRequested;
    public event EventHandler<int>? PageRequested;

    public void UpdateItems(IReadOnlyList<string> paths, bool colorEnabled)
    {
        _colorEnabled = colorEnabled;
        ClearThumbnails();

        if (paths == null || paths.Count == 0)
        {
            ShowEmpty();
            return;
        }

        foreach (var path in paths)
        {
            var entry = CreateThumbnail(path, colorEnabled);
            if (entry == null)
            {
                continue;
            }

            _thumbnails.Add(entry);
            _pathsByChild[entry.Child] = entry.Path;
            _flowBox.Append(entry.Child);
        }

        _flowBox.UnselectAll();

        if (_thumbnails.Count == 0)
        {
            ShowEmpty();
        }
        else
        {
            ShowGrid();
        }
    }

    public void EnsureGridVisible()
    {
        if (_thumbnails.Count == 0)
        {
            ShowEmpty();
            return;
        }

        if (_stack.VisibleChild != _gridPage)
        {
            ShowGrid();
        }
    }

    private void OnThumbnailActivated(FlowBox sender, FlowBox.ChildActivatedSignalArgs args)
    {
        var child = args.Child;
        if (child is null)
        {
            return;
        }

        if (_pathsByChild.TryGetValue(child, out var path))
        {
            ShowViewer(path);
        }
    }

    private void ShowViewer(string path)
    {
        SetChromeVisible(false);
        var texture = TryLoadTexture(path, out var errorMessage, true, FullMaxDimension, FullMaxDimension);
        if (texture != null)
        {
            SetFullTexture(texture);
            _fullLabel.SetText(Path.GetFileName(path));
            _viewerZoom.Reset();
        }
        else
        {
            SetFullTexture(null);
            string fileName = Path.GetFileName(path);
            string message = string.IsNullOrEmpty(fileName)
                ? errorMessage ?? "Preview unavailable"
                : $"{fileName} – {errorMessage ?? "Preview unavailable"}";
            _fullLabel.SetText(message);
        }

        _stack.SetVisibleChild(_viewerPage);
    }

    private void ShowGrid()
    {
        _viewerZoom.Reset();
        SetChromeVisible(true);
        if (_thumbnails.Count == 0)
        {
            ShowEmpty();
            return;
        }

        SetFullTexture(null);
        _stack.SetVisibleChild(_gridPage);
    }

    private void ShowEmpty()
    {
        _viewerZoom.Reset();
        SetChromeVisible(true);
        SetFullTexture(null);
        _stack.SetVisibleChild(_emptyPage);
    }

    private void SetChromeVisible(bool visible)
    {
        if (_header is not null)
        {
            _header.Visible = visible;
        }
        if (_rowsControl is not null)
        {
            _rowsControl.Visible = visible;
        }
        if (_footer is not null)
        {
            _footer.Visible = visible;
        }
    }

    public void SetGridRows(int rows)
    {
        rows = Math.Clamp(rows, 2, 6);
        uint perLine = (uint)rows;
        _flowBox.MinChildrenPerLine = perLine;
        _flowBox.MaxChildrenPerLine = perLine;
    }

    private void ClearThumbnails()
    {
        foreach (var entry in _thumbnails)
        {
            _flowBox.Remove(entry.Child);
            entry.Dispose();
        }

        _thumbnails.Clear();
        _pathsByChild.Clear();
    }

    private ThumbnailEntry? CreateThumbnail(string path, bool colorEnabled)
    {
        string displayPath = path ?? string.Empty;
        string captionText = Path.GetFileName(displayPath);

        var child = FlowBoxChild.New();
        child.AddCssClass("gallery-thumb");
        child.Hexpand = true;
        child.Vexpand = true;
        child.Halign = Align.Fill;
        child.Valign = Align.Fill;
        child.SetSizeRequest(-1, -1);

        var container = Box.New(Orientation.Vertical, 0);
        container.Hexpand = true;
        container.Vexpand = true;
        container.Halign = Align.Fill;
        container.Valign = Align.Fill;
        container.SetSizeRequest(-1, -1);
        container.AddCssClass("gallery-thumb-body");

        var picture = new Picture
        {
            WidthRequest = ThumbnailWidth,
            HeightRequest = ThumbnailHeight,
            ContentFit = ContentFit.Cover
        };
        picture.SetSizeRequest(-1, -1);
        picture.Halign = Align.Fill;
        picture.Valign = Align.Fill;
        picture.CanShrink = true;
        picture.Vexpand = true;
        picture.Hexpand = true;
        picture.AddCssClass("gallery-thumb-picture");

        string? error;
        Texture? texture = TryLoadTexture(displayPath, out error, colorEnabled, ThumbnailWidth, ThumbnailHeight);
        if (texture != null)
        {
            picture.SetPaintable(texture);
        }
        else
        {
            picture.AddCssClass("gallery-thumb-picture-missing");
        }

        container.Append(picture);

        if (string.IsNullOrEmpty(captionText))
        {
            captionText = displayPath;
        }

        string tooltip = captionText ?? string.Empty;
        if (!string.IsNullOrEmpty(error))
        {
            tooltip = $"{tooltip}\n{error}";
        }

        if (!string.IsNullOrEmpty(tooltip))
        {
            child.SetTooltipText(tooltip);
        }

        child.SetChild(container);

        return new ThumbnailEntry(displayPath, child, picture, texture);
    }

    public void UpdatePagination(int currentPage, int totalPages)
    {
        if (totalPages <= 0)
        {
            _pageLabel.SetText("No photos");
            _prevButton.Sensitive = false;
            _nextButton.Sensitive = false;
            return;
        }

        _pageLabel.SetText($"Page {currentPage + 1} / {totalPages}");
        _prevButton.Sensitive = currentPage > 0;
        _nextButton.Sensitive = currentPage < totalPages - 1;
    }

    private static Texture? TryLoadTexture(string path, out string? error, bool colorEnabled, int? maxWidth = null, int? maxHeight = null)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "File not found";
            return null;
        }

        if (!System.IO.File.Exists(path))
        {
            error = "File not found";
            return null;
        }

        int? effectiveMaxWidth = maxWidth;
        int? effectiveMaxHeight = maxHeight;
        if (!effectiveMaxWidth.HasValue && !effectiveMaxHeight.HasValue)
        {
            effectiveMaxWidth = FullMaxDimension;
            effectiveMaxHeight = FullMaxDimension;
        }

        if (IsRawFile(path))
        {
            if (colorEnabled)
            {
                var magickTexture = TryLoadWithMagick(path, true, effectiveMaxWidth, effectiveMaxHeight);
                if (magickTexture != null)
                {
                    return magickTexture;
                }
            }

            var previewTexture = TryLoadEmbeddedPreviewTexture(path, effectiveMaxWidth, effectiveMaxHeight);
            if (previewTexture != null)
            {
                return previewTexture;
            }

            if (!colorEnabled)
            {
                // Fall back to a quick standard load to ensure thumbnails still display when color previews are disabled.
                var fallbackTexture = TryLoadStandardImage(path, effectiveMaxWidth, effectiveMaxHeight);
                if (fallbackTexture != null)
                {
                    return fallbackTexture;
                }
            }

            error = "Preview unavailable";
            return null;
        }

        var standardTexture = TryLoadStandardImage(path, effectiveMaxWidth, effectiveMaxHeight);
        if (standardTexture != null)
        {
            return standardTexture;
        }

        error = "Preview unavailable";
        return null;
    }

    private static bool IsRawFile(string path)
    {
        string extension = Path.GetExtension(path);
        return !string.IsNullOrEmpty(extension) && RawExtensions.Contains(extension);
    }

    private static Texture? TryLoadStandardImage(string path, int? maxWidth, int? maxHeight)
    {
        try
        {
            Pixbuf? pixbuf;
            if (maxWidth.HasValue || maxHeight.HasValue)
            {
                int width = maxWidth ?? -1;
                int height = maxHeight ?? -1;
                pixbuf = Pixbuf.NewFromFileAtScale(path, width, height, preserveAspectRatio: true);
            }
            else
            {
                pixbuf = Pixbuf.NewFromFile(path);
            }

            if (pixbuf == null)
            {
                return null;
            }

            return TextureFromPixbuf(pixbuf, maxWidth, maxHeight);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load image '{path}': {ex.Message}");
            return null;
        }
    }

    private static Texture? TryLoadEmbeddedPreviewTexture(string path, int? maxWidth, int? maxHeight)
    {
        try
        {
            byte[]? previewData = ExtractEmbeddedPreview(path);
            if (previewData == null || previewData.Length == 0)
            {
                return null;
            }

            string tempFile = Path.Combine(Path.GetTempPath(), $"opendslm-preview-{Guid.NewGuid():N}.jpg");
            try
            {
                System.IO.File.WriteAllBytes(tempFile, previewData);
                return TryLoadStandardImage(tempFile, maxWidth, maxHeight);
            }
            finally
            {
                try
                {
                    System.IO.File.Delete(tempFile);
                }
                catch
                {
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load embedded preview for '{path}': {ex.Message}");
            return null;
        }
    }

    private static Texture? TextureFromPixbuf(Pixbuf pixbuf, int? maxWidth, int? maxHeight)
    {
        Pixbuf? scaled = null;
        try
        {
            double scale = 1.0;
            if (maxWidth.HasValue && maxWidth.Value > 0)
            {
                scale = Math.Min(scale, maxWidth.Value / (double)pixbuf.Width);
            }
            if (maxHeight.HasValue && maxHeight.Value > 0)
            {
                scale = Math.Min(scale, maxHeight.Value / (double)pixbuf.Height);
            }

            if (scale < 1.0)
            {
                int width = Math.Max(1, (int)Math.Round(pixbuf.Width * scale));
                int height = Math.Max(1, (int)Math.Round(pixbuf.Height * scale));
                scaled = pixbuf.ScaleSimple(width, height, InterpType.Bilinear);
            }

            var source = scaled ?? pixbuf;
            return Texture.NewForPixbuf(source);
        }
        finally
        {
            scaled?.Dispose();
            pixbuf.Dispose();
        }
    }

    private static byte[]? ExtractEmbeddedPreview(string path)
    {
        try
        {
            var metadata = ImageMetadataReader.ReadMetadata(path);

            foreach (var directory in metadata.OfType<ExifThumbnailDirectory>())
            {
                if (directory.TryGetInt64(ExifThumbnailDirectory.TagThumbnailOffset, out long offset) &&
                    directory.TryGetInt32(ExifThumbnailDirectory.TagThumbnailLength, out int length) &&
                    offset > 0 && length > 0)
                {
                    try
                    {
                        return ReadFileSegment(path, offset, length);
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to extract embedded preview from '{path}': {ex.Message}");
            return null;
        }
    }

    private static byte[]? ReadFileSegment(string path, long offset, int length)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (offset > stream.Length) return null;

            stream.Seek(offset, SeekOrigin.Begin);
            int remaining = Math.Min(length, (int)Math.Max(0, stream.Length - offset));
            if (remaining <= 0) return null;

            using var ms = new MemoryStream(remaining);
            byte[] buffer = new byte[64 * 1024];
            while (remaining > 0)
            {
                int chunk = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                if (chunk <= 0) break;
                ms.Write(buffer, 0, chunk);
                remaining -= chunk;
            }

            return ms.Length > 0 ? ms.ToArray() : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to read thumbnail segment from '{path}': {ex.Message}");
            return null;
        }
    }

    private static Texture? TryLoadWithMagick(string path, bool color, int? maxWidth, int? maxHeight)
    {
        try
        {
            using var image = new MagickImage(path);
            image.AutoOrient();
            image.ColorSpace = color ? ColorSpace.sRGB : ColorSpace.Gray;
            if (!color)
            {
                image.ColorType = ColorType.Grayscale;
            }

            double scale = 1.0;
            if (maxWidth.HasValue && maxWidth.Value > 0 && image.Width > maxWidth.Value)
            {
                scale = Math.Min(scale, maxWidth.Value / (double)image.Width);
            }
            if (maxHeight.HasValue && maxHeight.Value > 0 && image.Height > maxHeight.Value)
            {
                scale = Math.Min(scale, maxHeight.Value / (double)image.Height);
            }
            if (!maxWidth.HasValue && !maxHeight.HasValue && (image.Width > FullMaxDimension || image.Height > FullMaxDimension))
            {
                scale = Math.Min(scale, Math.Min(FullMaxDimension / (double)image.Width, FullMaxDimension / (double)image.Height));
            }

            if (scale < 1.0)
            {
                int width = Math.Max(1, (int)Math.Round(image.Width * scale));
                int height = Math.Max(1, (int)Math.Round(image.Height * scale));
                image.Resize((uint)width, (uint)height);
            }

            string tempFile = Path.Combine(Path.GetTempPath(), $"opendslm-magick-{Guid.NewGuid():N}.jpg");
            try
            {
                image.Format = MagickFormat.Jpeg;
                if (!color)
                {
                    image.SetArtifact("jpeg:dct-method", "fast");
                    image.Quality = 60;
                }
                image.Write(tempFile);
                return TryLoadStandardImage(tempFile, maxWidth, maxHeight);
            }
            finally
            {
                try
                {
                    System.IO.File.Delete(tempFile);
                }
                catch
                {
                }
            }
        }
        catch (MagickMissingDelegateErrorException)
        {
            return null;
        }
        catch (MagickErrorException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Magick.NET failed to load '{path}': {ex.Message}");
            return null;
        }
    }

    private void SetFullTexture(Texture? texture)
    {
        if (_currentFullTexture != null)
        {
            _fullPicture.SetPaintable(null);
            _currentFullTexture.Dispose();
            _currentFullTexture = null;
        }

        if (texture != null)
        {
            _fullPicture.SetPaintable(texture);
            _currentFullTexture = texture;
        }
    }

    private sealed class ThumbnailEntry : IDisposable
    {
        public ThumbnailEntry(string path, FlowBoxChild child, Picture picture, Texture? texture)
        {
            Path = path;
            Child = child;
            Picture = picture;
            Texture = texture;
        }

        public string Path { get; }
        public FlowBoxChild Child { get; }
        public Picture Picture { get; }
        public Texture? Texture { get; }

        public void Dispose()
        {
            if (Texture != null)
            {
                Texture.Dispose();
            }

            Child.SetChild(null);
            Picture.Dispose();
            Child.Dispose();
        }
    }
}
