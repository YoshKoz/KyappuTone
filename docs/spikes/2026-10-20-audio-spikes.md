# Audio spikes — 2026-10-20

Throwaway probes run before Phase 1 implementation. Versions: .NET SDK 10.0.402,
NAudio 3.3.0, FFmpeg.AutoGen 9.0.1.1 with the bundled FFmpeg 9 DLLs
(`avcodec-63`, `avformat-63`, `avutil-61`, `swresample-7`), Vortice 3.8.3.

## 1. NAudio WASAPI shared output

Command: `dotnet run -c Release -- wasapi`

Output:

```
wasapi format=32 bit IEEEFloat: 48000Hz 2 channels peak=0,250 bytes=857088
spike=ok mode=wasapi
```

A 2 s 440 Hz float sine played through `WasapiOut(Shared, eventSync: true, latency: 10)`.
The provider is now `IWaveProvider.Read(Span<byte>)` in NAudio 3.x — the old
`Read(byte[], int, int)` overload no longer compiles. Byte count ≈ 2.2 s at 48 kHz
stereo f32, peak 0.250 as generated.

## 2. FFmpeg decode

Command: `dotnet run -c Release -- decode <fixture>.flac`

Output:

```
decode file=one-日本語-ąćęłńóśźż.flac sampleRate=44100 channels=1 frames=661500
spike=ok mode=decode
```

15 s mono 44.1 kHz FLAC decoded to 661,500 frames. The bundled DLLs load from
`Assets/FFmpeg` via `ffmpeg.RootPath`; the file path with Japanese and Polish characters
opens correctly. Not exercised in the spike: `swresample` conversion to 48 kHz stereo
(the production decoder owns that; covered by Task 4 tests).

## 3. Vortice D3D11 / DXGI swapchain

Command: `dotnet run -c Release -- dxgi`

Output:

```
dxgi device=Level_11_1 adapter=NVIDIA GeForce RTX 5080 factory=True
swapchain=True
spike=ok mode=dxgi
```

A D3D11 device, DXGI factory and flip-model `SwapChain1` for a plain Win32 window were
created and released successfully. The `SwapChainPanel` binding itself (`ISwapChainPanelNative`)
is not proven here; Task 9 owns that risk.

## Findings that change the plan

- NAudio 3.x requires `Span<byte>` providers; the engine's output adapter must use that sign.
- Vortice 3.8.3 is already in the app's dependency graph through Flyleaf, so no new
  visualizer dependency is needed.
- FFmpeg DLLs in the repo are FFmpeg 9 (ABI 63/61/7), matching FFmpeg.AutoGen 9.0.1.1.
