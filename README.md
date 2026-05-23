# AnPlay

AnPlay adalah macro recorder/player Windows statis dengan tema Dark Focus. Tidak ada AI, tidak ada API key, dan tidak ada request internet.

## Quick Use

- `F8`: mulai rekam langsung. Tekan `F8` lagi untuk stop rekam.
- `PrtSc`: play rekaman. Tekan `PrtSc` lagi untuk stop playback.
- `Repeat loop`: ulangi replay.
- `Loop limit = 0`: ulang terus sampai `PrtSc` atau `Stop` ditekan.
- `Save` / `Load`: simpan atau load macro JSON.
- `Gerak cursor halus`: replay mouse dibuat lebih natural mengikuti titik rekaman.
- `Tema Dark Focus`: tampilan gelap bawaan.

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
