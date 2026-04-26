using System.IO.Pipes;
using System.Text;

namespace Clip.Services;

public sealed class SingleInstanceService : IDisposable
{
    private readonly string _mutexName;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stopListening = new();
    private Mutex? _mutex;

    public SingleInstanceService(string mutexName, string pipeName)
    {
        _mutexName = mutexName;
        _pipeName = pipeName;
    }

    public bool TryClaim()
    {
        _mutex = new Mutex(initiallyOwned: true, _mutexName, out var createdNew);
        return createdNew;
    }

    public void StartListening(Action<string> commandReceived)
    {
        _ = Task.Run(async () =>
        {
            while (!_stopListening.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Message,
                        PipeOptions.Asynchronous);

                    await pipe.WaitForConnectionAsync(_stopListening.Token);
                    using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                    var command = await reader.ReadLineAsync();
                    if (!string.IsNullOrWhiteSpace(command))
                    {
                        commandReceived(command);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(250);
                }
            }
        });
    }

    public static async Task<bool> SendToExistingInstanceAsync(string pipeName, string command)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(900));
            await pipe.ConnectAsync(timeout.Token);
            await using var writer = new StreamWriter(pipe, Encoding.UTF8) { AutoFlush = true };
            await writer.WriteLineAsync(command);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _stopListening.Cancel();
        _stopListening.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }
}
