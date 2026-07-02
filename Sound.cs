using System;
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

// References:
// - http://marc.rawer.de/Gameboy/Docs/GBCPUman.pdf probably the best GameBoy CPU/memory manual
// - https://nightshade256.github.io/2021/03/27/gb-sound-emulation.html
// - https://gbdev.io/pandocs/Audio.html
//
// Design: the four sound channels form a single Audio Processing Unit (APU) that implements
// ISampleProvider. The output device *pulls* samples from it at the true playback rate, so the
// device's own clock is the single authoritative timebase. This removes the whole class of bugs
// the old push-based code had (per-channel buffer over/underruns, inter-channel latency drift,
// coarse per-chunk register sampling). Each channel is a persistent object that keeps its own
// phase; the CPU thread only ever writes the 0xFF10-0xFF26 register block and flags triggers,
// while the audio thread reads that state one sample at a time.

namespace ZarthGB
{
    class Sound
    {
        private const int SampleRate = 48000;
        private const int NumChannels = 2;
        private const int DesiredLatency = 150;     // ms of device buffering -> overall audio latency
        private const int CpuFrequency = 4194304;   // 4.19 MHz
        private const double FrameSequencerHz = 512.0;

        private readonly Memory memory;
        private IWavePlayer waveOut;

        private readonly SquareChannel channel1;
        private readonly SquareChannel channel2;
        private readonly WaveChannel channel3;
        private readonly NoiseChannel channel4;

        // Set on the CPU thread when a trigger bit is written, consumed on the audio thread.
        private volatile bool trigger1;
        private volatile bool trigger2;
        private volatile bool trigger3;
        private volatile bool trigger4;

        // Frame sequencer (512 Hz) fractional accumulator and step counter.
        private double frameSequencerAccumulator;
        private int frameSequencerStep;

        public Sound(Memory memory)
        {
            this.memory = memory;

            channel1 = new SquareChannel(memory, 0xff10, 0xff11, 0xff12, 0xff13, 0xff14, hasSweep: true);
            channel2 = new SquareChannel(memory, 0x0000, 0xff16, 0xff17, 0xff18, 0xff19, hasSweep: false);
            channel3 = new WaveChannel(memory);
            channel4 = new NoiseChannel(memory);

            SetSoundOutput();
        }

        // Called from Memory when a trigger register (NRx4) is written. We only latch the intent
        // here; the actual channel restart happens on the audio thread so envelope/sweep/length
        // stay coherent with sample generation.
        public void StartSound1() { if ((memory[0xff14] & 0x80) != 0) trigger1 = true; }
        public void StartSound2() { if ((memory[0xff19] & 0x80) != 0) trigger2 = true; }
        public void StartSound3() { if ((memory[0xff1e] & 0x80) != 0) trigger3 = true; }
        public void StartSound4() { if ((memory[0xff23] & 0x80) != 0) trigger4 = true; }

        public void SetSoundOutput()
        {
            waveOut?.Stop();
            waveOut?.Dispose();

            waveOut = CreateSoundPlayer(new ApuProvider(this));
            waveOut.Play();
        }

        // Kept alive by a periodic call from the emulator thread: the initial Play() in the
        // constructor can run before the device is ready, and this revives it if the backend ever
        // drops out. It is a cheap no-op once the device is already playing.
        public void EnsurePlaying()
        {
            var device = waveOut;
            if (device != null && device.PlaybackState != PlaybackState.Playing)
                device.Play();
        }

        // Release the output device on shutdown. Without this the backend's playback thread can keep
        // the process (and the sound device) alive, so repeated runs progressively lock out audio.
        public void Stop()
        {
            var device = waveOut;
            waveOut = null;
            device?.Stop();
            device?.Dispose();
        }

        #region Mixing / frame sequencer (audio thread)

