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

        var entranceTask = RunEntranceAnimationsAsync();
        _viewModel.SetMinimumDisplayTask(entranceTask);
        // Defer init until after the first frame is painted so the loading screen is visible
        // during DB checks instead of the static native splash.
        await Task.Delay(100);
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

    private async Task RunEntranceAnimationsAsync()
    {
        if (_entranceDone) return;

        const uint logoDuration = 400;
        const uint titleDuration = 350;
        const uint taglineDuration = 300;

        LogoBorder.Opacity = 0;
        LogoBorder.Scale = 0.85;
        TitleLabel.Opacity = 0;
        TitleLabel.TranslationY = 12;
        TaglineLabel.Opacity = 0;
        TaglineLabel.TranslationY = 8;

        await Task.Delay(100);

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

        if (_viewModel.IsBusy)
            await FadeInProgressSectionAsync();
    }

    private async Task FadeInProgressSectionAsync()
    {
        ProgressSection.Opacity = 0;
        await ProgressSection.FadeToAsync(1, 280, Easing.CubicOut);
    }
}
