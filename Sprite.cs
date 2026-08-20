namespace EmuGBSharp
{
    class Sprite
    {
        private Memory memory;        
        public int MemoryOffset { get; set; }
        
        public byte Y
        {
            get => memory[MemoryOffset];
            set => memory[MemoryOffset] = value;
        }
        public byte X
        {
            get => memory[MemoryOffset+1];
            set => memory[MemoryOffset+1] = value;
        }
        public byte TileNumber
        {
            get => memory[MemoryOffset+2];
            set => memory[MemoryOffset+2] = value;
        }
        public bool Priority
        {
            get => (memory[MemoryOffset+3] & (1<<7)) > 0;
            set => memory[MemoryOffset+3] = (byte)((memory[MemoryOffset+3] & ~(1<<7)) | (value ? 1<<7 : 0));
        }
        public bool VFlip
        {
            get => (memory[MemoryOffset+3] & (1<<6)) > 0;
            set => memory[MemoryOffset+3] = (byte)((memory[MemoryOffset+3] & ~(1<<6)) | (value ? 1<<6 : 0));
        }
        public bool HFlip
        {
            get => (memory[MemoryOffset+3] & (1<<5)) > 0;
            set => memory[MemoryOffset+3] = (byte)((memory[MemoryOffset+3] & ~(1<<5)) | (value ? 1<<5 : 0));
        }
        public bool Palette
        {
            get => (memory[MemoryOffset+3] & (1<<4)) > 0;
            set => memory[MemoryOffset+3] = (byte)((memory[MemoryOffset+3] & ~(1<<4)) | (value ? 1<<4 : 0));
        }
        
        public Sprite(Memory memory)
        {
            this.memory = memory;
        }
    }
}