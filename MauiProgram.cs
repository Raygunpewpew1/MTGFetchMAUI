using MTGFetchMAUI.Data;
using MTGFetchMAUI.Pages;
using MTGFetchMAUI.Services;
using MTGFetchMAUI.Services.DeckBuilder;
using MTGFetchMAUI.ViewModels;
using SkiaSharp.Views.Maui.Controls.Hosting;
using CommunityToolkit.Maui;
using UraniumUI;
using AppoMobi.Maui.Gestures;

#if ANDROID
using Plugin.Maui.OCR;
#endif

namespace MTGFetchMAUI;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

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
                handlers.AddHandler(typeof(MTGFetchMAUI.Controls.CardTextView), typeof(MTGFetchMAUI.Platforms.Android.Handlers.CardTextViewHandler));
#endif
            })
            .ConfigureFonts(fonts =>
            {

                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                fonts.AddFont("CrimsonText-Regular.ttf", "SerifFont");
                fonts.AddFont("CrimsonText-Bold.ttf", "SerifFontBold");
                fonts.AddFont("CrimsonText-Italic.ttf", "SerifFontItalic");
                fonts.AddFontAwesomeIconFonts();
                // Fonts registered here must exist in Resources/Fonts/
                // OpenSans ships with MAUI's default template but isn't included yet
            });

#if ANDROID
        // Plugin.Maui.OCR uses native Google ML Kit - only available on Android
        try
        {
            builder.UseOcr();
            builder.Services.AddSingleton(Plugin.Maui.OCR.OcrPlugin.Default);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OCR plugin init failed: {ex}");
        }
#endif

        // ── Services ─────────────────────────────────────────────────
        builder.Services.AddSingleton<IToastService, ToastService>();
        builder.Services.AddSingleton<DatabaseManager>();
        builder.Services.AddSingleton<CardManager>();
        // ICardRepository and IDeckRepository use CardManager's internal connected DatabaseManager
        builder.Services.AddSingleton<ICardRepository>(sp =>
            new CardRepository(sp.GetRequiredService<CardManager>().DatabaseManager));
        builder.Services.AddSingleton<ICollectionRepository, CollectionRepository>();
        builder.Services.AddSingleton<IDeckRepository>(sp =>
            new DeckRepository(sp.GetRequiredService<CardManager>().DatabaseManager));
        builder.Services.AddSingleton<DeckValidator>();
        builder.Services.AddSingleton<DeckBuilderService>();
        builder.Services.AddSingleton<FileImageCache>(_ =>
            new FileImageCache(
                cacheDir: Path.Combine(FileSystem.CacheDirectory, "ImageCache"),
                maxCacheSizeMB: 300,
                maxCacheAgeDays: 30));
        builder.Services.AddSingleton<ImageCacheService>();
        builder.Services.AddSingleton<ImageDownloadService>();
        builder.Services.AddSingleton<CardGalleryContext>();

        // ── ViewModels ──────────────────────────────────────────────
        builder.Services.AddSingleton<SearchViewModel>();
        builder.Services.AddSingleton<CollectionViewModel>();
        builder.Services.AddSingleton<StatsViewModel>();
        builder.Services.AddSingleton<DecksViewModel>();
        builder.Services.AddTransient<CardDetailViewModel>();
        builder.Services.AddTransient<DeckDetailViewModel>();
        builder.Services.AddTransient<LoadingViewModel>();

        // ── Pages ───────────────────────────────────────────────────
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<LoadingPage>();
        builder.Services.AddSingleton<SearchPage>();
        builder.Services.AddSingleton<CollectionPage>();
        builder.Services.AddSingleton<StatsPage>();
        builder.Services.AddSingleton<DecksPage>();
        builder.Services.AddTransient<CardDetailPage>();
        builder.Services.AddTransient<DeckDetailPage>();
        builder.Services.AddTransient<SearchFiltersPage>();

        return builder.Build();
    }
}
