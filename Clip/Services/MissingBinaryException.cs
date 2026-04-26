namespace Clip.Services;

public sealed class MissingBinaryException : Exception
{
    public MissingBinaryException(IEnumerable<string> missingPaths)
        : base("Required bundled binaries are missing: " + string.Join(", ", missingPaths))
    {
        MissingPaths = missingPaths.ToArray();
    }

    public IReadOnlyList<string> MissingPaths { get; }
}
