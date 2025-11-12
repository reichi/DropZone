param([string]$Rid = "win-x64")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root
dotnet restore
dotnet publish -c Release -r $Rid -p:PublishSingleFile=true --self-contained true
$pub = Join-Path $root "bin\Release\net8.0-windows\$Rid\publish"
$out = Join-Path $root "DropZoneApp_v22_${Rid}_publish.zip"
if (Test-Path $pub) {
  if (Test-Path $out) { Remove-Item $out -Force }
  Compress-Archive -Path (Join-Path $pub "*") -DestinationPath $out -Force
  Write-Host "Publish ZIP erstellt: $out"
} else {
  Write-Host "Publish-Ordner nicht gefunden: $pub"
}