        private void NextFrame(out float left, out float right)
        {
            ApplyPendingTriggers();
            StepFrameSequencer();

            bool powerOn = (memory[0xff26] & 0x80) != 0;
            if (!powerOn)
            {
                channel1.Disable();
                channel2.Disable();
                channel3.Disable();
                channel4.Disable();
                left = right = 0f;
                return;
            }

            float s1 = channel1.NextSample();
            float s2 = channel2.NextSample();
            float s3 = channel3.NextSample();
            float s4 = channel4.NextSample();

            // NR51: low nibble routes to the left mix, high nibble to the right mix.
            int routing = memory[0xff25];
            float leftMix =
                ((routing & 0x01) != 0 ? s1 : 0f) +
                ((routing & 0x02) != 0 ? s2 : 0f) +
                ((routing & 0x04) != 0 ? s3 : 0f) +
                ((routing & 0x08) != 0 ? s4 : 0f);
            float rightMix =
                ((routing & 0x10) != 0 ? s1 : 0f) +
                ((routing & 0x20) != 0 ? s2 : 0f) +
                ((routing & 0x40) != 0 ? s3 : 0f) +
                ((routing & 0x80) != 0 ? s4 : 0f);

            // NR50: master volume per side (0-7), no relation to Vin here.
            int control = memory[0xff24];
            float leftGain = ((control & 0x07) + 1) / 8.0f;
            float rightGain = (((control >> 4) & 0x07) + 1) / 8.0f;

            // Divide by the channel count so four simultaneous full-scale channels cannot clip.
            left = Clamp(leftMix * leftGain * 0.25f);
            right = Clamp(rightMix * rightGain * 0.25f);
        }

        private void ApplyPendingTriggers()
        {
            if (trigger1) { trigger1 = false; channel1.Trigger(); }
            if (trigger2) { trigger2 = false; channel2.Trigger(); }
            if (trigger3) { trigger3 = false; channel3.Trigger(); }
            if (trigger4) { trigger4 = false; channel4.Trigger(); }
        }

        private void StepFrameSequencer()
        {
            frameSequencerAccumulator += FrameSequencerHz / SampleRate;
            if (frameSequencerAccumulator < 1.0)
                return;

            frameSequencerAccumulator -= 1.0;
            frameSequencerStep = (frameSequencerStep + 1) & 7;

            // 512 Hz sequencer: length @256 Hz (even steps), sweep @128 Hz (2,6), envelope @64 Hz (7).
            if ((frameSequencerStep & 1) == 0)
            {
                channel1.ClockLength();
                channel2.ClockLength();
                channel3.ClockLength();
                channel4.ClockLength();
            }
            if (frameSequencerStep == 2 || frameSequencerStep == 6)
                channel1.ClockSweep();
            if (frameSequencerStep == 7)
            {
                channel1.ClockEnvelope();
                channel2.ClockEnvelope();
                channel4.ClockEnvelope();
            }
        }

        // Reflect channel on/off state back into NR52 so games that poll it for sequencing work.
        private void UpdateStatusRegister()
        {
            int status = memory[0xff26] & 0x80;
            status |= 0x70;                     // unused bits read as 1
            if (channel1.Enabled) status |= 0x01;
            if (channel2.Enabled) status |= 0x02;
            if (channel3.Enabled) status |= 0x04;
            if (channel4.Enabled) status |= 0x08;
            memory[0xff26] = (byte) status;
        }

        private static float Clamp(float value)
        {
            if (value > 1.0f) return 1.0f;
            if (value < -1.0f) return -1.0f;
            return value;
        }

        #endregion

        #region Output device

        private sealed class ApuProvider : ISampleProvider
        {
            private readonly Sound sound;

            public ApuProvider(Sound sound)
            {
                this.sound = sound;
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, NumChannels);
            }

            public WaveFormat WaveFormat { get; }

            public int Read(float[] buffer, int offset, int count)
            {
                for (int i = 0; i < count; i += 2)
                {
                    sound.NextFrame(out float left, out float right);
                    buffer[offset + i] = left;
                    buffer[offset + i + 1] = right;
                }
                sound.UpdateStatusRegister();
                return count;
            }
        }

