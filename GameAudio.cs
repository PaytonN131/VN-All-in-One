using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

// PCM stereo. Keep only the requested window of recent samples.
public sealed class AudioRing {
    public const int Rate = 44100, FrameBytes = 4, BytesPerSecond = Rate * FrameBytes;
    readonly object gate = new object();
    byte[] data;
    int head, count;

    public AudioRing(int seconds) { data = new byte[seconds * BytesPerSecond]; }

    public void Resize(int seconds) {
        lock (gate) {
            int capacity = seconds * BytesPerSecond;
            if (capacity == data.Length) return;
            byte[] next = new byte[capacity];
            count = CopyRecent(next);
            head = count % capacity;
            data = next;
        }
    }

    int CopyRecent(byte[] destination) {
        int length = Math.Min(count, destination.Length);
        int start = (head - length + data.Length) % data.Length;
        int first = Math.Min(length, data.Length - start);
        Buffer.BlockCopy(data, start, destination, 0, first);
        Buffer.BlockCopy(data, 0, destination, first, length - first);
        return length;
    }

    public byte[] Snapshot() {
        lock (gate) {
            byte[] result = new byte[count];
            CopyRecent(result);
            return result;
        }
    }

    public void Append(IntPtr input, int bytes) {
        lock (gate) {
            if (bytes > data.Length) {
                if (input != IntPtr.Zero) input = IntPtr.Add(input, bytes - data.Length);
                bytes = data.Length;
            }
            int first = Math.Min(bytes, data.Length - head);
            if (input == IntPtr.Zero) {
                Array.Clear(data, head, first);
                Array.Clear(data, 0, bytes - first);
            } else {
                Marshal.Copy(input, data, head, first);
                if (bytes > first) Marshal.Copy(IntPtr.Add(input, first), data, 0, bytes - first);
            }
            head = (head + bytes) % data.Length;
            count = Math.Min(count + bytes, data.Length);
        }
    }
}

