namespace CodexBarWin.Core.Internal;

/// <summary>
/// Streams a text file line by line, swallowing I/O errors so a single
/// broken/locked/half-written file never takes down the whole scan.
/// </summary>
internal static class JsonLineReader
{
    public static IEnumerable<string> ReadLinesSafe(string path)
    {
        StreamReader? reader;
        try
        {
            // Explicit FileShare.ReadWrite so we can still tail a file that
            // Codex/Claude Code currently has open for writing (the default
            // StreamReader(path) share mode is read-only-share and gets
            // rejected while a writer holds the file open on Windows).
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            reader = new StreamReader(stream);
        }
        catch
        {
            yield break;
        }

        using (reader)
        {
            while (true)
            {
                string? line;
                try
                {
                    line = reader.ReadLine();
                }
                catch
                {
                    yield break;
                }

                if (line is null)
                {
                    yield break;
                }

                yield return line;
            }
        }
    }
}
