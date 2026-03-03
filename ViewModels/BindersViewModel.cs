using AetherVault.Data;
using AetherVault.Models;
using AetherVault.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace AetherVault.ViewModels;

/// <summary>
/// ViewModel for the Binders list page.
/// Manages the user's named binders (collection sub-groups).
/// </summary>
public partial class BindersViewModel : BaseViewModel
{
    private readonly IBinderRepository _binderRepository;
    private readonly IToastService _toastService;

    [ObservableProperty]
    public partial ObservableCollection<BinderEntity> Binders { get; set; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; set; } = true;

    public BindersViewModel(IBinderRepository binderRepository, IToastService toastService)
    {
        _binderRepository = binderRepository;
        _toastService = toastService;
    }

    [RelayCommand]
    public async Task LoadBindersAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var binders = await _binderRepository.GetAllBindersAsync();
            Binders = new ObservableCollection<BinderEntity>(binders);
            IsEmpty = Binders.Count == 0;
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to load binders: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<BinderEntity?> CreateBinderAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        try
        {
            int newId = await _binderRepository.CreateBinderAsync(name.Trim());
            await LoadBindersAsync();
            return Binders.FirstOrDefault(b => b.Id == newId);
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to create binder: {ex.Message}", LogLevel.Error);
            return null;
        }
    }

    public async Task DeleteBinderAsync(int binderId)
    {
        try
        {
            await _binderRepository.DeleteBinderAsync(binderId);
            var toRemove = Binders.FirstOrDefault(b => b.Id == binderId);
            if (toRemove != null) Binders.Remove(toRemove);
            IsEmpty = Binders.Count == 0;
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to delete binder: {ex.Message}", LogLevel.Error);
        }
    }

    public async Task RenameBinder(int binderId, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        try
        {
            await _binderRepository.RenameBinderAsync(binderId, newName.Trim());
            var binder = Binders.FirstOrDefault(b => b.Id == binderId);
            if (binder != null) binder.Name = newName.Trim();
        }
        catch (Exception ex)
        {
            Logger.LogStuff($"Failed to rename binder: {ex.Message}", LogLevel.Error);
        }
    }
}