        private IWavePlayer CreateSoundPlayer(ISampleProvider provider)
        {
            // Convert to 16-bit PCM: this is the format Windows sound backends accept most reliably
            // (raw IEEE-float can be silently rejected by some drivers). DirectSound first because it
            // was the path that worked previously on this setup.
            var floatProvider = provider.ToWaveProvider();
            var pcm16 = provider.ToWaveProvider16();

            // Plain WaveOut (window-callback winmm) first: it was the audible backend previously on
            // this setup. WaveOutEvent (event-callback) failed waveOutOpen here, and DirectSound
            // pulls samples but is silent, so both are later fallbacks.
            if (TryCreatePlayer(() => new WaveOut { DesiredLatency = DesiredLatency }, floatProvider, "WaveOut float", out var player))
                return player;
            if (TryCreatePlayer(() => new WaveOut { DesiredLatency = DesiredLatency }, pcm16, "WaveOut PCM16", out player))
                return player;
            if (TryCreatePlayer(() => new WasapiOut(AudioClientShareMode.Shared, 100), floatProvider, "WASAPI shared float", out player))
                return player;
            if (TryCreatePlayer(() => new WaveOutEvent { DesiredLatency = DesiredLatency }, pcm16, "WaveOutEvent PCM16", out player))
                return player;
            if (TryCreatePlayer(() => new DirectSoundOut(DesiredLatency), pcm16, "DirectSound PCM16", out player))
                return player;

            throw new InvalidOperationException("No compatible sound output could be initialized.");
        }

        private static bool TryCreatePlayer(Func<IWavePlayer> factory, IWaveProvider provider, string name, out IWavePlayer player)
        {
            player = null;
            try
            {
                player = factory();
                player.Init(provider);
                return true;
            }
            catch (Exception exception)
            {
                Debug.Print($"{name} rejected sound format ({exception.Message}).");
                player?.Dispose();
                player = null;
                return false;
            }
        }

        #endregion

        #region Channels

        // Pulse channels 1 (with frequency sweep) and 2. Frequency is expressed as the GB 11-bit
        // period value; the audible tone is 131072 / (2048 - freq) Hz, stepped through an 8-entry
        // duty pattern.
        private sealed class SquareChannel
        {
            private static readonly byte[][] DutyTable =
            {
                new byte[] {0, 0, 0, 0, 0, 0, 0, 1},   // 12.5%
                new byte[] {1, 0, 0, 0, 0, 0, 0, 1},   // 25%
                new byte[] {1, 0, 0, 0, 0, 1, 1, 1},   // 50%
                new byte[] {0, 1, 1, 1, 1, 1, 1, 0},   // 75%
            };

            private readonly Memory memory;
            private readonly int nr0, nr1, nr2, nr3, nr4;
            private readonly bool hasSweep;

            private double phase;
            private int dutyPosition;
            private int volume;
            private int envelopeTimer;
            private int lengthCounter;

            private int shadowFrequency;
            private int sweepTimer;
            private bool sweepEnabled;
            private bool useShadowFrequency;

            public bool Enabled { get; private set; }

            public SquareChannel(Memory memory, int nr0, int nr1, int nr2, int nr3, int nr4, bool hasSweep)
            {
                this.memory = memory;
                this.nr0 = nr0;
                this.nr1 = nr1;
                this.nr2 = nr2;
                this.nr3 = nr3;
                this.nr4 = nr4;
                this.hasSweep = hasSweep;
            }

            private bool DacOn => (memory[nr2] & 0xF8) != 0;
            private int RegisterFrequency => ((memory[nr4] & 0x07) << 8) | memory[nr3];

            public void Disable() => Enabled = false;

