using System;
using System.Diagnostics;
using System.Drawing;

// References
// - https://github.com/CTurt/Cinoop mostly code from gpu.c and display.c -the second one heavily altered using below 
// - http://www.codeslinger.co.uk/pages/projects/gameboy/graphics.html for window and tiles implementation
// - https://rylev.github.io/DMG-01/public/book/graphics/tile_ram.html on Tiles and encoding
// - https://www.youtube.com/watch?v=zQE1K074v3s How Graphics worked on the Nintendo Game Boy | MVG -for LYC register and interrupt!!

namespace ZarthGB
{
    class Video
    {
        private Memory memory;
        const ushort VideoRamBegin = 0x8000;
        const ushort OamBegin = 0xfe00;

        Stopwatch stopwatch = new Stopwatch();
        // Full DMG frame period (~16.75 ms). The sleep in Step() paces one whole frame; the previous
        // "/ 2" was a hack tuned to Windows' coarse ~15.6 ms Thread.Sleep granularity, which no longer
        // applies now that Program raises the timer resolution to 1 ms.
        TimeSpan expectedFrameTime = TimeSpan.FromTicks((long) (TimeSpan.TicksPerSecond / 59.727500569606));
        // Absolute target for the next frame boundary. Pacing against a running timeline (rather than
        // restarting the clock each frame) makes small Thread.Sleep overshoots cancel out instead of
        // accumulating, so the long-term rate stays exact and the audio clock hears no periodic drift.
        TimeSpan nextFrameTime;
        public bool frameReady = false;
        public bool IsFrameReady
        {
            get
            {
                if (frameReady)
                {
                    frameReady = false;
                    return true;
                }
                
                return false;
            }
        }

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
        
        private byte Control => memory[0xFF40];
        private byte ScrollY => memory[0xFF42];
        private byte ScrollX => memory[0xFF43];
        private byte WindowY => memory[0xFF4A];
        private byte WindowX => (byte)(memory[0xFF4B] - 7);
        private byte Scanline
        {
            get { return memory[0xFF44]; }
            set { memory[0xFF44] = value; }
        }
        private byte ScanlineInterrupt => memory[0xFF45];
        
        private int GpuTick;
        private byte[] scanlineRow = new byte[160];
        private Color[] framebuffer;
        Sprite sprite;

        public enum GpuModeEnum
        {
            HBlank,
            VBlank,
            Oam,
            VRam,
        } 

        private GpuModeEnum GpuMode = GpuModeEnum.HBlank;

        private int Ticks => memory.Ticks;
        int LastTicks = 0;

        private byte BGEnable => (1 << 0);
        private byte SpriteEnable => (1 << 1);
        private byte SpriteDouble => (1 << 2);
        private byte TileMap => (1 << 3);
        private byte TileSet => (1 << 4);
        private byte WindowEnable => (1 << 5);
        private byte WindowTileMap => (1 << 6);
        private byte DisplayEnable => (1 << 7);

        
        public Video(Memory memory, Color[] framebuffer)
        {
            this.memory = memory;
            this.framebuffer = framebuffer;
            sprite = new Sprite(memory);
            stopwatch.Start();
        }
        
        public void Step() 
        {
            GpuTick += Ticks - LastTicks;
	        LastTicks = Ticks;

            switch(GpuMode) 
            {
                case GpuModeEnum.HBlank:
                    if(GpuTick >= 204) 
                    {
                        Scanline++;     // HBlank
                        if(Scanline == 143) 
                        {
                            if((InterruptEnable & Cpu.InterruptsVblank) > 0)
                                InterruptFlags |= Cpu.InterruptsVblank;

                            PaceFrame();
                            frameReady = true;

                            GpuMode = GpuModeEnum.VBlank;
                        }
                        else 
                            GpuMode = GpuModeEnum.Oam;
				
                        GpuTick -= 204;
                    }
			
                    break;
		
                case GpuModeEnum.VBlank:
                    if(GpuTick >= 456) 
                    {
                        Scanline++;
                        if(Scanline > 153) {
                            Scanline = 0;
                            GpuMode = GpuModeEnum.Oam;
                        }
                        GpuTick -= 456;
                    }
			
                    break;
		
                case GpuModeEnum.Oam:
                    if(GpuTick >= 80) 
                    {
                        GpuMode = GpuModeEnum.VRam;
                        GpuTick -= 80;
                    }
			
                    break;
		
                case GpuModeEnum.VRam:
                    if(GpuTick >= 172) 
                    {
                        GpuMode = GpuModeEnum.HBlank;
                        if((InterruptEnable & Cpu.InterruptsLcdstat) > 0 && Scanline == ScanlineInterrupt) 
                            InterruptFlags |= Cpu.InterruptsLcdstat;
                        RenderScanline();
                        GpuTick -= 172;
                    }
			
                    break;
            }
        }

