using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AsterismApp;

public static class CodeSyntaxHighlighter
{
    private static readonly Regex CodeBlockPattern = new(
        "<pre(?<pre>[^>]*)><code(?<code>[^>]*)>(?<body>.*?)</code></pre>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex LanguagePattern = new(
        "(?:^|\\s)language-(?<language>[a-zA-Z0-9_+#.-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, HashSet<string>> Keywords =
        new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["python"] = Words("and as assert async await break class continue def del elif else except False finally for from global if import in is lambda None nonlocal not or pass raise return True try while with yield match case"),
            ["javascript"] = Words("async await break case catch class const continue debugger default delete do else export extends false finally for function if import in instanceof let new null of return static super switch this throw true try typeof undefined var void while with yield"),
            ["typescript"] = Words("abstract any as async await boolean break case catch class const constructor continue declare default delete do else enum export extends false finally for from function get if implements import in infer instanceof interface keyof let namespace never new null number object of private protected public readonly return set static string super switch symbol this throw true try type typeof undefined unknown var void while with yield"),
            ["csharp"] = Words("abstract as async await base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach from get global goto if implicit in init int interface internal is lock long namespace new not null object operator out override params partial private protected public readonly record ref required return sbyte sealed set short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using var virtual void volatile when where while yield"),
            ["java"] = Words("abstract assert boolean break byte case catch char class const continue default do double else enum extends false final finally float for goto if implements import instanceof int interface long native new null package private protected public return short static strictfp super switch synchronized this throw throws transient true try void volatile while"),
            ["cpp"] = Words("alignas alignof and asm auto bool break case catch char class const constexpr continue default delete do double else enum explicit export extern false float for friend if inline int long namespace new noexcept nullptr operator private protected public register reinterpret_cast return short signed sizeof static struct switch template this throw true try typedef typename union unsigned using virtual void volatile wchar_t while"),
            ["sql"] = Words("add all alter and any as asc backup between by case check column constraint create database default delete desc distinct drop exec exists foreign from full group having in index inner insert into is join key left like limit not null on or order outer primary procedure right rownum select set table top truncate union unique update values view where with"),
            ["powershell"] = Words("begin break catch class continue data define do dynamicparam else elseif end enum exit filter finally for foreach from function hidden if in param process return static switch throw trap try until using var while workflow"),
            ["css"] = Words("important inherit initial unset revert none auto block inline flex grid absolute relative fixed sticky transparent currentColor"),
            ["json"] = Words("true false null")
        };

    private static readonly HashSet<string> Types = Words(
        "Array ArrayList bool boolean byte char DateTime decimal Dictionary double Exception float HashSet int List long Map object Object set short string String Task uint ulong ushort void IEnumerable IReadOnlyList IReadOnlyDictionary");

    public static string HighlightBlocks(string html) => CodeBlockPattern.Replace(html, match =>
    {
        var preAttributes = match.Groups["pre"].Value;
        var codeAttributes = match.Groups["code"].Value;
        var languageMatch = LanguagePattern.Match(codeAttributes);
        var decoded = WebUtility.HtmlDecode(match.Groups["body"].Value);
        var language = NormalizeLanguage(languageMatch.Success ? languageMatch.Groups["language"].Value : DetectLanguage(decoded));
        var label = language.Length == 0 ? "CODE" : language.ToUpperInvariant();
        var highlighted = Highlight(decoded, language);
        var highlightedClass = codeAttributes.Contains("class=", StringComparison.OrdinalIgnoreCase)
            ? codeAttributes
            : codeAttributes + " class=\"syntax-highlighted\"";
        return $"<pre{preAttributes} data-language=\"{WebUtility.HtmlEncode(label)}\"><code{highlightedClass}>{highlighted}</code></pre>";
    });

