using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuotaTrayApp
{
    internal sealed class ToastSound : IDisposable
    {
        private const uint HeaderDone = 1;
        private const int LeadInMs = 60;
        private const int OutputBufferMs = 65;
        private const int StartTimeoutMs = 450;
        private static ToastSound active;
        private readonly Timer timer = new Timer { Interval = 10 };
        private readonly Stopwatch elapsed = new Stopwatch();
        private readonly Buffer leadIn = new Buffer();
        private readonly Buffer sound = new Buffer();
        private IntPtr device;
        private Action ready;
        private long slideAfter = -1;
        private long closeAfter = -1;
        private long cleanupAfter;

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        internal struct Format
        {
            internal ushort Tag, Channels;
            internal uint Rate, BytesPerSecond;
            internal ushort BlockAlign, BitsPerSample, Extra;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Header
        {
            internal IntPtr Data;
            internal uint Size, Recorded;
            internal IntPtr User;
            internal uint Flags, Loops;
            internal IntPtr Next, Reserved;
        }

        private sealed class Buffer
        {
            private static readonly uint HeaderSize = (uint)Marshal.SizeOf(typeof(Header));
            private IntPtr header, data;
            private bool prepared;
            internal bool Done { get { return (Marshal.ReadInt32(header, (int)Marshal.OffsetOf(typeof(Header), "Flags")) & HeaderDone) != 0; } }

            internal void Queue(IntPtr device, byte[] samples)
            {
                data = Marshal.AllocHGlobal(samples.Length);
                Marshal.Copy(samples, 0, data, samples.Length);
                header = Marshal.AllocHGlobal((int)HeaderSize);
                Marshal.StructureToPtr(new Header { Data = data, Size = (uint)samples.Length }, header, false);
                Check(waveOutPrepareHeader(device, header, HeaderSize));
                prepared = true;
                Check(waveOutWrite(device, header, HeaderSize));
            }

            internal void Release(IntPtr device)
            {
                if (prepared && waveOutUnprepareHeader(device, header, HeaderSize) != 0) return;
                if (header != IntPtr.Zero) Marshal.FreeHGlobal(header);
                if (data != IntPtr.Zero) Marshal.FreeHGlobal(data);
                header = data = IntPtr.Zero;
            }
        }

        internal static byte[] ReadPcm(Stream stream, out Format format)
        {
            format = new Format();
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != 0x46464952) return null; // RIFF
                long end = reader.ReadUInt32() + 8L;
                if (reader.ReadUInt32() != 0x45564157 || end > stream.Length) return null; // WAVE

                while (stream.Position + 8 <= end)
                {
                    uint tag = reader.ReadUInt32();
                    uint size = reader.ReadUInt32();
                    long next = stream.Position + size + (size & 1);
                    if (next > end) return null;
                    if (tag == 0x20746d66 && size >= 16) // fmt
                    {
                        format = new Format
                        {
                            Tag = reader.ReadUInt16(), Channels = reader.ReadUInt16(), Rate = reader.ReadUInt32(),
                            BytesPerSecond = reader.ReadUInt32(), BlockAlign = reader.ReadUInt16(), BitsPerSample = reader.ReadUInt16()
                        };
                    }
                    else if (tag == 0x61746164) // data
                    {
                        if (format.Tag != 1 || (format.BitsPerSample != 8 && format.BitsPerSample != 16 &&
                            format.BitsPerSample != 24 && format.BitsPerSample != 32) || format.Channels == 0 ||
                            format.BlockAlign != format.Channels * (format.BitsPerSample / 8) || format.Rate == 0 || format.Rate > 192000 ||
                            format.BytesPerSecond != format.Rate * format.BlockAlign ||
                            size > int.MaxValue || size == 0 || size % format.BlockAlign != 0) return null;
                        return reader.ReadBytes((int)size);
                    }
                    stream.Position = next;
                }
            }
            return null;
        }

        internal static void ScalePcm(byte[] samples, Format format, int volume)
        {
            if (volume == 100) return;
            int width = format.BitsPerSample / 8;
            if (width == 1)
            {
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = (byte)(128 + (samples[i] - 128) * volume / 100);
                return;
            }
            for (int i = 0; i < samples.Length; i += width)
            {
                int last = i + width - 1;
                int value = (sbyte)samples[last];
                for (int index = last - 1; index >= i; index--) value = (value << 8) | samples[index];
                int adjusted = (int)((long)value * volume / 100);
                for (int index = i; index <= last; index++)
                {
                    samples[index] = (byte)adjusted;
                    adjusted >>= 8;
                }
            }
        }

        internal static Stream PcmWave(Format format, byte[] samples)
        {
            int padding = samples.Length & 1;
            var stream = new MemoryStream(44 + samples.Length + padding);
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(0x46464952u); // RIFF
                writer.Write(36 + samples.Length + padding);
                writer.Write(0x45564157u); // WAVE
                writer.Write(0x20746d66u); // fmt
                writer.Write(16);
                writer.Write(format.Tag);
                writer.Write(format.Channels);
                writer.Write(format.Rate);
                writer.Write(format.BytesPerSecond);
                writer.Write(format.BlockAlign);
                writer.Write(format.BitsPerSample);
                writer.Write(0x61746164u); // data
                writer.Write(samples.Length);
                writer.Write(samples);
                if (padding != 0) writer.Write((byte)0);
            }
            stream.Position = 0;
            return stream;
        }

        internal static void Stop()
        {
            if (active != null) active.Dispose();
        }

        internal static bool TryPlay(Format format, byte[] samples, Action ready)
        {
            Stop();
            var playback = new ToastSound { ready = ready };
            try
            {
                Check(waveOutOpen(out playback.device, uint.MaxValue, ref format, IntPtr.Zero, IntPtr.Zero, 0));
                Check(waveOutPause(playback.device));
                // Queue both buffers before restarting so the device can move straight from warm-up to the sound.
                int leadFrames = (int)(format.Rate * LeadInMs / 1000);
                var silence = new byte[leadFrames * format.BlockAlign];
                if (format.BitsPerSample == 8)
                    for (int i = 0; i < silence.Length; i++) silence[i] = 128;
                playback.leadIn.Queue(playback.device, silence);
                playback.sound.Queue(playback.device, samples);
                Check(waveOutRestart(playback.device));
                playback.cleanupAfter = (long)samples.Length * 1000 / format.BytesPerSecond + StartTimeoutMs + 1000;
                playback.elapsed.Start();
                playback.timer.Tick += playback.Tick;
                playback.timer.Start();
                active = playback;
                return true;
            }
            catch (InvalidOperationException)
            {
                playback.ready = null;
                playback.Dispose();
                return false;
            }
        }

        private void Tick(object sender, EventArgs e)
        {
            long now = elapsed.ElapsedMilliseconds;
            // The shared Windows output pipeline still has queued samples after returning the lead-in buffer.
            if (slideAfter < 0 && leadIn.Done) slideAfter = now + OutputBufferMs;
            if (ready != null && (now >= StartTimeoutMs || (slideAfter >= 0 && now >= slideAfter)))
            {
                Action start = ready;
                ready = null;
                start();
            }
            if (closeAfter < 0 && sound.Done) closeAfter = now + OutputBufferMs;
            if (now >= cleanupAfter || (closeAfter >= 0 && now >= closeAfter)) Dispose();
        }

        public void Dispose()
        {
            timer.Dispose();
            if (device != IntPtr.Zero)
            {
                waveOutReset(device);
                sound.Release(device);
                leadIn.Release(device);
                waveOutClose(device);
                device = IntPtr.Zero;
            }
            if (active == this) active = null;
            // A replacement or an audio-device failure must not leave a window waiting off-screen.
            Action start = ready;
            ready = null;
            if (start != null) start();
        }

        private static void Check(uint result)
        {
            if (result != 0) throw new InvalidOperationException("waveOut error " + result);
        }

        [DllImport("winmm.dll")] private static extern uint waveOutOpen(out IntPtr device, uint id, ref Format format, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] private static extern uint waveOutPause(IntPtr device);
        [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutWrite(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutRestart(IntPtr device);
        [DllImport("winmm.dll")] private static extern uint waveOutReset(IntPtr device);
        [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutClose(IntPtr device);
    }
}
