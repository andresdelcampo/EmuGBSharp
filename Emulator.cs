using System.Drawing;

namespace ZarthGB
{
    class Emulator
    {
        private Cartridge cartridge;
        private Memory memory;
        private Cpu cpu;
        private Video video;

        #region Buttons

        public bool KeyUp
        {
            set { memory.KeyUp = value; cpu.KeyPressed(); }
        }
        public bool KeyDown
        {
            set { memory.KeyDown = value; cpu.KeyPressed(); }
        }
        public bool KeyLeft
        {
            set { memory.KeyLeft = value; cpu.KeyPressed(); }
        }
        public bool KeyRight
        {
            set { memory.KeyRight = value; cpu.KeyPressed(); }
        }
        public bool KeyA
        {
            set { memory.KeyA = value; cpu.KeyPressed(); }
        }
        public bool KeyB
        {
            set { memory.KeyB = value; cpu.KeyPressed(); }
        }
        public bool KeyStart
        {
            set { memory.KeyStart = value; cpu.KeyPressed(); }
        }
        public bool KeySelect
        {
            set { memory.KeySelect = value; cpu.KeyPressed(); }
        }

        #endregion
        
        public Color[] Framebuffer { get; } = new Color[160 * 144];

        public bool IsFrameReady => video.IsFrameReady;
        
        public Emulator()
        {
            cartridge = new Cartridge();
            memory = new Memory();
            cpu = new Cpu(memory);
            video = new Video(memory, Framebuffer);
        }

        public void LoadCartridge(string filename)
        {
            cartridge.Load(filename);
            memory.SetCartridge(cartridge.Rom, cartridge.CartType);
        }

        public void RunStep()
        {
            cpu.Step();
            video.Step();
        }

        public void Reset()
        {
            cpu.Reset();
            memory.Reset();
        }

        public void Stop()
        {
            memory.StopSound();
        }
    }
}
