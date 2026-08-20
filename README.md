# ZarthGB
A GameBoy emulator in C# including sound -to some degree- with MBC1 support for >32 KB games.
It passes Blargg's CPU instruction tests.

The sound originally proved particularly tricky and far from perfect. 
I revisited in 2026 with AI help and made it a lot better (in the sound branch). See classes documentation in code for details. It is not in main as it is a bit unstable. 

**Keys:** A, S, backspace, return and arrow keys.

This project was primarily an educational exercise to understand emulator design, CPU emulation, memory banking, graphics timing, and Game Boy audio. It is not intended to compete with mature emulators, but to serve as a readable C# reference implementation. References mentioned where relevant.

You can alter the ROM to load in ZarthEmulator.cs or drag the rom onto the executable. Tested with BGBtest, Tetris and Super Mario Land.
