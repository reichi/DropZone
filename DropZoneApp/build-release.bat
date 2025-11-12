@echo off
setlocal
cd /d %~dp0
dotnet restore
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true --self-contained true
set P=bin\Release\net8.0-windows\win-x64\publish
if exist "%P%" (
  pushd "%P%"
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path * -DestinationPath ..\..\..\..\DropZoneApp_v22_win-x64_publish.zip -Force"
  popd
  echo.
  echo Publish ZIP erstellt: DropZoneApp_v22_win-x64_publish.zip
  echo Pfad: %cd%\DropZoneApp_v22_win-x64_publish.zip
) else (
  echo Publish-Ordner nicht gefunden!
)
endlocal