        // Throttle one emulated frame to real time. Sleeps for the bulk of the wait (cheap, yields
        // the CPU to the audio thread) then spin-waits the final ~1.5 ms, because Thread.Sleep only
        // guarantees a *minimum* and jitters. The target advances on a fixed timeline so overshoots
        // self-correct; if we ever fall more than a frame behind we resync instead of racing to
        // catch up (which is what produced the periodic "rush").
        private void PaceFrame()
        {
            if (!stopwatch.IsRunning)
            {
                stopwatch.Start();
                nextFrameTime = stopwatch.Elapsed;
            }

            nextFrameTime += expectedFrameTime;

            TimeSpan remaining = nextFrameTime - stopwatch.Elapsed;
            if (remaining > TimeSpan.Zero)
            {
                TimeSpan spinThreshold = TimeSpan.FromMilliseconds(1.5);
                if (remaining > spinThreshold)
                    System.Threading.Thread.Sleep(remaining - spinThreshold);
                while (stopwatch.Elapsed < nextFrameTime)
                    System.Threading.Thread.SpinWait(64);
            }
            else if (remaining < -expectedFrameTime)
            {
                nextFrameTime = stopwatch.Elapsed;
            }
        }

        private void RenderScanline()
        {
            if ((Control & DisplayEnable) == 0) return;

            if ((Control & BGEnable) != 0)
                RenderTiles();

            if ((Control & SpriteEnable) != 0)
                RenderSprites();
        }

        private void RenderTiles()
        {
            bool usingWindow = false;

            if ((Control & WindowEnable) != 0)
            {
                // is the current scanline we're drawing within the windows Y pos?
                if (WindowY <= Scanline)
                    usingWindow = true;
            }
            
            // which tile data are we using?
            //int tileData = ((Control & TileSet) != 0) ? 0x8000 : 0x8800;
            
            // which background mem?
            int mapOffset;
            if (usingWindow)
                mapOffset = ((Control & WindowTileMap) != 0) ? 0x1c00 : 0x1800;
            else
                mapOffset = ((Control & TileMap) != 0) ? 0x1c00 : 0x1800;

            // which of 32 vertical tiles the current scanline is drawing
            // which of the 8 vertical pixels of the current tile is the scanline on? add offset
            byte yPos;
            if (usingWindow)
                yPos = (byte)(Scanline - WindowY);
            else
                yPos = (byte)(Scanline + ScrollY);

            ushort tileRow = (ushort) ((yPos >> 3) << 5);
            
            int pixelOffset = Scanline * 160;
            
            for(int pixel = 0; pixel < 160; pixel++) 
            {
                byte xPos = (byte) (pixel + ScrollX);
                
                if (usingWindow)
                    if (pixel >= WindowX)
                        xPos = (byte) (pixel - WindowX); 
                
                ushort tileCol = (byte) (xPos >> 3);

                ushort tile = memory[VideoRamBegin + mapOffset + tileRow + tileCol];
                if((Control & TileSet) == 0 && tile < 128) tile += 256;

                byte colour = memory.Tiles[tile, yPos % 8, xPos % 8];
                scanlineRow[pixel] = colour;
                framebuffer[pixelOffset++] = memory.BackgroundPalette[colour];
            }
        }            
        
        private void RenderSprites()
        {
            bool spriteDouble = ((Control & SpriteDouble) != 0);
                
            for (int i = 0; i < 40; i++)
            {
                // Point sprite to the memory location of the sprite -each size 4 bytes
                sprite.MemoryOffset = OamBegin + i * 4;

                // 8 and 16 are the top left corner so that sprites can be drawn coming out from outside the screen
                int sx = sprite.X - 8;
                int sy = sprite.Y - 16;

                if (sy <= Scanline && (sy + (spriteDouble? 16 : 8)) > Scanline)
                {
                    int pixelOffset = Scanline * 160 + sx;

                    byte tileRow;
                    if (sprite.VFlip)
                        tileRow = (byte)((spriteDouble? 15 : 7) - (Scanline - sy));
                    else
                        tileRow = (byte)(Scanline - sy);

                    for (int x = 0; x < 8; x++)
                    {
                        if (sx + x >= 0 &&
                            sx + x < 160 &&
                            (!sprite.Priority || scanlineRow[sx + x] == 0))
                        {
                            byte colour;

                            if (sprite.HFlip)
                                colour = memory.Tiles[sprite.TileNumber, tileRow, 7 - x];
                            else
                                colour = memory.Tiles[sprite.TileNumber, tileRow, x];

                            if (colour > 0)
                                framebuffer[pixelOffset] = memory.SpritePalette[sprite.Palette ? 1 : 0, colour];
                        }
                        
                        pixelOffset++;
                    }
                }
            }
        }
    }
}