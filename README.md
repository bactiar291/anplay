# AnPlay

AnPlay is a small Windows macro recorder/player with optional Groq Vision auto-stop.

## Quick Use

- `F8`: start recording. Press `F8` again to stop recording.
- `PrtSc`: play the recorded macro. Press `PrtSc` again to stop playback or AI loop.
- `Rec F8`: same as pressing `F8`.
- `Play PrtSc`: same as pressing `PrtSc`.
- `Save` / `Load`: save or load macro JSON.
- `Start AI`: repeat the macro and use Groq Vision to stop when the screen condition is met.

## AI Stop Condition Examples

```text
berhenti kalau sudah tidak error Unable to send a verification code
kalau berhasil masuk nomornya dan hanya menunggu OTP maka berhenti
berhenti kalau muncul input OTP, waiting for code, verification code, success, banned, atau blocked
```

AnPlay uses a conservative verifier. It stops on clear success/OTP states and also stops on terminal failure states such as `Banned`, `Blocked`, `Suspended`, or `Too many attempts` to avoid continuing harmful loops.

## Build

Run on Windows:

```bat
build.bat
```

Output:

```text
dist\AnPlay.exe
```

## Settings

Settings and the Groq API key are stored locally:

```text
%LOCALAPPDATA%\AnPlay\settings.json
```

The API key is protected with Windows DPAPI for the current user. It is not embedded into the EXE.
