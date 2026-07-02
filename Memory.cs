using System;
using System.Drawing;

// References
// - https://github.com/CTurt/Cinoop mostly code from memory.c like IO and some registers
// - http://marc.rawer.de/Gameboy/Docs/GBCPUman.pdf probably the best GameBoy CPU/memory manual
// - https://knight.sc/reverse%20engineering/2018/11/19/game-boy-boot-sequence.html on Boot sequence
// - https://gist.github.com/drhelius/6063288 on Boot sequence
// - https://retrocomputing.stackexchange.com/questions/11732/how-does-the-gameboys-memory-bank-switching-work on MBC1 implementation

namespace ZarthGB
{
    class Memory
    {
	    private Sound sound;
        private byte[] memory;
        private byte[] cartridge;
        public int Ticks { get; set; }
        public bool TimerEnabled { get; private set; }
        public long TimerTick { get; private set; }
        
        #region Keyboard
        public bool KeyUp { get; set; }
        public bool KeyDown { get; set; }
        public bool KeyLeft { get; set; }
        public bool KeyRight { get; set; }
        public bool KeyA { get; set; }
        public bool KeyB { get; set; }
        public bool KeyStart { get; set; }
        public bool KeySelect { get; set; }

        private byte Keys1 => (byte)((KeyA ? 0 : 1 << 0) | 
                                     (KeyB ? 0 : 1 << 1) | 
                                     (KeySelect ? 0 : 1 << 2) |
                                     (KeyStart ? 0 : 1 << 3));
        private byte Keys2 => (byte)((KeyRight ? 0 : 1 << 0) | 
                                     (KeyLeft ? 0 : 1 << 1) | 
                                     (KeyUp ? 0 : 1 << 2) |
                                     (KeyDown ? 0 : 1 << 3));
        #endregion
        
        #region Video
        public byte[,,] Tiles;
        public Color[] Palette { get; } = new Color[4] 
        {
	        Color.White,
	        Color.LightGray,
	        Color.DarkGray,
	        Color.Black,
        };
        public Color[] BackgroundPalette { get; } = new Color[4] 
        {
	        Color.White,
	        Color.LightGray,
	        Color.DarkGray,
	        Color.Black,
        };
        public Color[,] SpritePalette { get; } = new Color[2,4] 
        {
	        {
		        Color.White,
		        Color.LightGray,
		        Color.DarkGray,
		        Color.Black,
	        },
	        {
		        Color.White,
		        Color.LightGray,
		        Color.DarkGray,
		        Color.Black,
	        }
        };
        #endregion

        #region Initial state
		private byte[] dmgBoot = 
		{
			0x31, 0xFE, 0xFF, 0xAF, 0x21, 0xFF, 0x9F, 0x32, 0xCB, 0x7C, 0x20, 0xFB,
			0x21, 0x26, 0xFF, 0x0E, 0x11, 0x3E, 0x80, 0x32, 0xE2, 0x0C, 0x3E, 0xF3,
			0xE2, 0x32, 0x3E, 0x77, 0x77, 0x3E, 0xFC, 0xE0, 0x47, 0x11, 0x04, 0x01,
			0x21, 0x10, 0x80, 0x1A, 0xCD, 0x95, 0x00, 0xCD, 0x96, 0x00, 0x13, 0x7B,
			0xFE, 0x34, 0x20, 0xF3, 0x11, 0xD8, 0x00, 0x06, 0x08, 0x1A, 0x13, 0x22,
			0x23, 0x05, 0x20, 0xF9, 0x3E, 0x19, 0xEA, 0x10, 0x99, 0x21, 0x2F, 0x99,
			0x0E, 0x0C, 0x3D, 0x28, 0x08, 0x32, 0x0D, 0x20, 0xF9, 0x2E, 0x0F, 0x18,
			0xF3, 0x67, 0x3E, 0x64, 0x57, 0xE0, 0x42, 0x3E, 0x91, 0xE0, 0x40, 0x04,
			0x1E, 0x02, 0x0E, 0x0C, 0xF0, 0x44, 0xFE, 0x90, 0x20, 0xFA, 0x0D, 0x20,
			0xF7, 0x1D, 0x20, 0xF2, 0x0E, 0x13, 0x24, 0x7C, 0x1E, 0x83, 0xFE, 0x62,
			0x28, 0x06, 0x1E, 0xC1, 0xFE, 0x64, 0x20, 0x06, 0x7B, 0xE2, 0x0C, 0x3E,
			0x87, 0xE2, 0xF0, 0x42, 0x90, 0xE0, 0x42, 0x15, 0x20, 0xD2, 0x05, 0x20,
			0x4F, 0x16, 0x20, 0x18, 0xCB, 0x4F, 0x06, 0x04, 0xC5, 0xCB, 0x11, 0x17,
			0xC1, 0xCB, 0x11, 0x17, 0x05, 0x20, 0xF5, 0x22, 0x23, 0x22, 0x23, 0xC9,
			0xCE, 0xED, 0x66, 0x66, 0xCC, 0x0D, 0x00, 0x0B, 0x03, 0x73, 0x00, 0x83,
			0x00, 0x0C, 0x00, 0x0D, 0x00, 0x08, 0x11, 0x1F, 0x88, 0x89, 0x00, 0x0E,
			0xDC, 0xCC, 0x6E, 0xE6, 0xDD, 0xDD, 0xD9, 0x99, 0xBB, 0xBB, 0x67, 0x63,
			0x6E, 0x0E, 0xEC, 0xCC, 0xDD, 0xDC, 0x99, 0x9F, 0xBB, 0xB9, 0x33, 0x3E,
			0x3C, 0x42, 0xB9, 0xA5, 0xB9, 0xA5, 0x42, 0x3C, 0x21, 0x04, 0x01, 0x11,
			0xA8, 0x00, 0x1A, 0x13, 0xBE, 0x20, 0xFE, 0x23, 0x7D, 0xFE, 0x34, 0x20,
			0xF5, 0x06, 0x19, 0x78, 0x86, 0x23, 0x05, 0x20, 0xFB, 0x86, 0x20, 0xFE,
			0x3E, 0x01, 0xE0, 0x50
		};
		
