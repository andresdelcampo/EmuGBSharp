# ZarthGB
A GameBoy emulator in C# including sound -to some degree- with MBC1 support for >32 KB games.
It passes Blargg's CPU instruction tests.

The sound originally proved particularly tricky and far from perfect. I revisited in 2026 with AI help and made it a lot better. See classes documentation in code for details.

**Keys:** A, S, backspace, return and arrow keys.

Created with the purpose of learning about emulation, not focused on code perfection but on simplicity. References mentioned where relevant.

You can alter the ROM to load in ZarthEmulator.cs or drag the rom onto the executable. Tested with BGBtest, Tetris and Super Mario Land.
