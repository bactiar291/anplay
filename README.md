# AnPlay

AnPlay adalah macro recorder/player Windows statis dengan tema Blue Black Focus. Tidak ada AI, tidak ada API key, dan tidak ada request internet.

## Quick Use

- `F8`: mulai rekam langsung. Tekan `F8` lagi untuk stop rekam.
- `PrtSc`: play rekaman. Tekan `PrtSc` lagi untuk stop playback.
- `Loop replay unlimited`: kalau dicentang replay ulang terus sampai `PrtSc` atau `Stop` ditekan.
- `Save` / `Load`: simpan atau load macro JSON.
- `Gerak cursor super halus`: replay mouse dirender lebih natural mengikuti titik rekaman.
- `Blue Black Focus`: tampilan gelap biru-hitam bawaan.

## Build

Run on Windows:

```bat
build.bat
```

Output:

```text
dist\AnPlay.exe
release\AnPlay-release.zip
```

## Settings

Settings disimpan lokal:

```text
%LOCALAPPDATA%\AnPlay\settings.json
```

Macro hanya tersimpan kalau user menekan `Save`.