		private byte[] ioReset = {
			0x0F, 0x00, 0x7C, 0xFF, 0x00, 0x00, 0x00, 0xF8, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01,
			0x80, 0xBF, 0xF3, 0xFF, 0xBF, 0xFF, 0x3F, 0x00, 0xFF, 0xBF, 0x7F, 0xFF, 0x9F, 0xFF, 0xBF, 0xFF,
			0xFF, 0x00, 0x00, 0xBF, 0x77, 0xF3, 0xF1, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
			0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF,
			0x91, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFC, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x7E, 0xFF, 0xFE,
			0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x3E, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
			0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xC0, 0xFF, 0xC1, 0x00, 0xFE, 0xFF, 0xFF, 0xFF,
			0xF8, 0xFF, 0x00, 0x00, 0x00, 0x8F, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
			0xCE, 0xED, 0x66, 0x66, 0xCC, 0x0D, 0x00, 0x0B, 0x03, 0x73, 0x00, 0x83, 0x00, 0x0C, 0x00, 0x0D,
			0x00, 0x08, 0x11, 0x1F, 0x88, 0x89, 0x00, 0x0E, 0xDC, 0xCC, 0x6E, 0xE6, 0xDD, 0xDD, 0xD9, 0x99,
			0xBB, 0xBB, 0x67, 0x63, 0x6E, 0x0E, 0xEC, 0xCC, 0xDD, 0xDC, 0x99, 0x9F, 0xBB, 0xB9, 0x33, 0x3E,
			0x45, 0xEC, 0x52, 0xFA, 0x08, 0xB7, 0x07, 0x5D, 0x01, 0xFD, 0xC0, 0xFF, 0x08, 0xFC, 0x00, 0xE5,
			0x0B, 0xF8, 0xC2, 0xCE, 0xF4, 0xF9, 0x0F, 0x7F, 0x45, 0x6D, 0x3D, 0xFE, 0x46, 0x97, 0x33, 0x5E,
			0x08, 0xEF, 0xF1, 0xFF, 0x86, 0x83, 0x24, 0x74, 0x12, 0xFC, 0x00, 0x9F, 0xB4, 0xB7, 0x06, 0xD5,
			0xD0, 0x7A, 0x00, 0x9E, 0x04, 0x5F, 0x41, 0x2F, 0x1D, 0x77, 0x36, 0x75, 0x81, 0xAA, 0x70, 0x3A,
			0x98, 0xD1, 0x71, 0x02, 0x4D, 0x01, 0xC1, 0xFF, 0x0D, 0x00, 0xD3, 0x05, 0xF9, 0x00, 0x0B, 0x00
		};
		#endregion

		#region MBC1

		private bool Mbc1Enabled;
		private bool RamEnabled;
		private int BankSelected;
		private bool Mbc1ModeRam;
		private int RamBankSelected;
		private byte[] Ram = new byte[32 * 1024];
		
		#endregion
		
