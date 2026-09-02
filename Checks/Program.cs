using AsterismApp;

if (args is ["--render-sample", var outputPath])
{
    var sample = """
# Confusion Matrix(혼동행렬)

이진 분류 모델의 **예측값**과 **실제값**을 비교하는 표.

|  | 실제 Positive | 실제 Negative |
| --- | --- | --- |
| **예측 Positive** | TP | FP |
| **예측 Negative** | FN | TN |

- **TP (True Positive)**: 실제 양성 → 양성 예측
- **TN (True Negative)**: 실제 음성 → 음성 예측
- **FP (False Positive)**: 실제 음성 → 양성으로 잘못 예측
- **FN (False Negative)**: 실제 양성 → 음성으로 잘못 예측

---

## 주요 평가 지표

| 지표 | 공식 | 의미 |
| --- | --- | --- |
| **Accuracy 정확도** | `(TP + TN) / 전체` | 전체 중 맞춘 비율 |
| **Precision 정밀도** | `TP / (TP + FP)` | 양성 예측 중 진짜 양성 |
| **Recall 재현율** | `TP / (TP + FN)` | 실제 양성 중 찾아낸 비율 |
| **Specificity 특이도** | `TN / (TN + FP)` | 실제 음성 중 찾아낸 비율 |

### 핵심 암기

```text
Precision = TP / (TP + FP)
Recall = TP / (TP + FN)
Specificity = TN / (TN + FP)
Accuracy = (TP + TN) / (TP + TN + FP + FN)
```

특히 **Precision**과 **Recall**의 차이만 확실히 기억하면 됩니다.
""";
    File.WriteAllText(outputPath, MarkdownPreviewRenderer.Render(sample, Path.GetDirectoryName(outputPath)!), System.Text.Encoding.UTF8);
    return;
}

