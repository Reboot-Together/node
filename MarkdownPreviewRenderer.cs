using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace AsterismApp;

public static class MarkdownPreviewRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseYamlFrontMatter()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    public static string Render(
        string markdown,
        string vaultPath,
        Func<string, string?>? resolveNote = null,
        IReadOnlyDictionary<string, bool>? foldStates = null,
        double initialScrollY = 0)
    {
        var body = RenderBody(markdown, vaultPath, resolveNote, 0);
        body = Regex.Replace(body, "<script[^>]*>.*?</script>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        body = Regex.Replace(body, "\\s+on[a-z]+\\s*=\\s*(['\"]).*?\\1", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        body = Regex.Replace(body, "href=(['\"])javascript:.*?\\1", "href=\"#\"", RegexOptions.IgnoreCase);
        body = Regex.Replace(body, "href=\"(?![a-z]+:|#)([^\"]+?)(?:\\.md)?(?:#[^\"]*)?\"", match => $"href=\"node-note://note/{Uri.EscapeDataString(WebUtility.HtmlDecode(match.Groups[1].Value).Replace("%20", " "))}\"");
        return HtmlShell(body, foldStates, initialScrollY);
    }

    private static string RenderBody(string markdown, string vaultPath, Func<string, string?>? resolveNote, int depth)
    {
        var prepared = Prepare(MarkdownText.NormalizeNewlines(markdown), vaultPath, resolveNote, depth);
        var document = Markdown.Parse(prepared.Text, Pipeline);
        if (depth == 0)
        {
            foreach (var block in document.Descendants<Block>())
            {
                if (block.Line < 0 || block.Line >= prepared.SourceOffsets.Count) continue;
                var attributes = block.GetAttributes();
                attributes.AddClass("source-position");
                attributes.AddProperty("data-source-offset", prepared.SourceOffsets[block.Line].ToString());
            }
        }
        return Markdown.ToHtml(document, Pipeline);
    }

    private sealed record PreparedMarkdown(string Text, IReadOnlyList<int> SourceOffsets);

    private static PreparedMarkdown Prepare(string markdown, string vaultPath, Func<string, string?>? resolveNote, int depth)
    {
        var lines = markdown.Split('\n');
        var lineOffsets = new int[lines.Length];
        var sourceOffset = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            lineOffsets[index] = sourceOffset;
            sourceOffset += lines[index].Length + 1;
        }
        var output = new StringBuilder();
        var sourceOffsets = new List<int>();
        var fenced = false;
        var comment = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
            {
                fenced = !fenced;
                AppendMappedLine(output, sourceOffsets, line, lineOffsets[index]);
                continue;
            }
            if (fenced) { AppendMappedLine(output, sourceOffsets, line, lineOffsets[index]); continue; }

            line = NormalizeMathDelimiters(line);
            line = RemoveComments(line, ref comment);
            var callout = Regex.Match(line, "^>\\s*\\[!([a-zA-Z0-9_-]+)\\]([+-])?\\s*(.*)$");
            if (callout.Success)
            {
                var calloutSourceOffset = lineOffsets[index];
                var body = new StringBuilder();
                while (index + 1 < lines.Length && Regex.IsMatch(lines[index + 1], "^>"))
                {
                    index++;
                    body.AppendLine(Regex.Replace(lines[index], "^>\\s?", ""));
                }
                var type = callout.Groups[1].Value.ToLowerInvariant();
                var title = callout.Groups[3].Value.Trim();
                if (title.Length == 0) title = char.ToUpperInvariant(type[0]) + type[1..];
                var inner = depth < 4 ? RenderBody(body.ToString(), vaultPath, resolveNote, depth + 1) : WebUtility.HtmlEncode(body.ToString());
                var fold = callout.Groups[2].Value;
                if (fold.Length > 0)
                    AppendMappedLine(output, sourceOffsets, $"<details class=\"callout\" data-source-offset=\"{calloutSourceOffset}\" data-callout=\"{WebUtility.HtmlEncode(type)}\"{(fold == "+" ? " open" : "")}><summary>{WebUtility.HtmlEncode(title)}</summary><div class=\"callout-content\">{inner}</div></details>", calloutSourceOffset);
                else
                    AppendMappedLine(output, sourceOffsets, $"<aside class=\"callout\" data-source-offset=\"{calloutSourceOffset}\" data-callout=\"{WebUtility.HtmlEncode(type)}\"><div class=\"callout-title\">{WebUtility.HtmlEncode(title)}</div><div class=\"callout-content\">{inner}</div></aside>", calloutSourceOffset);
                continue;
            }
            AppendMappedLine(output, sourceOffsets, TransformWikiLinks(line, vaultPath, resolveNote, depth), lineOffsets[index]);
        }
        return new PreparedMarkdown(output.ToString(), sourceOffsets);
    }

    private static void AppendMappedLine(StringBuilder output, List<int> sourceOffsets, string text, int sourceOffset)
    {
        var normalized = MarkdownText.NormalizeNewlines(text);
        output.Append(normalized).Append('\n');
        for (var index = 0; index <= normalized.Count(character => character == '\n'); index++)
            sourceOffsets.Add(sourceOffset);
    }

    private static string NormalizeMathDelimiters(string line)
    {
        var blockStart = Regex.Match(line, "^(\\s*)\\\\\\[\\s*$");
        if (blockStart.Success) return blockStart.Groups[1].Value + "$$";
        var blockEnd = Regex.Match(line, "^(\\s*)\\\\\\]\\s*$");
        if (blockEnd.Success) return blockEnd.Groups[1].Value + "$$";
        return Regex.Replace(line, "\\\\\\((.+?)\\\\\\)", match => "$" + match.Groups[1].Value + "$");
    }

    private static string RemoveComments(string line, ref bool inComment)
    {
        var output = new StringBuilder();
        var inlineCode = false;
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] == '`') { inlineCode = !inlineCode; if (!inComment) output.Append(line[index]); continue; }
            if (!inlineCode && index + 1 < line.Length && line[index] == '%' && line[index + 1] == '%') { inComment = !inComment; index++; continue; }
            if (!inComment) output.Append(line[index]);
        }
        return output.ToString();
    }

    private static string TransformWikiLinks(string line, string vaultPath, Func<string, string?>? resolveNote, int depth)
    {
        return Regex.Replace(line, "(`+[^`]*`+)|(!?\\[\\[([^\\]]+)\\]\\])", match =>
        {
            if (match.Value.StartsWith('`')) return match.Value;
            var embed = match.Value.StartsWith('!');
            var parts = match.Groups[3].Value.Split('|', 2);
            var target = parts[0].Trim();
            var noteName = target.Split('#')[0];
            if (noteName.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) noteName = noteName[..^3];
            var label = parts.Length > 1 ? parts[1].Trim() : target;
            if (!embed) return $"<a class=\"internal-link\" href=\"node-note://note/{Uri.EscapeDataString(noteName)}\">{WebUtility.HtmlEncode(label)}</a>";

            if (IsImage(target) && TryImageData(vaultPath, target, out var data))
            {
                var width = parts.Length > 1 && int.TryParse(parts[1].Split('x')[0], out var pixels) ? $" width=\"{pixels}\"" : "";
                return $"<img class=\"internal-image\" src=\"{data}\" alt=\"{WebUtility.HtmlEncode(noteName)}\"{width}>";
            }
            var body = depth < 3 ? resolveNote?.Invoke(noteName) : null;
            return body is null
                ? $"<span class=\"missing-embed\">![[{WebUtility.HtmlEncode(label)}]]</span>"
                : $"<section class=\"note-embed\"><header>{WebUtility.HtmlEncode(label)}</header>{RenderBody(body, vaultPath, resolveNote, depth + 1)}</section>";
        });
    }

    private static bool IsImage(string target) => new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg" }.Contains(Path.GetExtension(target.Split('#')[0]), StringComparer.OrdinalIgnoreCase);

    private static bool TryImageData(string vaultPath, string target, out string data)
    {
        data = "";
        try
        {
            var name = target.Split('#')[0];
            var direct = Path.GetFullPath(Path.Combine(vaultPath, name.Replace('/', Path.DirectorySeparatorChar)));
            var root = Path.GetFullPath(vaultPath) + Path.DirectorySeparatorChar;
            var path = direct.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(direct) ? direct : Directory.EnumerateFiles(vaultPath, Path.GetFileName(name), SearchOption.AllDirectories).FirstOrDefault();
            if (path is null || new FileInfo(path).Length > 10 * 1024 * 1024) return false;
            var mime = Path.GetExtension(path).ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".webp" => "image/webp", ".svg" => "image/svg+xml", _ => "image/bmp" };
            data = $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
            return true;
        }
        catch { return false; }
    }

    private static string HtmlShell(string body, IReadOnlyDictionary<string, bool>? foldStates, double initialScrollY)
    {
        var serializedFoldStates = JsonSerializer.Serialize(foldStates ?? new Dictionary<string, bool>());
        var serializedScrollY = JsonSerializer.Serialize(double.IsFinite(initialScrollY) && initialScrollY > 0 ? initialScrollY : 0);
        return $$"""
        <!doctype html>
        <html>
        <head>
          <meta charset="utf-8">
          <meta name="color-scheme" content="dark">
          <link rel="stylesheet" href="https://node-assets.local/katex.min.css">
          <script src="https://node-assets.local/katex.min.js"></script>
          <script src="https://node-assets.local/auto-render.min.js"></script>
          <style>
            *{box-sizing:border-box}html{background:#1e1e1e;color-scheme:dark;scrollbar-color:#555 transparent;scrollbar-width:thin}
            *::-webkit-scrollbar{width:8px;height:8px}*::-webkit-scrollbar-track{background:transparent}*::-webkit-scrollbar-thumb{min-height:28px;background:#555;background-clip:padding-box;border:2px solid transparent;border-radius:999px}*::-webkit-scrollbar-thumb:hover{background:#707070;background-clip:padding-box}*::-webkit-scrollbar-corner{background:transparent}
            body{margin:0 auto;max-width:820px;padding:22px 34px 72px;color:#d4d4d4;background:#1e1e1e;font:10.5px/1.72 'Segoe UI Variable Text','Segoe UI','Malgun Gothic',sans-serif;word-break:keep-all;overflow-wrap:anywhere}
            h1,h2,h3,h4,h5,h6{color:#f0f0f0;font-weight:700;letter-spacing:-.012em}
            h1{font-size:13.3px;line-height:1.32;margin:0 0 20px}h2{font-size:11.9px;line-height:1.38;margin:30px 0 11px;padding-bottom:7px;border-bottom:1px solid #303030}h3{font-size:10.5px;line-height:1.45;margin:24px 0 8px}h4{font-size:9.8px;margin:20px 0 7px}h5{font-size:9.1px;margin:18px 0 6px}h6{font-size:8.4px;margin:16px 0 5px}
            p{margin:0 0 14px}strong{font-weight:700;color:#f0f0f0}em{color:#b8b8b8}ul,ol{margin:5px 0 18px;padding-left:24px}li{margin:4px 0;padding-left:2px}li>p{margin:0}
            a{color:#c8c8c8;text-decoration:underline;text-decoration-color:#555}.internal-link{color:#d1af61;font-weight:600;text-decoration:none}.internal-link:hover,a:hover{text-decoration:underline}mark{background:#5b4b24;color:#fff1c7;padding:1px 3px;border-radius:2px}
            blockquote{margin:18px 0;padding:10px 15px;border-left:3px solid #5a5a5a;color:#c0c0c0;background:#252526;border-radius:0 5px 5px 0}blockquote>:last-child{margin-bottom:0}hr{border:0;border-top:1px solid #303030;margin:28px 0}
            table{border-collapse:collapse;width:max-content;max-width:100%;margin:18px 0 24px;font-size:9.8px}thead{border-bottom:1px solid #404040}th,td{padding:9px 10px;text-align:left;vertical-align:top;border-bottom:1px solid #303030}th{color:#e8e8e8;font-weight:700;background:#252526}tbody tr:hover{background:#242424}
            pre{position:relative;overflow:auto;margin:18px 0 22px;padding:17px 18px;background:#181818;border:1px solid #303030;border-radius:8px;white-space:pre-wrap;word-break:normal}code{font:9.1px/1.62 'Cascadia Mono','Consolas',monospace}p code,li code,td code{padding:2px 5px;color:#dedede;background:#2a2a2a;border-radius:3px;white-space:normal}
            .math{max-width:100%;overflow-x:auto;overflow-y:hidden}.katex-display{margin:18px 0;overflow-x:auto;overflow-y:hidden;padding:3px 0}.katex{font-size:1.05em}
            .task-list-item{list-style:none}.task-list-item input{width:14px;height:14px;margin:0 8px 0 -22px;accent-color:#d1af61}.callout{display:block;margin:18px 0;padding:13px 15px;border:1px solid #3a3a3a;border-left:4px solid #707070;border-radius:6px;background:#252526}.callout-title,.callout summary{font-weight:700;color:#d4d4d4}.callout-content>:last-child{margin-bottom:0}
            .note-embed{margin:18px 0;padding:14px 16px;border:1px solid #303030;border-radius:6px;background:#252526}.note-embed>header{margin-bottom:10px;color:#d1af61;font-weight:700}.internal-image{display:block;max-width:100%;height:auto;margin:18px auto;border-radius:6px}.missing-embed{color:#d0a36a}.footnotes{font-size:9.1px;color:#969696}
            .fold-tools{position:sticky;top:0;z-index:10;display:flex;justify-content:flex-end;gap:6px;margin:0 0 14px;padding:7px 0;background:rgba(30,30,30,.94);backdrop-filter:blur(8px)}
            .fold-tools button{appearance:none;border:1px solid #303030;border-radius:4px;background:#252526;color:#d4d4d4;padding:5px 9px;font:8.4px 'Segoe UI Variable Text','Segoe UI',sans-serif;cursor:pointer}.fold-tools button:hover{background:#333}
            .md-section{margin:0;border-bottom:1px solid #303030}.md-section>.md-summary{display:flex;align-items:flex-start;gap:8px;padding:2px 0;list-style:none;cursor:pointer;user-select:none}.md-section>.md-summary::-webkit-details-marker{display:none}.md-section>.md-summary::before{content:'›';flex:0 0 13px;margin-top:7px;color:#969696;font-size:12.6px;line-height:1;transition:transform .14s ease}.md-section[open]>.md-summary::before{transform:rotate(90deg)}
            .md-section>.md-summary>h1,.md-section>.md-summary>h2,.md-section>.md-summary>h3,.md-section>.md-summary>h4,.md-section>.md-summary>h5,.md-section>.md-summary>h6{flex:1;margin:0;padding:7px 0;border:0}.md-section[data-level='1']>.md-summary>h1{font-size:13.3px}.md-section[data-level='2']>.md-summary>h2{font-size:11.9px}.md-section[data-level='3']>.md-summary>h3{font-size:10.5px}.md-section[data-level='4']>.md-summary>h4{font-size:9.8px}.md-section[data-level='5']>.md-summary>h5{font-size:9.1px}.md-section[data-level='6']>.md-summary>h6{font-size:8.4px}
            .md-section>.md-section-body{padding:11px 0 15px 21px}.md-section>.md-section-body>.md-section{border-bottom:0;border-top:1px solid #2a2a2a}.md-section>.md-section-body>:last-child{margin-bottom:0}
            .source-hover{outline:1px solid rgba(209,175,97,.34);outline-offset:3px;border-radius:3px;cursor:text}
            @media(max-width:700px){body{padding:18px 20px 56px}table{font-size:8.4px}th,td{padding:7px 6px}.md-section>.md-section-body{padding-left:17px} }
          </style>
        </head>
        <body>
          {{body}}
          <script>
            (() => {
              const root = document.body;
              const initialFoldStates = {{serializedFoldStates}};
              const initialScrollY = {{serializedScrollY}};
              const originalNodes = Array.from(root.childNodes);
              const stack = [];
              const foldKeyCounts = new Map();
              let sectionCount = 0;
              for (const node of originalNodes) {
                const heading = node.nodeType === Node.ELEMENT_NODE && /^H[1-6]$/.test(node.tagName) ? node : null;
                if (heading) {
                  const level = Number(heading.tagName.substring(1));
                  while (stack.length && stack[stack.length - 1].level >= level) stack.pop();
                  const details = document.createElement('details');
                  details.className = 'md-section';
                  const foldKeyBase = `${level}:${heading.textContent.trim()}`;
                  const foldKeyCount = (foldKeyCounts.get(foldKeyBase) || 0) + 1;
                  foldKeyCounts.set(foldKeyBase, foldKeyCount);
                  const foldKey = `${foldKeyBase}#${foldKeyCount}`;
                  details.dataset.foldKey = foldKey;
                  details.open = Object.hasOwn(initialFoldStates, foldKey) ? initialFoldStates[foldKey] : true;
                  details.addEventListener('toggle', () => {
                    window.chrome.webview.postMessage({ type: 'fold-state', key: foldKey, open: details.open });
                  });
                  details.dataset.level = String(level);
                  const summary = document.createElement('summary');
                  summary.className = 'md-summary';
                  const sectionBody = document.createElement('div');
                  sectionBody.className = 'md-section-body';
                  heading.title = '클릭해서 접기 또는 펼치기';
                  summary.appendChild(heading);
                  details.append(summary, sectionBody);
                  (stack.length ? stack[stack.length - 1].body : root).appendChild(details);
                  stack.push({ level, body: sectionBody });
                  sectionCount++;
                } else {
                  (stack.length ? stack[stack.length - 1].body : root).appendChild(node);
                }
              }
              const sourceTarget = event => {
                const element = event.target instanceof Element ? event.target : event.target.parentElement;
                if (!element || element.closest('.fold-tools,a,button,input,textarea,select,summary,img')) return null;
                if (window.getSelection()?.toString()) return null;
                return element.closest('[data-source-offset]');
              };
              let hoveredSource = null;
              document.addEventListener('pointermove', event => {
                const next = sourceTarget(event);
                if (next === hoveredSource) return;
                hoveredSource?.classList.remove('source-hover');
                hoveredSource = next;
                hoveredSource?.classList.add('source-hover');
              }, { passive: true });
              document.addEventListener('pointerleave', () => {
                hoveredSource?.classList.remove('source-hover');
                hoveredSource = null;
              });
              document.addEventListener('click', event => {
                const element = sourceTarget(event);
                if (!element) return;
                const offset = Number(element.dataset.sourceOffset);
                if (!Number.isInteger(offset) || offset < 0) return;
                event.preventDefault();
                event.stopPropagation();
                window.chrome.webview.postMessage({ type: 'focus-editor', offset });
              });
              if (sectionCount) {
                const tools = document.createElement('nav');
                tools.className = 'fold-tools';
                tools.setAttribute('aria-label', '문서 접기 도구');
                const expand = document.createElement('button');
                expand.type = 'button';
                expand.textContent = '모두 펼치기';
                expand.addEventListener('click', () => document.querySelectorAll('.md-section').forEach(section => section.open = true));
                const collapse = document.createElement('button');
                collapse.type = 'button';
                collapse.textContent = '모두 접기';
                collapse.addEventListener('click', () => document.querySelectorAll('.md-section').forEach(section => section.open = false));
                tools.append(expand, collapse);
                root.prepend(tools);
              }

              if (window.renderMathInElement) {
                window.renderMathInElement(root, {
                  delimiters: [
                    { left: '$$', right: '$$', display: true },
                    { left: '\\[', right: '\\]', display: true },
                    { left: '$', right: '$', display: false },
                    { left: '\\(', right: '\\)', display: false }
                  ],
                  throwOnError: false,
                  strict: false,
                  trust: false
                });
              }

              window.scrollTo(0, initialScrollY);
              requestAnimationFrame(() => {
                window.scrollTo(0, initialScrollY);
                let scrollFrame = 0;
                window.addEventListener('scroll', () => {
                  if (scrollFrame) return;
                  scrollFrame = requestAnimationFrame(() => {
                    scrollFrame = 0;
                    const maxY = Math.max(0, document.documentElement.scrollHeight - window.innerHeight);
                    window.chrome.webview.postMessage({ type: 'preview-scroll', y: window.scrollY, maxY });
                  });
                }, { passive: true });
              });
            })();
          </script>
        </body>
        </html>
        """;
    }
}
