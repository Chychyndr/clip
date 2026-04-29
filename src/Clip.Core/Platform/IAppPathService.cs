namespace Clip.Core.Platform;

public interface IAppPathService
{
    string AppDataDirectory { get; }
    string DefaultDownloadsDirectory { get; }
    string ToolsDirectory { get; }
    string LogsDirectory { get; }
    string MetadataCacheDirectory { get; }
}
