# StuderBrowser

Eigener Browser auf Basis von .NET 10 WPF und WebView2 (Chromium-Engine, Evergreen-Runtime von Edge).

## Bauen

```powershell
.\build.ps1                          # publish nach dist\
.\build.ps1 -ImportFirefoxBookmarks  # zusätzlich Firefox-Lesezeichen nach dist\profile\bookmarks.json
```

Gestartet wird `dist\StuderBrowser.exe`. Profil (Cookies, Logins, Verlauf), Lesezeichen und deren Favicons liegen portabel unter `dist\profile\`.

Das App-Icon wird mit `python tools\generate_icon.py src\StuderBrowser\Resources\logo.ico` erzeugt (benötigt Pillow).

## Tastenkürzel

| Kürzel | Aktion |
| --- | --- |
| Ctrl+T / Ctrl+W | Tab öffnen / schliessen |
| Ctrl+Tab / Ctrl+Shift+Tab | Nächster / vorheriger Tab |
| Ctrl+L | Adressleiste |
| Ctrl+D | Lesezeichen setzen / entfernen |
| Alt+Links / Alt+Rechts | Zurück / Vorwärts |
| F5 | Neu laden |
| F12 | DevTools |

Mittelklick auf ein Lesezeichen öffnet es in einem neuen Tab, Mittelklick auf einen Tab schliesst ihn.
Einen Tab aus der Tab-Leiste ziehen öffnet ihn in einem eigenen Fenster; zieht man ihn auf die Tab-Leiste eines
anderen Fensters, rastet er dort wieder ein. Lesezeichen in der Leiste lassen sich per Drag & Drop umsortieren.

Installiert Edge Update eine neue WebView2-/Chromium-Version, erscheint rechts in der Adressleiste der Button
„Aktualisieren“. Er startet den Browser neu und öffnet alle Tabs wieder.
