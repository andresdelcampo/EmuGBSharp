using System;
using System.Diagnostics;

// References
// - GitHub Copilot -for a very fast start (500+ instructions in 3-4h), which would prove rather error prone later
// - https://gbdev.io/gb-opcodes/optables/ for visual reference of the instructions while coding them, or for timing
// - http://marc.rawer.de/Gameboy/Docs/GBCPUman.pdf probably the best GameBoy CPU manual -though not exempt of inaccuracies
// - https://github.com/retrio/gb-test-roms as source for Blargg test roms for CPU instuctions
// - https://github.com/TheSorm/Boy (for the toughest instructions to pass the Blargg tests, 2-3 would have been impossible to guess)

namespace EmuGBSharp
{
    class Cpu
    {
        private Memory memory;
        public bool Stopped { get; private set; }
        public bool Halted { get; private set; }
        
        private ushort[] pcTrace = new ushort[6553600];
        private int pcTracePtr = 0;
        
        #region Flags

        private byte flags;
        public byte Flags
        {
            get => flags;
            private set => flags = (byte) (value & 0xF0);   // Do not attempt to write the unused flag space
        }
        public bool FlagZ
        { 
            get => (Flags & 0x80) != 0;
            private set => Flags = (byte)((Flags & 0x7F) | (value ? 0x80 : 0));
        }
        public bool FlagN
        {
            get => (Flags & 0x40) != 0;
            private set => Flags = (byte)((Flags & 0xBF) | (value ? 0x40 : 0));
        }
        public bool FlagH
        {
            get => (Flags & 0x20) != 0;
            private set => Flags = (byte)((Flags & 0xDF) | (value ? 0x20 : 0));
        }
        public bool FlagC
        {
            get => (Flags & 0x10) != 0;
            private set => Flags = (byte)((Flags & 0xEF) | (value ? 0x10 : 0));
        }

        #endregion
        
        #region Registers
        
        public byte A { get; private set; }
        public byte B { get; private set; }
        public byte C { get; private set; }
        public byte D { get; private set; }
        public byte E { get; private set; }
        public byte H { get; private set; }
        public byte L { get; private set; }
        
        public ushort SP { get; private set; }
        public ushort PC { get; private set; }
        
        public ushort AF 
        { 
            get => (ushort) ((A << 8) | Flags);
            private set
            {
                A = (byte)(value >> 8);
                Flags = (byte) (value & 0xFF);
            }
        }
        public ushort BC
        {
            get => (ushort)((B << 8) | C);
            private set
            {
                B = (byte)(value >> 8);
                C = (byte)(value & 0xFF);
            }
        }
        public ushort DE
        {
            get => (ushort)((D << 8) | E);
            private set
            {
                D = (byte)(value >> 8);
                E = (byte)(value & 0xFF);
            }
        }
        public ushort HL
        {
            get => (ushort)((H << 8) | L);
            private set
            {
                H = (byte)(value >> 8);
                L = (byte)(value & 0xFF);
            }
        }

        #endregion

        #region Cycles

        public int Steps { get; private set; }
        //private int TransferInProgressTicks = 0;    
        public int Ticks 
        {
            get => memory.Ticks;
            private set
            {
                int increment = value - memory.Ticks;
                for (int i = 0; i < increment; i++)
                {
                    if ((memory.Ticks + i) % 268 == 0) // DIV goes 16384 times per second, so 1/268th of 4.19 MHz
                        memory.IncrementDiv();
                    
                    if (memory.TimerEnabled)
                    {
                        if ((memory.Ticks + i) % memory.TimerTick == 0)
                        {
                            int newCounterValue = memory[0xff05] + 1;
                            memory[0xff05] = (byte) (newCounterValue & 0xFF);
                            if (newCounterValue > 0xFF)
                            {
                                memory[0xff05] = memory[0xff06];
                                if ((InterruptEnable & InterruptsTimer) > 0)
                                {
                                    InterruptFlags |= InterruptsTimer;
                                }
                            }
                        }
                    }
                }
                
                // Process serial port if needed
                /*if (TransferInProgressTicks > 0)    // Transfer in progress
                {
                    TransferInProgressTicks--;
                    if (TransferInProgressTicks == 0)
                    {
                        memory[0xff01] = 0xFF; // No other GameBoy present
                        if ((memory[0xffff] & Cpu.InterruptsSerial) > 0)
                            memory[0xff0f] |= Cpu.InterruptsSerial;
                    }
                }
                if (memory[0xff02] >> 7 > 0)    // New transfer
                {
                    // Mark transfer finished -we don't have serial transfers here
                    memory[0xff02] = (byte)(memory[0xff02] & 0x7F);
                    TransferInProgressTicks = 8;
                }*/

                memory.Ticks = value;   
            }
        }
        
        private int[] normalInstructionHalfTicks = {
            2, 6, 4, 4, 2, 2, 4, 4, 10, 4, 4, 4, 2, 2, 4, 2, // 0x0_
            2, 6, 4, 4, 2, 2, 4, 4,  6, 4, 4, 4, 2, 2, 4, 2, // 0x1_
            4, 6, 4, 4, 2, 2, 4, 2,  4, 4, 4, 4, 2, 2, 4, 2, // 0x2_
            4, 6, 4, 4, 6, 6, 6, 2,  4, 4, 4, 4, 2, 2, 4, 2, // 0x3_
            2, 2, 2, 2, 2, 2, 4, 2,  2, 2, 2, 2, 2, 2, 4, 2, // 0x4_
            2, 2, 2, 2, 2, 2, 4, 2,  2, 2, 2, 2, 2, 2, 4, 2, // 0x5_
            2, 2, 2, 2, 2, 2, 4, 2,  2, 2, 2, 2, 2, 2, 4, 2, // 0x6_
            4, 4, 4, 4, 4, 4, 2, 4,  2, 2, 2, 2, 2, 2, 4, 2, // 0x7_
            2, 2, 2, 2, 2, 2, 4, 2,  2, 2, 2, 2, 2, 2, 4, 2, // 0x8_
            2, 2, 2, 2, 2, 2, 4, 2,  2, 2, 2, 2, 2, 2, 4, 2, // 0x9_
            2, 2, 2, 2, 2, 2, 4, 2,  2, 2, 2, 2, 2, 2, 4, 2, // 0xa_
            2, 2, 2, 2, 2, 2, 4, 2,  2, 2, 2, 2, 2, 2, 4, 2, // 0xb_
            4, 6, 6, 8, 6, 8, 4, 8,  4, 8, 6, 2, 6,12, 4, 8, // 0xc_
            4, 6, 6, 0, 6, 8, 4, 8,  4, 8, 6, 0, 6, 0, 4, 8, // 0xd_
            6, 6, 4, 0, 0, 8, 4, 8,  8, 2, 8, 0, 0, 0, 4, 8, // 0xe_
            6, 6, 4, 2, 0, 8, 4, 8,  6, 4, 8, 2, 0, 0, 4, 8  // 0xf_
        };
        private int[] prefixInstructionHalfTicks = {
            4, 4, 4, 4, 4, 4, 8, 4,  4, 4, 4, 4, 4, 4, 8, 4, // 0x0_
            4, 4, 4, 4, 4, 4, 8, 4,  4, 4, 4, 4, 4, 4, 8, 4, // 0x1_
            4, 4, 4, 4, 4, 4, 8, 4,  4, 4, 4, 4, 4, 4, 8, 4, // 0x2_
            4, 4, 4, 4, 4, 4, 8, 4,  4, 4, 4, 4, 4, 4, 8, 4, // 0x3_
            4, 4, 4, 4, 4, 4, 6, 4,  4, 4, 4, 4, 4, 4, 6, 4, // 0x4_
            4, 4, 4, 4, 4, 4, 6, 4,  4, 4, 4, 4, 4, 4, 6, 4, // 0x5_
            4, 4, 4, 4, 4, 4, 6, 4,  4, 4, 4, 4, 4, 4, 6, 4, // 0x6_
            4, 4, 4, 4, 4, 4, 6, 4,  4, 4, 4, 4, 4, 4, 6, 4, // 0x7_
            4, 4, 4, 4, 4, 4, 8, 4,  4, 4, 4, 4, 4, 4, 8, 4, // 0x8_
            4, 4, 4, 4, 4, 4, 8, 4,  4, 4, 4, 4, 4, 4, 8, 4, // 0x9_
            4, 4, 4, 4, 4, 4, 8, 4,  4, 4, 4, 4, 4, 4, 8, 4, // 0xa_
            4, 4, 4, 4, 4, 4, 8, 4,  4, 4, 4, 4, 4, 4, 8, 4, // 0xb_
            4, 4, 4, 4, 4, 4, 8, 4,  4, 4, 4, 4, 4, 4, 8, 4, // 0xc_
            4, 4, 4, 4, 4, 4, 8, 4,  4, 4, 4, 4, 4, 4, 8, 4, // 0xd_
            4, 4, 4, 4, 4, 4, 8, 4,  4, 4, 4, 4, 4, 4, 8, 4, // 0xe_
            4, 4, 4, 4, 4, 4, 8, 4,  4, 4, 4, 4, 4, 4, 8, 4  // 0xf_
        };

        #endregion

        #region Interrupts

