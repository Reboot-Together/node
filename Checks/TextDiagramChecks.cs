using System.Net;
using System.Text.RegularExpressions;
using AsterismApp;

internal static class TextDiagramChecks
{
    public static void Run(string root)
    {
        const string sample = """
확률분포
│
├─ 이산확률분포
│
│  ├─ 베르누이분포
│  │    │
│  │    └─ n번 독립 반복 → 이항분포
│  │                         │
│  │                         └─ n이 크고 p가 작을 때
│  │                              → 푸아송분포
│  │
│  ├─ 초기하분포
│  │    │
│  │    └─ 비복원추출
│  │         ↕
│  │       이항분포와 비교
│  │       (복원/독립추출)
│  │
│  └─ 푸아송분포
│       │
│       └─ 일정 시간 동안 "발생 횟수"
│             │
│             └─ 발생 사이의 "대기시간"
│                    ↓
│                 지수분포
│
└─ 연속확률분포
   │
   ├─ 지수분포
   │    └─ 푸아송 과정의 사건 간 대기시간
   │
   └─ 정규분포
        │
        ├─ 표준화 → 표준정규분포 Z
        │
        ├─ 표준정규변수 제곱들의 합
        │       → 카이제곱분포 χ²
        │
        └─ 표준정규 / √(카이제곱/자유도)
                → t분포
""";
        var normalizedSample = MarkdownText.NormalizeNewlines(sample);
        if (!TextDiagramRenderer.IsWholeDiagram(normalizedSample)) throw new Exception("확률분포 트리 전체 감지 실패");
        var markdown = "설명 문단\n" + normalizedSample + "\n다음 문단";
        var session = new PreviewEditSession(markdown);
        var rendered = MarkdownPreviewRenderer.Render(markdown, root, editSession: session);
        var blocks = Diagrams(rendered);
        if (blocks.Count != 1 || MarkdownText.NormalizeNewlines(PlainText(blocks[0].Value)) != normalizedSample)
            throw new Exception("텍스트 다이어그램 공백·줄바꿈·복사 원문 보존 실패");
        if (!blocks[0].Value.Contains("data-source-offset=\"6\"")
            || !blocks[0].Value.Contains($"data-source-end=\"{6 + normalizedSample.Length}\"")
            || blocks[0].Value.Contains("data-edit-start")
            || !rendered.Contains($"data-edit-start=\"{7 + normalizedSample.Length}\""))
            throw new Exception("다이어그램과 다음 문단 원문 위치 연결 실패");
        if (!blocks[0].Value.Contains("diagram-connector") || !blocks[0].Value.Contains("width:10ch")
            || !rendered.Contains("overflow-x:auto;white-space:pre;overflow-wrap:normal;word-break:normal;tab-size:4")
            || !rendered.Contains("selection.getRangeAt(0).cloneContents().textContent"))
            throw new Exception("다이어그램 연결선·한글 폭·가로 스크롤 스타일 실패");

        var crlf = MarkdownPreviewRenderer.Render(normalizedSample.Replace("\n", "\r\n"), root);
        if (MarkdownText.NormalizeNewlines(PlainText(Diagrams(crlf)[0].Value)) != normalizedSample) throw new Exception("CRLF 트리 보존 실패");
        var tabs = "root\n├─ <script> & [[링크]]\n│\t\t**서식 아님**\n└─ 끝";
        var tabHtml = MarkdownPreviewRenderer.Render(tabs, root);
        if (PlainText(Diagrams(tabHtml)[0].Value) != tabs || Diagrams(tabHtml)[0].Value.Contains("<script>"))
            throw new Exception("트리 탭·특수문자·HTML 안전 렌더링 실패");
        foreach (var language in new[] { "", "text", "txt", "plaintext" })
        {
            var fenced = MarkdownPreviewRenderer.Render($"```{language}\n{normalizedSample}\n```", root);
            if (Diagrams(fenced).Count != 1 || MarkdownText.NormalizeNewlines(PlainText(Diagrams(fenced)[0].Value)) != normalizedSample + "\n")
                throw new Exception("코드 펜스 트리 원문 보존 실패: " + language);
        }
        foreach (var ordinary in new[]
        {
            "문장 안의 │와 ├─ 기호는 설명입니다.\n다음 문장",
            "root\n│\n└─ 하나뿐인 가지",
            "| 이름 | 값 |\n| --- | --- |\n| ├─ | └─ |",
            "```python\n" + normalizedSample + "\n```",
            "````python\n```\n" + normalizedSample + "\n````",
            "<pre>\n" + normalizedSample + "\n</pre>",
            "---\ntitle: diagram\ntree: |\n  root\n  ├─ a\n  └─ b\n---",
            "%%\n" + normalizedSample + "\n%%",
            "$$\n" + normalizedSample + "\n$$"
        })
            if (Diagrams(MarkdownPreviewRenderer.Render(ordinary, root)).Count != 0)
                throw new Exception("일반 문장·코드·HTML·메타데이터·수식 오인 방지 실패: " + ordinary[..Math.Min(24, ordinary.Length)]);
        if (Diagrams(MarkdownPreviewRenderer.Render(normalizedSample + "\n\n설명\n\n" + normalizedSample, root)).Count != 2)
            throw new Exception("복수 다이어그램 분리 실패");
        var quoted = "앞 문단\n\n> [!note] 구조\n> " + normalizedSample.Replace("\n", "\n> ");
        var quotedDiagram = Diagrams(MarkdownPreviewRenderer.Render(quoted, root));
        if (quotedDiagram.Count != 1 || quotedDiagram[0].Value.Contains("data-source-offset"))
            throw new Exception("콜아웃 내부 트리 상대 위치의 원문 오연결 방지 실패");
    }

    private static MatchCollection Diagrams(string html) => Regex.Matches(html, "<pre class=\"text-diagram\"[^>]*>.*?</pre>", RegexOptions.Singleline);
    private static string PlainText(string html) => WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", ""));
}
