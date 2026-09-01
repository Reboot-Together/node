# Asterism

Asterism은 흩어진 기록을 자신만의 성좌로 연결하는 Windows용 로컬 Markdown 노트 앱입니다. 노트 파일은 사용자가 지정한 폴더에 그대로 저장되며, 위키 링크·백링크·성좌 지도·Markdown 편집과 미리보기·ChatGPT 공유 링크 가져오기를 지원합니다.

앱 안의 `Asterism 안내` 폴더에는 시작 방법, Markdown, 링크, 성좌 지도, 단축키를 설명하는 읽기 전용 문서가 포함됩니다. 이 문서는 사용자 저장소에 파일을 만들지 않으며 앱 업데이트와 함께 자동으로 갱신됩니다.

Markdown 미리보기는 언어가 지정된 코드 블록의 문법을 오프라인으로 강조합니다. 동일한 부모 아래의 같은 수준 제목은 아코디언처럼 하나씩 열리며, `모두 펼치기`를 누르면 전체 문서를 한 번에 볼 수 있습니다.

## 로컬 AI 연결

내장된 다국어 임베딩 모델이 노트의 제목과 본문을 이 PC에서 직접 분석해 의미가 비슷한 노트를 추천합니다. 인터넷 연결, 계정, API 키가 필요하지 않으며 노트 본문은 외부로 전송되지 않습니다.

- 추천의 `＋ 링크`를 누르면 현재 Markdown에 `[[노트 제목]]` 링크가 추가됩니다.
- 로컬 그래프의 흐린 실선은 AI가 제안한 관계이고, 밝은 실선은 Markdown에 실제로 저장된 링크입니다.
- Markdown 파일이 원본이며, 벡터는 `%LOCALAPPDATA%\Asterism\semantic-index.db`에 재생성 가능한 캐시로만 저장됩니다. 기존 Node 설치의 캐시는 중복 생성을 피하기 위해 그대로 재사용할 수 있습니다.
- 변경되지 않은 본문 조각은 다시 계산하지 않습니다.

## 개발

```powershell
dotnet restore Checks/Asterism.Checks.csproj
dotnet build Asterism.csproj -c Release
dotnet run --project Checks/Asterism.Checks.csproj -c Release
```

## 릴리스

릴리스 워크플로를 수동 실행하고 `MAJOR.MINOR.PATCH` 버전을 지정하면 GitHub Actions가 Windows x64 자체 포함 빌드를 만들고, Inno Setup 설치 파일과 SHA-256 체크섬을 GitHub Release에 등록합니다.

앱의 **업데이트** 버튼은 안정화 릴리스 목록을 읽어 원하는 버전을 선택하게 합니다. 설치 파일은 GitHub가 제공하는 SHA-256 값과 대조한 뒤에만 실행됩니다.
