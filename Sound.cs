using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

// References:
// - http://marc.rawer.de/Gameboy/Docs/GBCPUman.pdf probably the best GameBoy CPU/memory manual
// - https://nightshade256.github.io/2021/03/27/gb-sound-emulation.html
// - https://github.com/naudio/NAudio/blob/master/Docs/PlaySineWave.md

namespace ZarthGB
{
    class Sound
    {
        public const int PlayStep = 16;  // In ms
        public const int DesiredLatency = 160;      // PS 8 -> DL 156

        private Memory memory;
        private const int SampleRate = 192000;    // Minimum 192kHz to get enough frequency resolution -else sounds distorted
        private const int NumChannels = 1;
        private Stopwatch stopwatch = new Stopwatch();
        private TimeSpan lastTime;
        private IWaveProvider mixer;
        private IWavePlayer waveOut;
        private int buffering;
        private const int BufferingRounds = 4;
        
        private byte ChannelControl => memory[0xff24];
        private int RightVolume => (ChannelControl >> 4) & 7;
        private int LeftVolume => ChannelControl & 7;
        private bool RightVinFromCartridge => (ChannelControl & 0x80) != 0;
        private bool LeftVinFromCartridge => (ChannelControl & 0x08) != 0;
        
        private byte Output => memory[0xff25];
        private bool Sound4ToRight => (Output & 0x80) != 0;
        private bool Sound3ToRight => (Output & 0x40) != 0;
        private bool Sound2ToRight => (Output & 0x20) != 0;
        private bool Sound1ToRight => (Output & 0x10) != 0;
        private bool Sound4ToLeft => (Output & 0x08) != 0;
        private bool Sound3ToLeft => (Output & 0x04) != 0;
        private bool Sound2ToLeft => (Output & 0x02) != 0;
        private bool Sound1ToLeft => (Output & 0x01) != 0;
        
        
        private byte OnOff 
        {
            get => memory[0xff26];
            set => memory[0xff26] = value;
        }
        private bool Sound1On => (OnOff & 1) != 0;
        private bool Sound2On => (OnOff & (1 << 1)) != 0;
        private bool Sound3On => (OnOff & (1 << 2)) != 0;
        private bool Sound4On => (OnOff & (1 << 3)) != 0;
        
        #region Sound1
        private GBSignalGenerator signal1;
        private BufferedWaveProvider waveBuffer1;
        private Dictionary<string, byte[]> bufferCache1 = new Dictionary<string, byte[]>();
        private byte Sweep1 => memory[0xff10];
        private int SweepShift => Sweep1 & 0x7; 
        private bool SweepAmplify => (Sweep1 & 0x8) == 0;
        private int SweepPeriod => (Sweep1 >> 4) & 0x7;
        private byte WaveLength1 => memory[0xff11];
        private int WaveDuty1 => WaveLength1 >> 6;
        private int Length1 => (64 - (WaveLength1 & 0x3F)) * 4;
        private int lengthPlayed1 = 0;
        private byte Envelope1 => memory[0xff12];
        private double Volume1 => (Envelope1 >> 4) / 15.0;
        private bool EnvelopeAmplify1 => (Envelope1 & 0x8) != 0;
        private int EnvelopePeriod1 => (Envelope1 & 0x7);
        private bool TriggerSound1 => (memory[0xff14] >> 7) != 0;
        private int Frequency1 => ((memory[0xff14] & 7) << 8) | memory[0xff13];
        private bool Loop1 => (memory[0xff14] & 0x40) == 0;
        
        public void StartSound1()
        {
            if (TriggerSound1)
            {
                if (SweepPeriod == 0)
                {
                    // No sweep
                    signal1 = new GBSignalGenerator(SampleRate, NumChannels)
                    {
                        Channel = GBSignalGenerator.ChannelType.Square,
                        Gain = Volume1,
                        Frequency = Frequency1,
                        WaveDuty = WaveDuty1,
                        EnvelopeAmplify = EnvelopeAmplify1,
                        EnvelopePeriod = EnvelopePeriod1,
                    };
                }
                else
                {
                    // Sweep
                    signal1 = new GBSignalGenerator(SampleRate, NumChannels)
                    {
                        Channel = GBSignalGenerator.ChannelType.Sweep,
                        Gain = Volume1,
                        Frequency = Frequency1,
                        WaveDuty = WaveDuty1,
                        EnvelopeAmplify = EnvelopeAmplify1,
                        EnvelopePeriod = EnvelopePeriod1,
                        SweepPeriod = SweepPeriod,
                        SweepAmplify = SweepAmplify,
                        SweepShift = SweepShift,
                    };
                    Debug.Print($"QUEUE SWEEP Amplify {SweepAmplify}, Period {SweepPeriod}, Shift {SweepShift}");
                }
                
                SetSound1On();
            }
        }

