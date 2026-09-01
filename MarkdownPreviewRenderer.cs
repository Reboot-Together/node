using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;

namespace NodeApp;

public static class MarkdownPreviewRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseYamlFrontMatter()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    public static string Render(string markdown, string vaultPath, Func<string, string?>? resolveNote = null)
    {
        var body = RenderBody(markdown, vaultPath, resolveNote, 0);
        body = Regex.Replace(body, "<script[^>]*>.*?</script>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        body = Regex.Replace(body, "\\s+on[a-z]+\\s*=\\s*(['\"]).*?\\1", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        body = Regex.Replace(body, "href=(['\"])javascript:.*?\\1", "href=\"#\"", RegexOptions.IgnoreCase);
        body = Regex.Replace(body, "href=\"(?![a-z]+:|#)([^\"]+?)(?:\\.md)?(?:#[^\"]*)?\"", match => $"href=\"node-note://note/{Uri.EscapeDataString(WebUtility.HtmlDecode(match.Groups[1].Value).Replace("%20", " "))}\"");
        return HtmlShell(body);
    }

    private static string RenderBody(string markdown, string vaultPath, Func<string, string?>? resolveNote, int depth)
    {
        var prepared = Prepare(MarkdownText.NormalizeNewlines(markdown), vaultPath, resolveNote, depth);
        return Markdown.ToHtml(prepared, Pipeline);
    }

    private static string Prepare(string markdown, string vaultPath, Func<string, string?>? resolveNote, int depth)
    {
        var lines = markdown.Split('\n');
        var output = new StringBuilder();
        var fenced = false;
        var comment = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
            {
                fenced = !fenced;
                output.AppendLine(line);
                continue;
            }
            if (fenced) { output.AppendLine(line); continue; }

            line = RemoveComments(line, ref comment);
            var callout = Regex.Match(line, "^>\\s*\\[!([a-zA-Z0-9_-]+)\\]([+-])?\\s*(.*)$");
            if (callout.Success)
            {
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
                    output.AppendLine($"<details class=\"callout\" data-callout=\"{WebUtility.HtmlEncode(type)}\"{(fold == "+" ? " open" : "")}><summary>{WebUtility.HtmlEncode(title)}</summary><div class=\"callout-content\">{inner}</div></details>");
                else
                    output.AppendLine($"<aside class=\"callout\" data-callout=\"{WebUtility.HtmlEncode(type)}\"><div class=\"callout-title\">{WebUtility.HtmlEncode(title)}</div><div class=\"callout-content\">{inner}</div></aside>");
                continue;
            }
            output.AppendLine(TransformWikiLinks(line, vaultPath, resolveNote, depth));
        }
        return output.ToString();
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

    private static string HtmlShell(string body)
    {
        return $$"""
        <!doctype html>
        <html>
        <head>
          <meta charset="utf-8">
          <meta name="color-scheme" content="light">
          <style>
            *{box-sizing:border-box}html{background:#fff}
            body{margin:0 auto;max-width:820px;padding:22px 34px 72px;color:#202123;background:#fff;font:14px/1.7 'Segoe UI Variable Text','Segoe UI','Malgun Gothic',sans-serif;word-break:keep-all;overflow-wrap:anywhere}
            h1,h2,h3,h4,h5,h6{color:#111827;font-weight:700;letter-spacing:-.012em}
            h1{font-size:19px;line-height:1.32;margin:0 0 20px}h2{font-size:17px;line-height:1.38;margin:30px 0 11px;padding-bottom:7px;border-bottom:1px solid #e5e7eb}h3{font-size:15px;line-height:1.45;margin:24px 0 8px}h4{font-size:13.5px;margin:20px 0 7px}h5{font-size:12.5px;margin:18px 0 6px}h6{font-size:11.5px;margin:16px 0 5px}
            p{margin:0 0 14px}strong{font-weight:700;color:#111}em{color:#3f3f46}ul,ol{margin:5px 0 18px;padding-left:24px}li{margin:4px 0;padding-left:2px}li>p{margin:0}
            a{color:#2563a9;text-decoration:none}.internal-link{color:#6558a8;font-weight:600}.internal-link:hover,a:hover{text-decoration:underline}mark{background:#fff0a8;color:#202123;padding:1px 3px;border-radius:2px}
            blockquote{margin:18px 0;padding:10px 15px;border-left:3px solid #d1d5db;color:#52525b;background:#fafafa;border-radius:0 7px 7px 0}blockquote>:last-child{margin-bottom:0}hr{border:0;border-top:1px solid #d9dce1;margin:28px 0}
            table{border-collapse:collapse;width:max-content;max-width:100%;margin:18px 0 24px;font-size:13px}thead{border-bottom:1px solid #cfd3d8}th,td{padding:9px 10px;text-align:left;vertical-align:top;border-bottom:1px solid #e5e7eb}th{color:#18181b;font-weight:700;background:#fafafa}tbody tr:hover{background:#fafafa}
            pre{position:relative;overflow:auto;margin:18px 0 22px;padding:17px 18px;background:#f4f4f4;border:1px solid #e7e7e7;border-radius:14px;white-space:pre-wrap;word-break:normal}code{font:12px/1.62 'Cascadia Mono','Consolas',monospace}p code,li code,td code{padding:2px 5px;color:#172033;background:#f1f3f5;border-radius:4px;white-space:normal}
            .task-list-item{list-style:none}.task-list-item input{width:14px;height:14px;margin:0 8px 0 -22px;accent-color:#171717}.callout{display:block;margin:18px 0;padding:13px 15px;border:1px solid #cfe3da;border-left:4px solid #10a37f;border-radius:8px;background:#f4faf7}.callout-title,.callout summary{font-weight:700;color:#245f4c}.callout-content>:last-child{margin-bottom:0}
            .note-embed{margin:18px 0;padding:14px 16px;border:1px solid #deded9;border-radius:9px;background:#fafaf8}.note-embed>header{margin-bottom:10px;color:#6558a8;font-weight:700}.internal-image{display:block;max-width:100%;height:auto;margin:18px auto;border-radius:8px}.missing-embed{color:#a35e16}.footnotes{font-size:12px;color:#71717a}
            .fold-tools{position:sticky;top:0;z-index:10;display:flex;justify-content:flex-end;gap:6px;margin:0 0 14px;padding:7px 0;background:rgba(255,255,255,.94);backdrop-filter:blur(8px)}
            .fold-tools button{appearance:none;border:1px solid #dededb;border-radius:6px;background:#f5f5f3;color:#404040;padding:5px 9px;font:11px 'Segoe UI Variable Text','Segoe UI',sans-serif;cursor:pointer}.fold-tools button:hover{background:#ececea}
            .md-section{margin:0;border-bottom:1px solid #ececea}.md-section>.md-summary{display:flex;align-items:flex-start;gap:8px;padding:2px 0;list-style:none;cursor:pointer;user-select:none}.md-section>.md-summary::-webkit-details-marker{display:none}.md-section>.md-summary::before{content:'›';flex:0 0 13px;margin-top:7px;color:#8a8a85;font-size:18px;line-height:1;transition:transform .14s ease}.md-section[open]>.md-summary::before{transform:rotate(90deg)}
            .md-section>.md-summary>h1,.md-section>.md-summary>h2,.md-section>.md-summary>h3,.md-section>.md-summary>h4,.md-section>.md-summary>h5,.md-section>.md-summary>h6{flex:1;margin:0;padding:7px 0;border:0}.md-section[data-level='1']>.md-summary>h1{font-size:19px}.md-section[data-level='2']>.md-summary>h2{font-size:17px}.md-section[data-level='3']>.md-summary>h3{font-size:15px}.md-section[data-level='4']>.md-summary>h4{font-size:13.5px}.md-section[data-level='5']>.md-summary>h5{font-size:12.5px}.md-section[data-level='6']>.md-summary>h6{font-size:11.5px}
            .md-section>.md-section-body{padding:11px 0 15px 21px}.md-section>.md-section-body>.md-section{border-bottom:0;border-top:1px solid #f0f0ed}.md-section>.md-section-body>:last-child{margin-bottom:0}
            @media(max-width:700px){body{padding:18px 20px 56px}table{font-size:12px}th,td{padding:7px 6px}.md-section>.md-section-body{padding-left:17px} }
          </style>
        </head>
        <body>
          {{body}}
          <script>
            (() => {
              const root = document.body;
              const originalNodes = Array.from(root.childNodes);
              const stack = [];
              let sectionCount = 0;
              for (const node of originalNodes) {
                const heading = node.nodeType === Node.ELEMENT_NODE && /^H[1-6]$/.test(node.tagName) ? node : null;
                if (heading) {
                  const level = Number(heading.tagName.substring(1));
                  while (stack.length && stack[stack.length - 1].level >= level) stack.pop();
                  const details = document.createElement('details');
                  details.className = 'md-section';
                  details.open = true;
                  details.dataset.level = String(level);
                  const summary = document.createElement('summary');
                  summary.className = 'md-summary';
                  const sectionBody = document.createElement('div');
                  sectionBody.className = 'md-section-body';
                  heading.title = '더블클릭해서 편집기로 이동';
                  summary.appendChild(heading);
                  details.append(summary, sectionBody);
                  (stack.length ? stack[stack.length - 1].body : root).appendChild(details);
                  stack.push({ level, body: sectionBody });
                  sectionCount++;
                } else {
                  (stack.length ? stack[stack.length - 1].body : root).appendChild(node);
                }
              }
              document.addEventListener('dblclick', event => {
                const element = event.target instanceof Element ? event.target : event.target.parentElement;
                if (!element || element.closest('.fold-tools,a,button,input,textarea,select')) return;
                event.preventDefault();
                event.stopPropagation();
                window.chrome.webview.postMessage({ type: 'focus-editor' });
              });
              if (!sectionCount) return;
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
            })();
          </script>
        </body>
        </html>
        """;
    }
}