        public const byte InterruptsVblank = (1 << 0);
        public const byte InterruptsLcdstat = (1 << 1);
        public const byte InterruptsTimer = (1 << 2);
        public const byte InterruptsSerial = (1 << 3);
        public const byte InterruptsJoypad = (1 << 4);

        private bool InterruptMasterEnable { get; set; }
        private byte InterruptEnable
        {
            get => memory[0xffff];
            set => memory[0xffff] = value;
        } 

        private byte InterruptFlags
        {
            get => memory[0xff0f];
            set => memory[0xff0f] = value;
        } 

        private void InterruptStep()
        {
            if (InterruptMasterEnable || Halted)
                if (InterruptEnable != 0 && InterruptFlags != 0)
                {
                    byte fire = (byte) (InterruptEnable & InterruptFlags);

                    if ((fire & InterruptsVblank) != 0)
                    {
                        Halted = false;
                        if (InterruptMasterEnable)
                        {
                            InterruptFlags = (byte) (InterruptFlags & ~InterruptsVblank);
                            Vblank();
                        }
                    }

                    if ((fire & InterruptsLcdstat) != 0)
                    {
                        Halted = false;
                        if (InterruptMasterEnable)
                        {
                            InterruptFlags = (byte)(InterruptFlags & ~InterruptsLcdstat);
                            LcdStat();
                        }
                    }

                    if ((fire & InterruptsTimer) != 0)
                    {
                        Halted = false;
                        if (InterruptMasterEnable)
                        {
                            InterruptFlags = (byte)(InterruptFlags & ~InterruptsTimer);
                            Timer();
                        }
                    }

                    if ((fire & InterruptsSerial) != 0)
                    {
                        Halted = false;
                        if (InterruptMasterEnable)
                        {
                            InterruptFlags = (byte) (InterruptFlags & ~InterruptsSerial);
                            Serial();
                        }
                    }

                    if ((fire & InterruptsJoypad) != 0)
                    {
                        Halted = false;
                        if (InterruptMasterEnable)
                        {
                            InterruptFlags = (byte)(InterruptFlags & ~InterruptsJoypad);
                            Joypad();
                        }
                    }
                }
        }

        private void Vblank()
        {
            //drawFramebuffer();

            InterruptMasterEnable = false;
            PushWord(PC);
            PC = 0x40;
            Ticks += 12;
        }

        private void LcdStat()
        {
            InterruptMasterEnable = false;
            PushWord(PC);
            PC = 0x48;
            Ticks += 12;
        }

        private void Timer()
        {
            InterruptMasterEnable = false;
            PushWord(PC);
            PC = 0x50;
            Ticks += 12;
        }

        private void Serial()
        {
            InterruptMasterEnable = false;
            PushWord(PC);
            PC = 0x58;
            Ticks += 12;
        }

        private void Joypad()
        {
            InterruptMasterEnable = false;
            PushWord(PC);
            PC = 0x60;
            Ticks += 12;
        }

        #endregion
        
        public Cpu(Memory memory)
        {
            this.memory = memory;
            Reset();
        }

        public void Reset()
        {
            // Default values for DMG (from TCAGBD.pdf)
            A = 0x01;
            Flags = 0xB0;
            B = 0x00;
            C = 0x13;
            D = 0x00;
            E = 0xD8;
            H = 0x01;
            L = 0x4D;
            SP = 0xFFFE;
            
            PC = 0x0000;    // But force to go through Boot ROM -because I can
            //PC = 0x0100;    // Skip Boot ROM
            //memory[0xFF50] = 0x01;	// Disable Boot ROM
            
            Stopped = false;
            Halted = false;
            Steps = 0;
        }

        public void KeyPressed()
        {
            Stopped = false;
            if((InterruptEnable & InterruptsJoypad) > 0) 
                InterruptFlags |= InterruptsJoypad;
        }
        
