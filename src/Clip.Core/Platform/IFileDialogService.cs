namespace Clip.Core.Platform;

public interface IFileDialogService
{
    Task<string?> PickFolderAsync(CancellationToken cancellationToken = default);
    Task<string?> PickTextFileAsync(CancellationToken cancellationToken = default);
}
