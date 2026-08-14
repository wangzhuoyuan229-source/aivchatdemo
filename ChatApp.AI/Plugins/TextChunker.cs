using System.Text;

namespace ChatApp.AI.Plugins;

/// <summary>Splits long text into overlapping chunks for embedding (F5).</summary>
public static class TextChunker
{
    public static IReadOnlyList<string> Chunk(string text, int targetSize = 1000, int overlap = 200)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var paragraphs = text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var buffer = new StringBuilder();
        int bufferLen = 0;

        void Flush()
        {
            if (buffer.Length > 0)
            {
                result.Add(buffer.ToString());
                var tail = buffer.ToString();
                buffer.Clear();
                if (tail.Length > overlap)
                {
                    buffer.Append(tail[^overlap..]);
                    buffer.Append('\n');
                }
                bufferLen = buffer.Length;
            }
        }

        foreach (var para in paragraphs)
        {
            var p = para.Trim();
            if (p.Length == 0) continue;

            if (p.Length > targetSize * 2)
            {
                Flush();
                foreach (var piece in SplitLong(p, targetSize, overlap))
                {
                    result.Add(piece);
                }
                continue;
            }

            if (bufferLen + p.Length + 1 > targetSize && bufferLen > 0)
                Flush();

            if (buffer.Length > 0) buffer.Append('\n');
            buffer.Append(p);
            bufferLen = buffer.Length;
        }
        Flush();
        return result;
    }

    private static IEnumerable<string> SplitLong(string text, int targetSize, int overlap)
    {
        for (int i = 0; i < text.Length; i += targetSize - overlap)
        {
            var len = Math.Min(targetSize, text.Length - i);
            yield return text.Substring(i, len);
            if (i + len >= text.Length) yield break;
        }
    }
}
