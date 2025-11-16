using System;
using System.IO;
using Gtk;

public sealed class MainWindowBuilder
{
    private const string MainLayoutFileName = "camera_main_window.ui";
    private const string LiveLayoutFileName = "camera_live_view.ui";
    private const string SettingsLayoutFileName = "camera_settings_page.ui";
    private const string GalleryLayoutFileName = "camera_gallery_page.ui";
    private const string PhotoControlsLayoutFileName = "mode_photo_controls.ui";

    private readonly CameraState _state;
    private readonly ActionDispatcher _dispatcher;
    private readonly CameraController _controller;

    private bool _suppressAutoToggle;
    private bool _suppressIsoChange;
    private bool _suppressShutterChange;
    private bool _suppressResolutionChange;
    private bool _suppressZoomChange;
    private bool _suppressPanChange;

    public MainWindowBuilder(CameraState state, ActionDispatcher dispatcher, CameraController controller)
    {
        _state = state;
        _dispatcher = dispatcher;
        _controller = controller;
    }

    public CameraWindow Build(Gtk.Application app)
    {
        using var mainBuilder = Builder.NewFromFile(ResolveLayoutPath(MainLayoutFileName));

        var window = Require<Gtk.ApplicationWindow>(mainBuilder, "main_window");
        window.SetApplication(app);
        window.AddCssClass("camera-window");
        var stack = Require<Stack>(mainBuilder, "page_stack");
        InstallHeaderBar(window);

        using var liveBuilder = Builder.NewFromFile(ResolveLayoutPath(LiveLayoutFileName));
        var liveOverlay = Require<Overlay>(liveBuilder, "live_overlay");
        var picture = Require<Picture>(liveBuilder, "live_picture");
        var hud = Require<Label>(liveBuilder, "hud_label");
        var settingsButton = Require<Button>(liveBuilder, "settings_button");
        var galleryButton = Require<Button>(liveBuilder, "gallery_button");
        var modeStack = Require<Stack>(liveBuilder, "mode_stack");

        using var photoBuilder = Builder.NewFromFile(ResolveLayoutPath(PhotoControlsLayoutFileName));
        var photoRoot = Require<Box>(photoBuilder, "photo_controls_box");
        var autoToggle = Require<CheckButton>(photoBuilder, "auto_toggle");
        var isoBox = Require<ComboBoxText>(photoBuilder, "iso_combo");
        var shutterBox = Require<ComboBoxText>(photoBuilder, "shutter_combo");
        var captureButton = Require<Button>(photoBuilder, "capture_button");

        modeStack.AddNamed(photoRoot, "photo");
        modeStack.SetVisibleChild(photoRoot);

        var photoView = new PhotoControlsView(photoRoot, autoToggle, isoBox, shutterBox, captureButton);
        ApplyButtonIcons(settingsButton, galleryButton, captureButton);

        using var settingsBuilder = Builder.NewFromFile(ResolveLayoutPath(SettingsLayoutFileName));
        var settingsRoot = Require<Box>(settingsBuilder, "settings_root");
        var settingsCloseButton = Require<Button>(settingsBuilder, "settings_close_button");
        var settingsResolutionCombo = Require<ComboBoxText>(settingsBuilder, "settings_resolution_combo");
        var outputDirEntry = Require<Entry>(settingsBuilder, "output_dir_entry");
        var outputDirApplyButton = Require<Button>(settingsBuilder, "output_dir_apply_button");
        var galleryColorToggle = Require<CheckButton>(settingsBuilder, "gallery_color_toggle");
        var galleryPageSizeSpin = Require<SpinButton>(settingsBuilder, "gallery_page_size_spin");
        var metadataMakeEntry = Require<Entry>(settingsBuilder, "metadata_make_entry");
        var metadataMakeEffectiveLabel = Require<Label>(settingsBuilder, "metadata_make_effective_label");
        var metadataModelEntry = Require<Entry>(settingsBuilder, "metadata_model_entry");
        var metadataModelEffectiveLabel = Require<Label>(settingsBuilder, "metadata_model_effective_label");
        var metadataUniqueEntry = Require<Entry>(settingsBuilder, "metadata_unique_entry");
        var metadataUniqueEffectiveLabel = Require<Label>(settingsBuilder, "metadata_unique_effective_label");
        var metadataSoftwareEntry = Require<Entry>(settingsBuilder, "metadata_software_entry");
        var metadataSoftwareEffectiveLabel = Require<Label>(settingsBuilder, "metadata_software_effective_label");
        var metadataArtistEntry = Require<Entry>(settingsBuilder, "metadata_artist_entry");
        var metadataArtistEffectiveLabel = Require<Label>(settingsBuilder, "metadata_artist_effective_label");
        var metadataCopyrightEntry = Require<Entry>(settingsBuilder, "metadata_copyright_entry");
        var metadataCopyrightEffectiveLabel = Require<Label>(settingsBuilder, "metadata_copyright_effective_label");
        var metadataApplyButton = Require<Button>(settingsBuilder, "metadata_apply_button");

        var settingsView = new CameraSettingsView(
            settingsRoot,
            settingsCloseButton,
            settingsResolutionCombo,
            outputDirEntry,
            outputDirApplyButton,
            galleryColorToggle,
            galleryPageSizeSpin,
            metadataMakeEntry,
            metadataMakeEffectiveLabel,
            metadataModelEntry,
            metadataModelEffectiveLabel,
            metadataUniqueEntry,
            metadataUniqueEffectiveLabel,
            metadataSoftwareEntry,
            metadataSoftwareEffectiveLabel,
            metadataArtistEntry,
            metadataArtistEffectiveLabel,
            metadataCopyrightEntry,
            metadataCopyrightEffectiveLabel,
            metadataApplyButton);

        using var galleryBuilder = Builder.NewFromFile(ResolveLayoutPath(GalleryLayoutFileName));
        var galleryRoot = Require<Box>(galleryBuilder, "gallery_root");
        var galleryBackButton = Require<Button>(galleryBuilder, "gallery_back_button");
        var galleryStack = Require<Stack>(galleryBuilder, "gallery_stack");
        var galleryEmptyBox = Require<Box>(galleryBuilder, "gallery_empty_box");
        var galleryScroller = Require<ScrolledWindow>(galleryBuilder, "gallery_scroller");
        var galleryFlow = Require<FlowBox>(galleryBuilder, "gallery_flow");
        var galleryViewerBox = Require<Box>(galleryBuilder, "gallery_viewer_box");
        var galleryViewerBackButton = Require<Button>(galleryBuilder, "gallery_viewer_back_button");
        var galleryPrevButton = Require<Button>(galleryBuilder, "gallery_prev_button");
        var galleryNextButton = Require<Button>(galleryBuilder, "gallery_next_button");
        var galleryPageLabel = Require<Label>(galleryBuilder, "gallery_page_label");
        var galleryFullPicture = Require<Picture>(galleryBuilder, "gallery_full_picture");
        var galleryFullLabel = Require<Label>(galleryBuilder, "gallery_full_label");

        var galleryView = new GalleryView(
            galleryRoot,
            galleryBackButton,
            galleryStack,
            galleryEmptyBox,
            galleryScroller,
            galleryFlow,
            galleryViewerBox,
            galleryViewerBackButton,
            galleryPrevButton,
            galleryNextButton,
            galleryPageLabel,
            galleryFullPicture,
            galleryFullLabel);

        ConfigureAutoToggle(photoView.AutoToggle);
        ConfigureIsoCombo(photoView.IsoBox);
        ConfigureShutterCombo(photoView.ShutterBox);
        ConfigureCaptureButton(photoView.CaptureButton);
        ConfigureResolutionCombo(settingsView.ResolutionCombo);
        ConfigureSettingsPanel(settingsView);
        ConfigureGallerySettings(settingsView);
        ConfigureMetadataSettings(settingsView);
        ConfigureNavigation(stack, liveOverlay, settingsView.Root, galleryView.Root, settingsButton, settingsView.CloseButton, galleryButton, galleryView);

        StyleInstaller.TryInstall();

        BindStateToControls(photoView, settingsView.ResolutionCombo, settingsView);
        BindGallery(galleryView);

        return new CameraWindow(
            window,
            picture,
            hud,
            settingsButton,
            galleryButton,
            photoView,
            stack,
            liveOverlay,
            settingsView,
            galleryView.Root,
            galleryView);
    }

