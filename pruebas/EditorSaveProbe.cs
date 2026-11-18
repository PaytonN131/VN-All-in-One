using System;
using System.IO;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

static class EditorSaveProbe {
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static object Field(object value,string name){return value.GetType().GetField(name,Private).GetValue(value);}
    static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
    static void Await(Task task){var clock=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted&&clock.ElapsedMilliseconds<20000)Pump();if(!task.IsCompleted)throw new Exception("El guardado no terminó automáticamente.");task.GetAwaiter().GetResult();}
    static void Render(Window window,string file){window.UpdateLayout();var view=(FrameworkElement)window.Content;var bitmap=new RenderTargetBitmap((int)view.ActualWidth,(int)view.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(file))png.Save(output);}
    [STAThread] static int Main(string[] args){
        new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        string game="_Prueba editores 42 "+Guid.NewGuid().ToString("N").Substring(0,8);
        ControlWindow owner=null;ClipEditor audio=null;SceneEditor video=null;
        try{
            owner=new ControlWindow{ShowActivated=false,ShowInTaskbar=false};owner.Show();Pump();var ui=(FrameworkElement)owner.Content;
            if(ui.FindName("WindowMode")!=null||ui.FindName("Borderless")!=null)throw new Exception("Aún existe el selector de modo.");
            foreach(string name in new[]{"AudioFolder","VideoFolder","ScreenshotFolder"})if(!(ui.FindName(name) is Button))throw new Exception("Falta un botón de carpeta.");
            var clips=new ObservableCollection<AudioClip>{new AudioClip(new byte[AudioRing.BytesPerSecond],game)};
            audio=new ClipEditor(owner,ui.Resources,clips);audio.Show();Pump();
            ((TextBox)Field(audio,"fileName")).Text="Mi frase favorita";
            string folder=CaptureFiles.Folder("Grabaciones",game),existing=Path.Combine(folder,"Mi frase favorita.mp3");File.WriteAllText(existing,"conservar");
            Directory.CreateDirectory(args[1]);Render(audio,Path.Combine(args[1],"editor-audio.png"));
            Await((Task)typeof(ClipEditor).GetMethod("Save",Private).Invoke(audio,null));
            if(audio.LastSaved!=Path.Combine(folder,"Mi frase favorita_02.mp3")||!File.Exists(audio.LastSaved)||clips.Count!=0||File.ReadAllText(existing)!="conservar")throw new Exception("No se guardó el audio con su nombre sin reemplazar el anterior.");
            audio.Close();audio=null;
            video=new SceneEditor(owner,ui.Resources,Path.GetFullPath(args[0]),game);video.Show();var clock=System.Diagnostics.Stopwatch.StartNew();while(!(bool)Field(video,"ready")&&clock.ElapsedMilliseconds<7000)Pump();
            if(!(bool)Field(video,"ready"))throw new Exception("No se abrió el vídeo.");
            ((TextBox)Field(video,"fileName")).Text="Mi escena favorita";((Slider)Field(video,"end")).Value=1;
            Render(video,Path.Combine(args[1],"editor-video.png"));
            Await((Task)typeof(SceneEditor).GetMethod("Export",Private).Invoke(video,null));
            string result=Path.Combine(CaptureFiles.Folder("Videos",game),"Mi escena favorita.mp4");
            if(!File.Exists(result)||new FileInfo(result).Length>SceneFiles.MaximumVideoBytes)throw new Exception("No se guardó el vídeo con su nombre y límite de tamaño.");
            Console.WriteLine("PASS: MP3 directo sin diálogo, nombre desde editor, colisiones sin reemplazo; recorte MP4 nombrado desde editor, hasta 30 MB; selector eliminado y tres botones de carpeta presentes.");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally{if(audio!=null)audio.Close();if(video!=null)video.Close();if(owner!=null)owner.Close();foreach(string category in new[]{"Grabaciones","Videos"}){string folder=Path.Combine(AudioFiles.Folder,category,game);if(Directory.Exists(folder)){foreach(string file in Directory.GetFiles(folder))File.Delete(file);Directory.Delete(folder);}}}
    }
}
