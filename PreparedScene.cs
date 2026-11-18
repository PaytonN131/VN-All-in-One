using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SD=System.Drawing;

sealed class SceneFrame {
    public byte[] Pixels;
    public int Width,Height;
}

sealed class PreparedScene : IDisposable {
    sealed class PhotoRequest {
        public SceneLook Look;
        public WindowCapture Capture; public readonly Stopwatch Clock=Stopwatch.StartNew();
        public readonly TaskCompletionSource<SceneFrame> Result=new TaskCompletionSource<SceneFrame>();
    }
    readonly Choice target;
    readonly ConcurrentQueue<PhotoRequest> photos=new ConcurrentQueue<PhotoRequest>();
    readonly AutoResetEvent wake=new AutoResetEvent(false);
    readonly object gate=new object();
    volatile bool closing;
    public volatile bool Ready;
    public volatile string Error;
    FastSceneRecording recording;

    public PreparedScene(Choice target) {
        this.target=target;
        var thread=new Thread(Run){IsBackground=true,Name="Captura preparada"};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();
    }
    public Task<SceneFrame> Photo(SceneLook look) {
        var request=new PhotoRequest{Look=look};
        if(closing||Error!=null){request.Result.SetException(new IOException(Error??"Captura cerrada."));return request.Result.Task;}
        photos.Enqueue(request);wake.Set();return request.Result.Task;
    }
    public FastSceneRecording Start(GameAudio audio,SceneLook look,string output,int limit,int fps) {
        lock(gate) {
            if(!Ready||closing||Error!=null)throw new IOException(Error??"La captura se está preparando. Vuelve a pulsar cuando esté lista.");
            if(recording!=null)throw new IOException("Ya hay una grabación.");
            var area=BorderlessWindow.ClientBounds(target.H);
            if(area.Width<2||area.Height<2)throw new IOException("El área del juego está vacía.");
            double scale=Math.Min(1,Math.Min(limit/(double)area.Height,limit*16.0/9/area.Width));
            recording=new FastSceneRecording(audio,look,output,Math.Max(2,(int)(area.Width*scale)&~1),
                Math.Max(2,(int)(area.Height*scale)&~1),fps,limit);
            wake.Set();return recording;
        }
    }
    SceneFrame Frame(WindowCapture capture,SceneLook look,int width,int height,byte[] buffer=null) {
        uint pid;Native.GetWindowThreadProcessId(target.H,out pid);
        if(pid!=target.Pid||!Native.IsWindow(target.H)||Native.IsIconic(target.H))
            throw new IOException("La ventana de captura no está disponible.");
        var area=BorderlessWindow.ClientBounds(target.H);
        if(width==0){width=area.Width;height=area.Height;}
        var data=buffer??new byte[checked(width*height*4)];
        return capture.Snapshot(area,data,width,height,look.NativeFilter)?new SceneFrame{Pixels=data,Width=width,Height=height}:null;
    }
    void Run() {
        Exception failure=null; PhotoRequest pending=null;
        try {
            using(var capture=new WindowCapture(target.H)) {
                var neutral=new SceneLook(0,0,1,1,0,1);
                var startup=Stopwatch.StartNew();
                while(!closing&&!Ready){
                    var area=BorderlessWindow.ClientBounds(target.H);double scale=Math.Min(1,Math.Min(1080.0/area.Height,1920.0/area.Width));
                    Ready=Frame(capture,neutral,Math.Max(2,(int)(area.Width*scale)&~1),Math.Max(2,(int)(area.Height*scale)&~1))!=null;
                    if(!Ready&&startup.ElapsedMilliseconds>5000)throw new IOException("La ventana no entregó el primer fotograma.");
                    if(!Ready)wake.WaitOne(10);
                }
                while(!closing) {
                    PhotoRequest request;
                    if(pending==null&&photos.TryDequeue(out request))pending=request;
                    if(pending!=null){
                        try{
                            if(pending.Capture==null)pending.Capture=new WindowCapture(target.H);
                            var frame=Frame(pending.Capture,pending.Look,0,0);
                            if(frame!=null){pending.Result.SetResult(frame);pending.Capture.Dispose();pending=null;}
                            else if(pending.Clock.ElapsedMilliseconds>2000)throw new IOException("No llegó una imagen nueva. No se guardó una captura anterior.");
                        }catch(Exception ex){pending.Result.TrySetException(ex);if(pending.Capture!=null)pending.Capture.Dispose();pending=null;}
                    }
                    FastSceneRecording active;
                    lock(gate)active=recording;
                    if(active!=null) {
                        if(active.Stopping) {
                            active.Complete();
                            lock(gate)if(recording==active)recording=null;
                        } else if(active.FrameDue) {
                            var frame=Frame(capture,active.Look,active.Width,active.Height,active.TakeBuffer());
                            if(frame!=null)active.Add(frame.Pixels);
                        }
                    }
                    if(active==null)capture.Pump(BorderlessWindow.ClientBounds(target.H));
                    wake.WaitOne(active==null&&pending==null?16:1);
                }
            }
        }catch(Exception ex){failure=ex;Error=ex.Message;}
        finally {
            Ready=false;
            if(pending!=null){if(pending.Capture!=null)pending.Capture.Dispose();pending.Result.TrySetException(failure??new IOException("Captura cerrada."));}
            lock(gate)if(recording!=null){recording.Fail(failure??new IOException("Captura cerrada."));recording.Complete();}
            PhotoRequest request;
            while(photos.TryDequeue(out request))request.Result.TrySetException(failure??new IOException("Captura cerrada."));
        }
    }
    public void Dispose(){closing=true;wake.Set();}
}