        public void Step()
        {
            InterruptStep();

            if (Stopped || Halted)
            {
                Ticks += 4;
                return;
            }

            #region Tracing
            pcTrace[pcTracePtr++] = PC;
            if (pcTracePtr == pcTrace.Length) pcTracePtr = 0;
            if (pcTracePtr > 20 &&
                pcTrace[pcTracePtr - 2] == PC - 1 &&
                pcTrace[pcTracePtr - 3] == PC - 2 &&
                pcTrace[pcTracePtr - 4] == PC - 3 &&
                pcTrace[pcTracePtr - 5] == PC - 4 &&
                pcTrace[pcTracePtr - 6] == PC - 5 &&
                pcTrace[pcTracePtr - 7] == PC - 6 &&
                pcTrace[pcTracePtr - 8] == PC - 7 &&
                pcTrace[pcTracePtr - 9] == PC - 8 &&
                pcTrace[pcTracePtr - 10] == PC - 9 &&
                pcTrace[pcTracePtr - 11] == PC - 10 &&
                pcTrace[pcTracePtr - 12] == PC - 11 &&
                pcTrace[pcTracePtr - 13] == PC - 12 &&
                pcTrace[pcTracePtr - 14] == PC - 13 &&
                pcTrace[pcTracePtr - 15] == PC - 14 &&
                pcTrace[pcTracePtr - 16] == PC - 15 &&
                pcTrace[pcTracePtr - 17] == PC - 16 &&
                pcTrace[pcTracePtr - 18] == PC - 17 &&
                pcTrace[pcTracePtr - 19] == PC - 18 &&
                pcTrace[pcTracePtr - 20] == PC - 19
                )
                PC = PC;

            if (PC == 0x100)
                PC = PC;

            if (PC == 0xC32F)
                PC = PC;
            #endregion
                
            Steps++;
            byte opcode = ReadByte();
            Ticks += normalInstructionHalfTicks[opcode] << 1;
                
            //Debug.Print($"Step={Steps} PC={PC:X4} OP={opcode:X2} A={A:X2} F={Flags:X2} B={B:X2} C={C:X2} D={D:X2} E={E:X2} H={H:X2} L={L:X2} SP={SP:X4}");
                
            switch(opcode)
            {
                case 0x00: // NOP
                    break;

                case 0x01: // LD BC, nn
                    BC = ReadWord();
                    break;
                    
                case 0x02: // LD (BC), A
                    WriteByte(BC, A);
                    break;
                    
                case 0x03: // INC BC
                    BC++;
                    break;
                    
                case 0x04: // INC B
                    B = Increment(B);
                    break;
                    
                case 0x05: // DEC B
                    B = Decrement(B);
                    break;
                    
                case 0x06: // LD B, n
                    B = ReadByte();
                    break;
                    
                case 0x07: // RLCA
                    A = Rlc(A);
                    FlagZ = false;
                    break;
                    
                case 0x08: // LD (nn), SP
                    WriteWord(ReadWord(), SP);
                    break;
                    
                case 0x09: // ADD HL, BC
                    HL = Add16(HL, BC);
                    break;
                    
                case 0x0A: // LD A, (BC)
                    A = ReadByte(BC);
                    break;
                    
                case 0x0B: // DEC BC
                    BC--;
                    break;
                    
                case 0x0C: // INC C
                    C = Increment(C);
                    break;
                    
                case 0x0D: // DEC C
                    C = Decrement(C);
                    break;
                    
                case 0x0E: // LD C, n
                    C = ReadByte();
                    break;
                    
                case 0x0F: // RRCA
                    A = Rrc(A);
                    FlagZ = false;
                    break;
                    
                case 0x10: // STOP
                    // Wait until button pressed
                    Stopped = true;
                    break;

                case 0x11: // LD DE, nn
                    DE = ReadWord();
                    break;
                    
                case 0x12: // LD (DE), A
                    WriteByte(DE, A);
                    break;
                    
                case 0x13: // INC DE
                    DE++;
                    break;
                    
                case 0x14: // INC D
                    D = Increment(D);
                    break;
                    
                case 0x15: // DEC D
                    D = Decrement(D);
                    break;
                    
                case 0x16: // LD D, n
                    D = ReadByte();
                    break;

                case 0x17: // RLA
                    A = Rl(A);
                    FlagZ = false;
                    break;
                    
                case 0x18: // JR n      // Verified OK - 01
                    PC += (ushort)(sbyte)ReadByte();
                    PC++;
                    break;
                    
                case 0x19: // ADD HL, DE
                    HL = Add16(HL, DE);
                    break;
                    
                case 0x1A: // LD A, (DE)
                    A = ReadByte(DE);
                    break;
                    
                case 0x1B: // DEC DE
                    DE--;
                    break;
                    
                case 0x1C: // INC E
                    E = Increment(E);
                    break;
                    
                case 0x1D: // DEC E
                    E = Decrement(E);
                    break;
                    
                case 0x1E: // LD E, n
                    E = ReadByte();
                    break;
                    
                case 0x1F: // RRA
                    A = Rr(A);
                    FlagZ = false;
                    break;
                    
                case 0x20: // JR NZ, n
                    if (!FlagZ)
                    {
                        PC += (ushort)(sbyte)ReadByte();
                        Ticks += 4;
                    }
                    PC++;                    
                    break;

                case 0x21: // LD HL, nn
                    HL = ReadWord();
                    break;
                    
                case 0x22: // LD (HL+), A
                    WriteByte(HL++, A);
                    break;
                    
                case 0x23: // INC HL
                    HL++;
                    break;
                    
                case 0x24: // INC H
                    H = Increment(H);
                    break;
                    
                case 0x25: // DEC H
                    H = Decrement(H);
                    break;
                    
                case 0x26: // LD H, n
                    H = ReadByte();
                    break;
                    
                case 0x27: // DAA               // Verified OK - 01, 11
                    int resultDAA = A;
                    if (!FlagN)
                    {
                        if (FlagH || (resultDAA & 0xF) > 9)
                            resultDAA += 0x06;
                        if (FlagC || resultDAA > 0x9F)
                            resultDAA += 0x60;
                    }
                    else
                    {
                        if (FlagH)
                            resultDAA = (resultDAA - 6) & 0xFF;
                        if (FlagC)
                            resultDAA -= 0x60;
                    }
                    FlagZ = false;
                    FlagH = false;
                    if ((resultDAA & 0x100) == 0x100)
                        FlagC = true;
                    resultDAA &= 0xFF;
                    if (resultDAA == 0)
                        FlagZ = true;
                    A = (byte) (resultDAA & 0xFF);
                    break;
                    
                case 0x28: // JR Z, n
                    if (FlagZ)
                    {
                        PC += (ushort)(sbyte)ReadByte();
                        Ticks += 4;                        
                    }
                    PC++;                    
                    break;
                    
                case 0x29: // ADD HL, HL
                    HL = Add16(HL, HL);
                    break;
                    
                case 0x2A: // LD A, (HL+)
                    A = ReadByte(HL++);
                    break;
                    
                case 0x2B: // DEC HL
                    HL--;
                    break;
                    
                case 0x2C: // INC L
                    L = Increment(L);
                    break;
                    
                case 0x2D: // DEC L
                    L = Decrement(L);
                    break;
                    
                case 0x2E: // LD L, n
                    L = ReadByte();
                    break;
                    
                case 0x2F: // CPL
                    A = (byte)~A;
                    FlagN = true;
                    FlagH = true;
                    break;
                    
                case 0x30: // JR NC, n
                    if (!FlagC)
                    {
                        PC += (ushort)(sbyte)ReadByte();
                        Ticks += 4;                        
                    }
                    PC++;                    
                    break;

                case 0x31: // LD SP, nn
                    SP = ReadWord();
                    break;
                        
                case 0x32: // LD (HL-), A
                    WriteByte(HL--, A);
                    break;                
                    
                case 0x33: // INC SP
                    SP++;
                    break;
                    
                case 0x34: // INC (HL)
                    WriteByte(HL, Increment(ReadByte(HL)));
                    break;
                    
                case 0x35: // DEC (HL)
                    WriteByte(HL, Decrement(ReadByte(HL)));
                    break;
                    
                case 0x36: // LD (HL), n
                    WriteByte(HL, ReadByte());
                    break;
                    
                case 0x37: // SCF
                    FlagN = false;
                    FlagH = false;
                    FlagC = true;
                    break;

                case 0x38: // JR C, n
                    if (FlagC)
                    {
                        PC += (ushort)(sbyte)ReadByte();
                        Ticks += 4;                        
                    }
                    PC++;                    
                    break;
                    
                case 0x39: // ADD HL, SP
                    HL = Add16(HL, SP);
                    break;
                    
                case 0x3A: // LD A, (HL-)
                    A = ReadByte(HL--);
                    break;
                    
                case 0x3B: // DEC SP
                    SP--;
                    break;
                    
                case 0x3C: // INC A
                    A = Increment(A);
                    break;
                    
                case 0x3D: // DEC A
                    A = Decrement(A);
                    break;
                    
                case 0x3E: // LD A, n
                    A = ReadByte();
                    break;
                    
                case 0x3F: // CCF
                    FlagN = false;
                    FlagH = false;
                    FlagC = !FlagC;
                    break;
                    
                case 0x40: // LD B, B
                    B = B;
                    break;
                    
                case 0x41: // LD B, C
                    B = C;
                    break;

                case 0x42: // LD B, D
                    B = D;
                    break;
                    
                case 0x43: // LD B, E
                    B = E;
                    break;
                    
                case 0x44: // LD B, H
                    B = H;
                    break;
                    
                case 0x45: // LD B, L
                    B = L;
                    break;
                    
                case 0x46: // LD B, (HL)
                    B = ReadByte(HL);
                    break;
                    
                case 0x47: // LD B, A
                    B = A;
                    break;
                    
                case 0x48: // LD C, B
                    C = B;
                    break;
                    
                case 0x49: // LD C, C
                    C = C;
                    break;
                    
                case 0x4A: // LD C, D
                    C = D;
                    break;
                    
                case 0x4B: // LD C, E
                    C = E;
                    break;
                    
                case 0x4C: // LD C, H
                    C = H;
                    break;
                    
                case 0x4D: // LD C, L
                    C = L;
                    break;
                    
                case 0x4E: // LD C, (HL)
                    C = ReadByte(HL);
                    break;
                    
                case 0x4F: // LD C, A
                    C = A;
                    break;
                    
                case 0x50: // LD D, B
                    D = B;
                    break;
                    
                case 0x51: // LD D, C
                    D = C;
                    break;
                    
                case 0x52: // LD D, D
                    D = D;
                    break;
                    
                case 0x53: // LD D, E
                    D = E;
                    break;
                    
                case 0x54: // LD D, H
                    D = H;
                    break;
                    
                case 0x55: // LD D, L
                    D = L;
                    break;
                    
                case 0x56: // LD D, (HL)
                    D = ReadByte(HL);
                    break;
                    
                case 0x57: // LD D, A
                    D = A;
                    break;
                    
                case 0x58: // LD E, B
                    E = B;
                    break;
                    
                case 0x59: // LD E, C
                    E = C;
                    break;
                    
                case 0x5A: // LD E, D
                    E = D;
                    break;
                    
                case 0x5B: // LD E, E
                    E = E;
                    break;
                    
                case 0x5C: // LD E, H
                    E = H;
                    break;
                    
                case 0x5D: // LD E, L
                    E = L;
                    break;
                    
                case 0x5E: // LD E, (HL)
                    E = ReadByte(HL);
                    break;
                    
                case 0x5F: // LD E, A
                    E = A;
                    break;
                    
                case 0x60: // LD H, B
                    H = B;
                    break;
                    
                case 0x61: // LD H, C
                    H = C;
                    break;
                    
                case 0x62: // LD H, D
                    H = D;
                    break;
                    
                case 0x63: // LD H, E
                    H = E;
                    break;
                    
                case 0x64: // LD H, H
                    H = H;
                    break;
                    
                case 0x65: // LD H, L
                    H = L;
                    break;
                    
                case 0x66: // LD H, (HL)
                    H = ReadByte(HL);
                    break;
                    
                case 0x67: // LD H, A
                    H = A;
                    break;
                    
                case 0x68: // LD L, B
                    L = B;
                    break;
                    
                case 0x69: // LD L, C
                    L = C;
                    break;
                    
                case 0x6A: // LD L, D
                    L = D;
                    break;
                    
                case 0x6B: // LD L, E
                    L = E;
                    break;
                    
                case 0x6C: // LD L, H
                    L = H;
                    break;
                    
                case 0x6D: // LD L, L
                    L = L;
                    break;
                    
                case 0x6E: // LD L, (HL)
                    L = ReadByte(HL);
                    break;
                    
                case 0x6F: // LD L, A
                    L = A;
                    break;
                    
                case 0x70: // LD (HL), B
                    WriteByte(HL, B);
                    break;
                    
                case 0x71: // LD (HL), C
                    WriteByte(HL, C);
                    break;
                    
                case 0x72: // LD (HL), D
                    WriteByte(HL, D);
                    break;
                    
                case 0x73: // LD (HL), E
                    WriteByte(HL, E);
                    break;
                    
                case 0x74: // LD (HL), H
                    WriteByte(HL, H);
                    break;
                    
                case 0x75: // LD (HL), L
                    WriteByte(HL, L);
                    break;
                    
                case 0x76: // HALT
                    /*if (InterruptMasterEnable)
                        Halted = true;      // Halt execution until interrupt occurs
                    else    
                        PC++;   // DMG issue skips one instruction when in DI
                    */  // In theory this is the behavior, but if fails the tests
                    Halted = true;
                    break;
                    
                case 0x77: // LD (HL), A
                    WriteByte(HL, A);
                    break;
                    
                case 0x78: // LD A, B
                    A = B;
                    break;
                    
                case 0x79: // LD A, C
                    A = C;
                    break;
                    
                case 0x7A: // LD A, D
                    A = D;
                    break;
                    
                case 0x7B: // LD A, E
                    A = E;
                    break;
                    
                case 0x7C: // LD A, H
                    A = H;
                    break;
                    
                case 0x7D: // LD A, L
                    A = L;
                    break;

                case 0x7E: // LD A, (HL)
                    A = ReadByte(HL);
                    break;
                    
                case 0x7F: // LD A, A
                    A = A;
                    break;
                    
                case 0x80: // ADD A, B
                    Add(B);
                    break;
                    
                case 0x81: // ADD A, C
                    Add(C);
                    break;
                    
                case 0x82: // ADD A, D
                    Add(D);
                    break;
                    
                case 0x83: // ADD A, E
                    Add(E);
                    break;
                    
                case 0x84: // ADD A, H
                    Add(H);
                    break;
                    
                case 0x85: // ADD A, L
                    Add(L);
                    break;
                    
                case 0x86: // ADD A, (HL)
                    Add(ReadByte(HL));
                    break;
                    
                case 0x87: // ADD A, A
                    Add(A);
                    break;
                    
                case 0x88: // ADC A, B
                    Adc(B);
                    break;
                    
                case 0x89: // ADC A, C
                    Adc(C);
                    break;
                    
                case 0x8A: // ADC A, D
                    Adc(D);
                    break;
                    
                case 0x8B: // ADC A, E
                    Adc(E);
                    break;
                    
                case 0x8C: // ADC A, H
                    Adc(H);
                    break;
                    
                case 0x8D: // ADC A, L
                    Adc(L);
                    break;
                    
                case 0x8E: // ADC A, (HL)
                    Adc(ReadByte(HL));
                    break;
                    
                case 0x8F: // ADC A, A
                    Adc(A);
                    break;
                    
                case 0x90: // SUB A, B
                    Sub(B);
                    break;
                    
                case 0x91: // SUB A, C
                    Sub(C);
                    break;
                    
                case 0x92: // SUB A, D
                    Sub(D);
                    break;
                    
                case 0x93: // SUB A, E
                    Sub(E);
                    break;
                    
                case 0x94: // SUB A, H
                    Sub(H);
                    break;
                    
                case 0x95: // SUB A, L
                    Sub(L);
                    break;
                    
                case 0x96: // SUB A, (HL)
                    Sub(ReadByte(HL));
                    break;
                    
                case 0x97: // SUB A, A
                    Sub(A);
                    break;
                    
                case 0x98: // SBC A, B
                    Sbc(B);
                    break;
                    
                case 0x99: // SBC A, C
                    Sbc(C);
                    break;
                    
                case 0x9A: // SBC A, D
                    Sbc(D);
                    break;
                    
                case 0x9B: // SBC A, E
                    Sbc(E);
                    break;
                    
                case 0x9C: // SBC A, H
                    Sbc(H);
                    break;
                    
                case 0x9D: // SBC A, L
                    Sbc(L);
                    break;
                    
                case 0x9E: // SBC A, (HL)
                    Sbc(ReadByte(HL));
                    break;
                    
                case 0x9F: // SBC A, A
                    Sbc(A);
                    break;
                    
                case 0xA0: // AND B
                    And(B);
                    break;
                    
                case 0xA1: // AND C
                    And(C);
                    break;
                    
                case 0xA2: // AND D
                    And(D);
                    break;
                    
                case 0xA3: // AND E
                    And(E);
                    break;
                    
                case 0xA4: // AND H
                    And(H);
                    break;
                    
                case 0xA5: // AND L
                    And(L);
                    break;
                    
                case 0xA6: // AND (HL)
                    And(ReadByte(HL));
                    break;
                    
                case 0xA7: // AND A
                    And(A);
                    break;
                    
                case 0xA8: // XOR B
                    Xor(B);
                    break;
                    
                case 0xA9: // XOR C
                    Xor(C);
                    break;
                    
                case 0xAA: // XOR D
                    Xor(D);
                    break;
                    
                case 0xAB: // XOR E
                    Xor(E);
                    break;
                    
                case 0xAC: // XOR H
                    Xor(H);
                    break;
                    
                case 0xAD: // XOR L
                    Xor(L);
                    break;
                    
                case 0xAE: // XOR (HL)
                    Xor(ReadByte(HL));
                    break;
                    
                case 0xAF: // XOR A
                    Xor(A);
                    break;
                    
                case 0xB0: // OR B
                    Or(B);
                    break;
                    
                case 0xB1: // OR C
                    Or(C);
                    break;
                    
                case 0xB2: // OR D
                    Or(D);
                    break;
                    
                case 0xB3: // OR E
                    Or(E);
                    break;
                    
                case 0xB4: // OR H
                    Or(H);
                    break;
                    
                case 0xB5: // OR L
                    Or(L);
                    break;
                    
                case 0xB6: // OR (HL)
                    Or(ReadByte(HL));
                    break;
                    
                case 0xB7: // OR A
                    Or(A);
                    break;
                    
                case 0xB8: // CP B
                    Compare(B);
                    break;
                    
                case 0xB9: // CP C
                    Compare(C);
                    break;
                    
                case 0xBA: // CP D
                    Compare(D);
                    break;
                    
                case 0xBB: // CP E
                    Compare(E);
                    break;
                    
                case 0xBC: // CP H
                    Compare(H);
                    break;
                    
                case 0xBD: // CP L
                    Compare(L);
                    break;
                    
                case 0xBE: // CP (HL)
                    Compare(ReadByte(HL));
                    break;
                    
                case 0xBF: // CP A
                    Compare(A);
                    break;
                    
                case 0xC0: // RET NZ
                    if (!FlagZ)
                    {
                        PC = PopWord();
                        Ticks += 12;                            
                    }
                    break;
                    
                case 0xC1: // POP BC
                    BC = PopWord();
                    break;
                    
                case 0xC2: // JP NZ, nn
                    if (!FlagZ)
                    {
                        PC = ReadWord();
                        Ticks += 4;
                    }
                    else 
                        PC += 2;
                    break;

                case 0xC3: // JP nn
                    PC = ReadWord();
                    break;

                case 0xC4: // CALL NZ, nn
                    if (!FlagZ)
                    {
                        Call();
                        Ticks += 12;
                    }
                    else
                        PC += 2;
                    break;
                    
                case 0xC5: // PUSH BC
                    PushWord(BC);
                    break;
                    
                case 0xC6: // ADD A, n
                    Add(ReadByte());
                    break;
                    
                case 0xC7: // RST 00H
                    Rst(0x00);
                    break;
                    
                case 0xC8: // RET Z
                    if (FlagZ)
                    {
                        PC = PopWord();
                        Ticks += 12;                            
                    }
                    break;
                    
                case 0xC9: // RET
                    PC = PopWord();
                    break;
                    
                case 0xCA: // JP Z, nn
                    if (FlagZ)
                    {
                        PC = ReadWord();
                        Ticks += 4;                        
                    }
                    else 
                        PC += 2;
                    break;
                    
                case 0xCB: // Prefix CB
                    PrefixedInstruction();
                    break;
                    
                case 0xCC: // CALL Z, nn
                    if (FlagZ)
                    {
                        Call();
                        Ticks += 12;
                    }
                    else
                        PC += 2;
                    break;

                case 0xCD: // CALL nn
                    Call();
                    break;
                    
                case 0xCE: // ADC A, n
                    Adc(ReadByte());
                    break;

                case 0xCF: // RST 08H
                    Rst(0x08);
                    break;
                    
                case 0xD0: // RET NC
                    if (!FlagC)
                    {
                        PC = PopWord();
                        Ticks += 12;                            
                    }
                    break;
                    
                case 0xD1: // POP DE
                    DE = PopWord();
                    break;
                    
                case 0xD2: // JP NC, nn
                    if (!FlagC)
                    {
                        PC = ReadWord();
                        Ticks += 4;                        
                    }
                    else 
                        PC += 2;
                    break;

                // 0xD3 - Illegal
                    
                case 0xD4: // CALL NC, nn
                    if (!FlagC)
                    {
                        Call();
                        Ticks += 12;
                    }
                    else
                        PC += 2;
                    break;
                    
                case 0xD5: // PUSH DE
                    PushWord(DE);
                    break;
                    
                case 0xD6: // SUB n
                    Sub(ReadByte());
                    break;
                    
                case 0xD7: // RST 10H
                    Rst(0x10);
                    break;
                    
                case 0xD8: // RET C
                    if (FlagC)
                    {
                        PC = PopWord();
                        Ticks += 12;                            
                    }
                    break;
                    
                case 0xD9: // RETI
                    InterruptMasterEnable = true;
                    PC = PopWord();
                    break;

                case 0xDA: // JP C, nn
                    if (FlagC)
                    {
                        PC = ReadWord();
                        Ticks += 4;                        
                    }
                    else
                        PC += 2;
                    break;
                    
                // 0xDB - Illegal

                case 0xDC: // CALL C, nn
                    if (FlagC)
                    {
                        Call();
                        Ticks += 12;
                    }
                    else
                        PC += 2;
                    break;
                    
                // 0xDD - Illegal

                case 0xDE: // SBC A, n
                    Sbc(ReadByte());
                    break;
                    
                case 0xDF: // RST 18H
                    Rst(0x18);
                    break;
                    
                case 0xE0: // LDH (n), A
                    WriteByte(Word(ReadByte(), 0xFF), A);
                    break;
                    
                case 0xE1: // POP HL
                    HL = PopWord();
                    break;
                    
                case 0xE2: // LD (C), A
                    WriteByte(Word(C, 0xFF), A);   // Actually like LDH
                    break;
                    
                // 0xE3 - Illegal
                // 0xE4 - Illegal
                    
                case 0xE5: // PUSH HL
                    PushWord(HL);
                    break;
                    
                case 0xE6: // AND n
                    And(ReadByte());
                    break;

                case 0xE7: // RST 20H
                    Rst(0x20);
                    break;
                    
                case 0xE8: // ADD SP, n         // Verified OK - 03
                    var value = (sbyte)ReadByte();
                    var result = SP + value;
                    FlagZ = false;
                    FlagN = false;
                    FlagC = (((ushort)(sbyte)SP & 0xff) + (value & 0xff) & 0x100) != 0;
                    FlagH = (((ushort)(sbyte)SP & 0x0f) + (value & 0x0f) & 0x10) != 0;
                    SP = (ushort) (result & 0xFFFF);
                    break;

                case 0xE9: // JP (HL)
                    PC = HL;
                    break;
                    
                case 0xEA: // LD (nn), A
                    WriteByte(ReadWord(), A);
                    break;
                        
                // 0xEB - Illegal
                // 0xEC - Illegal
                // 0xED - Illegal
                
                case 0xEE: // XOR n
                    Xor(ReadByte());
                    break;
                
                case 0xEF: // RST 28H
                    Rst(0x28);
                    break;
                    
                case 0xF0: // LDH A, (n)
                    var address = Word(ReadByte(), 0xFF);
                    A = ReadByte(address);
                    break;
                    
                case 0xF1: // POP AF        // Verified OK - 01
                    AF = PopWord();
                    break;
                    
                case 0xF2: // LD A, (C)
                    A = ReadByte(Word(C, 0xFF));    // Actually like LDH
                    break;
                    
                case 0xF3: // DI
                    InterruptMasterEnable = false; // Disable interrupts
                    break;
                    
                // 0xF4 - Illegal
                    
                case 0xF5: // PUSH AF
                    PushWord(AF);
                    break;
                    
                case 0xF6: // OR n
                    Or(ReadByte());
                    break;
                    
                case 0xF7: // RST 30H
                    Rst(0x30);
                    break;
                    
                case 0xF8: // LD HL, SP+n           // Verified OK - 03
                    value = (sbyte)ReadByte();
                    result = SP + value;
                    FlagZ = false;
                    FlagN = false;
                    FlagC = (((ushort)(sbyte)SP & 0xff) + (value & 0xff) & 0x100) != 0;
                    FlagH = (((ushort)(sbyte)SP & 0x0f) + (value & 0x0f) & 0x10) != 0;
                    HL = (ushort) (result & 0xFFFF);
                    break;
                    
                case 0xF9: // LD SP, HL
                    SP = HL;
                    break;
                    
                case 0xFA: // LD A, (nn)
                    A = ReadByte(ReadWord());
                    break;
                    
                case 0xFB: // EI
                    InterruptMasterEnable = true;
                    break;
                    
                // 0xFC - Illegal
                // 0xFD - Illegal
                    
                case 0xFE: // CP n
                    Compare(A, ReadByte());
                    break;
                    
                case 0xFF: // RST 38H
                    Rst(0x38);
                    break;
                    
                default:
                    throw new NotSupportedException($"Opcode ${opcode:X} not supported -after {Steps} successful steps.");
            }
        }

