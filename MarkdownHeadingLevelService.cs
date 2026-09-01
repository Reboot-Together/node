using System.Text;
using System.Text.RegularExpressions;

namespace AsterismApp;

public sealed record MarkdownHeadingEdit(string Text, int SelectionStart, int SelectionLength, bool Changed);

public static class MarkdownHeadingLevelService
{
    private static readonly Regex Heading = new("^( {0,3})(#{1,6})(?=[ \\t])", RegexOptions.Multiline | RegexOptions.Compiled);

    public static MarkdownHeadingEdit Change(string text, int selectionStart, int selectionLength, int levelDelta)
    {
        text ??= "";
        selectionStart = Math.Clamp(selectionStart, 0, text.Length);
        selectionLength = Math.Clamp(selectionLength, 0, text.Length - selectionStart);
        if (levelDelta is not (-1 or 1)) return new(text, selectionStart, selectionLength, false);

        var rangeStart = LineStart(text, selectionStart);
        var selectionEnd = selectionStart + selectionLength;
        var rangeEnd = LineEnd(text, selectionEnd);
        var edits = new List<TextEdit>();

        foreach (Match match in Heading.Matches(text, rangeStart))
        {
            if (match.Index >= rangeEnd) break;
            var hashes = match.Groups[2];
            if (levelDelta < 0 && hashes.Length > 1)
                edits.Add(new TextEdit(hashes.Index + hashes.Length - 1, 1, ""));
            else if (levelDelta > 0 && hashes.Length < 6)
                edits.Add(new TextEdit(hashes.Index + hashes.Length, 0, "#"));
        }

        if (edits.Count == 0) return new(text, selectionStart, selectionLength, false);

        var output = new StringBuilder(text);
        foreach (var edit in edits.AsEnumerable().Reverse())
        {
            output.Remove(edit.Index, edit.RemoveLength);
            output.Insert(edit.Index, edit.InsertText);
        }

        var mappedStart = MapPosition(selectionStart, edits);
        var mappedEnd = MapPosition(selectionEnd, edits);
        return new(output.ToString(), mappedStart, Math.Max(0, mappedEnd - mappedStart), true);
    }

    private static int LineStart(string text, int position)
    {
        while (position > 0 && text[position - 1] is not ('\r' or '\n')) position--;
        return position;
    }

    private static int LineEnd(string text, int position)
    {
        while (position < text.Length && text[position] is not ('\r' or '\n')) position++;
        return position;
    }

    private static int MapPosition(int position, IReadOnlyList<TextEdit> edits)
    {
        var mapped = position;
        foreach (var edit in edits)
        {
            if (edit.RemoveLength == 0)
            {
                if (position >= edit.Index) mapped += edit.InsertText.Length;
                continue;
            }

            if (position >= edit.Index + edit.RemoveLength)
                mapped += edit.InsertText.Length - edit.RemoveLength;
            else if (position > edit.Index)
                mapped = edit.Index + edit.InsertText.Length;
        }
        return mapped;
    }

    private sealed record TextEdit(int Index, int RemoveLength, string InsertText);
}