            public void Trigger()
            {
                Enabled = DacOn;
                lengthCounter = 64 - (memory[nr1] & 0x3F);
                volume = memory[nr2] >> 4;
                int period = memory[nr2] & 0x07;
                envelopeTimer = period == 0 ? 8 : period;

                if (hasSweep)
                {
                    shadowFrequency = RegisterFrequency;
                    int sweepPeriod = (memory[nr0] >> 4) & 0x07;
                    int sweepShift = memory[nr0] & 0x07;
                    sweepTimer = sweepPeriod == 0 ? 8 : sweepPeriod;
                    sweepEnabled = sweepPeriod > 0 || sweepShift > 0;
                    useShadowFrequency = sweepEnabled;
                    if (sweepShift > 0)
                        CalculateSweepFrequency();     // immediate overflow check on trigger
                }
            }

            public void ClockLength()
            {
                if ((memory[nr4] & 0x40) != 0 && lengthCounter > 0)
                {
                    lengthCounter--;
                    if (lengthCounter == 0)
                        Enabled = false;
                }
            }

            public void ClockEnvelope()
            {
                int period = memory[nr2] & 0x07;
                if (period == 0)
                    return;

                if (envelopeTimer > 0)
                    envelopeTimer--;
                if (envelopeTimer != 0)
                    return;

                envelopeTimer = period;
                bool amplify = (memory[nr2] & 0x08) != 0;
                if (amplify && volume < 15) volume++;
                else if (!amplify && volume > 0) volume--;
            }

            public void ClockSweep()
            {
                if (!hasSweep)
                    return;

                int period = (memory[nr0] >> 4) & 0x07;
                if (sweepTimer > 0)
                    sweepTimer--;
                if (sweepTimer != 0)
                    return;

                sweepTimer = period == 0 ? 8 : period;
                if (!sweepEnabled || period == 0)
                    return;

                int newFrequency = CalculateSweepFrequency();
                int shift = memory[nr0] & 0x07;
                if (newFrequency <= 2047 && shift > 0)
                {
                    shadowFrequency = newFrequency;
                    CalculateSweepFrequency();         // second overflow check
                }
            }

            private int CalculateSweepFrequency()
            {
                int shift = memory[nr0] & 0x07;
                bool negate = (memory[nr0] & 0x08) != 0;
                int delta = shadowFrequency >> shift;
                int newFrequency = negate ? shadowFrequency - delta : shadowFrequency + delta;
                if (newFrequency > 2047)
                    Enabled = false;
                return newFrequency;
            }

            public float NextSample()
            {
                if (!Enabled || !DacOn)
                    return 0f;

                int frequency = useShadowFrequency ? shadowFrequency : RegisterFrequency;
                if (frequency >= 2048)
                    return 0f;

                double toneHz = 131072.0 / (2048 - frequency);
                phase += toneHz * 8.0 / SampleRate;
                while (phase >= 1.0)
                {
                    phase -= 1.0;
                    dutyPosition = (dutyPosition + 1) & 7;
                }

                int duty = memory[nr1] >> 6;
                float amplitude = volume / 15.0f;
                return DutyTable[duty][dutyPosition] != 0 ? amplitude : -amplitude;
            }
        }

        // Wave channel 3: plays the 32 4-bit samples in wave RAM (0xFF30-0xFF3F) at
        // 65536 / (2048 - freq) Hz, attenuated by the NR32 output level.
        private sealed class WaveChannel
        {
            private readonly Memory memory;
            private double phase;
            private int position;
            private int lengthCounter;

            public bool Enabled { get; private set; }

            public WaveChannel(Memory memory) => this.memory = memory;

            private bool DacOn => (memory[0xff1a] & 0x80) != 0;
            private int RegisterFrequency => ((memory[0xff1e] & 0x07) << 8) | memory[0xff1d];

            public void Disable() => Enabled = false;

            public void Trigger()
            {
                Enabled = DacOn;
                lengthCounter = 256 - memory[0xff1b];
                position = 0;
                phase = 0;
            }

