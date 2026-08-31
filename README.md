# Node

Node는 Windows용 로컬 Markdown 노트 앱입니다. 노트 파일은 사용자가 지정한 폴더에 그대로 저장되며, 위키 링크·백링크·로컬 그래프·제목 단위 접기와 편집·ChatGPT 공유 링크 가져오기를 지원합니다.

## 개발

```powershell
dotnet restore Checks/Node.Checks.csproj
dotnet build Node.csproj -c Release
dotnet run --project Checks/Node.Checks.csproj -c Release
```

## 릴리스

`vMAJOR.MINOR.PATCH` 형식의 태그를 푸시하면 GitHub Actions가 Windows x64 자체 포함 빌드를 만들고, Inno Setup 설치 파일과 SHA-256 체크섬을 GitHub Release에 등록합니다.

앱의 **업데이트** 버튼은 안정화 릴리스 목록을 읽어 원하는 버전을 선택하게 합니다. 설치 파일은 GitHub가 제공하는 SHA-256 값과 대조한 뒤에만 실행됩니다.