    private static string Highlight(string code, string language)
    {
        var output = new StringBuilder(code.Length * 2);
        var keywords = Keywords.GetValueOrDefault(language) ?? [];
        var caseInsensitive = language is "sql" or "powershell";
        string[] lineComments = language switch
        {
            "python" or "powershell" or "bash" or "yaml" => ["#"],
            "sql" => ["--"],
            _ => ["//"]
        };

        for (var index = 0; index < code.Length;)
        {
            var lineComment = lineComments.FirstOrDefault(marker => StartsWith(code, index, marker));
            if (lineComment is not null)
            {
                var end = code.IndexOf('\n', index);
                if (end < 0) end = code.Length;
                Append(output, "comment", code[index..end]);
                index = end;
                continue;
            }
            if (StartsWith(code, index, "/*") || StartsWith(code, index, "<!--"))
            {
                var terminator = StartsWith(code, index, "<!--") ? "-->" : "*/";
                var end = code.IndexOf(terminator, index + 2, StringComparison.Ordinal);
                end = end < 0 ? code.Length : end + terminator.Length;
                Append(output, "comment", code[index..end]);
                index = end;
                continue;
            }

            var current = code[index];
            if (current is '\'' or '"' or '`')
            {
                var quote = current;
                var end = index + 1;
                while (end < code.Length)
                {
                    if (code[end] == '\\') { end = Math.Min(code.Length, end + 2); continue; }
                    if (code[end++] == quote) break;
                }
                Append(output, "string", code[index..end]);
                index = end;
                continue;
            }
            if (char.IsDigit(current))
            {
                var end = index + 1;
                while (end < code.Length && (char.IsLetterOrDigit(code[end]) || code[end] is '.' or '_' or 'x' or 'X')) end++;
                Append(output, "number", code[index..end]);
                index = end;
                continue;
            }
            if (char.IsLetter(current) || current is '_' or '$')
            {
                var end = index + 1;
                while (end < code.Length && (char.IsLetterOrDigit(code[end]) || code[end] is '_' or '$')) end++;
                var word = code[index..end];
                var comparisonWord = caseInsensitive ? word.ToLowerInvariant() : word;
                var tokenClass = keywords.Contains(comparisonWord)
                    ? "keyword"
                    : Types.Contains(word)
                        ? "type"
                        : NextNonWhitespace(code, end) == '('
                            ? "function"
                            : null;
                if (tokenClass is null) output.Append(WebUtility.HtmlEncode(word));
                else Append(output, tokenClass, word);
                index = end;
                continue;
            }
            if ("{}[]()<>:=+-*/%!&|?.;,".Contains(current)) Append(output, "operator", current.ToString());
            else output.Append(WebUtility.HtmlEncode(current.ToString()));
            index++;
        }
        return output.ToString();
    }

    private static void Append(StringBuilder output, string tokenClass, string value) =>
        output.Append("<span class=\"tok-").Append(tokenClass).Append("\">")
            .Append(WebUtility.HtmlEncode(value)).Append("</span>");

    private static bool StartsWith(string value, int index, string candidate) =>
        index + candidate.Length <= value.Length
        && value.AsSpan(index, candidate.Length).SequenceEqual(candidate);

    private static char NextNonWhitespace(string value, int index)
    {
        while (index < value.Length && char.IsWhiteSpace(value[index])) index++;
        return index < value.Length ? value[index] : '\0';
    }

    private static string DetectLanguage(string code)
    {
        if (Regex.IsMatch(code, "^\\s*(SELECT|INSERT|UPDATE|CREATE)\\b", RegexOptions.IgnoreCase)) return "sql";
        if (Regex.IsMatch(code, "^\\s*(def |from |import |print\\()", RegexOptions.Multiline)) return "python";
        if (Regex.IsMatch(code, "^\\s*(const |let |function |export )", RegexOptions.Multiline)) return "javascript";
        if (Regex.IsMatch(code, "^\\s*(using |namespace |public (?:sealed )?class )", RegexOptions.Multiline)) return "csharp";
        if (Regex.IsMatch(code, "^\\s*[<{[]")) return "json";
        return "";
    }

    private static string NormalizeLanguage(string language) => language.Trim().ToLowerInvariant() switch
    {
        "py" => "python",
        "js" or "jsx" => "javascript",
        "ts" or "tsx" => "typescript",
        "cs" or "c#" or "dotnet" => "csharp",
        "c++" or "cc" or "cxx" => "cpp",
        "ps1" or "pwsh" => "powershell",
        "sh" or "shell" => "bash",
        "yml" => "yaml",
        var normalized => normalized
    };

    private static HashSet<string> Words(string values) =>
        values.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
}
