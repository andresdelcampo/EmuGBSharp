# EmuGBSharp

EmuGBSharp is an educational Game Boy (DMG) emulator written in C# using Windows Forms. It is intended as a readable reference for learning about CPU emulation, memory banking, graphics timing, input, and Game Boy audio—not as a replacement for mature emulators.

## Current status

- Passes Blargg's CPU instruction tests.
- Supports plain cartridges and MBC1 banking, including ROMs larger than 32 KB.
- Implements the four-shade DMG display and four audio channels.
- Audio timing and accuracy remain experimental.
- Tested with BGBtest, Tetris, and Super Mario Land.

Game Boy Color features and serial/link-cable transfers are not implemented.

## Requirements

- Windows
- .NET 10 SDK

## Build and run

```powershell
dotnet build EmuGBSharp.sln
dotnet run --project EmuGBSharp.csproj -- "C:\path\to\game.gb"
```

You can also drag a ROM file onto the built executable. When no path is supplied, the fallback ROM configured in `MainForm_Load` is loaded.

## Controls

| Keyboard | Game Boy |
| --- | --- |
| Arrow keys | D-pad |
| A | A |
| S | B |
| Backspace | Start |
| Enter | Select |

## Architecture

The emulator is a hand-written interpreter. `Program` opens `MainForm`, which owns an `Emulator` composed of the CPU, video, memory, cartridge, and sound components.

- `Cpu.cs` fetches and executes instructions and advances timers.
- `Memory.cs` acts as the system bus and handles memory-mapped I/O, MBC1 banking, DMA, palettes, timers, and sound triggers.
- `Video.cs` models scanline states and renders the framebuffer.
- `Sound.cs` implements a pull-based four-channel APU using NAudio.
- `MainForm.cs` handles ROM loading, keyboard input, the emulation worker, and framebuffer presentation.

There is currently no automated test project. CPU correctness is checked with Blargg test ROMs; the commented test list in `MainForm_Load` records the known results.

## License

See [LICENSE.txt](LICENSE.txt).
