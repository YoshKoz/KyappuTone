# KyappuTone Phase 1 — Audio Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the inherited playback path with an in-process C# engine that owns decoding, a 10-band EQ, dual-deck crossfade, gapless advance, a limiter and an FFT tap — then surface it through the existing `IMediaBackend` abstraction without breaking the shell integration.

**Architecture:** New `KyappuTone.Audio` project (Windows-targeted) holds decoding and WASAPI output. Pure DSP (biquads, EQ, crossfade mixer, limiter, FFT) lives in `KyappuTone.Core.Audio` and is fully unit-tested. A `KyappuAudioBackend` implements the existing `IMediaBackend` and becomes the default for audio files; Flyleaf stays as the fallback for containers the engine rejects. Visualizers read a lock-free FFT tap.

**Tech Stack:** .NET 10, FFmpeg.AutoGen 9.0.1.1 + bundled LGPL FFmpeg DLLs, NAudio.Wasapi 3.3.0, FftSharp 2.2.0, Vortice.Direct2D1/DXGI 3.8.3 (already transitively present), xUnit + Shouldly.

**Spec:** `docs/superpowers/specs/2026-10-20-merged-music-player-design.md`

## Global Constraints

- Windows only, x64 and ARM64; minimum OS `10.0.18362.0`; target `net10.0-windows10.0.26100.0` for Windows-facing projects, `net10.0` for pure DSP.
- GPLv3; no Shiranami code or assets.
- No paid license or key required to build or run.
- Audio thread must not allocate after warm-up, must not take locks held by UI code, and must not throw out of the render callback.
- Single process. No helper service.
- Existing shell integration must keep working: SMTC, tray, taskbar overlay, thumbnail toolbar, lock screen, lyrics sync, scrobbling-free.
- All new behaviour behind `IMediaBackend`; user can switch back to Flyleaf in settings (`AudioBackend` setting) until Phase 1 exits.

## Review Focus

1. A file that FFmpeg opens but cannot decode end-to-end (truncated/fake `.flac`) — playback must fail visibly and leave the app usable, never hang the audio thread.
2. Device unplugged or sample-rate change mid-playback — engine must recover or pause cleanly, not raise `WasapiOut` exceptions into the UI.
3. EQ pushed to +12 dB on all bands with a loud master — limiter must prevent clipping (no samples outside [-1, 1]) and output must not distort into NaN.
4. Track change during a crossfade — both decks must stop, the new track must start exactly once, and the position clock must not jump backwards.
5. Sleep timer firing while paused or during a crossfade — must pause once, clear itself, and not resume automatically.

## Task 1: Throwaway spikes

**Files:**
- Create: `spikes/README.md`
- Create: `spikes/AudioSpike/AudioSpike.csproj`, `Program.cs`
- Modify: none in the app

**Interfaces:**
- Consumes: bundled FFmpeg DLLs in `Assets/FFmpeg`, NAudio.Wasapi, Vortice 3.8.3
- Produces: written findings in `docs/spikes/2026-10-20-audio-spikes.md`; no production code

- [ ] **Step 1: Prove NAudio WASAPI shared output on .NET 10**

`spikes/AudioSpike/AudioSpike.csproj` targets `net10.0-windows10.0.26100.0`, references `NAudio.Wasapi` 3.3.0. `Program.cs` builds a 2-second 440 Hz sine as `IWaveProvider` at 48 kHz stereo and plays it through `WasapiOut(AudioClientShareMode.Shared, 10)`, printing `format=...` and the peak level, then exits `0` when playback stops.

Run it. Expected: audible tone (or a headless success line if no output device), exit code 0, no `COMException`.

- [ ] **Step 2: Prove FFmpeg decode through FFmpeg.AutoGen**

In the same spike project reference `FFmpeg.AutoGen` 9.0.1.1, set `ffmpeg.RootPath` to the repo `Assets/FFmpeg` folder, open `%TEMP%\kyapputone-fixtures\one-日本語-ąćęłńóśźż.flac`, decode to `f32` with `swresample`, print `sampleRate=... channels=... frames=...`, and assert decoded duration is within 0.1 s of 15 s.

Run it. Expected: exit 0 with frame count ≈ 720000.

- [ ] **Step 3: Prove a Vortice D3D11/DXGI swapchain can be created**

Add a mode that creates a `D3D11` device (`BgraSupport`), a `DXGI` factory, and a `SwapChain1` for a hidden `HWND` created with `CreateWindowEx`, then presents one clear frame and releases everything. Print `swapchain=ok`.

Expected: exit 0. This proves the Vortice version already in the dependency graph works on this machine; the `SwapChainPanel` binding itself is validated in Task 9.

