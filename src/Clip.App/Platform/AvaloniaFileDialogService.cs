using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Clip.Core.Platform;

namespace Clip.App.Platform;

public sealed class AvaloniaFileDialogService : IFileDialogService
{
    private static readonly FilePickerFileType TextFileType = new("Text files")
    {
        Patterns = ["*.txt"],
        MimeTypes = ["text/plain"]
    };

    private readonly Window _owner;

    public AvaloniaFileDialogService(Window owner)
    {
        _owner = owner;
    }

    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        var folders = await _owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose download folder",
            AllowMultiple = false
        });

        return folders.FirstOrDefault()?.Path.LocalPath;
    }

    public async Task<string?> PickTextFileAsync(CancellationToken cancellationToken = default)
    {
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import links from text file",
            AllowMultiple = false,
            FileTypeFilter = [TextFileType]
        });

        return files.FirstOrDefault()?.Path.LocalPath;
    }
}