        private void PrefixedInstruction()
        {
            var opcode = ReadByte();
            Ticks += prefixInstructionHalfTicks[opcode] << 1;

            switch (opcode)
            {
                case 0x00: // RLC B
                    B = Rlc(B);
                    break;
                
                case 0x01: // RLC C
                    C = Rlc(C);
                    break;
                
                case 0x02: // RLC D
                    D = Rlc(D);
                    break;
                
                case 0x03: // RLC E
                    E = Rlc(E);
                    break;
                
                case 0x04: // RLC H
                    H = Rlc(H);
                    break;
                
                case 0x05: // RLC L
                    L = Rlc(L);
                    break;
                
                case 0x06: // RLC (HL)
                    var value = ReadByte(HL);
                    value = Rlc(value);
                    WriteByte(HL, value);
                    break;
                
                case 0x07: // RLC A
                    A = Rlc(A);
                    break;
                
                case 0x08: // RRC B
                    B = Rrc(B);
                    break;
                
                case 0x09: // RRC C
                    C = Rrc(C);
                    break;
                
                case 0x0A: // RRC D
                    D = Rrc(D);
                    break;
                
                case 0x0B: // RRC E
                    E = Rrc(E);
                    break;
                
                case 0x0C: // RRC H
                    H = Rrc(H);
                    break;
                
                case 0x0D: // RRC L
                    L = Rrc(L);
                    break;
                
                case 0x0E: // RRC (HL)
                    var value2 = ReadByte(HL);
                    value2 = Rrc(value2);
                    WriteByte(HL, value2);
                    break;
                
                case 0x0F: // RRC A
                    A = Rrc(A);
                    break;
                
                case 0x10: // RL B
                    B = Rl(B);
                    break;
                
                case 0x11: // RL C
                    C = Rl(C);
                    break;
                
                case 0x12: // RL D
                    D = Rl(D);
                    break;
                
                case 0x13: // RL E
                    E = Rl(E);
                    break;
                
                case 0x14: // RL H
                    H = Rl(H);
                    break;
                
                case 0x15: // RL L
                    L = Rl(L);
                    break;
                
                case 0x16: // RL (HL)
                    var value3 = ReadByte(HL);
                    value3 = Rl(value3);
                    WriteByte(HL, value3);
                    break;
                
                case 0x17: // RL A
                    A = Rl(A);
                    break;
                
                case 0x18: // RR B
                    B = Rr(B);
                    break;
                
                case 0x19: // RR C
                    C = Rr(C);
                    break;
                
                case 0x1A: // RR D
                    D = Rr(D);
                    break;
                
                case 0x1B: // RR E
                    E = Rr(E);
                    break;
                
                case 0x1C: // RR H
                    H = Rr(H);
                    break;
                
                case 0x1D: // RR L
                    L = Rr(L);
                    break;
                
                case 0x1E: // RR (HL)
                    var value4 = ReadByte(HL);
                    value4 = Rr(value4);
                    WriteByte(HL, value4);
                    break;
                
                case 0x1F: // RR A
                    A = Rr(A);
                    break;
                
                case 0x20: // SLA B
                    B = Sla(B);
                    break;
                
                case 0x21: // SLA C
                    C = Sla(C);
                    break;
                
                case 0x22: // SLA D
                    D = Sla(D);
                    break;
                
                case 0x23: // SLA E
                    E = Sla(E);
                    break;

                case 0x24: // SLA H
                    H = Sla(H);
                    break;
                
                case 0x25: // SLA L
                    L = Sla(L);
                    break;
                
                case 0x26: // SLA (HL)
                    var value5 = ReadByte(HL);
                    value5 = Sla(value5);
                    WriteByte(HL, value5);
                    break;
                
                case 0x27: // SLA A
                    A = Sla(A);
                    break;
                
                case 0x28: // SRA B
                    B = Sra(B);
                    break;
                
                case 0x29: // SRA C
                    C = Sra(C);
                    break;
                
                case 0x2A: // SRA D
                    D = Sra(D);
                    break;
                
                case 0x2B: // SRA E
                    E = Sra(E);
                    break;
                
                case 0x2C: // SRA H
                    H = Sra(H);
                    break;
                
                case 0x2D: // SRA L
                    L = Sra(L);
                    break;
                
                case 0x2E: // SRA (HL)
                    var value6 = ReadByte(HL);
                    value6 = Sra(value6);
                    WriteByte(HL, value6);
                    break;
                
                case 0x2F: // SRA A
                    A = Sra(A);
                    break;
                
                case 0x30: // SWAP B
                    B = Swap(B);
                    break;

                case 0x31: // SWAP C
                    C = Swap(C);
                    break;
                
                case 0x32: // SWAP D
                    D = Swap(D);
                    break;
                
                case 0x33: // SWAP E
                    E = Swap(E);
                    break;
                    
                case 0x34: // SWAP H
                    H = Swap(H);
                    break;
                
                case 0x35: // SWAP L
                    L = Swap(L);
                    break;
                
                case 0x36: // SWAP (HL)
                    var value7 = ReadByte(HL);
                    value7 = Swap(value7);
                    WriteByte(HL, value7);
                    break;
                
                case 0x37: // SWAP A
                    A = Swap(A);
                    break;
                
                case 0x38: // SRL B
                    B = Srl(B);
                    break;
                
                case 0x39: // SRL C
                    C = Srl(C);
                    break;
                
                case 0x3A: // SRL D
                    D = Srl(D);
                    break;
                
                case 0x3B: // SRL E
                    E = Srl(E);
                    break;
                
                case 0x3C: // SRL H
                    H = Srl(H);
                    break;
                
                case 0x3D: // SRL L
                    L = Srl(L);
                    break;
                
                case 0x3E: // SRL (HL)
                    var value8 = ReadByte(HL);
                    value8 = Srl(value8);
                    WriteByte(HL, value8);
                    break;
                
                case 0x3F: // SRL A
                    A = Srl(A);
                    break;
                
                case 0x40: // BIT 0, B
                    Bit(B, 0);
                    break;
                
                case 0x41: // BIT 0, C
                    Bit(C, 0);
                    break;
                
                case 0x42: // BIT 0, D
                    Bit(D, 0);
                    break;
                
                case 0x43: // BIT 0, E
                    Bit(E, 0);
                    break;
                
                case 0x44: // BIT 0, H
                    Bit(H, 0);
                    break;
                
                case 0x45: // BIT 0, L
                    Bit(L, 0);
                    break;
                
                case 0x46: // BIT 0, (HL)
                    Bit(ReadByte(HL), 0);
                    break;
                
                case 0x47: // BIT 0, A
                    Bit(A, 0);
                    break;
                
                case 0x48: // BIT 1, B
                    Bit(B, 1);
                    break;
                
                case 0x49: // BIT 1, C
                    Bit(C, 1);
                    break;
                
                case 0x4A: // BIT 1, D
                    Bit(D, 1);
                    break;
                
                case 0x4B: // BIT 1, E
                    Bit(E, 1);
                    break;
                
                case 0x4C: // BIT 1, H
                    Bit(H, 1);
                    break;
                
                case 0x4D: // BIT 1, L
                    Bit(L, 1);
                    break;
                
                case 0x4E: // BIT 1, (HL)
                    Bit(ReadByte(HL), 1);
                    break;
                
                case 0x4F: // BIT 1, A
                    Bit(A, 1);
                    break;
                
                case 0x50: // BIT 2, B
                    Bit(B, 2);
                    break;
                
                case 0x51: // BIT 2, C
                    Bit(C, 2);
                    break;
                
                case 0x52: // BIT 2, D
                    Bit(D, 2);
                    break;
                
                case 0x53: // BIT 2, E
                    Bit(E, 2);
                    break;
                
                case 0x54: // BIT 2, H
                    Bit(H, 2);
                    break;
                
                case 0x55: // BIT 2, L
                    Bit(L, 2);
                    break;
                
                case 0x56: // BIT 2, (HL)
                    Bit(ReadByte(HL), 2);
                    break;

                case 0x57: // BIT 2, A
                    Bit(A, 2);
                    break;
                
                case 0x58: // BIT 3, B
                    Bit(B, 3);
                    break;
                
                case 0x59: // BIT 3, C
                    Bit(C, 3);
                    break;
                
                case 0x5A: // BIT 3, D
                    Bit(D, 3);
                    break;
                
                case 0x5B: // BIT 3, E
                    Bit(E, 3);
                    break;
                
                case 0x5C: // BIT 3, H
                    Bit(H, 3);
                    break;
                
                case 0x5D: // BIT 3, L
                    Bit(L, 3);
                    break;
                
                case 0x5E: // BIT 3, (HL)
                    Bit(ReadByte(HL), 3);
                    break;
                
                case 0x5F: // BIT 3, A
                    Bit(A, 3);
                    break;
                
                case 0x60: // BIT 4, B
                    Bit(B, 4);
                    break;
                
                case 0x61: // BIT 4, C
                    Bit(C, 4);
                    break;
                
                case 0x62: // BIT 4, D
                    Bit(D, 4);
                    break;
                
                case 0x63: // BIT 4, E
                    Bit(E, 4);
                    break;
                
                case 0x64: // BIT 4, H
                    Bit(H, 4);
                    break;
                
                case 0x65: // BIT 4, L
                    Bit(L, 4);
                    break;
                
                case 0x66: // BIT 4, (HL)
                    Bit(ReadByte(HL), 4);
                    break;
                
                case 0x67: // BIT 4, A
                    Bit(A, 4);
                    break;
                
                case 0x68: // BIT 5, B
                    Bit(B, 5);
                    break;
                
                case 0x69: // BIT 5, C
                    Bit(C, 5);
                    break;
                
                case 0x6A: // BIT 5, D
                    Bit(D, 5);
                    break;
                
                case 0x6B: // BIT 5, E
                    Bit(E, 5);
                    break;
                
                case 0x6C: // BIT 5, H
                    Bit(H, 5);
                    break;
                
                case 0x6D: // BIT 5, L
                    Bit(L, 5);
                    break;
                
                case 0x6E: // BIT 5, (HL)
                    Bit(ReadByte(HL), 5);
                    break;
                
                case 0x6F: // BIT 5, A
                    Bit(A, 5);
                    break;
                
                case 0x70: // BIT 6, B
                    Bit(B, 6);
                    break;
                
                case 0x71: // BIT 6, C
                    Bit(C, 6);
                    break;
                
                case 0x72: // BIT 6, D
                    Bit(D, 6);
                    break;
                
                case 0x73: // BIT 6, E
                    Bit(E, 6);
                    break;
                
                case 0x74: // BIT 6, H
                    Bit(H, 6);
                    break;
                
                case 0x75: // BIT 6, L
                    Bit(L, 6);
                    break;
                
                case 0x76: // BIT 6, (HL)
                    Bit(ReadByte(HL), 6);
                    break;
                
                case 0x77: // BIT 6, A
                    Bit(A, 6);
                    break;
                
                case 0x78: // BIT 7, B
                    Bit(B, 7);
                    break;
                
                case 0x79: // BIT 7, C
                    Bit(C, 7);
                    break;
                
                case 0x7A: // BIT 7, D
                    Bit(D, 7);
                    break;
                
                case 0x7B: // BIT 7, E
                    Bit(E, 7);
                    break;
                
                case 0x7C: // BIT 7, H
                    Bit(H, 7);
                    break;
                
                case 0x7D: // BIT 7, L
                    Bit(L, 7);
                    break;
                
                case 0x7E: // BIT 7, (HL)
                    Bit(ReadByte(HL), 7);
                    break;
                
                case 0x7F: // BIT 7, A
                    Bit(A, 7);
                    break;
                
                case 0x80: // RES 0, B
                    B = ResetBit(B, 0);
                    break;
                
                case 0x81: // RES 0, C
                    C = ResetBit(C, 0);
                    break;
                
                case 0x82: // RES 0, D
                    D = ResetBit(D, 0);
                    break;
                
                case 0x83: // RES 0, E
                    E = ResetBit(E, 0);
                    break;
                
                case 0x84: // RES 0, H
                    H = ResetBit(H, 0);
                    break;
                
                case 0x85: // RES 0, L
                    L = ResetBit(L, 0);
                    break;
                
                case 0x86: // RES 0, (HL)
                    var value9 = ReadByte(HL);
                    WriteByte(HL, ResetBit(value9, 0));
                    break;
                
                case 0x87: // RES 0, A
                    A = ResetBit(A, 0);
                    break;
                
                case 0x88: // RES 1, B
                    B = ResetBit(B, 1);
                    break;
                
                case 0x89: // RES 1, C
                    C = ResetBit(C, 1);
                    break;
                
                case 0x8A: // RES 1, D
                    D = ResetBit(D, 1);
                    break;
                
                case 0x8B: // RES 1, E
                    E = ResetBit(E, 1);
                    break;
                
                case 0x8C: // RES 1, H
                    H = ResetBit(H, 1);
                    break;
                
                case 0x8D: // RES 1, L
                    L = ResetBit(L, 1);
                    break;
                
                case 0x8E: // RES 1, (HL)
                    var value10 = ReadByte(HL);
                    WriteByte(HL, ResetBit(value10, 1));
                    break;
                
                case 0x8F: // RES 1, A
                    A = ResetBit(A, 1);
                    break;
                
                case 0x90: // RES 2, B
                    B = ResetBit(B, 2);
                    break;
                
                case 0x91: // RES 2, C
                    C = ResetBit(C, 2);
                    break;
                
                case 0x92: // RES 2, D
                    D = ResetBit(D, 2);
                    break;
                
                case 0x93: // RES 2, E
                    E = ResetBit(E, 2);
                    break;
                
                case 0x94: // RES 2, H
                    H = ResetBit(H, 2);
                    break;
                
                case 0x95: // RES 2, L
                    L = ResetBit(L, 2);
                    break;
                
                case 0x96: // RES 2, (HL)
                    var value11 = ReadByte(HL);
                    WriteByte(HL, ResetBit(value11, 2));
                    break;
                
                case 0x97: // RES 2, A
                    A = ResetBit(A, 2);
                    break;
                
                case 0x98: // RES 3, B
                    B = ResetBit(B, 3);
                    break;
                
                case 0x99: // RES 3, C
                    C = ResetBit(C, 3);
                    break;
                
                case 0x9A: // RES 3, D
                    D = ResetBit(D, 3);
                    break;
                
                case 0x9B: // RES 3, E
                    E = ResetBit(E, 3);
                    break;
                
                case 0x9C: // RES 3, H
                    H = ResetBit(H, 3);
                    break;
                
                case 0x9D: // RES 3, L
                    L = ResetBit(L, 3);
                    break;
                
                case 0x9E: // RES 3, (HL)
                    var value12 = ReadByte(HL);
                    WriteByte(HL, ResetBit(value12, 3));
                    break;
                
                case 0x9F: // RES 3, A
                    A = ResetBit(A, 3);
                    break;
                
                case 0xA0: // RES 4, B
                    B = ResetBit(B, 4);
                    break;
                
                case 0xA1: // RES 4, C
                    C = ResetBit(C, 4);
                    break;
                
                case 0xA2: // RES 4, D
                    D = ResetBit(D, 4);
                    break;
                
                case 0xA3: // RES 4, E
                    E = ResetBit(E, 4);
                    break;
                
                case 0xA4: // RES 4, H
                    H = ResetBit(H, 4);
                    break;
                
                case 0xA5: // RES 4, L
                    L = ResetBit(L, 4);
                    break;
                
                case 0xA6: // RES 4, (HL)
                    var value13 = ReadByte(HL);
                    WriteByte(HL, ResetBit(value13, 4));
                    break;
                
                case 0xA7: // RES 4, A
                    A = ResetBit(A, 4);
                    break;
                
                case 0xA8: // RES 5, B
                    B = ResetBit(B, 5);
                    break;
                
                case 0xA9: // RES 5, C
                    C = ResetBit(C, 5);
                    break;
                
                case 0xAA: // RES 5, D
                    D = ResetBit(D, 5);
                    break;
                
                case 0xAB: // RES 5, E
                    E = ResetBit(E, 5);
                    break;
                
                case 0xAC: // RES 5, H
                    H = ResetBit(H, 5);
                    break;
                
                case 0xAD: // RES 5, L
                    L = ResetBit(L, 5);
                    break;
                
                case 0xAE: // RES 5, (HL)
                    var value14 = ReadByte(HL);
                    WriteByte(HL, ResetBit(value14, 5));
                    break;
                
                case 0xAF: // RES 5, A
                    A = ResetBit(A, 5);
                    break;
                
                case 0xB0: // RES 6, B
                    B = ResetBit(B, 6);
                    break;
                
                case 0xB1: // RES 6, C
                    C = ResetBit(C, 6);
                    break;
                
                case 0xB2: // RES 6, D
                    D = ResetBit(D, 6);
                    break;
                
                case 0xB3: // RES 6, E
                    E = ResetBit(E, 6);
                    break;
                
                case 0xB4: // RES 6, H
                    H = ResetBit(H, 6);
                    break;
                
                case 0xB5: // RES 6, L
                    L = ResetBit(L, 6);
                    break;
                
                case 0xB6: // RES 6, (HL)
                    var value15 = ReadByte(HL);
                    WriteByte(HL, ResetBit(value15, 6));
                    break;
                
                case 0xB7: // RES 6, A
                    A = ResetBit(A, 6);
                    break;
                
                case 0xB8: // RES 7, B
                    B = ResetBit(B, 7);
                    break;
                
                case 0xB9: // RES 7, C
                    C = ResetBit(C, 7);
                    break;
                
                case 0xBA: // RES 7, D
                    D = ResetBit(D, 7);
                    break;
                
                case 0xBB: // RES 7, E
                    E = ResetBit(E, 7);
                    break;
                
                case 0xBC: // RES 7, H
                    H = ResetBit(H, 7);
                    break;
                
                case 0xBD: // RES 7, L
                    L = ResetBit(L, 7);
                    break;
                
                case 0xBE: // RES 7, (HL)
                    var value16 = ReadByte(HL);
                    WriteByte(HL, ResetBit(value16, 7));
                    break;
                
                case 0xBF: // RES 7, A
                    A = ResetBit(A, 7);
                    break;
                
                case 0xC0: // SET 0, B
                    B = SetBit(B, 0);
                    break;
                    
                case 0xC1: // SET 0, C
                    C = SetBit(C, 0);
                    break;
                
                case 0xC2: // SET 0, D
                    D = SetBit(D, 0);
                    break;
                
                case 0xC3: // SET 0, E
                    E = SetBit(E, 0);
                    break;
                
                case 0xC4: // SET 0, H
                    H = SetBit(H, 0);
                    break;
                
                case 0xC5: // SET 0, L
                    L = SetBit(L, 0);
                    break;
                
                case 0xC6: // SET 0, (HL)
                    var value17 = ReadByte(HL);
                    WriteByte(HL, SetBit(value17, 0));
                    break;
                
                case 0xC7: // SET 0, A
                    A = SetBit(A, 0);
                    break;
                
                case 0xC8: // SET 1, B
                    B = SetBit(B, 1);
                    break;
                
                case 0xC9: // SET 1, C
                    C = SetBit(C, 1);
                    break;
                
                case 0xCA: // SET 1, D
                    D = SetBit(D, 1);
                    break;
                
                case 0xCB: // SET 1, E
                    E = SetBit(E, 1);
                    break;
                
                case 0xCC: // SET 1, H
                    H = SetBit(H, 1);
                    break;
                
                case 0xCD: // SET 1, L
                    L = SetBit(L, 1);
                    break;
                
                case 0xCE: // SET 1, (HL)
                    var value18 = ReadByte(HL);
                    WriteByte(HL, SetBit(value18, 1));
                    break;
                
                case 0xCF: // SET 1, A
                    A = SetBit(A, 1);
                    break;
                
                case 0xD0: // SET 2, B
                    B = SetBit(B, 2);
                    break;
                
                case 0xD1: // SET 2, C
                    C = SetBit(C, 2);
                    break;
                
                case 0xD2: // SET 2, D
                    D = SetBit(D, 2);
                    break;
                
                case 0xD3: // SET 2, E
                    E = SetBit(E, 2);
                    break;
                
                case 0xD4: // SET 2, H
                    H = SetBit(H, 2);
                    break;
                
                case 0xD5: // SET 2, L
                    L = SetBit(L, 2);
                    break;
                
                case 0xD6: // SET 2, (HL)
                    var value19 = ReadByte(HL);
                    WriteByte(HL, SetBit(value19, 2));
                    break;
                
                case 0xD7: // SET 2, A
                    A = SetBit(A, 2);
                    break;
                
                case 0xD8: // SET 3, B
                    B = SetBit(B, 3);
                    break;
                
                case 0xD9: // SET 3, C
                    C = SetBit(C, 3);
                    break;
                
                case 0xDA: // SET 3, D
                    D = SetBit(D, 3);
                    break;
                
                case 0xDB: // SET 3, E
                    E = SetBit(E, 3);
                    break;
                
                case 0xDC: // SET 3, H
                    H = SetBit(H, 3);
                    break;
                
                case 0xDD: // SET 3, L
                    L = SetBit(L, 3);
                    break;
                
                case 0xDE: // SET 3, (HL)
                    var value20 = ReadByte(HL);
                    WriteByte(HL, SetBit(value20, 3));
                    break;
                
                case 0xDF: // SET 3, A
                    A = SetBit(A, 3);
                    break;
                
                case 0xE0: // SET 4, B
                    B = SetBit(B, 4);
                    break;
                
                case 0xE1: // SET 4, C
                    C = SetBit(C, 4);
                    break;
                
                case 0xE2: // SET 4, D
                    D = SetBit(D, 4);
                    break;
                
                case 0xE3: // SET 4, E
                    E = SetBit(E, 4);
                    break;
                
                case 0xE4: // SET 4, H
                    H = SetBit(H, 4);
                    break;
                
                case 0xE5: // SET 4, L
                    L = SetBit(L, 4);
                    break;
                
                case 0xE6: // SET 4, (HL)
                    var value21 = ReadByte(HL);
                    WriteByte(HL, SetBit(value21, 4));
                    break;
                
                case 0xE7: // SET 4, A
                    A = SetBit(A, 4);
                    break;
                
                case 0xE8: // SET 5, B
                    B = SetBit(B, 5);
                    break;
                
                case 0xE9: // SET 5, C
                    C = SetBit(C, 5);
                    break;
                
                case 0xEA: // SET 5, D
                    D = SetBit(D, 5);
                    break;
                
                case 0xEB: // SET 5, E
                    E = SetBit(E, 5);
                    break;
                
                case 0xEC: // SET 5, H
                    H = SetBit(H, 5);
                    break;
                
                case 0xED: // SET 5, L
                    L = SetBit(L, 5);
                    break;
                
                case 0xEE: // SET 5, (HL)
                    var value22 = ReadByte(HL);
                    WriteByte(HL, SetBit(value22, 5));
                    break;
                
                case 0xEF: // SET 5, A
                    A = SetBit(A, 5);
                    break;
                
                case 0xF0: // SET 6, B
                    B = SetBit(B, 6);
                    break;
                
                case 0xF1: // SET 6, C
                    C = SetBit(C, 6);
                    break;
                
                case 0xF2: // SET 6, D
                    D = SetBit(D, 6);
                    break;
                
                case 0xF3: // SET 6, E
                    E = SetBit(E, 6);
                    break;
                
                case 0xF4: // SET 6, H
                    H = SetBit(H, 6);
                    break;
                
                case 0xF5: // SET 6, L
                    L = SetBit(L, 6);
                    break;
                
                case 0xF6: // SET 6, (HL)
                    var value23 = ReadByte(HL);
                    WriteByte(HL, SetBit(value23, 6));
                    break;
                
                case 0xF7: // SET 6, A
                    A = SetBit(A, 6);
                    break;
                
                case 0xF8: // SET 7, B
                    B = SetBit(B, 7);
                    break;
                
                case 0xF9: // SET 7, C
                    C = SetBit(C, 7);
                    break;
                
                case 0xFA: // SET 7, D
                    D = SetBit(D, 7);
                    break;
                
                case 0xFB: // SET 7, E
                    E = SetBit(E, 7);
                    break;
                
                case 0xFC: // SET 7, H
                    H = SetBit(H, 7);
                    break;
                
                case 0xFD: // SET 7, L
                    L = SetBit(L, 7);
                    break;
                
                case 0xFE: // SET 7, (HL)
                    var value24 = ReadByte(HL);
                    WriteByte(HL, SetBit(value24, 7));
                    break;
                
                case 0xFF: // SET 7, A
                    A = SetBit(A, 7);
                    break;
            }
        }

