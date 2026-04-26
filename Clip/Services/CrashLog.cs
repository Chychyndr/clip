using System.Text;

namespace Clip.Services;

public static class CrashLog
{
    private static readonly object Gate = new();

    public static string Path => System.IO.Path.Combine(ClipConstants.AppDataDirectory, "crash.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(Exception exception, string context) =>
        Write("ERROR", $"{context}{Environment.NewLine}{Describe(exception)}");

    private static void Write(string level, string message)
    {
        try
        {
            ClipConstants.EnsureAppDirectories();
            var builder = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("O"))
                .Append(" [")
                .Append(level)
                .Append("] ")
                .AppendLine(message);

            lock (Gate)
            {
                File.AppendAllText(Path, builder.ToString());
            }
        }
        catch
        {
            // Logging must never become the reason the app fails to start.
        }
    }

    private static string Describe(Exception exception)
    {
        var builder = new StringBuilder();
        var current = exception;
        var depth = 0;

        while (current is not null)
        {
            builder
                .AppendLine(depth == 0 ? current.GetType().FullName : $"Inner {depth}: {current.GetType().FullName}")
                .Append("HResult: 0x")
                .AppendLine(current.HResult.ToString("X8"))
                .Append("Message: ")
                .AppendLine(current.Message);

            foreach (var propertyName in new[] { "LineNumber", "LinePosition", "FileName" })
            {
                var property = current.GetType().GetProperty(propertyName);
                if (property is null)
                {
                    continue;
                }

                builder
                    .Append(propertyName)
                    .Append(": ")
                    .AppendLine(property.GetValue(current)?.ToString() ?? "");
            }

            builder.AppendLine(current.StackTrace);
            current = current.InnerException;
            depth++;
        }

        return builder.ToString();
    }
}