        private void SetSound1On()
        {
            lengthPlayed1 = 0;
            OnOff = (byte) (OnOff | 0x01);
        }

        private void SetSound1Off()
        {
            OnOff = (byte) (OnOff & 0xFE);      // 11111110
            lengthPlayed1 = 0;
            signal1 = null;
        }

        #endregion

        #region Sound2
        private GBSignalGenerator signal2;
        private BufferedWaveProvider waveBuffer2;
        private Dictionary<string, byte[]> bufferCache2 = new Dictionary<string, byte[]>();
        private byte WaveLength2 => memory[0xff16];
        private int WaveDuty2 => WaveLength2 >> 6;
        private int Length2 => (64 - (WaveLength2 & 0x3F)) * 4;
        private int lengthPlayed2 = 0;
        private byte Envelope2 => memory[0xff17];
        private double Volume2 => (Envelope2 >> 4) / 15.0;
        private bool EnvelopeAmplify2 => (Envelope2 & 0x8) != 0;
        private int EnvelopePeriod2 => (Envelope2 & 0x7);
        private bool TriggerSound2 => (memory[0xff19] >> 7) != 0;
        private int Frequency2 => ((memory[0xff19] & 7) << 8) | memory[0xff18];
        private bool Loop2 => (memory[0xff19] & 0x40) == 0;

        public void StartSound2()
        {
            if (TriggerSound2)
            {
                signal2 = new GBSignalGenerator(SampleRate, NumChannels)
                {
                    Channel = GBSignalGenerator.ChannelType.Square,
                    Gain = Volume2,
                    Frequency = Frequency2,
                    WaveDuty = WaveDuty2,
                    EnvelopeAmplify = EnvelopeAmplify2,
                    EnvelopePeriod = EnvelopePeriod2,
                };

                SetSound2On();
            }
        }

        private void SetSound2On()
        {
            lengthPlayed2 = 0;
            OnOff = (byte) (OnOff | 0x02);
        }

        private void SetSound2Off()
        {
            OnOff = (byte) (OnOff & 0xFD);      // 11111101
            lengthPlayed2 = 0;
            signal2 = null;
        }

        #endregion

        #region Sound3
        private GBSignalGenerator signal3;
        private BufferedWaveProvider waveBuffer3;
        private Dictionary<string, byte[]> bufferCache3 = new Dictionary<string, byte[]>();
        private bool SoundOn3 => (memory[0xff1a] & 0x7) != 0;
        private int Length3 => 256 - memory[0xff1b];
        private int lengthPlayed3 = 0;
        private int OutputLevel3 => (memory[0xff1c] & 0x60) >> 5;
        private bool TriggerSound3 => (memory[0xff1e] >> 7) != 0;
        private int Frequency3 => (memory[0xff1e] & 7) << 8 | memory[0xff1d];        
        private bool Loop3 => (memory[0xff1e] & 0x40) == 0;
        private int WaveRamStart = 0xff30;
        private int[] Samples => new[]
        {
            memory[WaveRamStart] >> 4, memory[WaveRamStart] & 0xF,
            memory[WaveRamStart + 1] >> 4, memory[WaveRamStart + 1] & 0xF,
            memory[WaveRamStart + 2] >> 4, memory[WaveRamStart + 2] & 0xF,
            memory[WaveRamStart + 3] >> 4, memory[WaveRamStart + 3] & 0xF,
            memory[WaveRamStart + 4] >> 4, memory[WaveRamStart + 4] & 0xF,
            memory[WaveRamStart + 5] >> 4, memory[WaveRamStart + 5] & 0xF,
            memory[WaveRamStart + 6] >> 4, memory[WaveRamStart + 6] & 0xF,
            memory[WaveRamStart + 7] >> 4, memory[WaveRamStart + 7] & 0xF,
            memory[WaveRamStart + 8] >> 4, memory[WaveRamStart + 8] & 0xF,
            memory[WaveRamStart + 9] >> 4, memory[WaveRamStart + 9] & 0xF,
            memory[WaveRamStart + 10] >> 4, memory[WaveRamStart + 10] & 0xF,
            memory[WaveRamStart + 11] >> 4, memory[WaveRamStart + 11] & 0xF,
            memory[WaveRamStart + 12] >> 4, memory[WaveRamStart + 12] & 0xF,
            memory[WaveRamStart + 13] >> 4, memory[WaveRamStart + 13] & 0xF,
            memory[WaveRamStart + 14] >> 4, memory[WaveRamStart + 14] & 0xF,
            memory[WaveRamStart + 15] >> 4, memory[WaveRamStart + 15] & 0xF,
        };
        