        #region Memory helpers

        private byte ReadByte()     // Verified OK - 06
        {
            return memory[PC++];
        }

        private byte ReadByte(ushort address)
        {
            return memory[address];
        }

        private ushort ReadWord()       // Verified OK - 08
        {
            var lsb = memory[PC++];
            var msb = memory[PC++];
            return Word(lsb, msb);
        }
        
        private ushort ReadWord(ushort address)
        {
            var lsb = memory[address];
            var msb = memory[(ushort) (address + 1)];
            return Word(lsb, msb);
        }

        private ushort Word(byte lsb, byte msb)     // Verified OK - 08
        {
            return (ushort)((msb << 8) | lsb);
        }
        
        private void WriteByte(ushort address, byte value)      // Verified OK - 06
        {
            memory[address] = value;
        }
        
        private void WriteWord(ushort address, ushort value)    // Verified OK - 08
        {
            memory[address] = (byte)(value & 0xFF);
            memory[address + 1] = (byte)(value >> 8);
        }

        #endregion

        #region Main instructions helpers
        
        private void PushWord(ushort value)     // Verified OK - 08
        {
            SP -= 2;
            WriteWord(SP, value);
        }
        
        private ushort PopWord()                // Verified OK - 08
        {
            var value = ReadWord(SP);
            SP += 2;
            return value;
        }
        
