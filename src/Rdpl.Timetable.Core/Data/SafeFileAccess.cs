using System.Text;

namespace Rdpl.Timetable.Data;

/// <summary>Thrown for any unusable input (missing/oversized/malformed file). Message is safe to show to users.</summary>
public sealed class DataInputException : Exception
{
    public DataInputException(string message)
        : base(message)
    {
    }

    public DataInputException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// File access guard used by every loader:
/// paths must resolve inside an allowed root (no traversal), must not be symlinks/reparse points,
/// and are size-capped before being read.
/// </summary>
public static class SafeFileAccess
{
    public const long MaxJsonBytes = 5L * 1024 * 1024;
    public const long MaxMarkdownBytes = 5L * 1024 * 1024;
    public const long MaxWorkbookBytes = 25L * 1024 * 1024;

    /// <summary>Resolves <paramref name="path"/> (absolute or relative to <paramref name="root"/>) and enforces it stays inside the root.</summary>
    public static string ResolveInsideRoot(string root, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.IndexOf('\0') >= 0)
        {
            throw new DataInputException("Invalid path.");
        }

        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(fullRoot, path));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison) && !string.Equals(full, fullRoot, comparison))
        {
            throw new DataInputException($"Path '{path}' resolves outside the allowed folder.");
        }

        return full;
    }

    public static bool Exists(string fullPath) => File.Exists(fullPath);

    /// <summary>Validates an existing regular file: not a link, not larger than <paramref name="maxBytes"/>.</summary>
    public static FileInfo RequireRegularFile(string fullPath, long maxBytes)
    {
        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            throw new DataInputException($"Required input file not found: {info.Name}");
        }

        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget is not null)
        {
            throw new DataInputException($"Input file '{info.Name}' is a symbolic link; refusing to follow it.");
        }

        if (info.Length > maxBytes)
        {
            throw new DataInputException($"Input file '{info.Name}' is {info.Length:N0} bytes; the limit is {maxBytes:N0} bytes.");
        }

        return info;
    }

    public static string ReadAllText(string fullPath, long maxBytes)
    {
        RequireRegularFile(fullPath, maxBytes);
        try
        {
            return File.ReadAllText(fullPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));
        }
        catch (DecoderFallbackException ex)
        {
            throw new DataInputException($"Input file '{Path.GetFileName(fullPath)}' is not valid UTF-8.", ex);
        }
        catch (IOException ex)
        {
            throw new DataInputException($"Input file '{Path.GetFileName(fullPath)}' could not be read.", ex);
        }
    }

    public static FileStream OpenRead(string fullPath, long maxBytes)
    {
        RequireRegularFile(fullPath, maxBytes);
        return new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, FileOptions.SequentialScan);
    }

    /// <summary>Writes atomically (temp file + move) so a crash never leaves a half-written report.</summary>
    public static void WriteAllTextAtomic(string fullPath, string content)
    {
        var directory = Path.GetDirectoryName(fullPath) ?? throw new DataInputException("Invalid output path.");
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temp, content, new UTF8Encoding(false));
        File.Move(temp, fullPath, overwrite: true);
    }
}
