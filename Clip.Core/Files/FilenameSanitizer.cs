using System.Text;

namespace Clip.Core.Files;

public static class FilenameSanitizer
{
    private static readonly HashSet<char> InvalidCharacters = BuildInvalidCharacters();

    public static string Sanitize(string? fileName, int maxFileNameLength = 180, string fallbackName = "download")
    {
        var input = string.IsNullOrWhiteSpace(fileName) ? fallbackName : fileName.Trim();
        var lastDot = input.LastIndexOf('.');
        var extension = lastDot > 0 ? input[lastDot..] : "";
        var name = lastDot > 0 ? input[..lastDot] : input;

        if (string.IsNullOrWhiteSpace(name))
        {
            name = fallbackName;
        }

        var cleanName = SanitizePart(name).Trim(' ', '.', '_');
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            cleanName = fallbackName;
        }

        var cleanExtension = SanitizeExtension(extension);
        var maxNameLength = Math.Max(1, maxFileNameLength - cleanExtension.Length);
        if (cleanName.Length > maxNameLength)
        {
            cleanName = cleanName[..maxNameLength].Trim(' ', '.', '_');
        }

        if (string.IsNullOrWhiteSpace(cleanName))
        {
            cleanName = fallbackName;
        }

        return cleanName + cleanExtension;
    }

    public static string EnsureUniquePath(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        var sanitized = Sanitize(fileName);
        var candidate = Path.Combine(directory, sanitized);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var extension = Path.GetExtension(sanitized);
        var name = Path.GetFileNameWithoutExtension(sanitized);
        var index = 2;
        do
        {
            candidate = Path.Combine(directory, $"{name}-{index}{extension}");
            index++;
        }
        while (File.Exists(candidate));

        return candidate;
    }

    private static string SanitizePart(string text)
    {
        var builder = new StringBuilder(text.Length);
        var lastWasReplacement = false;

        foreach (var ch in text)
        {
            if (InvalidCharacters.Contains(ch) || char.IsControl(ch))
            {
                if (!lastWasReplacement)
                {
                    builder.Append('_');
                    lastWasReplacement = true;
                }

                continue;
            }

            builder.Append(ch);
            lastWasReplacement = false;
        }

        return builder.ToString();
    }

    private static string SanitizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "";
        }

        var clean = SanitizePart(extension).Replace("_", "", StringComparison.Ordinal);
        return clean.StartsWith(".", StringComparison.Ordinal) ? clean : "." + clean;
    }

    private static HashSet<char> BuildInvalidCharacters()
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars())
        {
            '<',
            '>',
            ':',
            '"',
            '/',
            '\\',
            '|',
            '?',
            '*'
        };

        return invalid;
    }
}