        public void StartSound3()
        {
            if (TriggerSound3)
            {
                signal3 = new GBSignalGenerator(SampleRate, NumChannels)
                {
                    Channel = GBSignalGenerator.ChannelType.Samples,
                    Frequency = Frequency3,
                    Samples = Samples,
                    OutputShift = Pattern2Shift(OutputLevel3)
                };

                SetSound3On();
            }
        }

        private int Pattern2Shift(int outputLevel)
        {
            switch (outputLevel)
            {
                case 0: return 4;
                case 1: return 0;
                case 2: return 1;
                case 3: return 2;
                default: throw new Exception("Invalid output level");
            }
        }
        
        private void SetSound3On()
        {
            lengthPlayed3 = 0;
            OnOff = (byte) (OnOff | 0x04);
        }

        private void SetSound3Off()
        {
            OnOff = (byte) (OnOff & 0xFB);      // 11111011
            lengthPlayed3 = 0;
            signal3 = null;
        }
        
        #endregion

        #region Sound4

        private GBSignalGenerator signal4;
        private BufferedWaveProvider waveBuffer4;
        private Dictionary<string, byte[]> bufferCache4 = new Dictionary<string, byte[]>();
        private int Length4 => (64 - (memory[0xff20] & 0x3F)) * 4;
        private int lengthPlayed4 = 0;
        private byte Envelope4 => memory[0xff21];
        private double Volume4 => (Envelope4 >> 4) / 15.0;
        private bool EnvelopeAmplify4 => (Envelope4 & 0x8) != 0;
        private int EnvelopePeriod4 => (Envelope4 & 0x7);
        private byte PolynomialCounter => memory[0xff22];
        private int CounterShift => (PolynomialCounter >> 4);
        private bool CounterWidthMode => (PolynomialCounter & 8) != 0;
        private int CounterDividingRatio => (PolynomialCounter & 7);
        private bool TriggerSound4 => (memory[0xff23] >> 7) != 0;
        private bool Loop4 => (memory[0xff23] & 0x40) == 0;

        public void StartSound4()
        {
            if (TriggerSound4)
            {
                signal4 = new GBSignalGenerator(SampleRate, NumChannels)
                {
                    Channel = GBSignalGenerator.ChannelType.Noise,
                    Gain = Volume4,
                    CounterShift = CounterShift,
                    CounterWidthMode = CounterWidthMode,
                    CounterDivisor = CounterDividingRatio,
                    EnvelopeAmplify = EnvelopeAmplify4,
                    EnvelopePeriod = EnvelopePeriod4,
                };

                SetSound4On();
            }
        }
        
        private void SetSound4On()
        {
            lengthPlayed4 = 0;
            OnOff = (byte) (OnOff | 0x08);
        }

        private void SetSound4Off()
        {
            OnOff = (byte) (OnOff & 0xF7);      // 11110111
            lengthPlayed4 = 0;
            signal4 = null;
        }
        
        #endregion
        
