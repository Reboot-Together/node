using NodeApp;

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
    var dropTarget = store.CreateFolder(root, "드롭 대상");
    var draggedNote = store.Move(renamedTarget.Path, dropTarget);
    if (!File.Exists(draggedNote.Path) || Path.GetDirectoryName(draggedNote.Path) != dropTarget) throw new Exception("드래그 노트 이동 기반 작업 실패");
    var movedFolder = store.MoveFolder(folder, dropTarget);
    if (!Directory.Exists(movedFolder) || Directory.Exists(folder) || !Directory.Exists(Path.Combine(movedFolder, "새 폴더"))) throw new Exception("드래그 폴더 이동 기반 작업 실패");
    var blockedDescendantMove = false;
    try { store.MoveFolder(movedFolder, Path.Combine(movedFolder, "새 폴더")); }
    catch (InvalidOperationException) { blockedDescendantMove = true; }
    if (!blockedDescendantMove) throw new Exception("폴더를 자기 하위로 이동하는 작업 차단 실패");
    var treeItems = new VaultTreeService().Build(root, store.Load(), new HashSet<string>([dropTarget], StringComparer.OrdinalIgnoreCase));
    if (!treeItems.Any(item => item.IsFolder && item.Path == dropTarget) || !treeItems.Any(item => item.Note?.Path == draggedNote.Path)) throw new Exception("저장소 폴더 트리 구성 실패");
    var networkNotes = store.Load();
    var graphLinks = linkService.Build(networkNotes);
    var graphLayout = new GraphLayoutService().Calculate(networkNotes, graphLinks, 1200, 800, source.Title);
    if (!graphLayout.Points.ContainsKey(source.Title) || Math.Abs(graphLayout.Points[source.Title].X - 600) > 1 || Math.Abs(graphLayout.Points[source.Title].Y - 400) > 1) throw new Exception("그래프 중심 배치 실패");
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
    if (!confusionMatrix.Contains("<h2") || !confusionMatrix.Contains("<h3") || !confusionMatrix.Contains("<table") || !confusionMatrix.Contains("<strong>") || !confusionMatrix.Contains("<pre><code")) throw new Exception("ChatGPT 스타일 문서 렌더링 실패");

    var windowsTextBoxMarkdown = "## CR 줄바꿈\r\r| 항목 | 값 |\r| --- | --- |\r| TP | True Positive |\r\r- **목록** 항목";
    var normalizedRender = MarkdownPreviewRenderer.Render(windowsTextBoxMarkdown, root);
    if (!normalizedRender.Contains("<h2") || !normalizedRender.Contains("<table") || !normalizedRender.Contains("<ul>") || !normalizedRender.Contains("<strong>")) throw new Exception("WinUI CR 줄바꿈 렌더링 실패");

    var crNote = store.Create("# 제목 정규화");
    crNote = store.Save(crNote.Path, crNote.Title, windowsTextBoxMarkdown, NoteMetadata.Manual);
    var crReloaded = store.Load().Single(note => note.Path == crNote.Path);
    if (crReloaded.Title != "제목 정규화" || crReloaded.Body.Contains('\r') || !crReloaded.Body.Contains("\n\n| 항목")) throw new Exception("Markdown 저장 줄바꿈 정규화 실패");

    var sectionMarkdown = "## 첫 구역\n\n첫 본문\n\n### 하위 구역\n\n하위 본문\n\n## 둘째 구역\n\n둘째 본문";
    var sectionBodies = MarkdownSectionService.ExtractBodies(sectionMarkdown);
    if (sectionBodies.Count != 3 || sectionBodies[0] != "첫 본문\n\n### 하위 구역\n\n하위 본문" || sectionBodies[1] != "하위 본문") throw new Exception("하위 제목을 포함한 편집 구역 분석 실패");
    var sectionRender = MarkdownPreviewRenderer.Render(sectionMarkdown, root);
    if (!sectionRender.Contains("document.addEventListener('dblclick'") || sectionRender.Contains("document.addEventListener('pointerdown'")) throw new Exception("편집 영역 밖 더블클릭 종료 연결 실패");
    if (!sectionRender.Contains("heading.addEventListener('dblclick'") || sectionRender.Contains("sectionBody.addEventListener('dblclick'")) throw new Exception("제목 더블클릭 편집 진입 연결 실패");
    var replacedSection = MarkdownSectionService.ReplaceBody(sectionMarkdown, 1, "수정된 하위 본문\n\n- 항목");
    if (!replacedSection.Contains("### 하위 구역\n수정된 하위 본문\n\n- 항목\n## 둘째 구역") || !replacedSection.Contains("첫 본문")) throw new Exception("제목별 본문 교체 실패");
    var replacedParentSection = MarkdownSectionService.ReplaceBody(sectionMarkdown, 0, "새 본문\n\n### 새 하위 구역\n\n새 하위 본문");
    if (!replacedParentSection.Contains("## 첫 구역\n새 본문\n\n### 새 하위 구역\n\n새 하위 본문\n## 둘째 구역") || replacedParentSection.Contains("하위 본문\n\n## 둘째 구역")) throw new Exception("상위 제목에서 하위 항목 일괄 편집 실패");
    if (!UpdateService.TryParseVersion("v0.1.0", out var parsedVersion) || parsedVersion != new Version(0, 1, 0) || UpdateService.TryParseVersion("latest", out _)) throw new Exception("업데이트 버전 분석 실패");
    Console.WriteLine("Node checks passed.");
}
finally { Directory.Delete(root, true); }
