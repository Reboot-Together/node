namespace AsterismApp;

public sealed class BuiltInGuideService
{
    public const string FolderPath = "asterism-guide://root";

    private readonly IReadOnlyList<NoteInfo> _notes =
    [
        Guide("Asterism 소개", "about", """
        # 흩어진 기록을 나만의 성좌로

        별들이 연결되어 하나의 패턴으로 보이는 것을 **Asterism**이라고 부릅니다. 이 앱은 따로 흩어져 있던 기록을 연결해, 사용자가 바라보고 이해하는 지식의 우주를 만드는 도구입니다.

        ## 로컬 우선

        노트는 사용자가 선택한 폴더에 일반 Markdown 파일로 저장됩니다. 자동 링크 추천을 위한 의미 분석도 PC 안에서 실행되며, 원문을 외부 AI 서버로 보내지 않습니다.

        ## 안내 문서

        이 `Asterism 안내` 폴더는 실제 저장소에 생성되지 않는 읽기 전용 가상 폴더입니다. 앱 버전이 올라가면 기능 설명도 함께 갱신됩니다.

        - [[처음 시작하기]]
        - [[마크다운 사용법]]
        - [[링크와 성좌 지도]]
        - [[단축키와 편집]]
        """),
        Guide("처음 시작하기", "getting-started", """
        # Asterism에 오신 것을 환영합니다

        Asterism은 Markdown 노트를 서로 연결해 나만의 지식 성좌를 만드는 로컬 노트 앱입니다. 작성한 문서는 선택한 저장소에 일반 `.md` 파일로 남습니다.

        ## 기본 흐름

        1. 왼쪽 탐색기에서 노트를 선택합니다.
        2. 아래 편집기에 Markdown을 작성합니다.
        3. 위 미리보기에서 결과를 확인합니다.
        4. `[[다른 노트 제목]]`을 입력해 노트를 연결합니다.

        ## 폴더 탐색

        같은 부모 아래의 폴더는 한 번에 하나만 펼쳐집니다. 다른 형제 폴더를 열면 기존 폴더가 자동으로 접히며, 각 폴더 안의 하위 단계는 별도의 그룹으로 동작합니다.

        ## 다음에 읽을 문서

        - [[마크다운 사용법]]
        - [[링크와 성좌 지도]]
        - [[단축키와 편집]]

        > [!note] 읽기 전용 안내
        > 이 폴더의 문서는 수정할 수 없으며 Asterism을 업데이트하면 함께 갱신됩니다.
        """),
        Guide("마크다운 사용법", "markdown", """
        # 마크다운 사용법

        ## 제목

        `#`의 개수로 제목 수준을 정합니다.

        ```markdown
        # 가장 큰 제목
        ## 두 번째 제목
        ### 세 번째 제목
        ```

        ## 글자와 목록

        - `**굵게**` → **굵게**
        - `*기울임*` → *기울임*
        - `==강조==` → ==강조==
        - `- 항목` → 글머리표 목록
        - `1. 항목` → 번호 목록

        일반 줄바꿈도 미리보기에서 그대로 표시됩니다.

        ## 코드 블록

        백틱 세 개 뒤에 언어 이름을 적으면 문법에 따라 색상이 표시됩니다.

        ```python
        def greet(name: str):
            print(f"Hello, {name}")
        ```

        ## 이미지와 수식

        클립보드 이미지를 편집기에 붙여넣으면 `attachments` 폴더에 저장됩니다. 인라인 수식은 `$x + y$`, 블록 수식은 `$$ ... $$`로 작성합니다.
        """),
        Guide("링크와 성좌 지도", "links-and-graph", """
        # 링크와 성좌 지도

        ## 노트 연결

        `[[노트 제목]]`을 입력하면 해당 노트로 이동하는 내부 링크가 만들어집니다. `[[노트 제목|표시할 이름]]`처럼 별칭도 사용할 수 있습니다.

        ## 백링크

        `성좌 지도`의 **연결 정보**를 펼치면 현재 노트로 연결되는 문서를 볼 수 있습니다.

        ## 발견된 성좌

        로컬 임베딩 모델이 내용이 비슷한 노트를 PC 안에서 찾아 추천합니다. `＋ 링크`를 누르기 전까지는 실제 Markdown 링크로 저장되지 않습니다.

        ## 성좌 지도 조작

        문서 상단의 `성좌 지도` 탭이나 `Ctrl + G`를 누르면 현재 노트를 중심으로 한 지도가 작업공간에 열립니다.

        - 별 클릭: 해당 노트를 새로운 중심으로 탐험
        - 휠: 마우스 위치를 기준으로 확대·축소
        - 드래그: 지도 이동
        - 빈 공간 더블클릭: 현재 노트를 화면 중앙에 맞추기
        - `Esc`: 문서로 돌아가기
        """),
        Guide("단축키와 편집", "shortcuts", """
        # 단축키와 편집

        ## 저장

        문서는 입력을 멈춘 뒤 자동 저장됩니다. `Ctrl + Enter`를 누르면 즉시 저장하고 미리보기를 갱신합니다.

        ## 문서와 성좌 지도 전환

        - `Ctrl + G` : 현재 노트의 문서와 성좌 지도 전환
        - `Esc` : 성좌 지도에서 문서로 복귀

        ## 제목 수준 변경

        - `Alt + <` : 제목 수준 증가 (`##` → `#`)
        - `Alt + >` : 제목 수준 감소 (`##` → `###`)

        ## 미리보기 탐색

        미리보기의 문단을 클릭하면 대응되는 편집 위치가 중앙으로 이동합니다. 미리보기를 스크롤하면 편집기도 같은 진행률을 따라갑니다.

        ## 여러 문서 보기

        제목 오른쪽의 나란히 열기 버튼이나 탐색기 우클릭 메뉴를 사용하면 한 화면에 문서를 최대 세 개까지 열 수 있습니다.
        """)
    ];

    public IReadOnlyList<NoteInfo> Notes => _notes;

    public NoteInfo? FindByTitle(string title) => _notes.FirstOrDefault(note =>
        note.Title.Equals(title, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<VaultItem> BuildItems(IReadOnlySet<string> expandedFolders, string? query)
    {
        var normalizedQuery = query?.Trim() ?? "";
        var matches = _notes.Where(note => normalizedQuery.Length == 0
            || $"{note.Title}\n{note.Body}".Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)).ToList();
        if (normalizedQuery.Length > 0 && matches.Count == 0
            && !"Asterism 안내".Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)) return [];

        var expanded = normalizedQuery.Length > 0 || expandedFolders.Contains(FolderPath);
        var items = new List<VaultItem>
        {
            new("Asterism 안내", FolderPath, true, false, expanded, 0, null)
        };
        if (expanded)
            items.AddRange(matches.Select(note => new VaultItem(note.Title, note.Path, false, false, false, 1, note)));
        return items;
    }

    private static NoteInfo Guide(string title, string slug, string body) => new(
        title,
        $"asterism-guide://{slug}",
        body.Trim(),
        DateTime.MinValue,
        new NoteMetadata("Asterism", DateTime.MinValue, "Built-in", "Guide"),
        IsReadOnly: true);
}
