using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmuGBSharp
{
	class Cartridge
	{
		const int RomOffsetName = 0x134;
		const int RomOffsetType = 0x147;
		const int RomOffsetRomSize = 0x148;
		const int RomOffsetRamSize = 0x149;

		public enum RomType {
			Plain = 0x00,
			Mbc1 = 0x01,
			Mbc1Ram = 0x02,
			Mbc1RamBatt = 0x03,
			Mbc2 = 0x05,
			Mbc2Battery = 0x06,
			Ram = 0x08,
			RamBattery = 0x09,
			Mmm01 = 0x0B,
			Mmm01Sram = 0x0C,
			Mmm01SramBatt = 0x0D,
			Mbc3TimerBatt = 0x0F,
			Mbc3TimerRamBatt = 0x10,
			Mbc3 = 0x11,
			Mbc3Ram = 0x12,
			Mbc3RamBatt = 0x13,
			Mbc5 = 0x19,
			Mbc5Ram = 0x1A,
			Mbc5RamBatt = 0x1B,
			Mbc5Rumble = 0x1C,
			Mbc5RumbleSram = 0x1D,
			Mbc5RumbleSramBatt = 0x1E,
			PocketCamera = 0x1F,
			BandaiTama5 = 0xFD,
			HudsonHuc3 = 0xFE,
			HudsonHuc1 = 0xFF,
		};
		
		public byte[] Rom { get; private set; }
		public int RomSize { get; private set; }
		public int RamSize { get; private set; }
		public RomType CartType { get; private set; }

		public void Load(string filename)
		{
			Rom = File.ReadAllBytes(filename);
			if (Rom.Length < 0x180)
                throw new InvalidDataException("ROM is too small!");

			var name = new byte[17];
			for (int i = 0; i < 16; i++)
			{
				if (Rom[i + RomOffsetName] == 0x80 || Rom[i + RomOffsetName] == 0xc0) 
					name[i] = (byte)'\0';
				else 
					name[i] = Rom[i + RomOffsetName];
			}

			RomSize = (int)Math.Pow(2.0, (double)(Rom[RomOffsetRomSize] + 1));
			RamSize = (int)Math.Pow(2.0, (double)(Rom[RomOffsetRamSize] + 1));
			
			CartType = (RomType)Rom[RomOffsetType];

			if (CartType != RomType.Plain && CartType != RomType.Mbc1)
				throw new InvalidDataException("Cartridge not supported!");
				
			if (Rom.Length != RomSize * 16 * 1024)
				throw new InvalidDataException("ROM filesize does not equal ROM size!");
		}
    }
}