- [ ] **Step 4: Record findings and delete the spike project**

Write `docs/spikes/2026-10-20-audio-spikes.md` with the exact versions, the commands run, exit codes, and any deviations (for example, if FFmpeg DLL names differ). Keep `spikes/README.md` noting that the folder is throwaway and unbuilt. Delete the `AudioSpike` project sources. Commit.

## Task 2: DSP primitives in Core

**Files:**
- Create: `src/KyappuTone.Core/Audio/Biquad.cs`
- Create: `src/KyappuTone.Core/Audio/TenBandEqualizer.cs`
- Create: `src/KyappuTone.Core/Audio/EqPreset.cs`
- Test: `tests/KyappuTone.Core.Tests/Audio/BiquadTests.cs`
- Test: `tests/KyappuTone.Core.Tests/Audio/TenBandEqualizerTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces:
  - `public sealed class Biquad { public Biquad(float sampleRate, float frequency, float q, float gainDb); public float Process(float x); public void Reset(); public static (float Magnitude, float Phase) ResponseAt(float frequency); }`
  - `public sealed class TenBandEqualizer { public TenBandEqualizer(int sampleRate, int channels); public float PreampDb { get; set; } public void SetBandGain(int band, float gainDb); public float[] BandGainsDb { get; } public void Apply(Span<float> interleaved); public void Reset(); public static IReadOnlyList<float> CenterFrequencies { get; } }` (31.5, 63, 125, 250, 500, 1k, 2k, 4k, 8k, 16k)
  - `public static class EqPreset { public static IReadOnlyList<(string Name, float[] Gains)> All { get; } }` with 13 presets

- [ ] **Step 1: Write failing biquad tests**

```csharp
[Fact] public void PeakingBoost_AtCenterFrequency_HasExpectedGain()
[Fact] public void PeakingCut_AtCenterFrequency_HasExpectedGain()
[Fact] public void ZeroGain_IsTransparent()   // output equals input within 1e-6
[Fact] public void ResponseAt_MatchesMeasuredSineAmplitude()
```
The first three assert within 0.2 dB of the analytic `10^(gainDb/20)` using `Biquad.ResponseAt` and a 48 kHz sine passed through `Process`.

- [ ] **Step 2: Run them, expect compile failure**

`dotnet test tests/KyappuTone.Core.Tests -v:m` → error: `Biquad` not found.

- [ ] **Step 3: Implement the RBJ peaking biquad and the 10-band EQ**

Use the RBJ cookbook peaking EQ, Q = 1.41, gain ±12 dB clamp. `TenBandEqualizer.Apply` processes interleaved samples with one `Biquad` per band per channel, preamp as linear gain, and skips inactive work when all gains are 0 and preamp is 0.

- [ ] **Step 4: Write and pass EQ transparency/preset tests**

```csharp
[Fact] public void FlatEq_LeavesSignalUnchanged()
[Fact] public void Preset_All_Has13Entries_And_Flat_IsAllZero()
[Fact] public void Apply_ClampsGainsTo_MinusTwelve_PlusTwelve()
```

- [ ] **Step 5: Commit**

`git commit -m "feat(core): add biquad and 10-band equalizer"`

## Task 3: Crossfade mixer, limiter, FFT tap

**Files:**
- Create: `src/KyappuTone.Core/Audio/CrossfadeMixer.cs`
- Create: `src/KyappuTone.Core/Audio/SoftLimiter.cs`
- Create: `src/KyappuTone.Core/Audio/FftTap.cs`
- Test: `tests/KyappuTone.Core.Tests/Audio/CrossfadeMixerTests.cs`
- Test: `tests/KyappuTone.Core.Tests/Audio/SoftLimiterTests.cs`
- Test: `tests/KyappuTone.Core.Tests/Audio/FftTapTests.cs`

**Interfaces:**
- `CrossfadeMixer(int channels)`: `void Begin(ReadOnlySpan<float> outgoing, ReadOnlySpan<float> incoming, int fadeFrames)`, `void Render(Span<float> destination, Func<Span<float>> pullA, Func<Span<float>> pullB)`, `bool IsFading`.
  Simpler contract used by the engine: mixer holds gains `GainA`/`GainB` advanced per frame with an equal-power ramp; the engine supplies deck buffers.
- `SoftLimiter(float ceiling = 0.98f)`: `void Process(Span<float> buffer)`; lookahead 64 frames, hard ceiling after soft knee.
- `FftTap(int fftSize = 2048, int sampleRate = 48000)`: `void Push(ReadOnlySpan<float> interleavedStereo)`, `bool TryRead(Span<float> magnitudes)`, `int BinCount`.

- [ ] **Step 1: Failing tests**

```csharp
[Fact] public void Crossfade_GainsSumToUnity_ForWholeFade()   // equal-power: cos^2 + sin^2 ~= 1
[Fact] public void Crossfade_ReachesFullIncoming_AtEnd()
[Fact] public void Limiter_KeepsSamplesWithinCeiling()        // 1.5 amplitude input
[Fact] public void Limiter_LeavesQuietSignalUnchanged()
[Fact] public void FftTap_SinePeakLandsInExpectedBin()        // 1 kHz at 48 kHz, 2048 FFT -> bin 42..43
[Fact] public void FftTap_ReportsSilenceAsLowMagnitude()
```

- [ ] **Step 2: RED run** — `dotnet test` fails with missing types.

- [ ] **Step 3: Implement** the equal-power ramp (`cos`/`sin` of π/2 progress), the lookahead limiter (delay line, gain envelope, release coefficient), and the FFT tap (FftSharp window + magnitude, lock-free single-producer/single-consumer ring of 2048 floats).

- [ ] **Step 4: GREEN run** — all tests pass.

- [ ] **Step 5: Commit** — `feat(core): add crossfade, limiter and FFT tap`.

## Task 4: Audio project, decoder, offline render

**Files:**
- Create: `src/KyappuTone.Audio/KyappuTone.Audio.csproj`
- Create: `src/KyappuTone.Audio/Decoding/FfmpegDecoder.cs`
- Create: `src/KyappuTone.Audio/Decoding/DecodedFormat.cs`
- Create: `src/KyappuTone.Audio/IAudioOutput.cs`, `src/KyappuTone.Audio/NullAudioOutput.cs`
- Create: `tests/KyappuTone.Audio.Tests/KyappuTone.Audio.Tests.csproj`
- Test: `tests/KyappuTone.Audio.Tests/FfmpegDecoderTests.cs`

**Interfaces:**
- `FfmpegDecoder : IDisposable` — `static FfmpegDecoder Open(string path)`, `DecodedFormat Format`, `TimeSpan Duration`, `int Read(Span<float> destination)`, `void Seek(TimeSpan)`, `void Dispose()`.
- `DecodedFormat` — `int SampleRate`, `int Channels`, `const int TargetSampleRate = 48000`, `int TargetChannels = 2`.
- `IAudioOutput` — `void Start(Func<Span<float>> render, int sampleRate, int channels)`, `void Stop()`, `double LatencyMs { get; }`.

Decoder output is always interleaved `f32`, 48 kHz, stereo, via `swresample`.

- [ ] **Step 1: Create projects and register in the solution**

`KyappuTone.Audio` targets `net10.0-windows10.0.26100.0`, references `KyappuTone.Core`, `FFmpeg.AutoGen` 9.0.1.1, `NAudio.Wasapi` 3.3.0, `FftSharp` 2.2.0. Tests target the same TFM and reference the Audio project plus xUnit/Shouldly as in Core tests. Add both to `KyappuTone.sln`.

- [ ] **Step 2: Failing decoder tests using generated fixtures**

Tests create a 2 s PCM WAV, a mono 44.1 kHz WAV, and a 5 s silent WAV in `%TEMP%` with `BinaryWriter`, then:

```csharp
[Fact] public void Open_ReportsDuration_And_NormalizesFormat()
[Fact] public void Read_ReturnsInterleavedStereo48k()
[Fact] public void Seek_ChangesPosition()
[Fact] public void Read_PastEnd_ReturnsZero()
[Fact] public void Open_GarbageFile_ThrowsAudioFormatException()
```

- [ ] **Step 3: RED run** — `dotnet test tests/KyappuTone.Audio.Tests` fails to compile.

- [ ] **Step 4: Implement the decoder** with `avformat_open_input`, `av_find_best_stream`, `avcodec`, `swr_alloc_set_opts2`, an `AVAudioFifo` for block alignment, `av_seek_frame` + `avcodec_flush_buffers` for seeks, and `ffmpeg.RootPath` pointed at `Assets/FFmpeg`. Convert errors to `AudioFormatException`.

- [ ] **Step 5: GREEN run** and commit `feat(audio): add FFmpeg decoder with normalized output`.

## Task 5: Engine transport, mixing, gapless and crossfade

**Files:**
- Create: `src/KyappuTone.Audio/AudioEngine.cs`
- Create: `src/KyappuTone.Audio/Deck.cs`
- Test: `tests/KyappuTone.Audio.Tests/AudioEngineTests.cs`

**Interfaces:**
- `AudioEngine` — `AudioEngine(IAudioOutput output, int sampleRate = 48000)`, `void Load(string path, bool play = false)`, `void Play()`, `void Pause()`, `void Seek(TimeSpan)`, `void SetNext(string path, TimeSpan crossfade)`, `TimeSpan Position { get; }`, `TimeSpan Duration { get; }`, `bool IsPlaying { get; }`, `TenBandEqualizer Equalizer { get; }`, `FftTap Fft { get; }`, `event EventHandler<TimeSpan>? PositionChanged`, `event EventHandler? TrackEnded`.
- All public methods are thread-safe against the render thread; render path allocates nothing after `Load`.

- [ ] **Step 1: Failing engine tests with `NullAudioOutput`**

`NullAudioOutput` drives `render` on a caller-controlled timer so tests deterministically pull N frames.

```csharp
[Fact] public void Load_Then_Play_AdvancesPosition_ByRenderedFrames()
[Fact] public void Pause_StopsAdvancing()
[Fact] public void Seek_MovesPosition_WithinTolerance()
[Fact] public void GaplessAdvance_RaisesTrackEnded_Once_And_StartsNext()
[Fact] public void Crossfade_OverlapsDecks_AndKeepsPeakBelowCeiling()
[Fact] public void Load_WhileCrossfading_StopsBothDecks_StartsNewTrackOnce()
```

- [ ] **Step 2: RED run.**

- [ ] **Step 3: Implement** deck state machine (`Idle`, `Playing`, `FadingOut`), a single render callback that pulls deck buffers, runs per-deck EQ (shared coefficient set), mixes with the crossfade ramp, runs the limiter, pushes to `FftTap`, and updates the position clock from frames rendered.

- [ ] **Step 4: Real-time test** — render 60 s of a fixture through the engine with all bands at +6 dB in under 10 s wall clock (offline), assert peak ≤ 0.98 and no NaN.

- [ ] **Step 5: Commit** — `feat(audio): add transport engine with gapless and crossfade`.

## Task 6: WASAPI output and backend integration

**Files:**
- Create: `src/KyappuTone.Audio/WasapiAudioOutput.cs`
- Create: `Common/Services/Backends/KyappuAudioBackend.cs`
- Modify: `Common/Enums.cs` (`BackendType.KyappuAudio`)
- Modify: `Common/Services/MusicPlayer.cs` (construct, select, and fall back)
- Modify: `Common/Models.cs` / player-type mapping so audio codecs route to the new backend
- Test: `tests/KyappuTone.Audio.Tests/WasapiAudioOutputTests.cs` (device-present guard)

**Interfaces:**
- `WasapiAudioOutput : IAudioOutput` — `WasapiAudioOutput(string? deviceId = null)`, `double LatencyMs`, event `EventHandler<AudioDeviceLostEventArgs>? DeviceLost`.
- `KyappuAudioBackend : IMediaBackend` — wraps `AudioEngine` + `WasapiAudioOutput`; maps `CurTimeTicks`, `Volume`, `IsMuted`, raises `StateChanged`/`OpenCompleted`/`PositionChanged` exactly as `FlyleafMediaBackend` does.

- [ ] **Step 1: Failing integration test** for `WasapiAudioOutput` skipped when no render device exists (`[Fact(Skip=...)]` dynamic guard is acceptable if it prints the reason).

- [ ] **Step 2: Implement WASAPI output** with `WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: 10)`; render callback writes interleaved f32; on `DeviceLost`, raise the event and let the backend pause and surface a toast.

- [ ] **Step 3: Implement the backend** and select it for audio codecs; keep Flyleaf for anything it refuses (`AudioFormatException` triggers fallback once and logs).

- [ ] **Step 4: Manual verification** — play the four fixtures through the new backend, check seek, pause, resume, next/previous, SMTC metadata still updates, volume modes still work.

- [ ] **Step 5: Commit** — `feat: play audio through the in-process engine by default`.

## Task 7: EQ surface, presets and persistence

**Files:**
- Modify: `Views/SettingsPage.xaml`, `Views/SettingsPage.xaml.cs`
- Create: `Common/Services/AudioSettingsService.cs`
- Modify: `Common/Enums.cs` (`LocalSave` keys), `Common/Models.cs` if needed
- Test: `tests/KyappuTone.Core.Tests/Audio/EqPresetTests.cs`

**Interfaces:**
- `AudioSettingsService` — `float PreampDb { get; set; }`, `float[] BandGains { get; }`, `string ActivePreset { get; set; }`, `void ApplyTo(TenBandEqualizer eq)`, `event EventHandler? Changed`; persists to `ApplicationData.Current.LocalSettings` as a compact string.
- UI: EQ expander with 10 sliders (−12…+12 dB), preamp slider, preset combo (13 presets + Custom), enable toggle (bypass when flat).

- [ ] **Step 1: Failing test** — presets round-trip through a serialized string and `ApplyTo` sets every band.
- [ ] **Step 2: RED**, implement, **GREEN**.
- [ ] **Step 3: Build the UI**, wire `Changed` to the engine's `Equalizer`, restore on startup.
- [ ] **Step 4: Manual check** — change bands while playing, no clicks; restart keeps values; flat bypass is transparent.
- [ ] **Step 5: Commit** — `feat: add equalizer settings and presets`.

## Task 8: Sleep timer

**Files:**
- Create: `src/KyappuTone.Core/Audio/SleepTimer.cs`
- Modify: `Views/MainPage.xaml` + code-behind (menu entry and countdown)
- Test: `tests/KyappuTone.Core.Tests/Audio/SleepTimerTests.cs`

**Interfaces:**
- `SleepTimer(TimeProvider time)` — `void Start(TimeSpan after, Action onElapsed)`, `void Cancel()`, `bool IsRunning`, `TimeSpan Remaining`, `event EventHandler? Tick`.

- [ ] **Step 1: Failing tests** using `FakeTimeProvider` or a manual clock: fires once, cancel prevents firing, remaining decreases, restart resets.
- [ ] **Step 2: RED → implement → GREEN.**
- [ ] **Step 3: UI** — presets (15/30/45/60/90 min, custom 1–600) and live countdown in the player overflow; pause once on elapse, clear state.
- [ ] **Step 4: Commit** — `feat: add sleep timer`.

## Task 9: Visualizer host and first three styles

**Files:**
- Create: `Controls/VisualizerPanel.cs` + `.xaml` (SwapChainPanel host)
- Create: `Common/Visuals/D2DRenderer.cs`, `IVisualizerStyle.cs`
- Create: `Common/Visuals/Styles/BarsStyle.cs`, `WaveformStyle.cs`, `MirrorStyle.cs`
- Modify: `Views/MainPlayerPage.xaml` (host + style picker)
- Test: `tests/KyappuTone.Core.Tests/Audio/VisualizerFeedTests.cs` (band mapping only)

**Interfaces:**
- `IVisualizerStyle` — `void Draw(CanvasRenderTarget target, ReadOnlySpan<float> magnitudes, TimeSpan position)`, `string Name`.
- Host: 60 Hz `DispatcherQueueTimer`, reads `FftTap.TryRead`, swaps D2D buffers into the `SwapChainPanel` via `ISwapChainPanelNative.SetSwapChain`.

- [ ] **Step 1: Failing band-mapping test** — 64 log-spaced bars map the 1 kHz bin to the same bar regardless of FFT size.
- [ ] **Step 2: RED → implement mapping in Core → GREEN.**
- [ ] **Step 3: Wire the host** and draw three styles; first frame must appear within 200 ms of opening the player page.
- [ ] **Step 4: Manual check** — resize window, switch styles, toggle low-perf mode (timer stops), CPU stays under 8 % while playing.
- [ ] **Step 5: Commit** — `feat: add visualizer host with bars, waveform and mirror styles`.

## Task 10: Soak test, perf record, tag

**Files:**
- Create: `scripts/soak-audio.ps1`
- Create: `docs/perf/2026-10-2x-phase1-baseline.md`
- Test: full suites

- [ ] **Step 1: Soak harness** — runs the app with the fixture playlist for 1 hour, samples CPU and working set every 30 s into CSV, and greps the event log for `KyappuTone` crashes; fails on any crash or on zero progress.
- [ ] **Step 2: Run 15 minutes** (shortened for CI-time; the 1 h run is manual before release), record: CPU average with EQ only, with EQ + visualizer, working set, xrun count from engine diagnostics, position drift.
- [ ] **Step 3: Full test suite** — `dotnet test` both test projects, all green.
- [ ] **Step 4: Update spec perf table** with measured numbers, commit, tag `v0.1.0-alpha`.

## Self-review notes

- Spec Phase 1 also lists lyrics timing against the new clock and the audio settings page; both are covered by Task 6 (clock) and Task 7 (settings) rather than separate tasks.
- 12 visualizer styles remain the Phase 1 target; this plan lands the host plus 3 styles and leaves the remaining 9 as a follow-up plan once the host is proven on real hardware.
- Vortice spike in Task 1 validates the library, not the `SwapChainPanel` binding; Task 9 owns that risk.
- No task touches the Shiranami-derived feature set; acquisition and library intelligence stay in later phases.
