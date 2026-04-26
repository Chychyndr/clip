using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Clip.Services;

public sealed class FileDialogService
{
    public async Task<string?> PickFolderAsync(IntPtr windowHandle)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, windowHandle);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
