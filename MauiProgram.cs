using AetherVault.Data;
using AetherVault.Pages;
using AetherVault.Platforms.Android;
using AetherVault.Services;
using AetherVault.Services.DeckBuilder;
using AetherVault.Services.ImportExport;
using AetherVault.ViewModels;
using AppoMobi.Maui.Gestures;
using CommunityToolkit.Maui;
using SkiaSharp.Views.Maui.Controls.Hosting;
using UraniumUI;

namespace AetherVault;

/// <summary>
/// Application entry point and dependency injection (DI) setup.
/// This is the equivalent of a Delphi DPR / project file: it configures the app and registers
/// all services, repositories, ViewModels, and Pages so the container can create and inject them.
/// </summary>
public static class MauiProgram
{
    /// <summary>
    /// Builds and configures the MAUI application. Called once at startup.
    /// </summary>
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        // Core MAUI app + community and UI libraries (SkiaSharp for card grid, UraniumUI for Material inputs, gestures for swipe)
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseSkiaSharp()
            .UseUraniumUI()
            .UseUraniumUIMaterial()
            .UseGestures()
            .ConfigureMauiHandlers(handlers =>
            {
#if ANDROID
                handlers.AddHandler<AetherVault.Controls.CardTextView, AetherVault.Platforms.Android.Handlers.CardTextViewHandler>();
#endif
            })
            .ConfigureFonts(fonts =>
            {
                // Fonts registered here must exist in Resources/Fonts/
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                fonts.AddFont("CrimsonText-Regular.ttf", "SerifFont");
                fonts.AddFont("CrimsonText-Bold.ttf", "SerifFontBold");
                fonts.AddFont("CrimsonText-Italic.ttf", "SerifFontItalic");
                fonts.AddFontAwesomeIconFonts();
            });

        // ── Services (singleton = one instance for the whole app) ─────
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<DatabaseManager>();
        builder.Services.AddSingleton<ICardRepository, CardRepository>();
        builder.Services.AddSingleton<ITokenRepository, TokenRepository>();
        builder.Services.AddSingleton<ICollectionRepository, CollectionRepository>();
        builder.Services.AddSingleton<IDeckRepository, DeckRepository>();
        builder.Services.AddSingleton<CardManager>();
        builder.Services.AddSingleton<DeckValidator>();
        builder.Services.AddSingleton<DeckBuilderService>();
        builder.Services.AddSingleton<FileImageCache>(sp =>
        {
            var cachePath = Path.Combine(FileSystem.CacheDirectory, "ImageCache");
            return new FileImageCache(cachePath);
        });
        builder.Services.AddSingleton<ImageCacheService>();
        builder.Services.AddSingleton<ImageDownloadService>();
        builder.Services.AddSingleton<CollectionImporter>();
        builder.Services.AddSingleton<CollectionExporter>();
        builder.Services.AddSingleton<DeckImporter>();
        builder.Services.AddSingleton<DeckUrlImporter>();
        builder.Services.AddSingleton<DeckExporter>();
        builder.Services.AddSingleton<MtgJsonDeckListService>();
        builder.Services.AddSingleton<MtgJsonDeckImporter>();
        builder.Services.AddSingleton<CardGalleryContext>();
        builder.Services.AddSingleton<DeckSynergyNavigationContext>();
        builder.Services.AddSingleton<IToastService, ToastService>();
        builder.Services.AddSingleton<ICardImageSaveService, CardImageSaveService>();
        builder.Services.AddSingleton<IGridPriceLoadService, GridPriceLoadService>();
        // ── ViewModels (singleton = shared state e.g. search; transient = new instance per navigation) ──
        builder.Services.AddSingleton<SearchViewModel>();
        builder.Services.AddSingleton<CollectionViewModel>();
        builder.Services.AddSingleton<StatsViewModel>();
        builder.Services.AddSingleton<DecksViewModel>();
        builder.Services.AddTransient<CardDetailViewModel>();
        builder.Services.AddTransient<DeckDetailViewModel>();
        builder.Services.AddTransient<LoadingViewModel>();
        builder.Services.AddTransient<CardSearchPickerViewModel>();
        builder.Services.AddTransient<SearchFiltersViewModel>();
        builder.Services.AddTransient<MtgJsonDecksViewModel>();
        builder.Services.AddSingleton<ISearchFilterTarget>(sp => sp.GetRequiredService<SearchViewModel>());
        builder.Services.AddSingleton<Services.ISearchFiltersOpener, Services.SearchFiltersOpenerService>();

        // ── Pages (tab content pages are singleton; AppShell is transient so Shell/Android fragments are fresh after LoadingPage) ──
        builder.Services.AddTransient<AppShell>();
        builder.Services.AddTransient<LoadingPage>();
        builder.Services.AddSingleton<SearchPage>();
        builder.Services.AddSingleton<CollectionPage>();
        builder.Services.AddSingleton<StatsPage>();
        builder.Services.AddSingleton<SettingsPage>();
        builder.Services.AddSingleton<DecksPage>();
        builder.Services.AddTransient<CardDetailPage>();
        builder.Services.AddTransient<DeckDetailPage>();
        builder.Services.AddTransient<SearchFiltersPage>();
        builder.Services.AddSingleton<DeckBrowseListResultCache>();
        builder.Services.AddTransient<DeckAddCardsViewModel>();
        builder.Services.AddTransient<DeckAddCardsPage>();
        builder.Services.AddTransient<CardSearchPickerPage>();
        builder.Services.AddTransient<CreateDeckPage>();
        builder.Services.AddTransient<PasteDecklistPage>();
        builder.Services.AddTransient<AddToDeckPage>();
        builder.Services.AddTransient<CollectionAddPage>();
        builder.Services.AddTransient<MtgJsonDecksPage>();
        return builder.Build();
    }
}
