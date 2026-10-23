using System.Reflection;

namespace NXVideoMaker;

/// <summary>
/// Extracts embedded resources to a temporary working directory
/// and cleans them up when disposed.
/// </summary>
public sealed class ResourceHelper : IDisposable
{
    public string WorkDir { get; }

    private static readonly Assembly Asm = Assembly.GetExecutingAssembly();

    public ResourceHelper()
    {
        WorkDir = Path.Combine(Path.GetTempPath(), $"NXVideoMaker_{Guid.NewGuid():N}");
        Directory.CreateDirectory(WorkDir);
    }

    /// <summary>
    /// Extracts a single embedded resource to WorkDir.
    /// Resource names are in the form "NXVideoMaker.Resources.filename".
    /// Returns the full path to the extracted file.
    /// </summary>
    public string Extract(string resourceSuffix, string? targetFileName = null)
    {
        string resourceName = FindResource(resourceSuffix)
            ?? throw new InvalidOperationException(
                $"Embedded resource matching '{resourceSuffix}' not found.");

        string outPath = Path.Combine(WorkDir, targetFileName ?? Path.GetFileName(resourceSuffix));

        using var stream = Asm.GetManifestResourceStream(resourceName)!;
        using var file   = File.Create(outPath);
        stream.CopyTo(file);

        return outPath;
    }

    /// <summary>
    /// Extracts all embedded resources whose names contain <paramref name="folderHint"/>
    /// into a subdirectory of WorkDir, preserving relative paths.
    /// Returns the path to the subdirectory.
    /// </summary>
    public string ExtractFolder(string folderHint, string targetSubDir)
    {
        string outDir = Path.Combine(WorkDir, targetSubDir);
        Directory.CreateDirectory(outDir);

        string prefix = $"NXVideoMaker.Resources.{folderHint}.";

        foreach (string name in Asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            // Convert embedded dots back to path separators for sub-folders
            string relative = name[prefix.Length..].Replace('.', Path.DirectorySeparatorChar);

            // Restore the file extension dot (last segment)
            int lastSep = relative.LastIndexOf(Path.DirectorySeparatorChar);
            if (lastSep >= 0)
            {
                // Only replace the final separator-to-dot if it looks like an extension
                // e.g.  subdir\filenameExt  ->  subdir\filename.ext  (already done above)
            }
            // Simpler: the last '/' component has one dot that IS the extension;
            // all intermediate components had their dots turned to separators which
            // is fine as long as folder names don't contain dots.

            string destPath = Path.Combine(outDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            using var stream = Asm.GetManifestResourceStream(name)!;
            using var file   = File.Create(destPath);
            stream.CopyTo(file);
        }

        return outDir;
    }

    private static string? FindResource(string suffix)
    {
        string normalized = suffix.Replace('/', '.').Replace('\\', '.');
        return Asm.GetManifestResourceNames()
                  .FirstOrDefault(n => n.EndsWith(normalized, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        try { Directory.Delete(WorkDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