        private void Call()                     // Verified OK - 07
        {
            PushWord((ushort) (PC + 2));
            PC = ReadWord();
        }
        
        private void Rst(byte lsbAddress)       // Verified OK - 07
        {
            PushWord(PC);
            PC = lsbAddress;
        }
        
        private byte Increment(byte value)      // Verified OK - 09
        {
            FlagH = (value & 0x0F) == 0x0F;
            value++;
            FlagZ = value == 0;
            FlagN = false;
            return value;
        }
        
        private byte Decrement(byte value)      // Verified OK - 09
        {
            FlagH = (value & 0x0F) == 0;
            value--;
            FlagZ = value == 0;
            FlagN = true;
            return value;
        }
        
        private void And(byte value)        // Verified OK - 09
        {
            A = (byte) (A & value);
            FlagZ = A == 0;
            FlagN = false;
            FlagH = true;
            FlagC = false;
        }
        
        private void Or(byte value)         // Verified OK - 09
        {
            A = (byte) (A | value);
            FlagZ = A == 0;
            FlagN = false;
            FlagH = false;
            FlagC = false;
        }
        
        private void Xor(byte value)        // Verified OK - 09
        {
            A = (byte) (A ^ value);
            FlagZ = A == 0;
            FlagN = false;
            FlagH = false;
            FlagC = false;
        }
        