		public Memory()
        {
	        Reset();
	        sound = new Sound(this);
		}

		public void StopSound()
		{
			sound.Stop();
		}

		public void IncrementDiv()
		{
			memory[0xff04]++;

			// Audio timing is driven by the sound device pulling samples, but we still poke the
			// device periodically so it starts/recovers (the initial Play() can run before it is
			// ready). Cheap no-op once it is already playing.
			if (memory[0xff04] % 256 == 0)
				sound.EnsurePlaying();
		}

		public void Reset()
		{
			Tiles = new byte[384,8,8];
			Ticks = 0;

			memory = new byte[0x10000];

			for (int i = 0; i < dmgBoot.Length; i++)
				memory[i] = dmgBoot[i];

			for (int i = 0; i < ioReset.Length; i++)
				memory[0xff00 + i] = ioReset[i];

			#region Init FF registers
			memory[0xFF05] = 0x00;
			memory[0xFF06] = 0x00;
			memory[0xFF07] = 0x00;
			memory[0xFF10] = 0x80;
			memory[0xFF11] = 0xBF;
			memory[0xFF12] = 0xF3;
			memory[0xFF14] = 0xBF;
			memory[0xFF16] = 0x3F;
			memory[0xFF17] = 0x00;
			memory[0xFF19] = 0xBF;
			memory[0xFF1A] = 0x7F;
			memory[0xFF1B] = 0xFF;
			memory[0xFF1C] = 0x9F;
			memory[0xFF1E] = 0xBF;
			memory[0xFF20] = 0xFF;
			memory[0xFF21] = 0x00;
			memory[0xFF22] = 0x00;
			memory[0xFF23] = 0xBF;
			memory[0xFF24] = 0x77;
			memory[0xFF25] = 0xF3;
			memory[0xFF26] = 0xF1;
			memory[0xFF40] = 0x91;
			memory[0xFF42] = 0x00;
			memory[0xFF43] = 0x00;
			memory[0xFF45] = 0x00;
			memory[0xFF47] = 0xFC;
			memory[0xFF48] = 0xFF;
			memory[0xFF49] = 0xFF;
			memory[0xFF4A] = 0x00;
			memory[0xFF4B] = 0x00;
			memory[0xFF50] = 0x00;	// Enable Boot ROM (explicitly added)
			memory[0xFFFF] = 0x00;
			#endregion
		}

		public void SetCartridge(byte[] cart, Cartridge.RomType romType)
		{
			Mbc1Enabled = romType == Cartridge.RomType.Mbc1;
			if (!Mbc1Enabled && cart.Length != 32 * 1024)
				throw new InvalidOperationException("Wrong plain cartridge length!");

			cartridge = new byte[cart.Length];
			for (int i = 0; i < cart.Length; i++)
				cartridge[i] = cart[i];

			BankSelected = 1;
			RamBankSelected = 0;
			Mbc1ModeRam = false;
		}
		
