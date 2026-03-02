namespace AetherVault;

using Microsoft.Extensions.DependencyInjection;
using AetherVault.Pages;
using System;

public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;

    public App(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var loadingPage = _serviceProvider.GetRequiredService<LoadingPage>();
        return new Window(loadingPage);
    }
}