        private void Add(byte value)        // Verified OK - 09
        {
            var result = (ushort)(A + value);
            FlagC = (result & 0xFF00) != 0;
            FlagH = (A & 0x0F) + (value & 0x0F) > 0x0F;
            A = (byte) (result & 0xFF);
            FlagZ = A == 0;
            FlagN = false;
        }
        
        private void Adc(byte value)        // Verified OK - 09
        {
            var result = (ushort)(A + value + (FlagC ? 1 : 0));
            FlagH = (A & 0x0F) + (value & 0x0F) + (FlagC ? 1 : 0) > 0x0F;
            FlagC = (result & 0xFF00) != 0;
            A = (byte) (result & 0xFF);
            FlagZ = A == 0;
            FlagN = false;
        }
        
        private void Sub(byte value)        // Verified OK - 09
        {
            FlagN = true;
            FlagC = value > A;
            FlagZ = value == A;
            FlagH = (value & 0x0F) > (A & 0x0F);
            A -= value;
        }
        
        private void Sbc(byte value)        // Verified OK - 09
        {
            int result = A - value - (FlagC ? 1 : 0);
            FlagH = ((sbyte)A & 0x0F) - (value & 0x0F) - (FlagC ? 1 : 0) < 0;
            FlagC = result < 0;
            A = (byte)result;
            FlagZ = A == 0;
            FlagN = true;
        }
        