            public void ClockLength()
            {
                if ((memory[0xff1e] & 0x40) != 0 && lengthCounter > 0)
                {
                    lengthCounter--;
                    if (lengthCounter == 0)
                        Enabled = false;
                }
            }

            public void ClockEnvelope() { }   // no envelope on the wave channel
            public void ClockSweep() { }

            public float NextSample()
            {
                if (!Enabled || !DacOn)
                    return 0f;

                int outputLevel = (memory[0xff1c] >> 5) & 0x03;
                if (outputLevel == 0)
                    return 0f;                 // muted

                int frequency = RegisterFrequency;
                if (frequency >= 2048)
                    return 0f;

                double sampleHz = 65536.0 / (2048 - frequency);
                phase += sampleHz * 32.0 / SampleRate;
                while (phase >= 1.0)
                {
                    phase -= 1.0;
                    position = (position + 1) & 31;
                }

                int packed = memory[0xff30 + (position >> 1)];
                int nibble = (position & 1) == 0 ? (packed >> 4) : (packed & 0x0F);
                int shift = outputLevel - 1;   // 1->0 (full), 2->1 (half), 3->2 (quarter)
                int digital = nibble >> shift;
                return (float) (digital / 7.5 - 1.0);
            }
        }

        // Noise channel 4: a 15/7-bit LFSR clocked at 4194304 / (divisor << shift) Hz.
        private sealed class NoiseChannel
        {
            private static readonly int[] Divisors = {8, 16, 32, 48, 64, 80, 96, 112};

            private readonly Memory memory;
            private double phase;
            private ushort lfsr = 0x7FFF;
            private int volume;
            private int envelopeTimer;
            private int lengthCounter;

            public bool Enabled { get; private set; }

            public NoiseChannel(Memory memory) => this.memory = memory;

            private bool DacOn => (memory[0xff21] & 0xF8) != 0;

            public void Disable() => Enabled = false;

            public void Trigger()
            {
                Enabled = DacOn;
                lengthCounter = 64 - (memory[0xff20] & 0x3F);
                volume = memory[0xff21] >> 4;
                int period = memory[0xff21] & 0x07;
                envelopeTimer = period == 0 ? 8 : period;
                lfsr = 0x7FFF;
            }

            public void ClockLength()
            {
                if ((memory[0xff23] & 0x40) != 0 && lengthCounter > 0)
                {
                    lengthCounter--;
                    if (lengthCounter == 0)
                        Enabled = false;
                }
            }

            public void ClockEnvelope()
            {
                int period = memory[0xff21] & 0x07;
                if (period == 0)
                    return;

                if (envelopeTimer > 0)
                    envelopeTimer--;
                if (envelopeTimer != 0)
                    return;

                envelopeTimer = period;
                bool amplify = (memory[0xff21] & 0x08) != 0;
                if (amplify && volume < 15) volume++;
                else if (!amplify && volume > 0) volume--;
            }

            public void ClockSweep() { }

            public float NextSample()
            {
                if (!Enabled || !DacOn)
                    return 0f;

                int register = memory[0xff22];
                int shift = register >> 4;
                if (shift >= 14)                   // shifts 14/15 are invalid: the LFSR never clocks
                    return 0f;

                int divisorCode = register & 0x07;
                bool widthMode = (register & 0x08) != 0;
                double clockHz = (double) CpuFrequency / (Divisors[divisorCode] << shift);

                phase += clockHz / SampleRate;
                while (phase >= 1.0)
                {
                    phase -= 1.0;
                    StepLfsr(widthMode);
                }

                float amplitude = volume / 15.0f;
                return (lfsr & 0x01) == 0 ? amplitude : -amplitude;
            }

            private void StepLfsr(bool widthMode)
            {
                int xor = (lfsr ^ (lfsr >> 1)) & 1;
                lfsr = (ushort) ((lfsr >> 1) | (xor << 14));
                if (widthMode)
                    lfsr = (ushort) ((lfsr & ~0x40) | (xor << 6));
            }
        }

        #endregion
    }
}