        public Sound(Memory memory)
        {
            this.memory = memory;
            Reset();

            waveBuffer1 = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate,NumChannels));
            waveBuffer2 = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate,NumChannels));
            waveBuffer3 = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate,NumChannels));
            waveBuffer4 = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate,NumChannels));
            waveBuffer1.BufferDuration = TimeSpan.FromMilliseconds( PlayStep * 10 );
            waveBuffer2.BufferDuration = TimeSpan.FromMilliseconds( PlayStep * 10 );
            waveBuffer3.BufferDuration = TimeSpan.FromMilliseconds( PlayStep * 10 );
            waveBuffer4.BufferDuration = TimeSpan.FromMilliseconds( PlayStep * 10 );
            waveBuffer1.DiscardOnBufferOverflow = true;
            waveBuffer2.DiscardOnBufferOverflow = true;
            waveBuffer3.DiscardOnBufferOverflow = true;
            waveBuffer4.DiscardOnBufferOverflow = true;
            waveBuffer1.ReadFully = false;
            waveBuffer2.ReadFully = false;
            waveBuffer3.ReadFully = false;
            waveBuffer4.ReadFully = false;

            SetSoundOutput();
            
            buffering = BufferingRounds;
        }

        public void SetSoundOutput()
        {
            mixer = new SoundMixer(this);

            waveOut?.Stop();
            waveOut?.Dispose();

            waveOut = CreateSoundPlayer(mixer);
        }

        private IWavePlayer CreateSoundPlayer(IWaveProvider waveProvider)
        {
            if (TryCreateSoundPlayer(CreateWaveOutPlayer, waveProvider, "WaveOut", out var player))
                return player;

            var standardWaveProvider = CreateStandardOutputProvider(waveProvider);

            if (TryCreateSoundPlayer(CreateDirectSoundPlayer, standardWaveProvider, "DirectSound standard PCM", out player))
                return player;

            if (TryCreateSoundPlayer(CreateWaveOutPlayer, standardWaveProvider, "WaveOut standard PCM", out player))
                return player;

            if (TryCreateSoundPlayer(() => new WaveOutEvent { DesiredLatency = DesiredLatency }, standardWaveProvider, "WaveOutEvent standard PCM", out player))
                return player;

            throw new InvalidOperationException("No compatible sound output could be initialized.");
        }

        private WaveOut CreateWaveOutPlayer()
        {
            return new WaveOut
            {
                DesiredLatency = DesiredLatency
            };
        }

        private DirectSoundOut CreateDirectSoundPlayer()
        {
            return new DirectSoundOut(DesiredLatency);
        }

        private IWaveProvider CreateStandardOutputProvider(IWaveProvider waveProvider)
        {
            var resampledProvider = new WdlResamplingSampleProvider(waveProvider.ToSampleProvider(), 48000);
            return resampledProvider.ToWaveProvider16();
        }

        private bool TryCreateSoundPlayer(Func<IWavePlayer> playerFactory, IWaveProvider waveProvider, string name, out IWavePlayer player)
        {
            player = null;

            try
            {
                player = playerFactory();
                player.Init(waveProvider);
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

        private class SoundMixer : IWaveProvider
        {
            private readonly Sound sound;
            private byte[] sound1Buffer = Array.Empty<byte>();
            private byte[] sound2Buffer = Array.Empty<byte>();
            private byte[] sound3Buffer = Array.Empty<byte>();
            private byte[] sound4Buffer = Array.Empty<byte>();

            public SoundMixer(Sound sound)
            {
                this.sound = sound;
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);
            }

            public WaveFormat WaveFormat { get; }

            public int Read(byte[] buffer, int offset, int count)
            {
                Array.Clear(buffer, offset, count);

                int sampleFrames = count / WaveFormat.BlockAlign;
                int monoBytes = sampleFrames * sizeof(float);

                EnsureBuffer(ref sound1Buffer, monoBytes);
                EnsureBuffer(ref sound2Buffer, monoBytes);
                EnsureBuffer(ref sound3Buffer, monoBytes);
                EnsureBuffer(ref sound4Buffer, monoBytes);

                int sound1Bytes = sound.waveBuffer1.Read(sound1Buffer, 0, monoBytes);
                int sound2Bytes = sound.waveBuffer2.Read(sound2Buffer, 0, monoBytes);
                int sound3Bytes = sound.waveBuffer3.Read(sound3Buffer, 0, monoBytes);
                int sound4Bytes = sound.waveBuffer4.Read(sound4Buffer, 0, monoBytes);

                for (int frame = 0; frame < sampleFrames; frame++)
                {
                    int monoOffset = frame * sizeof(float);
                    float left = 0.0f;
                    float right = 0.0f;

                    AddSample(sound1Buffer, sound1Bytes, monoOffset, sound.Sound1ToLeft, sound.Sound1ToRight, ref left, ref right);
                    AddSample(sound2Buffer, sound2Bytes, monoOffset, sound.Sound2ToLeft, sound.Sound2ToRight, ref left, ref right);
                    AddSample(sound3Buffer, sound3Bytes, monoOffset, sound.Sound3ToLeft, sound.Sound3ToRight, ref left, ref right);
                    AddSample(sound4Buffer, sound4Bytes, monoOffset, sound.Sound4ToLeft, sound.Sound4ToRight, ref left, ref right);

                    int outputOffset = offset + (frame * WaveFormat.BlockAlign);
                    WriteFloat(buffer, outputOffset, left);
                    WriteFloat(buffer, outputOffset + sizeof(float), right);
                }

                return count;
            }

            private static void EnsureBuffer(ref byte[] buffer, int length)
            {
                if (buffer.Length < length)
                    buffer = new byte[length];
            }

            private static void AddSample(byte[] buffer, int bytesRead, int offset, bool toLeft, bool toRight, ref float left, ref float right)
            {
                if (offset + sizeof(float) > bytesRead)
                    return;

                float sample = BitConverter.ToSingle(buffer, offset);

                if (toLeft)
                    left += sample;

                if (toRight)
                    right += sample;
            }

            private static void WriteFloat(byte[] buffer, int offset, float value)
            {
                byte[] bytes = BitConverter.GetBytes(Math.Clamp(value, -1.0f, 1.0f));
                Buffer.BlockCopy(bytes, 0, buffer, offset, bytes.Length);
            }
        }

        public void Reset()
        {
            stopwatch.Restart();
        }

        public void Play()
        {
            TimeSpan elapsed = stopwatch.Elapsed - lastTime;
            lastTime = stopwatch.Elapsed;
            int playStep = elapsed.Milliseconds << 1;
            
            Debug.Print($"Sound: {playStep} / {PlayStep}, {waveOut.PlaybackState}");
            Debug.Print($"Buffer1: {waveBuffer1.BufferedDuration.Milliseconds}, Buffer2: {waveBuffer2.BufferedDuration.Milliseconds}, Buffer3: {waveBuffer3.BufferedDuration.Milliseconds}, Buffer4: {waveBuffer4.BufferedDuration.Milliseconds}, ");
            //Debug.Print($"Sound1On: {Sound1On}, Sound2On: {Sound2On}, Loop1: {Loop1}, Loop2: {Loop2}");
            
            if (signal1 != null)
            {
                var playLength = (Loop1 || (lengthPlayed1 + playStep) <= Length1) ? playStep : Length1 - lengthPlayed1;
                if (playLength > 0)
                {
                    byte[] buffer;
                    int bytes;
                    string key = $"{playLength}-{Frequency1}-{WaveDuty1}-{EnvelopeAmplify1}-{EnvelopePeriod1}-{SweepAmplify}-{SweepPeriod}-{SweepShift}-{Volume1}";

                    if (!Loop1 && bufferCache1.ContainsKey(key))
                    {
                        buffer = bufferCache1[key];
                        bytes = buffer.Length;
                    }
                    else
                    {
                        buffer = new byte[waveBuffer1.WaveFormat.AverageBytesPerSecond * playLength / 1000];
                        var sample = signal1.Take(TimeSpan.FromMilliseconds(playLength));
                        bytes = sample.ToWaveProvider().Read(buffer, 0, buffer.Length);
                        bufferCache1[key] = buffer;
                    }
                    waveBuffer1.AddSamples(buffer, 0, bytes);
                    lengthPlayed1 += playLength;
                    
                    if (!Loop1) SetSound1Off();
                }
                else
                    SetSound1Off();
            }
            else
                SetSound1Off();
            
            if (signal2 != null)
            {
                var playLength = (Loop2 || (lengthPlayed2 + playStep) <= Length2) ? playStep : Length2 - lengthPlayed2;
                if (playLength > 0)
                {
                    byte[] buffer;
                    int bytes;
                    string key = $"{playLength}-{Frequency2}-{WaveDuty2}-{EnvelopeAmplify2}-{EnvelopePeriod2}-{Volume2}";

                    if (!Loop2 && bufferCache2.ContainsKey(key))
                    {
                        buffer = bufferCache2[key];
                        bytes = buffer.Length;
                    }
                    else
                    {
                        buffer = new byte[waveBuffer2.WaveFormat.AverageBytesPerSecond * playLength / 1000];
                        var sample = signal2.Take(TimeSpan.FromMilliseconds(playLength));
                        bytes = sample.ToWaveProvider().Read(buffer, 0, buffer.Length);
                        bufferCache2[key] = buffer;
                    }
                    waveBuffer2.AddSamples(buffer, 0, bytes);
                    lengthPlayed2 += playLength;
                    
                    if (!Loop2) SetSound2Off();
                }
                else
                    SetSound2Off();
            }
            else
                SetSound2Off();
            
            if (signal3 != null)
            {
                var playLength = (Loop3 || (lengthPlayed3 + playStep) <= Length3) ? playStep : Length3 - lengthPlayed3;
                if (playLength > 0)
                {
                    byte[] buffer;
                    int bytes;
                    string key = $"{playLength}-{Frequency3}-{OutputLevel3}-{Samples.Sum()}";
    
                    if (!Loop3 && bufferCache3.ContainsKey(key))
                    {
                        buffer = bufferCache3[key];
                        bytes = buffer.Length;
                    }
                    else
                    {
                        buffer = new byte[waveBuffer3.WaveFormat.AverageBytesPerSecond * playLength / 1000];
                        var sample = signal3.Take(TimeSpan.FromMilliseconds(playLength));
                        bytes = sample.ToWaveProvider().Read(buffer, 0, buffer.Length);
                        bufferCache3[key] = buffer;
                    }
                    waveBuffer3.AddSamples(buffer, 0, bytes);
                    lengthPlayed3 += playLength;
 
                    if (!Loop3) SetSound3Off();
                }
                else
                    SetSound3Off();
            }
            else
                SetSound3Off();
            
            if (signal4 != null)
            {
                var playLength = (Loop4 || (lengthPlayed4 + playStep) <= Length4) ? playStep : Length4 - lengthPlayed4;
                if (playLength > 0)
                {
                    byte[] buffer;
                    int bytes;
                    string key = $"{playLength}-{EnvelopeAmplify4}-{EnvelopePeriod4}-{Volume4}-{CounterShift}-{CounterWidthMode}-{CounterDividingRatio}";
            
                    if (!Loop4 && bufferCache4.ContainsKey(key))
                    {
                        buffer = bufferCache4[key];
                        bytes = buffer.Length;
                    }
                    else
                    {
                        buffer = new byte[waveBuffer4.WaveFormat.AverageBytesPerSecond * playLength / 1000];
                        var sample = signal4.Take(TimeSpan.FromMilliseconds(playLength));
                        bytes = sample.ToWaveProvider().Read(buffer, 0, buffer.Length);
                        bufferCache4[key] = buffer;
                    }
                    waveBuffer4.AddSamples(buffer, 0, bytes);
                    lengthPlayed4 += playLength;
                    
                    if (!Loop4) SetSound4Off();
                }
                else
                    SetSound4Off();
            }
            else
                SetSound4Off();
    
            //Debug.Print($"Buffer1: {waveBuffer1.BufferedDuration.Milliseconds}, Buffer2: {waveBuffer2.BufferedDuration.Milliseconds}, Buffer3: {waveBuffer3.BufferedDuration.Milliseconds}, Buffer4: {waveBuffer4.BufferedDuration.Milliseconds}, ");

            if (waveOut.PlaybackState != PlaybackState.Playing)
            {
                if (Sound1On || Sound2On || Sound3On || Sound4On)
                {
                    if (buffering > 0)
                        buffering--; // Give time to the buffers to build up
                    else
                        waveOut.Play();
                }
                else
                    buffering = BufferingRounds;
            }
            
            if (!Sound1On) SetSound1Off();
            if (!Sound2On) SetSound2Off();
            if (!Sound3On || !SoundOn3) SetSound3Off();
            if (!Sound4On) SetSound4Off();
        }
    }
}
