# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

ZarthGB is a C# WinForms Game Boy (DMG) emulator targeting `net5.0-windows`. See `AGENTS.md` for
code-style and repository-hygiene conventions (both files apply).

## Commands

- Build: `dotnet build ZarthGB.sln` (compile check).
- Run: `dotnet run --project ZarthGB.csproj`, or launch with a ROM path as the first argument.
- There is **no test project**. Correctness is validated by loading Blargg CPU test ROMs manually:
  edit `ZarthEmulator_Load` in `ZarthEmulator.cs` to `LoadCartridge(...)` the desired ROM (the
  commented list there records which Blargg tests pass). ROMs must live next to the executable —
  see the `<None Update="...gb">` copy entries in `ZarthGB.csproj`.

## Architecture

The emulator is a hand-written interpreter. All hardware components share a single `Memory`
instance, which doubles as the interconnect: reads/writes to memory-mapped I/O registers are how
components communicate, rather than direct method calls between CPU/Video/Sound.

**Composition** — `Program.cs` → `ZarthEmulator` (Form) → `Emulator` → `Cpu`, `Video`, `Memory`,
`Cartridge`. `Emulator` wires them together and exposes `RunStep()`, key setters, and the
`Framebuffer`.

**Execution model** (`ZarthEmulator.cs`) — a background thread spins `emulator.RunStep()` in a
tight loop (honoring a `CancellationToken` so it stops on form close); a WinForms `Timer` at
~59.7 Hz polls `IsFrameReady` and repaints. Frame pacing is done *inside* `Video.Step()` at VBlank
via `PaceFrame()`. `RunStep()` calls `cpu.Step()` then `video.Step()`.

**Frame pacing / audio-master clock** (`Video.PaceFrame`) — the sound device is the authoritative
timebase. `Sound` exposes `SamplesConsumed` (stereo frames the output device has actually pulled);
`PaceFrame` advances an emulated-sample counter each frame and waits until emulation is at most a
~250 ms lead ahead of `SamplesConsumed`, slaving emulation speed to the sound card's crystal so
audio and the game never drift apart. If the audio device is not running (or stalls), it falls back
to a precise self-correcting wall-clock pacer (sleep + short spin) so the emulator never freezes.
`Program.Main` calls `timeBeginPeriod(1)` so `Thread.Sleep` in that fallback is accurate.

**Memory-as-bus** (`Memory.cs`) — the `this[int address]` indexer is the heart of the system and
contains most of the "wiring" logic:
- Boot ROM (`dmgBoot`) is overlaid on `0x0000–0x00FF` until `0xFF50` is written.
- MBC1 bank switching is handled in the setter (writes to `0x0000–0x7FFF` are control registers,
  not memory). Only `Plain` and `Mbc1` cartridge types are supported (`Cartridge.Load` throws
  otherwise).
- Writes with side effects are dispatched here: VRAM writes call `UpdateTile` (decoding 2bpp tile
  data into the `Tiles[tile,y,x]` array up front), OAM DMA (`0xFF46`), palette registers
  (`0xFF47–49`), timer config (`0xFF07`), and sound triggers (`0xFF14/19/1E/23`).
- `IncrementDiv()` drives the DIV timer and kicks the sound device (`EnsurePlaying`) periodically.
  Audio is *pulled* by the device, not pushed from here (see Sound below).

**CPU** (`Cpu.cs`, ~2500 lines) — 8-bit registers exposed with 16-bit pair accessors (`AF`, `BC`,
`DE`, `HL`), flags packed in `Flags`. `Step()` runs interrupt handling, then fetches and executes
one opcode via a large `switch`. Instruction timing comes from the `normalInstructionHalfTicks` /
`prefixInstructionHalfTicks` tables (values are *half*-ticks, shifted left by 1). The `Ticks`
setter is where the DIV register and the configurable timer (`0xFF05/06/07`) are advanced.
Interrupt constants (`InterruptsVblank`, etc.) are `public const` and referenced by `Video` and
`Memory` to raise interrupts by ORing into `0xFF0F`.

**Video** (`Video.cs`) — a scanline GPU cycling through `Oam → VRam → HBlank` per line and
`VBlank` at line 143, driven by tick deltas. `RenderScanline` draws background/window tiles
(reading the pre-decoded `memory.Tiles`) then sprites (via the `Sprite` helper, which is a typed
view over a 4-byte OAM entry). Output is written as `System.Drawing.Color` into the shared
`Framebuffer`; the Form maps those four gray levels to brushes when painting.

**Sound** (`Sound.cs`) — a pull-based APU (NAudio). `Sound` is a single mixing `ISampleProvider`
(`ApuProvider`) that the output device *pulls* from one stereo frame at a time, so the device clock
is the single timebase (no push/buffer over/underruns). Four persistent channel objects
(`SquareChannel` ×2 with sweep on ch1, `WaveChannel`, `NoiseChannel`) hold their own phase and
synthesize at native 48 kHz via phase accumulators; a 512 Hz frame sequencer clocks
length/envelope/sweep. Channels read the `0xFF10–0xFF26` register block live; trigger register
writes (`StartSoundN`, from the `Memory` setter) set volatile flags applied on the audio thread.
`NextFrame` mixes the four channels (NR51 routing, NR50 master volume) and writes NR52 status back.
Device selection falls back through WaveOut/WASAPI/DirectSound (16-bit PCM preferred); the device is
started once and stopped/disposed on shutdown (`Stop()`). `GBSignalGenerator.cs` (the old
push-based per-burst synth) has been removed.

## Notes

- `Cpu.Step()` contains a `pcTrace` ring buffer and several `PC = PC` / debug `Debug.Print` lines —
  these are dormant debugging hooks (breakpoint anchors), not functional logic.
- Serial-transfer handling is intentionally stubbed out (commented) — 2-player link games hang.
- The DMG palette is fixed to four grays; there is no Game Boy Color support despite the folder name.
