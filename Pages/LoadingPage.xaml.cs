using AetherVault.Services;
using AetherVault.ViewModels;
using System.ComponentModel;

namespace AetherVault.Pages;

/// <summary>
/// First screen shown at startup. Displays logo, progress, and tips while LoadingViewModel runs InitAsync
/// (DB download/connect). When initialization succeeds, the ViewModel navigates to AppShell and this page is replaced.
/// </summary>
public partial class LoadingPage : ContentPage
{
    private readonly LoadingViewModel _viewModel;
    private bool _entranceDone;
    private Task? _initTask;
    /// <summary>Single-flight guard: duplicate Android <c>OnAppearing</c> before <see cref="_initTask"/> is set must not start a second <see cref="LoadingViewModel.InitAsync"/> or overwrite <see cref="_initTask"/>.</summary>
    private int _initSingleFlight;

    public LoadingPage(LoadingViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Android can deliver OnAppearing more than once; a second run would see TryBeginStartup==false
        // and return without work while the visible page never leaves the loading state.
        if (_initTask is { IsCompleted: false })
            return;

        // If a previous init finished but we are still the window root (shell swap did not happen),
        // allow a second OnAppearing to run startup again instead of stranding on the splash.
        if (_initTask is { IsCompleted: true } &&
            Application.Current?.Windows.Count > 0 &&
            ReferenceEquals(Application.Current.Windows[0].Page, this))
            Interlocked.Exchange(ref _initSingleFlight, 0);

        if (Interlocked.CompareExchange(ref _initSingleFlight, 1, 0) != 0)
            return;

        var entranceTask = RunEntranceAnimationsAsync();
        _viewModel.SetMinimumDisplayTask(entranceTask);
        // Run init in parallel with entrance animations; InitAsync uses Task.Run for file/DB I/O.
        try
        {
            _initTask = _viewModel.InitAsync();
            await _initTask;
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"[Loading] Init failed: {ex.GetType().Name}: {ex.Message}", LogLevel.Error);
            Logger.LogStuff(ex.StackTrace ?? "", LogLevel.Error);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _viewModel.StatusMessage = $"Startup error: {ex.Message}";
                _viewModel.StatusIsError = true;
                _viewModel.ShowRetry = true;
                _viewModel.IsBusy = false;
            });
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoadingViewModel.IsBusy) && _viewModel.IsBusy && _entranceDone)
            _ = FadeInProgressSectionAsync();
    }

    /// <summary>
    /// After <see cref="OnDisappearing"/>, MAUI can still run pending animation continuations; touching views then crashes on Android.
    /// </summary>
    private bool SplashViewsAreLive => Handler is not null;

    private async Task RunEntranceAnimationsAsync()
    {
        if (_entranceDone) return;

        const uint logoDuration = 400;
        const uint titleDuration = 350;
        const uint taglineDuration = 300;

        try
        {
            LogoBorder.Opacity = 0;
            LogoBorder.Scale = 0.85;
            TitleLabel.Opacity = 0;
            TitleLabel.TranslationY = 12;
            TaglineLabel.Opacity = 0;
            TaglineLabel.TranslationY = 8;

            await Task.Delay(100);

            if (!SplashViewsAreLive) return;

            await LogoBorder.FadeToAsync(1, logoDuration, Easing.CubicOut);
            await LogoBorder.ScaleToAsync(1, 250, Easing.CubicOut);

            await Task.WhenAll(
                TitleLabel.FadeToAsync(1, titleDuration, Easing.CubicOut),
                TitleLabel.TranslateToAsync(0, 0, titleDuration, Easing.CubicOut)
            );

            await Task.WhenAll(
                TaglineLabel.FadeToAsync(1, taglineDuration, Easing.CubicOut),
                TaglineLabel.TranslateToAsync(0, 0, taglineDuration, Easing.CubicOut)
            );

            _entranceDone = true;

            if (_viewModel.IsBusy && SplashViewsAreLive)
                await FadeInProgressSectionAsync();
        }
        catch (Exception ex) when (IsBenignSplashTearDown(ex))
        {
            Logger.LogStuff($"[Loading] Entrance animations stopped (page torn down): {ex.GetType().Name}", LogLevel.Debug);
        }
    }

    private async Task FadeInProgressSectionAsync()
    {
        try
        {
            if (!SplashViewsAreLive) return;
            ProgressSection.Opacity = 0;
            await ProgressSection.FadeToAsync(1, 280, Easing.CubicOut);
        }
        catch (Exception ex) when (IsBenignSplashTearDown(ex))
        {
            Logger.LogStuff($"[Loading] Progress fade skipped (page torn down): {ex.GetType().Name}", LogLevel.Debug);
        }
    }

    private static bool IsBenignSplashTearDown(Exception ex) =>
        ex is ObjectDisposedException or TaskCanceledException;
}