		public byte this[int address]
        {
	        get
	        {
		        // If requesting low bytes and boot rom is not disabled (inverted bool)
		        if (address <= 0x00FF && memory[0xFF50] == 0x00)
			        return dmgBoot[address];

		        if (Mbc1Enabled)
		        {
			        if (address <= 0x3FFF)
				        return cartridge[address];
			        if (address <= 0x7FFF)
				        return cartridge[(BankSelected * 16 * 1024) + (address - 0x4000)];
			        if (RamEnabled && Mbc1ModeRam && address >= 0xA000 && address <= 0xBFFFF)
				        return Ram[(RamBankSelected * 16 * 1024) + (address - 0xA000)];
		        }
		        
		        if (!Mbc1Enabled)
			        if (address <= 0x7FFF)
				        return cartridge[address];
		        
		        if (address == 0xff00) 
		        {
			        byte io = memory[address];
			        if((io & 0x20) == 0) 
				        return (byte)(0xc0 | Keys1 | 0x10);
			        if((io & 0x10) == 0) 
				        return (byte)(0xc0 | Keys2 | 0x20);
					if((io & 0x30) == 0) 
						return 0xff;
			        return 0;
		        }
		        
			    return memory[address]; 
	        }
	        set
	        {
		        if (Mbc1Enabled)
		        {
			        if (address <= 0x1FFF)
			        {
				        RamEnabled = ((value & 0x0F) == 0x0A);
				        RamBankSelected = 0;
				        return;
			        }
			        if (address <= 0x3FFF)
			        {
				        int bankSelectedLow5 = value & 0x1F;
				        if (bankSelectedLow5 == 0)
							bankSelectedLow5 = 1;
				        BankSelected = (BankSelected & 0x60) | bankSelectedLow5;
				        return;
			        }
			        if (address <= 0x5FFF)
			        {
				        int bankSelectedHigh2 = value & 3;	// Only two bits
						if(Mbc1ModeRam)
							RamBankSelected = bankSelectedHigh2;
						else
							BankSelected = (BankSelected & 0x1F) | (bankSelectedHigh2 << 5);
						return;
			        }
			        if (address <= 0x7FFF)
			        {
				        Mbc1ModeRam = value == 1;
				        return;
			        }
			        if (Mbc1ModeRam && address >= 0xA000 && address <= 0xBFFF)
			        {
				        Ram[(RamBankSelected * 16 * 1024) + (address - 0xA000)] = value;
				        return;
			        }
		        }

		        memory[address] = value; 

		        // Tileset changed (video ram)
		        if(address >= 0x8000 && address <= 0x97ff) 
			        UpdateTile(address, value);
		        
		        // Echo of 8KB internal RAM (0xC00-0xDE00 = 0xE000-0xFE00)
		        else if (address >= 0xC000 && address < 0xDE00)
			        memory[address+0x2000] = value; 
		        else if (address >= 0xE000 && address < 0xFE00)
			        memory[address-0x2000] = value; 
		        
		        // Serial transfer start (a futile attempt to prevent Tetris 2P hang)
		        /*else if (address == 0xff02 && (value >> 7) > 0)
		        {
			        // Mark transfer finished -we don't have serial transfers here
			        memory[address-1] = 0xFF;	// No other GameBoy present
			        memory[address] = (byte)(value & 0x7F);
			        // but the moment we launch the interruption, the 2P game starts...!
			        if((memory[0xffff] & Cpu.InterruptsSerial) > 0) 
				        memory[0xff0f] |= Cpu.InterruptsSerial;
		        }*/
		        
		        // DIV (timer) reset -writing to it resets it to 0
		        else if (address == 0xff04)
			        memory[address] = 0;
		        
		        // Configurable Timer
		        else if (address == 0xff07)
		        {
			        switch (value & 0x03)
			        {
				        // Clock cycles: 4.19 MHz = 4.393.533,44 vs frequency (below)
				        case 0: TimerTick = 1072; break;	// 4,096  KHz	-> 1072,64 clock cycles
				        case 1: TimerTick = 17;   break;	// 262,144 KHz	-> 16,76
				        case 2: TimerTick = 67;   break;	// 65,536  KHz	-> 67,04
				        case 3: TimerTick = 268;  break;	// 16,384  KHz	-> 268,16
			        }

			        if ((value & 0x04) > 0)
				        TimerEnabled = true;
			        else
				        TimerEnabled = false;
		        }
		        
		        // Sound
		        else if (address == 0xff14)
			        sound.StartSound1();
		        else if (address == 0xff19)
			        sound.StartSound2();
		        else if (address == 0xff1e)
			        sound.StartSound3();
		        else if (address == 0xff23)
			        sound.StartSound4();

		        // OAM DMA
		        else if (address == 0xff46)
		        {
			        int sourceAddress = value << 8;			        
			        for (int j = 0; j < 160; j++)
				        memory[0xfe00 + j] = memory[sourceAddress++];
		        }

		        // Alter background palette
		        else if(address == 0xff47)	// write only 
		        { 
			        for(int i = 0; i < 4; i++) BackgroundPalette[i] = Palette[(value >> (i * 2)) & 3];
		        }
		        
		        // Alter sprite palettes
		        else if(address == 0xff48)	// write only 
		        { 
			        for(int i = 0; i < 4; i++) SpritePalette[0,i] = Palette[(value >> (i * 2)) & 3];
		        }
		        else if(address == 0xff49)	// write only 
		        { 
			        for(int i = 0; i < 4; i++) SpritePalette[1,i] = Palette[(value >> (i * 2)) & 3];
		        }
	        }
        }
		
		private void UpdateTile(int address, byte value) 
		{
			address &= 0xfffe;
	
			ushort tile = (ushort) ((address >> 4) & 0x1FF);
			ushort y = (ushort) ((address >> 1) & 7);
	
			byte x, bitIndex;
			for(x = 0; x < 8; x++) 
			{
				bitIndex = (byte) (1 << (7 - x));
		
				Tiles[tile,y,x] = (byte) ((((memory[address] & bitIndex) > 0) ? 1 : 0) + 
				                          (((memory[address + 1] & bitIndex) > 0) ? 2 : 0));
			}
		}
    }
}
