namespace Clip.Services;

public sealed class OutputPathHolder
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string _directory = ClipConstants.DefaultDownloadDirectory;

    public async Task<string> GetAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return _directory;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetAsync(string directory)
    {
        await _gate.WaitAsync();
        try
        {
            _directory = directory;
        }
        finally
        {
            _gate.Release();
        }
    }
}
