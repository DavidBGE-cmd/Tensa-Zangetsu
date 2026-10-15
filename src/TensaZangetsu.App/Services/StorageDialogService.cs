using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace TensaZangetsu.App.Services;

public interface IStorageDialogService
{
    Task<string?> OpenFileDialogAsync(string title, params (string Name, string[] Extensions)[] filters);
    Task<string?> OpenFolderDialogAsync(string title);
}

public class StorageDialogService : IStorageDialogService
{
    public async Task<string?> OpenFileDialogAsync(string title, params (string Name, string[] Extensions)[] filters)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null) return null;

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        };

        if (filters != null && filters.Length > 0)
        {
            options.FileTypeFilter = filters.Select(f => new FilePickerFileType(f.Name)
            {
                Patterns = f.Extensions.Select(e => e.StartsWith("*.") ? e : (e.StartsWith(".") ? $"*{e}" : $"*.{e}")).ToArray()
            }).ToList();
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);
        if (files.Count > 0)
        {
            var item = files[0];
            return item.TryGetLocalPath() ?? item.Path.LocalPath;
        }

        return null;
    }

    public async Task<string?> OpenFolderDialogAsync(string title)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null) return null;

        var options = new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        };

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(options);
        if (folders.Count > 0)
        {
            var item = folders[0];
            return item.TryGetLocalPath() ?? item.Path.LocalPath;
        }

        return null;
    }

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow != null ? TopLevel.GetTopLevel(desktop.MainWindow) : null;
        }
        return null;
    }
}
