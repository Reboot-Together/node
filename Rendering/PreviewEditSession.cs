using System.Text.Json;
using System.Text.RegularExpressions;

namespace AsterismApp;

// A render-scoped allowlist: stale previews cannot overwrite a newer editor buffer.
public sealed class PreviewEditSession(string source)
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    private readonly string _source = MarkdownText.NormalizeNewlines(source);
    private readonly Dictionary<int, string> _ranges = [];

    public void Register(int start, string original) => _ranges[start] = original;

    public static string DecodeLiteral(string source) => Regex.Replace(source,
        @"\\([!""#$%&'()*+,\-./:;<=>?@\[\]\\^_`{|}~])", "$1");

    public bool TryApply(string current, JsonElement message, out string updated)
    {
        updated = current;
        if (!message.TryGetProperty("session", out var session) || session.GetString() != Id
            || MarkdownText.NormalizeNewlines(current) != _source
            || !message.TryGetProperty("start", out var startValue) || !startValue.TryGetInt32(out var start)
            || !_ranges.TryGetValue(start, out var original)
            || !message.TryGetProperty("text", out var textValue) || textValue.ValueKind != JsonValueKind.String)
            return false;
        var text = textValue.GetString()!;
        if (text.Length > 16000 || text.Any(char.IsControl) || text.Contains('$') || string.IsNullOrWhiteSpace(text))
            return false;
        if (DecodeLiteral(original) == text) return true;

        // Keep delimiter-adjacent whitespace; inserted punctuation must stay literal Markdown.
        var leading = original.Length - original.TrimStart().Length;
        var trailing = original.Length - original.TrimEnd().Length;
        var escaped = Regex.Replace(text.Trim(), @"[!""#$%&'()*+,\-./:;<=>?@\[\]\\^_`{|}~]", @"\$0");
        var replacement = original[..leading] + escaped + (trailing > 0 ? original[^trailing..] : "");
        var from = MarkdownText.OriginalOffsetFromNormalized(current, start);
        var to = MarkdownText.OriginalOffsetFromNormalized(current, start + original.Length);
        updated = current.Remove(from, to - from).Insert(from, replacement);
        return true;
    }
}
