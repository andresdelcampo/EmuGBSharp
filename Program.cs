using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EmuGBSharp
{
    static class Program
    {
        // Windows' default timer resolution is ~15.6 ms, so Thread.Sleep (used to pace frames in
        // Video.Step) rounds up to that granularity and the emulation speed oscillates at a low
        // frequency. Because the audio device now plays at a steady rate, that shows up as an
        // audible ~1-2 Hz tempo wobble. Raising the resolution to 1 ms makes frame pacing accurate.
        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            timeBeginPeriod(1);
            try
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            finally
            {
                timeEndPeriod(1);
            }
        }
    }
}
