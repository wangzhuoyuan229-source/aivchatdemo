$ErrorActionPreference = 'Stop'

# API keys and local data stay under %LOCALAPPDATA%\ChatApp.
dotnet publish .\ChatApp.UI\ChatApp.UI.csproj `
  -c Release `
  -p:PublishProfile=Win-x64 `
  -o .\publish\win-x64

$knowledgePath = '.\publish\win-x64\BundledKnowledge'
if (-not (Test-Path $knowledgePath -PathType Container)) {
  throw 'Bundled knowledge was not copied to the publish output.'
}
$knowledgeCount = (Get-ChildItem $knowledgePath -File -Recurse).Count
Write-Host "Published to .\publish\win-x64 with $knowledgeCount bundled knowledge files. Distribute the complete folder, not only ChatApp.UI.exe."