var root = Path.Combine(Path.GetTempPath(), "graph-notes-check-" + Guid.NewGuid());
Directory.CreateDirectory(root);
try
{
    var uiSettingsPath = Path.Combine(root, "ui-layout.json");
    var uiSettingsService = new UiLayoutSettingsService(uiSettingsPath);
    if (uiSettingsService.Load() != UiLayoutSettings.Default) throw new Exception("UI 배치 기본값 로드 실패");
    uiSettingsService.Save(new UiLayoutSettings(.72, true, 420, 1.15, "blue"));
    var savedUiSettings = uiSettingsService.Load();
    if (Math.Abs(savedUiSettings.PreviewRatio - .72) > .001
        || !savedUiSettings.ExplorerCollapsed
        || Math.Abs(savedUiSettings.InspectorWidth - 420) > .001
        || Math.Abs(savedUiSettings.FontScale - 1.15) > .001
        || savedUiSettings.AccentTheme != "blue")
        throw new Exception("UI 배치와 모양 설정 저장 실패");

    var chunkNote = new NoteInfo(
        "데이터베이스 성능",
        Path.Combine(root, "chunk.md"),
        "## 커넥션 풀\n\nDB 연결을 재사용한다.\n\n## 인덱스\n\n검색 속도를 높인다.",
        DateTime.Now,
        NoteMetadata.Manual);
    var semanticChunks = SemanticTextChunker.Split(chunkNote);
    if (semanticChunks.Count == 0
        || semanticChunks.Select(chunk => chunk.Key).Distinct().Count() != semanticChunks.Count
        || semanticChunks.Any(chunk => string.IsNullOrWhiteSpace(chunk.ContentHash)))
        throw new Exception("제목 수준 로컬 AI 청크 생성 실패");

    var modelAssets = Path.Combine(Environment.CurrentDirectory, "Assets", "SemanticModel");
    using (var embeddingModel = new LocalEmbeddingModel(modelAssets, Path.Combine(root, "models")))
    {
        var pool = embeddingModel.Embed("데이터베이스 연결을 미리 만들어 재사용하는 커넥션 풀");
        var similarPool = embeddingModel.Embed("DB 접속 연결을 여러 개 준비해 반복해서 사용하는 방식");
        var unrelated = embeddingModel.Embed("수채화 물감으로 풍경화를 그리는 방법");
        static double Dot(float[] left, float[] right) => left.Zip(right, (a, b) => a * b).Sum();
        var similarScore = Dot(pool, similarPool);
        var unrelatedScore = Dot(pool, unrelated);
        if (pool.Length != 384 || similarScore <= unrelatedScore)
            throw new Exception($"오프라인 다국어 임베딩 추론 실패: 유사 {similarScore:F4}, 무관 {unrelatedScore:F4}");
    }

    using (var semanticModel = new LocalEmbeddingModel(modelAssets, Path.Combine(root, "models")))
    using (var semanticService = new SemanticLinkService(
        semanticModel,
        new SemanticIndexStore(Path.Combine(root, "semantic-index.db"))))
    {
        var semanticNotes = new[]
        {
            new NoteInfo("커넥션 풀", Path.Combine(root, "pool.md"), "데이터베이스 연결을 미리 여러 개 만들고 반복해서 재사용한다.", DateTime.Now, NoteMetadata.Manual),
            new NoteInfo("DB 연결 재사용", Path.Combine(root, "reuse.md"), "DB 접속 연결을 준비해 두고 요청마다 빌려 쓰는 방식이다.", DateTime.Now, NoteMetadata.Manual),
            new NoteInfo("수채화", Path.Combine(root, "paint.md"), "물감과 붓으로 풍경화를 그리는 방법을 기록한다.", DateTime.Now, NoteMetadata.Manual)
        };
        var firstIndex = await semanticService.BuildAsync(root, semanticNotes);
        if (!firstIndex.SuggestionsByPath[semanticNotes[0].Path].Any(item => item.Note.Path == semanticNotes[1].Path)
            || firstIndex.SuggestionsByPath[semanticNotes[0].Path].Any(item => item.Note.Path == semanticNotes[2].Path))
            throw new Exception("의미 기반 노트 연결 추천 실패");
        var secondIndex = await semanticService.BuildAsync(root, semanticNotes);
        if (secondIndex.EmbeddedChunkCount != 0 || secondIndex.ReusedChunkCount == 0)
            throw new Exception("변경되지 않은 임베딩 캐시 재사용 실패");
    }

    var store = new NoteRepository(root);
    var linkService = new NoteLinkService();
    var source = store.Create("원본");
    var target = store.Create("연결됨");
    source = store.Save(source.Path, source.Title, "[[연결됨]]", NoteMetadata.Manual);
    var study = new NoteMetadata("Database", new DateTime(2026, 8, 31), "ChatGPT", "Study");
    var imported = store.Create("Connection Pool", study);
    var fallbackTrash = Path.Combine(root, ".trash");
    Directory.CreateDirectory(fallbackTrash);
    File.WriteAllText(Path.Combine(fallbackTrash, "삭제된 노트.md"), "# 삭제된 노트");
    var notes = store.Load();
    if (notes.Any(note => note.Title == "삭제된 노트")) throw new Exception("휴지통 노트 제외 실패");
    var links = linkService.Build(notes);
    if (!links[source.Title].SequenceEqual([target.Title])) throw new Exception("내부 링크 인식 실패");
    var reloaded = notes.Single(note => note.Path == imported.Path).Metadata;
    if (reloaded.Category != study.Category || reloaded.Source != study.Source) throw new Exception("YAML 속성 저장 실패");
    var folder = Path.Combine(root, "분류");
    var movedTarget = store.Move(target.Path, folder);
    if (!File.Exists(movedTarget.Path) || !Path.GetDirectoryName(movedTarget.Path)!.Equals(folder, StringComparison.OrdinalIgnoreCase) || store.Load().All(note => note.Path != movedTarget.Path)) throw new Exception("노트 폴더 이동 실패");
    var renamedTarget = store.Rename(movedTarget.Path, "이름이 바뀐 노트");
    if (File.Exists(movedTarget.Path) || !File.Exists(renamedTarget.Path) || renamedTarget.Title != "이름이 바뀐 노트" || Path.GetFileNameWithoutExtension(renamedTarget.Path) != "이름이 바뀐 노트") throw new Exception("노트와 실제 파일 이름 변경 실패");
    var createdFolder = store.CreateFolder(folder, "새 폴더");
    if (!Directory.Exists(createdFolder) || Path.GetDirectoryName(createdFolder) != folder) throw new Exception("실제 폴더 생성 실패");
    var noteCreatedFromFolderMenu = store.CreateInFolder(createdFolder, "폴더 안 새 노트");
    if (!File.Exists(noteCreatedFromFolderMenu.Path) || Path.GetDirectoryName(noteCreatedFromFolderMenu.Path) != createdFolder) throw new Exception("선택한 폴더 내부 새 노트 생성 실패");
    var dropTarget = store.CreateFolder(root, "드롭 대상");
    var draggedNote = store.Move(renamedTarget.Path, dropTarget);
    if (!File.Exists(draggedNote.Path) || Path.GetDirectoryName(draggedNote.Path) != dropTarget) throw new Exception("드래그 노트 이동 기반 작업 실패");
    var movedFolder = store.MoveFolder(folder, dropTarget);
    if (!Directory.Exists(movedFolder) || Directory.Exists(folder) || !Directory.Exists(Path.Combine(movedFolder, "새 폴더"))) throw new Exception("드래그 폴더 이동 기반 작업 실패");
    var blockedDescendantMove = false;
    try { store.MoveFolder(movedFolder, Path.Combine(movedFolder, "새 폴더")); }
    catch (InvalidOperationException) { blockedDescendantMove = true; }
    if (!blockedDescendantMove) throw new Exception("폴더를 자기 하위로 이동하는 작업 차단 실패");
    var treeService = new VaultTreeService();
    var cachedFolders = treeService.LoadFolders(root);
    var treeItems = treeService.Build(root, store.Load(), cachedFolders, new HashSet<string>([dropTarget], StringComparer.OrdinalIgnoreCase));
    if (!treeItems.Any(item => item.IsFolder && item.Path == dropTarget) || !treeItems.Any(item => item.Note?.Path == draggedNote.Path)) throw new Exception("저장소 폴더 트리 구성 실패");
    if (treeItems.Any(item => item.IsFolder && item.FolderIconOpacity != 1) || treeItems.Any(item => !item.IsFolder && item.FolderIconOpacity != 0)) throw new Exception("폴더 아이콘 표시 구분 실패");
    var nestedNote = store.CreateInFolder(Path.Combine(movedFolder, "새 폴더"), "깊은 노트");
    var ancestors = treeService.AncestorFolders(root, nestedNote.Path);
    if (!ancestors.SequenceEqual([Path.GetDirectoryName(nestedNote.Path)!, movedFolder, dropTarget, root], StringComparer.OrdinalIgnoreCase)) throw new Exception("선택 노트의 상위 폴더 경로 계산 실패");
    var sortRoot = Path.Combine(root, "정렬 검사");
    Directory.CreateDirectory(sortRoot);
    var sortStore = new NoteRepository(sortRoot);
    sortStore.CreateFolder(sortRoot, "10 폴더");
    sortStore.CreateFolder(sortRoot, "2 폴더");
    sortStore.Create("10 노트");
    sortStore.Create("2 노트");
    var sortedItems = treeService.Build(sortRoot, sortStore.Load(), treeService.LoadFolders(sortRoot), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    if (!sortedItems.Skip(1).Select(item => item.Name).SequenceEqual(["2 폴더", "10 폴더", "2 노트", "10 노트"])) throw new Exception("폴더 우선 이름 자연 정렬 실패");
    var networkNotes = store.Load();
    var graphLinks = linkService.Build(networkNotes);
    var graphService = new GraphLayoutService();
    var graphLayout = graphService.Calculate(networkNotes, graphLinks, 720, 1200, source.Title);
    if (!graphLayout.Points.ContainsKey(source.Title) || Math.Abs(graphLayout.Points[source.Title].X - 360) > 1 || Math.Abs(graphLayout.Points[source.Title].Y - 600) > 1) throw new Exception("세로형 그래프 중심 배치 실패");
    var focusedPoint = graphLayout.Points[source.Title];
    if (graphLayout.SelectedNeighbors.Any(title =>
        graphLayout.Points.TryGetValue(title, out var neighbor)
        && Math.Sqrt(Math.Pow(neighbor.X - focusedPoint.X, 2) + Math.Pow(neighbor.Y - focusedPoint.Y, 2)) > 52.01))
        throw new Exception("현재 노트 주변 선택 별의 초기 밀집 배치 실패");
    var focus = graphLayout.Points[source.Title];
    var directions = graphLayout.Points
        .Where(pair => !pair.Key.Equals(source.Title, StringComparison.OrdinalIgnoreCase))
        .Select(pair => Math.Atan2(pair.Value.Y - focus.Y, pair.Value.X - focus.X))
        .ToList();
    if (directions.Count >= 3)
    {
        var directionalBias = Math.Sqrt(
            Math.Pow(directions.Average(Math.Cos), 2)
            + Math.Pow(directions.Average(Math.Sin), 2));
        if (directionalBias > .36) throw new Exception("선택 노트 기준 그래프 한쪽 쏠림 보정 실패");
    }
    var repeatedLayout = new GraphLayoutService().Calculate(networkNotes, graphLinks, 720, 1200, source.Title);
    if (graphLayout.Points.Any(pair => repeatedLayout.Points[pair.Key] != pair.Value))
        throw new Exception("그래프 배치의 실행 간 결정성 실패");
    graphService.Calculate(networkNotes, graphLinks, 720, 1200, null);
    if (graphService.SimulationRuns != 1) throw new Exception("동일 그래프 레이아웃 캐시 재사용 실패");
    var zoomedViewport = GraphViewportService.CalculateZoomedViewportOffset(
        new GraphPoint(100, 50),
        new GraphPoint(200, 150),
        1.2,
        new GraphPoint(1440, 960),
        new GraphPoint(400, 300));
    if (Math.Abs(zoomedViewport.X - 160) > .001
        || Math.Abs(zoomedViewport.Y - 90) > .001
        || Math.Abs((zoomedViewport.X + 200) / 1.2 - 300) > .001
        || Math.Abs((zoomedViewport.Y + 150) / 1.2 - 200) > .001)
        throw new Exception("마우스 포인터 기준 그래프 확대 위치 계산 실패");
    if (GraphViewportService.ChangeZoom(1, false, 10) != GraphViewportService.MinimumZoom
        || GraphViewportService.ChangeZoom(1, true, 10) != GraphViewportService.MaximumZoom
        || GraphViewportService.LabelMode(.69, false) != GraphLabelMode.FocusOnly
        || GraphViewportService.LabelMode(.7, false) != GraphLabelMode.Orbit
        || GraphViewportService.LabelMode(.3, true) != GraphLabelMode.Orbit
        || GraphViewportService.LabelMode(1.1, false) != GraphLabelMode.Detail)
        throw new Exception("그래프 확대 범위와 단계별 라벨 표시 규칙 실패");
    if (GraphViewportService.NodeRadius(1, false, 1) >= 2.5
        || GraphViewportService.NodeRadius(1, false, 20) > 3
        || GraphViewportService.NodeRadius(1, true, 20) > 5
        || GraphViewportService.NodeRadius(GraphViewportService.MinimumZoom, false, 1) >= .8)
        throw new Exception("그래프 노드 크기 단계 계산 실패");
    var cursorRight = GraphViewportService.CalculateCursorMotion(new GraphPoint(0, 0), new GraphPoint(100, 0), .1);
    var cursorUp = GraphViewportService.CalculateCursorMotion(new GraphPoint(0, 100), new GraphPoint(0, 0), .1);
    var cursorSlow = GraphViewportService.CalculateCursorMotion(new GraphPoint(0, 0), new GraphPoint(1, 0), 1);
    if (Math.Abs(cursorRight.Angle - 90) > .001
        || Math.Abs(cursorUp.Angle) > .001
        || cursorRight.FlameStrength < .99
        || cursorSlow.FlameStrength != 0)
        throw new Exception("그래프 우주선 커서 방향과 속도 계산 실패");
    var labelPlacements = new GraphLabelLayoutService().Arrange(
    [
        new GraphLabelCandidate("현재", new GraphPoint(100, 100), 6, 11, 100, 0, GraphLabelRole.Focus),
        new GraphLabelCandidate("연결 A", new GraphPoint(180, 100), 4, 10, 100, 2, GraphLabelRole.Neighbor),
        new GraphLabelCandidate("연결 B", new GraphPoint(180, 105), 4, 10, 100, 2, GraphLabelRole.Neighbor)
    ], new GraphPoint(100, 100), 300, 220);
    if (labelPlacements.Count < 2
        || labelPlacements.All(placement => placement.Candidate.Role != GraphLabelRole.Focus)
        || labelPlacements.SelectMany((left, index) => labelPlacements.Skip(index + 1).Select(right =>
            left.Position.X < right.Position.X + right.Width + 5
            && left.Position.X + left.Width + 5 > right.Position.X
            && left.Position.Y < right.Position.Y + right.Height + 5
            && left.Position.Y + left.Height + 5 > right.Position.Y)).Any(overlap => overlap))
        throw new Exception("궤도형 그래프 라벨 충돌 회피 실패");
    var wrappedLabel = new GraphLabelLayoutService().Arrange(
        [new GraphLabelCandidate("아주 긴 문서 제목도 두 줄로 읽을 수 있어야 한다", new GraphPoint(100, 100), 4, 10, 70, 0, GraphLabelRole.Focus)],
        new GraphPoint(100, 100),
        240,
        180).Single();
    if (wrappedLabel.Width > 70.01 || wrappedLabel.Height < 30)
        throw new Exception("긴 그래프 문서 제목의 두 줄 배치 실패");
    if (!linkService.ExtractTargets("[[연결됨]] [[연결됨|별칭]]").SetEquals(["연결됨"])) throw new Exception("위키 링크 대상 인덱스 생성 실패");
    var rendered = MarkdownPreviewRenderer.Render("""
==강조==와 [[연결됨|위키 링크]] %%숨김%%

| 항목 | 값 |
| --- | --- |
| A | B |

> [!tip] 도움말
> **콜아웃** 본문

- [x] 완료
""", root);
    if (!rendered.Contains("<mark>") || !rendered.Contains("internal-link") || !rendered.Contains("<table") || !rendered.Contains("class=\"callout\"") || !rendered.Contains("md-section") || !rendered.Contains("모두 접기") || rendered.Contains("숨김")) throw new Exception("Markdown 미리보기 렌더링 실패");
    var highlightedCode = MarkdownPreviewRenderer.Render("""
    ```python
    def greet(name):
        # comment
        print("hello", name)
    ```
    """, root);
    if (!highlightedCode.Contains("data-language=\"PYTHON\"")
        || !highlightedCode.Contains("tok-keyword\">def")
        || !highlightedCode.Contains("tok-comment\"># comment")
        || !highlightedCode.Contains("tok-string\">&quot;hello&quot;"))
        throw new Exception("오프라인 코드 문법 강조 실패");

    var confusionMatrix = MarkdownPreviewRenderer.Render("""
## 주요 평가 지표

| 지표 | 공식 | 의미 |
| --- | --- | --- |
| **Accuracy 정확도** | `(TP + TN) / 전체` | 전체 중 맞춘 비율 |
| **Precision 정밀도** | `TP / (TP + FP)` | 양성 예측 중 진짜 양성 |

### 핵심 암기

```text
Precision = TP / (TP + FP)
Recall = TP / (TP + FN)
```

- **TP (True Positive)**: 실제 양성 → 양성 예측
""", root);
    if (!confusionMatrix.Contains("<h2") || !confusionMatrix.Contains("<h3") || !confusionMatrix.Contains("<table") || !confusionMatrix.Contains("<strong>") || !confusionMatrix.Contains("<pre") || !confusionMatrix.Contains("<code")) throw new Exception("ChatGPT 스타일 문서 렌더링 실패");
    if (!confusionMatrix.Contains("width:max-content;max-width:100%") || confusionMatrix.Contains("table{border-collapse:collapse;width:100%")) throw new Exception("내용 기반 표 너비 적용 실패");

    var windowsTextBoxMarkdown = "## CR 줄바꿈\r\r| 항목 | 값 |\r| --- | --- |\r| TP | True Positive |\r\r- **목록** 항목";
    var normalizedRender = MarkdownPreviewRenderer.Render(windowsTextBoxMarkdown, root);
    if (!normalizedRender.Contains("<h2") || !normalizedRender.Contains("<table") || !normalizedRender.Contains("<ul") || !normalizedRender.Contains("<strong>")) throw new Exception("WinUI CR 줄바꿈 렌더링 실패");

    var visibleLineBreakRender = MarkdownPreviewRenderer.Render("첫 줄\n둘째 줄", root);
    if (!visibleLineBreakRender.Contains("첫 줄<br") || !visibleLineBreakRender.Contains("둘째 줄")) throw new Exception("일반 본문 단일 줄바꿈 표시 실패");
    if (MarkdownText.OriginalOffsetFromNormalized("첫 줄\r\n둘째 줄", 4) != 5
        || MarkdownText.OriginalOffsetFromNormalized("첫 줄\r\n둘째 줄", 8) != 9)
        throw new Exception("미리보기 원문 위치의 CRLF 보정 실패");

    var promotedHeading = MarkdownHeadingLevelService.Change("## 제목", 5, 0, -1);
    if (!promotedHeading.Changed || promotedHeading.Text != "# 제목" || promotedHeading.SelectionStart != 4) throw new Exception("제목 한 수준 증가 단축키 처리 실패");
    var demotedHeadings = MarkdownHeadingLevelService.Change("## 첫째\n본문\n### 둘째", 0, 17, 1);
    if (!demotedHeadings.Changed || demotedHeadings.Text != "### 첫째\n본문\n#### 둘째") throw new Exception("선택 제목 한 수준 감소 단축키 처리 실패");
    if (MarkdownHeadingLevelService.Change("# 최대", 0, 0, -1).Changed || MarkdownHeadingLevelService.Change("###### 최소", 0, 0, 1).Changed) throw new Exception("제목 수준 변경 범위 제한 실패");

    var attachmentDirectory = Path.Combine(root, "attachments");
    Directory.CreateDirectory(attachmentDirectory);
    File.WriteAllBytes(Path.Combine(attachmentDirectory, "붙여넣기.png"), [0x89, 0x50, 0x4e, 0x47]);
    var imageRender = MarkdownPreviewRenderer.Render("![[attachments/붙여넣기.png]]", root);
    if (!imageRender.Contains("class=\"internal-image\"") || !imageRender.Contains("src=\"data:image/png;base64,")) throw new Exception("붙여넣은 이미지 렌더링 실패");

    var crNote = store.Create("# 제목 정규화");
    crNote = store.Save(crNote.Path, crNote.Title, windowsTextBoxMarkdown, NoteMetadata.Manual);
    var crReloaded = store.Load().Single(note => note.Path == crNote.Path);
    if (crReloaded.Title != "제목 정규화" || crReloaded.Body.Contains('\r') || !crReloaded.Body.Contains("\n\n| 항목")) throw new Exception("Markdown 저장 줄바꿈 정규화 실패");

    var headingNote = store.Create("제목 보존 검사");
    headingNote = store.Save(headingNote.Path, headingNote.Title, "# 첫 본문 제목\n\n내용\n\n# 둘째 본문 제목\n\n마지막", NoteMetadata.Manual, headingNote.Title);
    var headingReloaded = store.Load().Single(note => note.Path == headingNote.Path);
    if (!headingReloaded.Body.StartsWith("# 첫 본문 제목", StringComparison.Ordinal)
        || !headingReloaded.Body.Contains("# 둘째 본문 제목", StringComparison.Ordinal))
        throw new Exception("본문 1단계 제목 재로드 보존 실패");

    var sectionMarkdown = "## 첫 구역\n\n첫 본문\n\n### 하위 구역\n\n하위 본문\n\n## 둘째 구역\n\n둘째 본문";
    var sectionRender = MarkdownPreviewRenderer.Render(sectionMarkdown, root);
    if (!sectionRender.Contains("data-level='1']>.md-summary>h1{font-size:calc(13.3px * var(--font-scale))") || sectionRender.Contains("font-size:19px")) throw new Exception("노트 제목보다 작은 본문 1단계 제목 크기 적용 실패");
    var themedRender = MarkdownPreviewRenderer.Render(sectionMarkdown, root, initialScrollY: 0, fontScale: 1.2, accentColor: "#6CB6FF");
    if (!themedRender.Contains(":root{--font-scale:1.2;--accent:#6CB6FF}")
        || !themedRender.Contains("color:var(--accent)"))
        throw new Exception("미리보기 글자 크기와 강조색 설정 적용 실패");
    if (!sectionRender.Contains("document.addEventListener('click'")
        || !sectionRender.Contains("type: 'focus-editor', offset")
        || !sectionRender.Contains("type: 'hover-editor', offset, endOffset")
        || !sectionRender.Contains("type: 'hover-editor-clear'")
        || !sectionRender.Contains("data-source-offset=\"0\"")
        || sectionRender.Contains("document.addEventListener('dblclick'"))
        throw new Exception("미리보기 클릭 원문 위치 연결 실패");
    var repeatedSourceRender = MarkdownPreviewRenderer.Render("같은 문장\n\n같은 문장", root);
    if (!repeatedSourceRender.Contains("data-source-offset=\"0\"")
        || !repeatedSourceRender.Contains("data-source-offset=\"7\""))
        throw new Exception("반복 문단 원문 위치 구분 실패");
    var calloutSourceRender = MarkdownPreviewRenderer.Render("앞\n\n> [!note] 제목\n> 본문", root);
    if (!calloutSourceRender.Contains("class=\"callout\" data-source-offset=\"3\""))
        throw new Exception("콜아웃 원문 위치 연결 실패");
    if (!sectionRender.Contains("if (sectionCount)")) throw new Exception("제목 없는 노트의 미리보기 상호작용 연결 실패");
    if (!sectionRender.Contains("heading.title = '클릭해서 접기 또는 펼치기'") || sectionRender.Contains("update-section") || sectionRender.Contains("section-editor") || sectionRender.Contains("beginEditing")) throw new Exception("수준별 편집 제거 및 제목 접기 연결 실패");
    if (!sectionRender.Contains("initialFoldStates[foldKey] : true")) throw new Exception("제목 구역 기본 펼침 상태 적용 실패");
    if (sectionRender.Contains("exclusiveSections")
        || sectionRender.Contains("details.parentElement?.children")
        || !sectionRender.Contains("document.querySelectorAll('.md-section').forEach(section => section.open = true)"))
        throw new Exception("본문 제목의 독립 접기와 모두 펼치기 복원 실패");

    var folderExpansionService = new FolderExpansionService();
    var expansionRoot = Path.Combine(root, "accordion");
    var expansionFolders = new[]
    {
        expansionRoot,
        Path.Combine(expansionRoot, "A"),
        Path.Combine(expansionRoot, "B"),
        Path.Combine(expansionRoot, "A", "A1"),
        Path.Combine(expansionRoot, "A", "A2")
    };
    var expandedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    folderExpansionService.InitializeDefaults(expansionRoot, expansionFolders, expandedFolders);
    if (expandedFolders.Count(folder => Path.GetDirectoryName(folder)?.Equals(expansionRoot, StringComparison.OrdinalIgnoreCase) == true) != 1)
        throw new Exception("탐색기 형제 폴더 기본 상호 배타 상태 실패");
    folderExpansionService.ExpandExclusive(expansionRoot, expansionFolders, expandedFolders, expansionFolders[2]);
    if (!expandedFolders.Contains(expansionFolders[2]) || expandedFolders.Contains(expansionFolders[1]))
        throw new Exception("탐색기 같은 수준 폴더 상호 배타 펼침 실패");
    folderExpansionService.ExpandExclusive(expansionRoot, expansionFolders, expandedFolders, expansionFolders[4]);
    if (!expandedFolders.Contains(expansionFolders[4]) || expandedFolders.Contains(expansionFolders[3]))
        throw new Exception("탐색기 하위 폴더 독립 그룹 처리 실패");

    var guideService = new BuiltInGuideService();
    var guideItems = guideService.BuildItems(
        new HashSet<string>([BuiltInGuideService.FolderPath], StringComparer.OrdinalIgnoreCase),
        null);
    if (guideItems.Count < 5
        || guideItems[0] is not { IsFolder: true, IsVirtual: true }
        || guideItems.Skip(1).Any(item => item.Note is not { IsReadOnly: true })
        || guideService.FindByTitle("마크다운 사용법") is null)
        throw new Exception("읽기 전용 Asterism 안내 문서 구성 실패");

    var rememberedFoldRender = MarkdownPreviewRenderer.Render("# Section\n\nBody", root, null, new Dictionary<string, bool> { ["1:Section#1"] = false });
    if (!rememberedFoldRender.Contains("\"1:Section#1\":false")
        || !rememberedFoldRender.Contains("type: 'fold-state'")
        || !rememberedFoldRender.Contains("Object.hasOwn(initialFoldStates, foldKey)"))
        throw new Exception("미리보기 갱신 시 제목 접힘 상태 복원 실패");

    var rememberedScrollRender = MarkdownPreviewRenderer.Render("# Section\n\nBody", root, null, null, 321.5);
    if (!rememberedScrollRender.Contains("const initialScrollY = 321.5")
        || !rememberedScrollRender.Contains("window.scrollTo(0, initialScrollY)")
        || !rememberedScrollRender.Contains("type: 'preview-scroll'")
        || !rememberedScrollRender.Contains("document.documentElement.scrollHeight - window.innerHeight")
        || !rememberedScrollRender.Contains("y: window.scrollY, maxY")
        || !rememberedScrollRender.Contains("*::-webkit-scrollbar{width:8px;height:8px}")
        || !rememberedScrollRender.Contains("scrollbar-color:#555 transparent"))
        throw new Exception("미리보기 갱신 시 스크롤 위치 복원 실패");

    var mathRender = MarkdownPreviewRenderer.Render("인라인 $\\sum_{i=1}^{n} x_i$\n\n$$\n\\frac{1}{n} \\sum_{i=1}^{n} x_i\n$$", root);
    if (!mathRender.Contains("<span class=\"math\"")
        || !System.Text.RegularExpressions.Regex.IsMatch(mathRender, "<div[^>]*class=\\\"[^\\\"]*math")
        || !mathRender.Contains("https://node-assets.local/katex.min.js")
        || !mathRender.Contains("https://node-assets.local/auto-render.min.js")
        || !mathRender.Contains("window.renderMathInElement(root"))
        throw new Exception("LaTeX 수식 렌더링 연결 실패");

    var chatGptMathRender = MarkdownPreviewRenderer.Render("인라인 \\(x + y\\)\n\n\\[\n\\sum_{i=1}^{n} x_i\n\\]", root);
    if (!chatGptMathRender.Contains("<span class=\"math\">\\(x + y\\)</span>")
        || !System.Text.RegularExpressions.Regex.IsMatch(chatGptMathRender, "<div[^>]*class=\\\"[^\\\"]*math")
        || !chatGptMathRender.Contains("\\sum_{i=1}^{n} x_i"))
        throw new Exception("ChatGPT 스타일 LaTeX 구분자 보존 실패");
    if (!UpdateService.TryParseVersion("v0.1.0", out var parsedVersion) || parsedVersion != new Version(0, 1, 0) || UpdateService.TryParseVersion("latest", out _)) throw new Exception("업데이트 버전 분석 실패");
    Console.WriteLine("Asterism checks passed.");
}
finally { Directory.Delete(root, true); }
