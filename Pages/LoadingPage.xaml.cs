using AetherVault.ViewModels;
using System.ComponentModel;

namespace AetherVault.Pages;

public partial class LoadingPage : ContentPage
{
    private readonly LoadingViewModel _viewModel;
    private bool _entranceDone;

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
        RunEntranceAnimationsAsync();
        await _viewModel.InitAsync();
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

    private async void RunEntranceAnimationsAsync()
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
