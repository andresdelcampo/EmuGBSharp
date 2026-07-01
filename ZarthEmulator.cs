using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace ZarthGB
{
    public partial class ZarthEmulator : Form
    {
        Emulator emulator = new Emulator();
        CancellationTokenSource cts;

        Brush WhiteBrush = new SolidBrush(Color.White);
        Brush LightGrayBrush = new SolidBrush(Color.LightGray);
        Brush DarkGrayBrush = new SolidBrush(Color.DarkGray);
        Brush BlackBrush = new SolidBrush(Color.Black);

        public ZarthEmulator()
        {
            InitializeComponent();
        }

        private void ZarthEmulator_Load(object sender, EventArgs e)
        {
            string[] args = Environment.GetCommandLineArgs();
            
            if (args.Length > 1)
                emulator.LoadCartridge(args[1]);
            else
                //emulator.LoadCartridge("bgbtest.gb");
                emulator.LoadCartridge("tetris.gb");
                //emulator.LoadCartridge("sml.gb");
            
            // CPU tests
            //emulator.LoadCartridge("01-special.gb");              // OK!
            //emulator.LoadCartridge("02-interrupts.gb");           // OK!
            //emulator.LoadCartridge("03-op sp,hl.gb");             // OK!
            //emulator.LoadCartridge("04-op r,imm.gb");             // OK!
            //emulator.LoadCartridge("05-op rp.gb");                // OK!
            //emulator.LoadCartridge("06-ld r,r.gb");               // OK!
            //emulator.LoadCartridge("07-jr,jp,call,ret,rst.gb");   // OK!
            //emulator.LoadCartridge("08-misc instrs.gb");          // OK!
            //emulator.LoadCartridge("09-op r,r.gb");               // OK!    
            //emulator.LoadCartridge("10-bit ops.gb");              // OK!
            //emulator.LoadCartridge("11-op a,(hl).gb");            // OK!
            //emulator.LoadCartridge("cpu_instrs.gb");              // OK! -waits for keys sometimes?
            
                
            cts = new CancellationTokenSource();
            ThreadPool.QueueUserWorkItem(new WaitCallback(RunEmulator), cts.Token);

            var timer = new System.Windows.Forms.Timer();
            timer.Interval = ((int) (1000 / 59.727500569606)); // msecs
            timer.Tick += new EventHandler(timer_Tick);
            timer.Start();
        }

        void RunEmulator(object obj)
        {
            while (true)
            {
                emulator.RunStep();
            }
        }

        private void timer_Tick(object sender, EventArgs e)
        {
            if (emulator.IsFrameReady)
                Render();
        }

        private void ZarthEmulator_Paint(object sender, PaintEventArgs e)
        {
            Render();
        }

        private void Render()
        {
            using (var bmp = new Bitmap(pictureBox.Width, pictureBox.Height))
            using (var gfx = Graphics.FromImage(bmp))
            {
                gfx.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                gfx.Clear(Color.DarkSlateGray);

                for (int i = 0; i < 144; i++)
                {
                    for (int j = 0; j < 160; j++)
                    {
                        Color pixelColor = emulator.Framebuffer[i * 160 + j];
                        
                        Brush b;
                        if (pixelColor == Color.White)
                            b = WhiteBrush;
                        else if (pixelColor == Color.LightGray)
                            b = LightGrayBrush;
                        else if (pixelColor == Color.DarkGray)
                            b = DarkGrayBrush;
                        else if (pixelColor == Color.Black)
                            b = BlackBrush;
                        else
                            b = null;
                        
                        /*
                        // Pattern to test colors
                        Brush b;
                        if ((i + j) % 4 == 0)
                            b = WhiteBrush;
                        else if ((i + j) % 4 == 1)
                            b = LightGrayBrush;
                        else if ((i + j) % 4 == 2)
                            b = DarkGrayBrush;
                        else
                            b = BlackBrush;
                        */

                        if (b != null)
                            gfx.FillRectangle(b, j * 4, i * 4, 4, 4);
                    }
                }

                // copy the bitmap to the picturebox
                pictureBox.Image?.Dispose();
                pictureBox.Image = (Bitmap)bmp.Clone();
            }
        }

        private void ZarthEmulator_FormClosed(object sender, FormClosedEventArgs e)
        {
            cts.Cancel();
        }
        
        private void ZarthEmulator_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Left:
                    emulator.KeyLeft = true;
                    break;
                case Keys.Right:
                    emulator.KeyRight = true;
                    break;
                case Keys.Up:
                    emulator.KeyUp = true;
                    break;
                case Keys.Down:
                    emulator.KeyDown = true;
                    break;
                case Keys.A:        // A
                    emulator.KeyA = true;
                    break;
                case Keys.S:        // B
                    emulator.KeyB = true;
                    break;
                case Keys.Back:     // Start
                    emulator.KeyStart = true;
                    break;
                case Keys.Return:   // Select
                    emulator.KeySelect = true;
                    break;                
            }
        }

        private void ZarthEmulator_KeyUp(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Left:
                    emulator.KeyLeft = false;
                    break;
                case Keys.Right:
                    emulator.KeyRight = false;
                    break;
                case Keys.Up:
                    emulator.KeyUp = false;
                    break;
                case Keys.Down:
                    emulator.KeyDown = false;
                    break;
                case Keys.A:        // A
                    emulator.KeyA = false;
                    break;
                case Keys.S:        // B
                    emulator.KeyB = false;
                    break;
                case Keys.Back:     // Start
                    emulator.KeyStart = false;
                    break;
                case Keys.Return:   // Select
                    emulator.KeySelect = false;
                    break;                
            }
        }
    }
}