        // 16 bit airthmetic add
        // Flags affected:
        // Z - Not affected.
        // N - Reset.
        // H - Set if carry from bit 11.
        // C - Set if carry from bit 15. 
        private ushort Add16(ushort a, ushort b)    // Verified OK - 05
        {
            int result = a + b;
            FlagC = (result & 0xFFFF0000) != 0;
            FlagN = false;
            FlagH = ((a & 0x0FFF) + (b & 0x0FFF)) > 0x0FFF;
            //Debug.Print($"a={a:X4}, b={b:X4}, res={((ushort)(result & 0xFFFF)):X4}, C={FlagC}, H={FlagH}");
            return (ushort) (result & 0xFFFF);
        }
        
        private void Compare(byte a)                // Verified OK - 09
        {
            Compare(A, a);
        }
        
        // Compare A with n. This is basically an A - n subtraction instruction but
        // the results are thrown away.
        // Flags affected:
        // Z - Set if result is zero. (Set if A = n.)
        // N - Set.
        // H - Set if no borrow from bit 4.
        // C - Set for no borrow. (Set if A < n.)
        private void Compare(byte a, byte b)        // Verified OK - 09
        {
            FlagN = true;
            FlagC = b > a;
            FlagZ = b == a;
            FlagH = (b & 0x0F) > (a & 0x0F);
        }
        
        #endregion
    
        // Rotate n left. Old bit 7 to Carry flag.
        // Flags affected:
        // Z - Set if result is zero.
        // N - Reset.
        // H - Reset.
        // C - Contains old bit 7 data.
        private byte Rlc(byte value)        // Verified OK - 09
        {
            FlagC = (value & 0x80) != 0;
            byte result = (byte) (value << 1);
            result = (byte) (result | (FlagC? 1 : 0));
            FlagZ = result == 0;
            FlagN = false;
            FlagH = false;
            return result;
        }
        
        // Rotate n right. Old bit 0 to Carry flag.
        // Flags affected:
        // Z - Set if result is zero.
        // N - Reset.
        // H - Reset.
        // C - Contains old bit 0 data.
        private byte Rrc(byte value)        // Verified OK - 09
        {
            FlagC = (value & 0x01) != 0;
            byte result = (byte) (value >> 1);
            if (FlagC)
                result |= 0x80;
            FlagZ = result == 0;
            FlagN = false;
            FlagH = false;
            return result;
        }

        // Rotate n left through Carry flag.
        // Flags affected:
        // Z - Set if result is zero.
        // N - Reset.
        // H - Reset.
        // C - Contains old bit 7 data.
        private byte Rl(byte value)         // Verified OK - 09
        {
            int carry = FlagC ? 1 : 0;
            FlagC = (value & 0x80) != 0;
            byte result = (byte) (value << 1);
            result = (byte) (result | carry);
            FlagZ = result == 0;
            FlagN = false;
            FlagH = false;
            return result;
        }
        
        // Rotate n right through Carry flag.
        // Flags affected:
        // Z - Set if result is zero.
        // N - Reset.
        // H - Reset.
        // C - Contains old bit 0 data.
        private byte Rr(byte value)         // Verified OK - 09
        {
            byte result = (byte) (value >> 1);
            if (FlagC)
                result |= 0x80;
            FlagC = (value & 0x01) != 0;
            FlagZ = result == 0;
            FlagN = false;
            FlagH = false;
            return result;
        }
        
        // Shift n left into Carry. LSB of n set to 0
        // Flags affected:
        // Z - Set if result is zero.
        // N - Reset.
        // H - Reset.
        // C - Contains old bit 7 data.
        private byte Sla(byte value)            // Verified OK - 09
        {
            FlagC = (value & 0x80) != 0;
            byte result = (byte) (value << 1);
            FlagZ = result == 0;
            FlagN = false;
            FlagH = false;
            return result;
        }
        
        // Shift n right into Carry. MSB doesn't change. <-- !!!
        // Flags affected:
        // Z - Set if result is zero.
        // N - Reset.
        // H - Reset.
        // C - Contains old bit 0 data. 
        private byte Sra(byte value)            // Verified OK - 09
        {
            FlagC = (value & 0x01) != 0;
            var result = (byte) ((value & 0x80) | (value >> 1));
            FlagZ = result == 0;
            FlagN = false;
            FlagH = false;
            return result;
        }

        // Shift n right into Carry. MSB set to 0.
        // Flags affected:
        // Z - Set if result is zero.
        // N - Reset.
        // H - Reset.
        // C - Contains old bit 0 data.
        private byte Srl(byte value)            // Verified OK - 09
        {
            FlagC = (value & 0x01) != 0;
            var result = (byte) (value >> 1);
            FlagZ = result == 0;
            FlagN = false;
            FlagH = false;
            return result;
        }
        
        private ushort SwapWord(ushort value)
        {
            return (ushort)(((value & 0x00FF) << 8) | ((value & 0xFF00) >> 8));
        }

        private byte Swap(byte value)               // Verified OK - 09
        {
            var result = (byte) (((value & 0x0F) << 4) | ((value & 0xF0) >> 4));
            FlagZ = result == 0;
            FlagN = false;
            FlagH = false;
            FlagC = false;
            return result;
        }
        
        private void Bit(byte value, byte bit)          // Verified OK - 10
        {
            FlagZ = (value & (1 << bit)) == 0;
            FlagN = false;
            FlagH = true;
        }
        
        private byte SetBit(byte value, byte bit)       // Verified OK - 10
        {
            return (byte) (value | (1 << bit));
        }
        
        private byte ResetBit(byte value, byte bit)     // Verified OK - 10
        {
            return (byte) (value & ~(1 << bit));
        }
        
    }
}
