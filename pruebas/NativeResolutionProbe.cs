using System;
using System.IO;
using System.Diagnostics;
using System.Media;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

static class NativeResolutionProbe {
    static void Verify(string video,int width,int height,string folder){
        string frame=SceneFiles.Output(folder,"Verificacion",".png");
        SceneFiles.RunFile("-ss 0.3 -i "+SceneFiles.Quote(video)+" -frames:v 1 -an -c:v png -f image2 -update 1",frame,10000);
        using(var bitmap=new System.Drawing.Bitmap(frame)){
            if(bitmap.Width!=width||bitmap.Height!=height)throw new Exception("El clip cambió la resolución: "+bitmap.Width+"x"+bitmap.Height+", se esperaba "+width+"x"+height);
            var pixel=bitmap.GetPixel(width/2,height/2);
            if(pixel.R<110||pixel.R>135||Math.Abs(pixel.R-pixel.B)>4||Math.Abs(pixel.R-pixel.G)>4)throw new Exception("No se conservó el filtro.");
        }
        Console.WriteLine("PASS: "+width+" × "+height+" exactos, filtro aplicado.");
    }
    [STAThread] static int Main(string[] args){
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
        var app=new Application();
        var scene=new Window{Title="Prueba de resolución automática",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Width=640,Height=360,MaxWidth=6000,MaxHeight=4000,Left=20,Top=20,ShowActivated=false,ShowInTaskbar=false,Background=new SolidColorBrush(Color.FromRgb(64,80,128))};
        scene.Content=new TextBlock{Text="Resolución nativa — detalle del juego",Foreground=Brushes.White,FontSize=19,Margin=new Thickness(20)};
        int result=1;
        scene.Loaded+=async delegate{
            try{
                string folder=Path.GetFullPath(args[0]);Directory.CreateDirectory(folder);
                var hwnd=new WindowInteropHelper(scene).Handle;var look=new SceneLook(.7,.17,1,0,0,1);
                var pcm=new byte[AudioRing.BytesPerSecond];for(int i=0;i<AudioRing.Rate;i++){short value=(short)(400*Math.Sin(i*2*Math.PI*440/AudioRing.Rate));pcm[i*4]=pcm[i*4+2]=(byte)value;pcm[i*4+1]=pcm[i*4+3]=(byte)(value>>8);}
                using(var stream=AudioFiles.Preview(new AudioClip(pcm,"Prueba"),0,AudioRing.Rate))
                using(var sound=new SoundPlayer(stream))
                using(var audio=new GameAudio((uint)Process.GetCurrentProcess().Id,0))
                using(var prepared=new PreparedScene(new Choice{H=hwnd,Pid=(uint)Process.GetCurrentProcess().Id,Title=scene.Title})){
                    sound.Load();sound.PlayLooping();
                    foreach(var size in new[]{new[]{640,360},new[]{641,361},new[]{1940,801},new[]{1600,1100}}){
                        scene.Width=size[0];scene.Height=size[1];scene.UpdateLayout();await Task.Delay(300);
                        Native.SetWindowPos(hwnd,IntPtr.Zero,20,20,size[0],size[1],0x14);await Task.Delay(150);
                        var area=BorderlessWindow.ClientBounds(hwnd);if(area.Width!=size[0]||area.Height!=size[1])throw new Exception("La ventana de prueba no aceptó la geometría: "+area.Width+" × "+area.Height);
                        var warm=Stopwatch.StartNew();while(!prepared.Ready&&warm.ElapsedMilliseconds<5000)await Task.Delay(10);if(!prepared.Ready)throw new Exception(prepared.Error??"No se preparó la captura.");
                        string path=SceneFiles.Output(folder,"Nativo-"+area.Width+"x"+area.Height,".mp4");
                        var recording=prepared.Start(audio,look,path,0,60);await Task.Delay(1400);recording.Stop();
                        Console.WriteLine("Resolución "+area.Width+"x"+area.Height+", primer fotograma: "+recording.FirstFrameMilliseconds.ToString("F1")+" ms.");string file=await recording.Result;
                        Verify(file,area.Width,area.Height,folder);await Task.Delay(200);
                    }
                    var last=BorderlessWindow.ClientBounds(hwnd);string resizeFile=SceneFiles.Output(folder,"Cambio-resolucion",".mp4");
                    var resizing=prepared.Start(audio,look,resizeFile,0,60);await Task.Delay(600);scene.Width=900;scene.Height=500;
                    var completed=await Task.WhenAny(resizing.Result,Task.Delay(5000));if(completed!=resizing.Result)throw new Exception("No finalizó el clip al cambiar de resolución.");
                    Verify(await resizing.Result,last.Width,last.Height,folder);if(String.IsNullOrEmpty(resizing.CompletionNotice))throw new Exception("Falta aviso de cambio de tamaño.");
                    Console.WriteLine("PASS: cambio de resolución finaliza el clip sin estirarlo.");sound.Stop();result=0;
                }
            }catch(Exception ex){Console.Error.WriteLine(ex);}
            finally{scene.Close();}
        };
        app.Run(scene);return result;
    }
}