public sealed class GameAudio : IDisposable {
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    struct WaveFormat {
        public ushort Tag, Channels;
        public uint Rate, BytesPerSecond;
        public ushort FrameBytes, Bits, Extra;
    }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct ActivationVariant {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public int Size;
        [FieldOffset(16)] public IntPtr Data;
    }
    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient {
        void Initialize(int shareMode, uint flags, long duration, long periodicity, ref WaveFormat format, IntPtr session);
        void GetBufferSize(out uint frames);
        void GetStreamLatency(out long latency);
        void GetCurrentPadding(out uint frames);
        void IsFormatSupported(int shareMode, IntPtr format, IntPtr closest);
        void GetMixFormat(out IntPtr format);
        void GetDevicePeriod(out long normal, out long minimum);
        void Start();
        void Stop();
        void Reset();
        void SetEventHandle(IntPtr handle);
        void GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }
    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient {
        void GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong position, out ulong qpc);
        void ReleaseBuffer(uint frames);
        void GetNextPacketSize(out uint frames);
    }
    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IActivationOperation {
        void GetActivateResult(out int result, [MarshalAs(UnmanagedType.IUnknown)] out object client);
    }
    [Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComVisible(true)]
    public interface IActivationCallback {
        [PreserveSig] int ActivateCompleted(IActivationOperation operation);
    }
    [Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComVisible(true)]
    public interface IAgileObject {}
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class Callback : IActivationCallback, IAgileObject {
        public readonly TaskCompletionSource<object> Completion = new TaskCompletionSource<object>();
        public int ActivateCompleted(IActivationOperation operation) {
            try {
                int result;
                object client;
                operation.GetActivateResult(out result, out client);
                Marshal.ThrowExceptionForHR(result);
                Completion.TrySetResult(client);
            } catch (Exception ex) { Completion.TrySetException(ex); }
            return 0;
        }
    }
    [DllImport("Mmdevapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int ActivateAudioInterfaceAsync(string device, ref Guid iid, ref ActivationVariant options,
        IActivationCallback callback, out IActivationOperation operation);

    readonly uint pid;
    volatile bool stopping;
    public volatile AudioRing Ring;
    public event Action<IntPtr, int> Packet;
    public volatile string Error;
    public volatile bool Running;

    public GameAudio(uint pid, int seconds) {
        this.pid = pid;
        if (seconds > 0) Ring = new AudioRing(seconds);
        var thread = new Thread(Capture) { IsBackground = true, Name = "Audio de la novela" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    static IAudioClient Activate(uint pid) {
        var callback = new Callback();
        IActivationOperation operation = null;
        IntPtr payload = Marshal.AllocHGlobal(12);
        try {
            Marshal.WriteInt32(payload, 0, 1); // Process loopback activation.
            Marshal.WriteInt32(payload, 4, unchecked((int)pid));
            Marshal.WriteInt32(payload, 8, 0); // Include only this process tree.
            var options = new ActivationVariant { Type = 65, Size = 12, Data = payload };
            Guid iid = typeof(IAudioClient).GUID;
            Marshal.ThrowExceptionForHR(ActivateAudioInterfaceAsync(@"VAD\Process_Loopback", ref iid, ref options, callback, out operation));
            if (!callback.Completion.Task.Wait(5000)) throw new TimeoutException("Windows no respondió al iniciar el audio.");
            return (IAudioClient)callback.Completion.Task.Result;
        } finally {
            if (operation != null) Marshal.ReleaseComObject(operation);
            Marshal.FreeHGlobal(payload);
        }
    }

    void Capture() {
        IAudioClient client = null;
        object service = null;
        string phase = "activar WASAPI";
        using (var ready = new AutoResetEvent(false)) {
            try {
                client = Activate(pid);
                if (stopping) return;
                var format = new WaveFormat { Tag = 1, Channels = 2, Rate = AudioRing.Rate,
                    BytesPerSecond = AudioRing.BytesPerSecond, FrameBytes = AudioRing.FrameBytes, Bits = 16 };
                const uint Loopback = 0x20000, EventCallback = 0x40000, AutoConvertPcm = 0x80000000;
                phase = "inicializar el formato de audio";
                client.Initialize(0, Loopback | EventCallback | AutoConvertPcm, 0, 0, ref format, IntPtr.Zero);
                phase = "iniciar la sesión de audio";
                Guid captureId = typeof(IAudioCaptureClient).GUID;
                client.GetService(ref captureId, out service);
                var capture = (IAudioCaptureClient)service;
                client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle());
                client.Start();
                Running = true;
                phase = "leer el audio";
                while (!stopping) {
                    ready.WaitOne(100);
                    uint frames;
                    capture.GetNextPacketSize(out frames);
                    while (!stopping && frames != 0) {
                        IntPtr samples;
                        uint flags;
                        ulong position, timestamp;
                        capture.GetBuffer(out samples, out frames, out flags, out position, out timestamp);
                        try {
                            IntPtr input = (flags & 2) != 0 ? IntPtr.Zero : samples;
                            int bytes = checked((int)frames * AudioRing.FrameBytes);
                            var ring = Ring;
                            if (ring != null) ring.Append(input, bytes);
                            var handler = Packet;
                            if (handler != null) handler(input, bytes);
                        }
                        finally { capture.ReleaseBuffer(frames); }
                        capture.GetNextPacketSize(out frames);
                    }
                }
            } catch (Exception ex) {
                if (!stopping) {
                    var cause = ex.GetBaseException();
                    Error = String.Format("No se pudo {0} (0x{1:X8}): {2}", phase, cause.HResult, cause.Message);
                }
            } finally {
                if (Running) try { client.Stop(); } catch (COMException) {}
                Running = false;
                if (service != null) Marshal.ReleaseComObject(service);
                if (client != null) Marshal.ReleaseComObject(client);
            }
        }
    }

    public void Dispose() { stopping = true; }
}