    private static void InstallHeaderBar(Gtk.ApplicationWindow window)
    {
        var headerBar = Gtk.HeaderBar.New();
        headerBar.ShowTitleButtons = true;
        var titleLabel = Gtk.Label.New("openDSLM – Live Preview + RAW");
        headerBar.SetTitleWidget(titleLabel);

        window.SetTitlebar(headerBar);
    }

    private void ApplyButtonIcons(Button settingsButton, Button galleryButton, Button captureButton)
    {
        SetButtonIcon(settingsButton, "settings-symbolic.svg");
        SetButtonIcon(galleryButton, "gallery-symbolic.svg");
        SetCaptureButtonIcon(captureButton, "capture-symbolic.svg");
    }

    private static void SetButtonIcon(Button button, string iconFile)
    {
        var path = ResolveIconPath(iconFile);
        if (path is null)
        {
            return;
        }

        var picture = Picture.NewForFilename(path);
        picture.CanShrink = true;
        picture.WidthRequest = 32;
        picture.HeightRequest = 32;
        picture.AddCssClass("icon-image");
        button.SetChild(picture);
    }

    private static void SetCaptureButtonIcon(Button button, string iconFile)
    {
        var path = ResolveIconPath(iconFile);
        if (path is null)
        {
            return;
        }

        var picture = Picture.NewForFilename(path);
        picture.CanShrink = true;
        picture.WidthRequest = 20;
        picture.HeightRequest = 20;
        picture.AddCssClass("icon-image");

        var label = Gtk.Label.New("CAPTURE");
        label.AddCssClass("capture-label");

        var box = Gtk.Box.New(Orientation.Horizontal, 6);
        box.Append(picture);
        box.Append(label);

        button.SetChild(box);
    }

