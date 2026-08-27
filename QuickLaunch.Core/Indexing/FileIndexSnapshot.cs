using System;
using System.IO;
using System.Text;

namespace QuickLaunch.Core.Indexing;

/// <summary>
/// Persists the index between runs.
/// </summary>
/// <remarks>
/// Walking the file system takes seconds, and a launcher is judged on the first query
/// after it starts. Reading a flat file back is far quicker than re-walking, so the last
/// index is loaded immediately and a fresh walk replaces it in the background.
/// </remarks>
public static class FileIndexSnapshot
{
    private const uint Magic = 0x58494C51;   // "QLIX"

    /// <summary>Bumped whenever the layout changes, so an old file is ignored, not misread.</summary>
    private const int Version = 1;

    public static void Save(FileIndex index, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Written beside the real file and moved into place, so an interrupted write
            // cannot leave a half-written index to be loaded next time.
            string temporary = path + ".tmp";

            using (var stream = File.Create(temporary))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(Magic);
                writer.Write(Version);
                writer.Write(index.Count);

                for (int i = 0; i < index.Count; i++)
                {
                    writer.Write(index.GetName(i));
                    writer.Write(index.GetParent(i));
                    writer.Write(index.IsDirectory(i));
                }
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The cache is an optimisation. Failing to write it costs a slower first
            // query, which is not worth surfacing to the user.
        }
    }

    /// <summary>Reads a previously saved index, or null if there is not a usable one.</summary>
    public static FileIndex? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8);

            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
            {
                return null;
            }

            int count = reader.ReadInt32();

            if (count < 0 || count > 20_000_000)
            {
                return null;
            }

            var names = new string[count];
            var parents = new int[count];
            var directories = new bool[count];

            for (int i = 0; i < count; i++)
            {
                names[i] = reader.ReadString();
                parents[i] = reader.ReadInt32();
                directories[i] = reader.ReadBoolean();

                // A parent must already have been read, or path reconstruction could loop.
                if (parents[i] >= i)
                {
                    return null;
                }
            }

            return new FileIndex(names, parents, directories);
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or EndOfStreamException
                                              or OutOfMemoryException)
        {
            // A truncated or corrupt cache just means a full walk instead.
            return null;
        }
    }
}
