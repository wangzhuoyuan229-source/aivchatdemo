$ErrorActionPreference = 'Stop'

# API keys and local data stay under %LOCALAPPDATA%\ChatApp.
dotnet publish .\ChatApp.UI\ChatApp.UI.csproj `
  -c Release `
  -p:PublishProfile=Win-x64 `
  -o .\publish\win-x64

Write-Host "Published to .\publish\win-x64\ChatApp.UI.exe"
