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

        const double FrameRateHz = 59.727500569606;
        // Full DMG frame period (~16.75 ms), used only by the wall-clock fallback pacer.
        TimeSpan expectedFrameTime = TimeSpan.FromTicks((long) (TimeSpan.TicksPerSecond / FrameRateHz));

        // Audio-master pacing: emulated audio position vs what the device has actually consumed.
        const double AudioLeadSeconds = 0.15;   // how far emulation may run ahead of consumption
        const int AudioStallMs = 600;           // no consumption progress this long => treat device as stalled
        double emulatedSamples;
        long pacingLastConsumed;
        bool audioPacing;
        bool audioStalled;
        Stopwatch pacingStall = new Stopwatch();

        // Wall-clock fallback (used only when the audio device is not running).
        Stopwatch stopwatch = new Stopwatch();
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

        // Throttle emulation to real time. The authoritative clock is the audio device: we advance
        // an emulated-audio-sample counter each frame and wait until emulation is no more than a
        // small lead ahead of what the device has actually consumed. That slaves emulation speed to
        // the sound card's crystal, so note production and playback never drift apart (which is what
        // caused the periodic tempo wobble). If the device is not running -- or stalls -- we fall
        // back to a precise self-correcting wall-clock pacer so the emulator never freezes.
        private void PaceFrame()
        {
            double sampleRate = memory.AudioSampleRate;

            if (memory.AudioDeviceRunning)
            {
                long consumed = memory.AudioSamplesConsumed;

                // Recover from a previous stall only once the device actually starts consuming again.
                if (audioStalled && consumed != pacingLastConsumed)
                {
                    audioStalled = false;
                    audioPacing = false;   // force a clean rebase below
                }

                if (!audioStalled)
                {
                    stopwatch.Reset();   // discard the wall-clock baseline; audio is in control now

                    // Allow emulation to run a little ahead of playback (keeps video smooth and the
                    // device buffer fed) but no further, so the long-term rate matches consumption.
                    double lead = sampleRate * AudioLeadSeconds;

                    if (!audioPacing)
                    {
                        // Took over from the fallback: rebase onto the current playback position so we
                        // neither stall catching up nor race ahead after the handoff.
                        audioPacing = true;
                        emulatedSamples = consumed + lead;
                        pacingLastConsumed = consumed;
                        pacingStall.Restart();
                    }

                    emulatedSamples += sampleRate / FrameRateHz;

                    while (emulatedSamples - memory.AudioSamplesConsumed > lead)
                    {
                        long c = memory.AudioSamplesConsumed;
                        if (c != pacingLastConsumed) { pacingLastConsumed = c; pacingStall.Restart(); }
                        else if (pacingStall.ElapsedMilliseconds > AudioStallMs) { audioStalled = true; break; }
                        System.Threading.Thread.Sleep(1);
                    }

                    if (!audioStalled)
                        return;   // paced by the audio clock this frame

                    // Device reports Playing but isn't consuming. Fall through to the wall-clock pacer
                    // so we keep real-time speed (never free-run); pacingLastConsumed stays frozen so
                    // the recovery check above re-engages audio pacing as soon as the device advances.
                }
            }
            else
            {
                audioPacing = false;
                audioStalled = false;
            }

            // ---- Wall-clock fallback (device not running, or stalled) ----
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