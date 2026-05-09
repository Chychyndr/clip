// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Concurrent;
using System.Text;

namespace Clip.Core.Files;

public static class AtomicFileWriter
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);

    public static async Task WriteAllTextAsync(
        string path,
        string contents,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var writeLock = Locks.GetOrAdd(fullPath, _ => new SemaphoreSlim(1, 1));
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            await WriteAllTextUnlockedAsync(fullPath, contents, cancellationToken);
        }
        finally
        {
            writeLock.Release();
        }
    }

    public static void WriteAllText(string path, string contents)
    {
        var fullPath = Path.GetFullPath(path);
        var writeLock = Locks.GetOrAdd(fullPath, _ => new SemaphoreSlim(1, 1));
        writeLock.Wait();
        try
        {
            WriteAllTextUnlocked(fullPath, contents);
        }
        finally
        {
            writeLock.Release();
        }
    }

    public static async Task DeleteIfExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var writeLock = Locks.GetOrAdd(fullPath, _ => new SemaphoreSlim(1, 1));
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        finally
        {
            writeLock.Release();
        }
    }

    public static void DeleteIfExists(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var writeLock = Locks.GetOrAdd(fullPath, _ => new SemaphoreSlim(1, 1));
        writeLock.Wait();
        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        finally
        {
            writeLock.Release();
        }
    }

    private static async Task WriteAllTextUnlockedAsync(
        string path,
        string contents,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var tempPath = CreateTempPath(path);
        try
        {
            await File.WriteAllTextAsync(tempPath, contents, Encoding.UTF8, cancellationToken);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void WriteAllTextUnlocked(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var tempPath = CreateTempPath(path);
        try
        {
            File.WriteAllText(tempPath, contents, Encoding.UTF8);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static string CreateTempPath(string path) => $"{path}.{Guid.NewGuid():N}.tmp";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
