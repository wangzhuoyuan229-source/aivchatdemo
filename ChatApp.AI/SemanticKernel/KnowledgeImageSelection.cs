using System.Text;
using System.Text.RegularExpressions;
using ChatApp.Core.Models;

namespace ChatApp.AI.SemanticKernel;

/// <summary>
/// Parses the private control token used by the chat model to select retrieved
/// knowledge images. The token is never exposed in the visible message stream.
/// </summary>
internal static partial class KnowledgeImageSelection
{
    private const string MarkerPrefix = "[[knowledge-image";

    internal sealed record Result(string Text, IReadOnlyList<int> DocumentIds);

    public static Result Parse(string raw, IReadOnlyCollection<KnowledgeImageHit> candidates, int maxImages = 3)
    {
        raw ??= string.Empty;
        var allowed = candidates.Select(c => c.DocumentId).ToHashSet();
        var selected = new List<int>();
        foreach (Match match in ImageMarkerRegex().Matches(raw))
        {
            if (!int.TryParse(match.Groups[1].Value, out var id) ||
                !allowed.Contains(id) || selected.Contains(id) || selected.Count >= maxImages)
                continue;
            selected.Add(id);
        }

        var clean = ImageMarkerRegex().Replace(raw, string.Empty).Trim();
        return new Result(clean, selected);
    }

    internal sealed class StreamFilter
    {
        private readonly StringBuilder _pending = new();
        private bool _insideMarker;

        public string Push(string delta)
        {
            if (string.IsNullOrEmpty(delta)) return string.Empty;
            _pending.Append(delta);
            var output = new StringBuilder();

            while (_pending.Length > 0)
            {
                if (_insideMarker)
                {
                    var end = IndexOf(_pending, "]]", StringComparison.Ordinal);
                    if (end < 0) break;
                    _pending.Remove(0, end + 2);
                    _insideMarker = false;
                    continue;
                }

                var marker = IndexOf(_pending, MarkerPrefix, StringComparison.OrdinalIgnoreCase);
                if (marker >= 0)
                {
                    output.Append(_pending.ToString(0, marker));
                    _pending.Remove(0, marker + MarkerPrefix.Length);
                    _insideMarker = true;
                    continue;
                }

                var keep = LongestMarkerPrefixSuffix(_pending);
                var emit = _pending.Length - keep;
                if (emit > 0)
                {
                    output.Append(_pending.ToString(0, emit));
                    _pending.Remove(0, emit);
                }
                break;
            }

            return output.ToString();
        }

        public string Complete()
        {
            if (_insideMarker)
            {
                _pending.Clear();
                _insideMarker = false;
                return string.Empty;
            }

            var tail = _pending.ToString();
            _pending.Clear();
            return ImageMarkerRegex().Replace(tail, string.Empty);
        }

        private static int LongestMarkerPrefixSuffix(StringBuilder value)
        {
            var max = Math.Min(value.Length, MarkerPrefix.Length - 1);
            for (var length = max; length > 0; length--)
            {
                var suffix = value.ToString(value.Length - length, length);
                if (MarkerPrefix.StartsWith(suffix, StringComparison.OrdinalIgnoreCase)) return length;
            }
            return 0;
        }

        private static int IndexOf(StringBuilder value, string needle, StringComparison comparison)
        {
            if (value.Length < needle.Length) return -1;
            return value.ToString().IndexOf(needle, comparison);
        }
    }

    [GeneratedRegex(@"\[\[knowledge-image\s*:\s*(\d+)\s*\]\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImageMarkerRegex();
}