sealed class FastSceneRecording {
    readonly GameAudio audio;
    readonly string output;
    readonly int fps,bitrate;
    readonly BlockingCollection<byte[]> frames=new BlockingCollection<byte[]>(48),samples=new BlockingCollection<byte[]>(256);
    readonly ManualResetEventSlim firstFrame=new ManualResetEventSlim(false);
    readonly ConcurrentBag<byte[]> buffers=new ConcurrentBag<byte[]>();
    readonly object audioGate=new object();
    readonly Stopwatch clock=Stopwatch.StartNew();
    readonly Action<IntPtr,int> receive;
    volatile bool stopping;
    volatile bool completed;
    volatile Exception failure;
    int count;
    long firstFrameTicks;
    public double FirstFrameMilliseconds {get{return Interlocked.Read(ref firstFrameTicks)*1000.0/Stopwatch.Frequency;}}
    public readonly int Width,Height;
    public readonly SceneLook Look;
    public readonly Task<string> Result;
    public bool Stopping { get { return stopping; } }
    public bool HasFrames { get { return Volatile.Read(ref count)>0; } }
    public TimeSpan Elapsed { get { return clock.Elapsed; } }
    public bool FrameDue { get {return clock.Elapsed.TotalSeconds*fps>=count;} }
    public byte[] TakeBuffer(){byte[] data;return buffers.TryTake(out data)?data:new byte[checked(Width*Height*4)];}
    public TimeSpan RecordedElapsed { get {return clock.Elapsed;} }
    public FastSceneRecording(GameAudio audio,SceneLook look,string output,int width,int height,int fps,int limit=1080) {
        this.audio=audio;Look=look;this.output=output;Width=width;Height=height;this.fps=fps;bitrate=fps==60?(limit==720?6:10):(limit==720?4:7);
        receive=delegate(IntPtr input,int length) {
            lock(audioGate) {
                if(completed)return;
                var data=new byte[length];
                if(input!=IntPtr.Zero)Marshal.Copy(input,data,0,length);
                if(!samples.TryAdd(data))Fail(new IOException("El codificador no recibe audio a tiempo."));
            }
        };
        audio.Packet+=receive;
        Result=Task.Run(()=>{if(!firstFrame.Wait(3000)){Fail(new IOException("La ventana no entregó el primer fotograma."));Complete();throw failure;}return Encode();});
    }
    public void Add(byte[] data) {
        lock(audioGate){
            if(completed)return;
            if(!frames.TryAdd(data))Fail(new IOException("El equipo no mantiene esta calidad. Prueba 720p o 30 FPS."));
            else {if(count==0)Interlocked.Exchange(ref firstFrameTicks,clock.ElapsedTicks);count++;firstFrame.Set();}
        }
        if(clock.Elapsed.TotalMinutes>=30)Stop();
    }
    public void Stop(){stopping=true;firstFrame.Set();}
    public void Fail(Exception ex){failure=ex;stopping=true;firstFrame.Set();}
    public void Complete() {
        audio.Packet-=receive;
        lock(audioGate) {
            if(completed)return;
            completed=true;frames.CompleteAdding();samples.CompleteAdding();
        }
    }
    string Encode() {
        string temp=output+".capture.mp4";
        try {
            string name="Nitidez-"+Guid.NewGuid().ToString("N");
            using(var pipe=new NamedPipeServerStream(name,PipeDirection.Out,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,0,32768))
            using(var process=SceneFiles.Start("-nofind_stream_info -thread_queue_size 4 -f rawvideo -pixel_format bgra -video_size "+
                Width+"x"+Height+" -framerate "+fps+" -i pipe:0 -nofind_stream_info -thread_queue_size 16 -f s16le -ar 44100 -ac 2 -i "+
                SceneFiles.Quote(@"\\.\pipe\"+name)+" -map 0:v:0 -map 1:a:0 -c:v h264_nvenc -preset p3 -tune ll -bf 0 -g "+fps*2+" -surfaces 6 -zerolatency 1"+
                " -rc cbr -b:v "+bitrate+"M -maxrate "+bitrate+"M -bufsize "+bitrate+"M -pix_fmt yuv420p -c:a aac -b:a 160k -shortest -movflags +faststart -f mp4 "+SceneFiles.Quote(temp),true)) {
                var errors=process.StandardError.ReadToEndAsync();process.BeginOutputReadLine();
                var connected=pipe.WaitForConnectionAsync();
                var video=Task.Run(delegate {
                    try {
                        foreach(var frame in frames.GetConsumingEnumerable()){
                            process.StandardInput.BaseStream.Write(frame,0,frame.Length);
                            if(buffers.Count<3)buffers.Add(frame);
                        }
                        process.StandardInput.Close();
                    }catch(IOException){}catch(ObjectDisposedException){}
                });
                Task sound=null;
                try {
                    var wait=Stopwatch.StartNew();
                    while(!connected.IsCompleted) {
                        if(process.HasExited)throw SceneFiles.Failure(errors.Result);
                        if(wait.ElapsedMilliseconds>10000)throw new IOException("El codificador no abrió el audio.");
                        Thread.Sleep(10);
                    }
                    connected.GetAwaiter().GetResult();
                    sound=Task.Run(delegate {
                        try {foreach(var packet in samples.GetConsumingEnumerable())pipe.Write(packet,0,packet.Length);}
                        catch(IOException){}catch(ObjectDisposedException){}
                        finally {pipe.Dispose();}
                    });
                    var drive=new DriveInfo(Path.GetPathRoot(output));
                    long finalizing=-1;
                    while(!process.WaitForExit(100)) {
                        if(audio.Error!=null)Fail(new IOException(audio.Error));
                        if(drive.AvailableFreeSpace<256L*1024*1024)Fail(new IOException("Espacio insuficiente en disco."));
                        if(failure!=null)throw failure;
                        if(completed&&finalizing<0)finalizing=clock.ElapsedMilliseconds;
                        if(finalizing>=0&&clock.ElapsedMilliseconds-finalizing>20000)throw new TimeoutException("El codificador no terminó.");
                    }
                    if(process.ExitCode!=0)throw SceneFiles.Failure(errors.Result);
                    if(failure!=null)throw failure;
                }finally {
                    stopping=true;Complete();
                    if(!process.HasExited){process.Kill();process.WaitForExit();}
                    pipe.Dispose();
                    video.GetAwaiter().GetResult();
                    if(sound!=null)sound.GetAwaiter().GetResult();
                }
            }
            try{SceneFiles.FinishFile(temp,output);}
            catch(Exception ex){
                string recovery=output+".pendiente";
                if(File.Exists(temp))File.Move(temp,recovery);
                throw new IOException("No se pudo finalizar. Captura recuperable en "+recovery,ex);
            }
            return output;
        }finally {Stop();Complete();if(File.Exists(temp))File.Delete(temp);}
    }
}
