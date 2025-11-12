# DropZoneApp v22 (fixed)
- Tray‑Umschalter „Beim Schließen wirklich beenden“ (sonst: Schließen → Tray).
- Pulse‑Animation nach erfolgreichem Drop (Drop‑Zone & Dock).
- Farbe & Rahmenstärke für Drop‑Zone, Dock, Hot‑Corner einstellbar.
- Settings immer im Vordergrund.
- Dateinamen‑Sanitizing (Leerzeichen/ungültige → `_`).
- Restore beim Start (Transparenz/Größe/Position/Farben).
- Outlook Drops: Anhänge & komplette Mails (.msg).
- Dock klick‑durchlässig, STRG halten zum Verschieben.
- Autostart robust (Registry + Startup‑Shortcut, `--minimized`).

## Build
```powershell
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true --self-contained true
```
