# 翻訳を PC だけで Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** translator_pc.exe alone turns the wearer's PC-mic speech into DeepL translations shown on the pet (KAT) / chatbox.

**Architecture:** Port the phone app's pure logic (Segmenter, JaEn choice, KAT encoding/display, Compose) to
`TranslatorPc.Core` with its tests; add DeepL request/parse, settings and an own-voice pipeline behind interfaces;
the exe wires sherpa-onnx SenseVoice, HttpClient DeepL and LibNMeCab kana.

**Tech Stack:** .NET 8, xUnit, NAudio 2.2.1, org.k2fsa.sherpa.onnx 2.0.1, LibNMeCab 0.10.2 + LibNMeCab.IpaDicBin.

**Spec:** `docs/superpowers/specs/2026-11-17-translator-pc-only-design.md`

## Global Constraints
- Kotlin originals are the reference: `Tools/translator/android/app/src/main/java/com/haru452/vrctranslator/core/*.kt`; port their tests too.
- Files ≤ 600 lines. The DeepL key never appears in screen output, logs or exceptions shown.
- Segmenter for the PC mic: floorMin 60, endSilenceS 1.2.
- KAT: pointer first, characters POINTER_SECONDS (0.1 s) later, step 0.25 s.

## Review Focus
- DeepL down / bad key / no network: the subtitle clears, the screen says why, the program keeps running.
- Toggle off mid-sentence: nothing half-recorded is translated later.
- Very long speech (60 s cap) and empty recognitions: no crash, nothing shown.
- Model files missing: clear message on screen, no crash.
- Chinese target: translation goes to the chatbox, not KAT.

---

### Task 1: Port the display side (Kat, Compose, KatDisplay)
**Files:** Create `TranslatorPc.Core/Kat.cs`, `Compose.cs`, `KatDisplay.cs`, `Lang.cs`; tests `TranslatorPc.Tests/KatTests.cs`, `ComposeTests.cs`, `KatDisplayTests.cs`.
- [ ] Port KatOscTests / ComposeTests / KatDisplayTests to xUnit (RED), port the code (GREEN). `dotnet test` → all pass.
- [ ] Commit `feat(pc): KAT display ported from the phone app`

### Task 2: Port speech side (Segmenter, JaEn choice)
**Files:** `TranslatorPc.Core/Segmenter.cs`, `SpeechChoice.cs`; tests ported from SegmenterTests / OwnLanguageTests.
- [ ] RED → GREEN → commit `feat(pc): segmenter and Japanese/English choice`

### Task 3: DeepL, settings, own-voice pipeline
**Files:** `TranslatorPc.Core/DeepL.cs` (`Endpoint(key)`, `Body(text, source, target)`, `Parse(json)`), `Settings.cs`
(JSON at a given path: deeplKey, targetLang, showOriginal, display, katSync), `OwnVoice.cs` (interfaces
`IRecognizer`, `ITranslator`, `IKana`; port of OwnVoicePipeline without LLM prompts); tests for each.
- [ ] Tests: free key → api-free host; body JSON; parse text; error JSON → exception without the key in its message;
  pipeline: JA → EN shown with kana original, ZH target → chatbox, translator throws → display.Error, toggle off drops.
- [ ] Commit `feat(pc): DeepL, settings and the own-voice pipeline`

### Task 4: The exe
**Files:** `TranslatorPc/SenseVoice.cs`, `DeepLClient.cs`, `MeCabKana.cs`, `Program.cs` (standalone), `publish.ps1`
(copies the model into `publish/models`), README.
- [ ] `dotnet test` all pass; publish; smoke: run exe with a WAV of Japanese speech fed through the pipeline
  (a `--test-wav` switch) and a real DeepL key from the user → translation printed.
- [ ] Commit `feat(pc): translator_pc.exe does it all — SenseVoice, DeepL, KAT`

### Task 5: Check with the user
- [ ] DeepL key entered (K), 翻訳 on, 10 sentences: subtitles right, times shown, CPU.