    private static string? ResolveIconPath(string fileName)
    {
        string? baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDir))
        {
            string candidate = Path.Combine(baseDir, "Resources", "icons", "hicolor", "scalable", "actions", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        string fallback = Path.Combine(Environment.CurrentDirectory, "Resources", "icons", "hicolor", "scalable", "actions", fileName);
        return File.Exists(fallback) ? fallback : null;
    }

    private static T Require<T>(Builder builder, string id) where T : class
    {
        if (builder.GetObject(id) is T instance)
        {
            return instance;
        }

        throw new InvalidOperationException($"The UI template is missing an object with id '{id}' of type {typeof(T).Name}.");
    }

    private static string ResolveLayoutPath(string fileName)
    {
        string baseDir = AppContext.BaseDirectory ?? string.Empty;
        string candidate = Path.Combine(baseDir, "Resources", "ui", fileName);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        string fallback = Path.Combine(Environment.CurrentDirectory, "Resources", "ui", fileName);
        if (File.Exists(fallback))
        {
            return fallback;
        }

        throw new FileNotFoundException($"Unable to locate {fileName}. Ensure it is copied alongside the application binaries.");
    }

    private void ConfigureAutoToggle(CheckButton autoToggle)
    {
        autoToggle.Active = _state.AutoExposureEnabled;
        autoToggle.OnToggled += (_, __) =>
        {
            if (_suppressAutoToggle) return;
            _dispatcher.FireAndForget(AppActionId.ToggleAutoExposure, new ToggleAutoExposurePayload(autoToggle.Active));
        };
    }

    private void ConfigureResolutionCombo(ComboBoxText resBox)
    {
        resBox.RemoveAll();
        for (int i = 0; i < CameraPresets.ResolutionOptions.Length; i++)
        {
            resBox.AppendText(CameraPresets.ResolutionOptions[i].Label);
        }
        resBox.Active = _state.ResolutionIndex;

        bool updating = false;
        resBox.OnChanged += async (_, __) =>
        {
            if (updating || _suppressResolutionChange) return;
            updating = true;
            try
            {
                await _dispatcher.DispatchAsync(AppActionId.SelectResolution, new SelectIndexPayload(resBox.Active));
            }
            finally
            {
                updating = false;
            }
        };
    }

    private void ConfigureIsoCombo(ComboBoxText isoBox)
    {
        isoBox.RemoveAll();
        foreach (var iso in CameraPresets.IsoSteps)
        {
            isoBox.AppendText(iso.ToString());
        }
        isoBox.Active = _state.IsoIndex;
        isoBox.Sensitive = !_state.AutoExposureEnabled;

        bool updating = false;
        isoBox.OnChanged += async (_, __) =>
        {
            if (updating || _suppressIsoChange) return;
            if (_state.AutoExposureEnabled) return;
            updating = true;
            try
            {
                await _dispatcher.DispatchAsync(AppActionId.SelectIso, new SelectIndexPayload(isoBox.Active));
            }
            finally
            {
                updating = false;
            }
        };
    }

    private void ConfigureShutterCombo(ComboBoxText shutterBox)
    {
        shutterBox.RemoveAll();
        foreach (var sec in CameraPresets.ShutterSteps)
        {
            shutterBox.AppendText(CameraController.ShutterLabel(sec));
        }
        shutterBox.Active = _state.ShutterIndex;
        shutterBox.Sensitive = !_state.AutoExposureEnabled;

        bool updating = false;
        shutterBox.OnChanged += async (_, __) =>
        {
            if (updating || _suppressShutterChange) return;
            if (_state.AutoExposureEnabled) return;
            updating = true;
            try
            {
                await _dispatcher.DispatchAsync(AppActionId.SelectShutter, new SelectIndexPayload(shutterBox.Active));
            }
            finally
            {
                updating = false;
            }
        };
    }

    private void ConfigureCaptureButton(Button captureButton)
    {
        captureButton.OnClicked += async (_, __) =>
        {
            captureButton.Sensitive = false;
            try
            {
                await _dispatcher.DispatchAsync(AppActionId.CaptureStill);
            }
            finally
            {
                captureButton.Sensitive = true;
            }
        };
    }

    private void ConfigureSettingsPanel(CameraSettingsView settingsView)
    {
        SetEntryText(settingsView.OutputDirectoryEntry, _state.OutputDirectory);

        async System.Threading.Tasks.Task CommitAsync()
        {
            string path = GetEntryText(settingsView.OutputDirectoryEntry).Trim();
            if (string.Equals(path, _state.OutputDirectory, StringComparison.Ordinal))
                return;

            try
            {
                await _dispatcher.DispatchAsync(AppActionId.UpdateOutputDirectory,
                    new UpdateOutputDirectoryPayload(path));
            }
            finally
            {
                // Refresh entry in case the daemon adjusted the value.
                SetEntryText(settingsView.OutputDirectoryEntry, _state.OutputDirectory);
            }
        }

        settingsView.OutputDirectoryEntry.OnActivate += async (_, __) => await CommitAsync();
        settingsView.OutputDirectoryApplyButton.OnClicked += async (_, __) => await CommitAsync();
    }

    private void ConfigureGallerySettings(CameraSettingsView settingsView)
    {
        var colorToggle = settingsView.GalleryColorToggle;
        var pageSizeSpin = settingsView.GalleryPageSizeSpin;
        ArgumentNullException.ThrowIfNull(colorToggle);
        ArgumentNullException.ThrowIfNull(pageSizeSpin);

        bool suppressToggle = false;
        bool suppressPageSize = false;

        colorToggle.Active = _state.GalleryColorEnabled;
        var pageSizeAdjustment = pageSizeSpin.Adjustment;
        ArgumentNullException.ThrowIfNull(pageSizeAdjustment);
        pageSizeAdjustment.Lower = 1;
        pageSizeAdjustment.Upper = 48;
        pageSizeSpin.Value = _state.GalleryPageSize;

        colorToggle.OnToggled += (_, __) =>
        {
            if (suppressToggle) return;
            bool enabled = colorToggle.Active;
            _dispatcher.FireAndForget(AppActionId.SetGalleryColorEnabled, new SetGalleryColorEnabledPayload(enabled));
        };

        pageSizeSpin.OnValueChanged += (_, __) =>
        {
            if (suppressPageSize) return;
            int requested = (int)Math.Max(1, Math.Round(pageSizeSpin.Value));
            _dispatcher.FireAndForget(AppActionId.SetGalleryPageSize, new SetGalleryPageSizePayload(requested));
        };

        _state.GalleryColorEnabledChanged += (_, enabled) =>
        {
            suppressToggle = true;
            colorToggle.Active = enabled;
            suppressToggle = false;
        };

        _state.GalleryPageSizeChanged += (_, size) =>
        {
            suppressPageSize = true;
            pageSizeSpin.Value = size;
            suppressPageSize = false;
        };
    }

    private void ConfigureMetadataSettings(CameraSettingsView settingsView)
    {
        if (settingsView.MetadataApplyButton is null)
        {
            return;
        }

        void Refresh()
        {
            UpdateMetadataControls(settingsView, _state.Metadata);
        }

        Refresh();
        _state.MetadataChanged += (_, __) => Refresh();

        settingsView.MetadataApplyButton.OnClicked += async (_, __) =>
        {
            var overrides = BuildMetadataOverrides(settingsView);
            if (overrides.IsEmpty)
            {
                return;
            }

            settingsView.MetadataApplyButton.Sensitive = false;
            try
            {
                await _dispatcher.DispatchAsync(AppActionId.UpdateMetadataOverrides,
                    new UpdateMetadataPayload(overrides));
            }
            finally
            {
                settingsView.MetadataApplyButton.Sensitive = true;
            }
        };
    }

    private void ConfigureNavigation(
        Stack stack,
        Widget livePage,
        Widget settingsPage,
        Widget galleryPage,
        Button settingsButton,
        Button settingsCloseButton,
        Button galleryButton,
        GalleryView galleryView)
    {
        livePage.SetName("live-view");
        settingsPage.SetName("settings-view");
        galleryPage.SetName("gallery-view");

        stack.AddChild(livePage);
        stack.AddChild(settingsPage);
        stack.AddChild(galleryPage);

        void ShowLive()
        {
            stack.SetVisibleChild(livePage);
            settingsButton.Sensitive = true;
            galleryButton.Sensitive = true;
            galleryView.EnsureGridVisible();
        }

        ShowLive();

        settingsButton.OnClicked += (_, __) =>
        {
            if (!settingsButton.Sensitive) return;
            stack.SetVisibleChild(settingsPage);
            settingsButton.Sensitive = false;
            galleryButton.Sensitive = true;
        };

        settingsCloseButton.OnClicked += (_, __) =>
        {
            ShowLive();
        };

        galleryButton.OnClicked += (_, __) =>
        {
            if (!galleryButton.Sensitive) return;
            _dispatcher.FireAndForget(AppActionId.SetGalleryPage, new SetGalleryPagePayload(0));
            _dispatcher.FireAndForget(AppActionId.LoadGallery);
            stack.SetVisibleChild(galleryPage);
            galleryButton.Sensitive = false;
            settingsButton.Sensitive = true;
            galleryView.EnsureGridVisible();
        };

        galleryView.BackRequested += (_, __) =>
        {
            ShowLive();
        };
    }

    private void BindStateToControls(PhotoControlsView photoControls, ComboBoxText resBox, CameraSettingsView settingsView)
    {
        if (photoControls is null)
        {
            return;
        }

        var autoToggle = photoControls.AutoToggle;
        var isoBox = photoControls.IsoBox;
        var shutterBox = photoControls.ShutterBox;

        _state.AutoExposureChanged += (_, enabled) =>
        {
            _suppressAutoToggle = true;
            try
            {
                if (autoToggle.Active != enabled)
                {
                    autoToggle.Active = enabled;
                }
            }
            finally
            {
                _suppressAutoToggle = false;
            }
            isoBox.Sensitive = !enabled;
            shutterBox.Sensitive = !enabled;
        };

        _state.IsoIndexChanged += (_, index) =>
        {
            _suppressIsoChange = true;
            try
            {
                if (isoBox.Active != index)
                {
                    isoBox.Active = index;
                }
            }
            finally
            {
                _suppressIsoChange = false;
            }
        };

        _state.ShutterIndexChanged += (_, index) =>
        {
            _suppressShutterChange = true;
            try
            {
                if (shutterBox.Active != index)
                {
                    shutterBox.Active = index;
                }
            }
            finally
            {
                _suppressShutterChange = false;
            }
        };

        _state.ResolutionIndexChanged += (_, index) =>
        {
            _suppressResolutionChange = true;
            try
            {
                if (resBox.Active != index)
                {
                    resBox.Active = index;
                }
            }
            finally
            {
                _suppressResolutionChange = false;
            }
        };

        _state.OutputDirectoryChanged += (_, path) =>
        {
            SetEntryText(settingsView.OutputDirectoryEntry, path ?? string.Empty);
        };



    }

    private void BindGallery(GalleryView galleryView)
    {
        if (galleryView is null)
        {
            throw new ArgumentNullException(nameof(galleryView));
        }

        bool galleryUpdating = false;

        void RefreshGallery()
        {
            if (galleryUpdating) return;
            galleryUpdating = true;
            try
            {
                var items = _state.GetGalleryPageItems();
                galleryView.UpdateItems(items, _state.GalleryColorEnabled);
                int totalPages = _state.GetGalleryPageCount();
                int currentPage = totalPages == 0 ? 0 : Math.Clamp(_state.GalleryPageIndex, 0, Math.Max(0, totalPages - 1));
                galleryView.UpdatePagination(currentPage, totalPages);
            }
            finally
            {
                galleryUpdating = false;
            }
        }

        RefreshGallery();

        _state.RecentCapturesChanged += (_, __) => RefreshGallery();
        _state.GalleryPageIndexChanged += (_, __) => RefreshGallery();
        _state.GalleryColorEnabledChanged += (_, __) => RefreshGallery();
        _state.GalleryPageSizeChanged += (_, __) => RefreshGallery();

        galleryView.PageRequested += (_, delta) =>
        {
            int requested = _state.GalleryPageIndex + delta;
            _dispatcher.FireAndForget(AppActionId.SetGalleryPage, new SetGalleryPagePayload(requested));
        };

        _state.OutputDirectoryChanged += (_, __) =>
        {
            _dispatcher.FireAndForget(AppActionId.LoadGallery);
        };

        _dispatcher.FireAndForget(AppActionId.LoadGallery);
    }

    private void UpdateMetadataControls(CameraSettingsView view, CameraMetadataSnapshot snapshot)
    {
        if (view.MetadataMakeEntry is not null)
            SetEntryText(view.MetadataMakeEntry, snapshot.MakeOverride ?? string.Empty);
        if (view.MetadataModelEntry is not null)
            SetEntryText(view.MetadataModelEntry, snapshot.ModelOverride ?? string.Empty);
        if (view.MetadataUniqueEntry is not null)
            SetEntryText(view.MetadataUniqueEntry, snapshot.UniqueModelOverride ?? string.Empty);
        if (view.MetadataSoftwareEntry is not null)
            SetEntryText(view.MetadataSoftwareEntry, snapshot.SoftwareOverride ?? string.Empty);
        if (view.MetadataArtistEntry is not null)
            SetEntryText(view.MetadataArtistEntry, snapshot.ArtistOverride ?? string.Empty);
        if (view.MetadataCopyrightEntry is not null)
            SetEntryText(view.MetadataCopyrightEntry, snapshot.CopyrightOverride ?? string.Empty);

        if (view.MetadataMakeEffectiveLabel is not null)
            SetEffectiveLabel(view.MetadataMakeEffectiveLabel, snapshot.EffectiveMake);
        if (view.MetadataModelEffectiveLabel is not null)
            SetEffectiveLabel(view.MetadataModelEffectiveLabel, snapshot.EffectiveModel);
        if (view.MetadataUniqueEffectiveLabel is not null)
            SetEffectiveLabel(view.MetadataUniqueEffectiveLabel, snapshot.EffectiveUniqueModel);
        if (view.MetadataSoftwareEffectiveLabel is not null)
            SetEffectiveLabel(view.MetadataSoftwareEffectiveLabel, snapshot.EffectiveSoftware);
        if (view.MetadataArtistEffectiveLabel is not null)
            SetEffectiveLabel(view.MetadataArtistEffectiveLabel, snapshot.EffectiveArtist);
        if (view.MetadataCopyrightEffectiveLabel is not null)
            SetEffectiveLabel(view.MetadataCopyrightEffectiveLabel, snapshot.EffectiveCopyright);
    }

    private MetadataOverrides BuildMetadataOverrides(CameraSettingsView view)
    {
        return new MetadataOverrides(
            GetMetadataEntryValue(view.MetadataMakeEntry),
            GetMetadataEntryValue(view.MetadataModelEntry),
            GetMetadataEntryValue(view.MetadataUniqueEntry),
            GetMetadataEntryValue(view.MetadataSoftwareEntry),
            GetMetadataEntryValue(view.MetadataArtistEntry),
            GetMetadataEntryValue(view.MetadataCopyrightEntry));
    }

    private static string? GetMetadataEntryValue(Entry entry)
    {
        if (entry is null) return null;
        string text = GetEntryText(entry).Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static void SetEffectiveLabel(Label label, string? value)
    {
        if (label is null) return;
        string text = string.IsNullOrWhiteSpace(value) ? "Effective: (default)" : $"Effective: {value}";
        label.SetText(text);
    }



    private static string GetEntryText(Entry entry)
    {
        return entry.GetText() ?? string.Empty;
    }

    private static void SetEntryText(Entry entry, string text)
    {
        entry.SetText(text ?? string.Empty);
    }
}
