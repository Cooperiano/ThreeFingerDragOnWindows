using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace TouchpadControl.Settings;

public readonly record struct SettingsCommitResult(bool Success, string? Error = null)
{
    public static SettingsCommitResult Saved => new(true);
    public static SettingsCommitResult Failed(string error) => new(false, error);
}

/// <summary>Publish a candidate only after validation and persistence both succeed.</summary>
public static class SettingsCommitTransaction
{
    public static SettingsCommitResult TryCommit<T>(T current, Func<T, T> clone, Action<T> edit,
        Action<T> validate, Action<T> persist, out T committed) where T : class
    {
        committed = current;
        try
        {
            var candidate = clone(current);
            if(ReferenceEquals(candidate, current))
                throw new InvalidOperationException("A settings edit needs an isolated candidate.");
            edit(candidate);
            validate(candidate);
            persist(candidate);
            committed = candidate;
            return SettingsCommitResult.Saved;
        }
        catch(Exception error)
        {
            return SettingsCommitResult.Failed(error.Message);
        }
    }
}

/// <summary>Same-directory replacement, one writer per path, and unique temporary files.</summary>
public static class AtomicSettingsFile
{
    private static readonly ConcurrentDictionary<string, object> Gates = new(StringComparer.OrdinalIgnoreCase);

    public static void Write(string path, string contents, Action<string, string>? publish = null)
    {
        path = Path.GetFullPath(path);
        lock(Gates.GetOrAdd(path, _ => new object()))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using(var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using(var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true))
                    {
                        writer.Write(contents);
                        writer.Flush();
                    }
                    stream.Flush(flushToDisk: true);
                }
                if(publish is null) File.Move(temporaryPath, path, overwrite: true);
                else publish(temporaryPath, path);
            }
            finally
            {
                // Cleanup cannot turn a successfully published edit into a reported failure.
                try { if(File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch(IOException) { }
                catch(UnauthorizedAccessException) { }
            }
        }
    }
}
