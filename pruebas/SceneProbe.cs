using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

static class SceneProbe {
    static void CheckRecordedFilter(string video, string folder) {
        string frame = SceneFiles.Output(folder, "Fotograma MP4", ".png");
        SceneFiles.RunFile("-ss 0.5 -i " + SceneFiles.Quote(video) +
            " -frames:v 1 -an -threads:v 1 -c:v png -f image2 -update 1", frame, 10000);
        using (var image = new System.Drawing.Bitmap(frame)) {
            var pixel = image.GetPixel(image.Width / 2, image.Height / 2);
            Console.WriteLine("MP4 decodificado: {0}", pixel);
            if (Math.Abs(pixel.R - pixel.B) > 4 || Math.Abs(pixel.R - pixel.G) > 4 || pixel.R < 105 || pixel.R > 140)
                throw new Exception("No se conservaron brillo y blanco y negro en el MP4.");
        }
    }
    [STAThread]
    static int Main(string[] args) {
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
        bool motion = args.Length > 1 && args[1] == "--motion";
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var scene = new Window { Title = "Escena sintética de Nitidez VN", Width = 820, Height = 480,
            ShowActivated = false, ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(64, 80, 128)) };
        var grid = new Grid();
        var frame = new TextBlock { FontSize = 34, Foreground = Brushes.White, Margin = new Thickness(25) };
        grid.Children.Add(frame); scene.Content = grid;
        System.Windows.Shapes.Rectangle sprite = null;
        EventHandler animate = null;
        if (motion) {
            var screen = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            scene.WindowStyle = WindowStyle.None;
            scene.Left = 0; scene.Top = 0; scene.Width = Math.Min(1920, screen.Width); scene.Height = Math.Min(1080, screen.Height);
            sprite = new System.Windows.Shapes.Rectangle { Width = 120, Height = 60, Fill = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var shift = new TranslateTransform(300, 0);
            sprite.RenderTransform = shift;
            grid.Children.Add(sprite);
            var clock = Stopwatch.StartNew();
            animate = delegate {
                double phase = clock.Elapsed.TotalSeconds / .4 % 1;
                shift.Y = 90 + 600 * (phase < .5 ? phase * 2 : 2 - phase * 2);
            };
            CompositionTarget.Rendering += animate;
        }
        int number = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        timer.Tick += delegate { frame.Text = "Fotograma " + ++number; };
        int result = 1;
        scene.Loaded += async delegate {
            timer.Start();
            try {
                await Task.Delay(350);
                string folder = Path.GetFullPath(args[0]);
                IntPtr hwnd = new WindowInteropHelper(scene).Handle;
                var look = new SceneLook(.7, .17, 1, 0, 0, 1);
                using(var prepared=new PreparedScene(new Choice{H=hwnd,Pid=(uint)Process.GetCurrentProcess().Id,Title="Prueba"})){
                var warm=Stopwatch.StartNew();
                while(!prepared.Ready&&warm.ElapsedMilliseconds<5000)await Task.Delay(10);
                if(!prepared.Ready)throw new Exception(prepared.Error??"La captura no se preparó.");
                for(int n=0;n<3;n++){
                    await Task.Delay(400);
                    byte value=(byte)(41+n*53);
                    scene.Background=new SolidColorBrush(Color.FromRgb(value,value,value));
                    await Task.Delay(50);
                    var fresh=await prepared.Photo(new SceneLook(0,0,1,1,0,1));
                    int at=((fresh.Height/2)*fresh.Width+fresh.Width/2)*4;
                    if(fresh.Pixels[at]!=value||fresh.Pixels[at+1]!=value||fresh.Pixels[at+2]!=value)throw new Exception("La captura usó una imagen anterior al diálogo actual.");
                }
                Console.WriteLine("PASS: 3 cambios de diálogo tras periodos inactivos, sin fotogramas antiguos; cursor de Windows excluido al crear la sesión.");
                scene.Background=new SolidColorBrush(Color.FromRgb(64,80,128));await Task.Delay(50);
                int limit = 0;
                int fps = args.Length > 2 && args[2] == "30" ? 30 : 60;
                string source = SceneFiles.Source(hwnd, limit, 60);
                string png = SceneFiles.Output(folder, "Captura", ".png");
                var latency=Stopwatch.StartNew();
                var photo=await prepared.Photo(look);
                Console.WriteLine("Captura de imagen: {0:F1} ms",latency.Elapsed.TotalMilliseconds);
                var image=BitmapSource.Create(photo.Width,photo.Height,96,96,PixelFormats.Bgra32,null,photo.Pixels,photo.Width*4);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
                using(var output=File.Create(png))encoder.Save(output);
                using (var b = new System.Drawing.Bitmap(png)) {
                    var pixel = b.GetPixel(b.Width / 2, b.Height / 2);
                    Console.WriteLine("PNG {0}x{1}, pixel filtrado: {2}", b.Width, b.Height, pixel);
                    if (Math.Abs(pixel.R - pixel.B) > 2 || pixel.R < 105 || pixel.R > 140)
                        throw new Exception("El shader no aplicó brillo y blanco y negro.");
                }
                var pcm = new byte[AudioRing.BytesPerSecond];
                for (int i = 0; i < AudioRing.Rate; i++) {
                    short s = (short)(400 * Math.Sin(2 * Math.PI * 440 * i / AudioRing.Rate));
                    pcm[i * 4] = pcm[i * 4 + 2] = (byte)s;
                    pcm[i * 4 + 1] = pcm[i * 4 + 3] = (byte)(s >> 8);
                }
                string video;
                var before = Process.GetProcessesByName("ffmpeg").Select(p => { using (p) return p.Id; }).ToArray();
                using (var wav = AudioFiles.Preview(new AudioClip(pcm, "Prueba"), 0, AudioRing.Rate))
                using (var player = new SoundPlayer(wav))
                using (var audio = new GameAudio((uint)Process.GetCurrentProcess().Id, 2)) {
                    player.Load(); player.PlayLooping();
                    var capture = prepared.Start(audio,look,SceneFiles.Output(folder, "Video", ".mp4"),limit,fps);
                    while (!capture.HasFrames && !capture.Result.IsCompleted) await Task.Delay(50);
                    Console.WriteLine("Primer fotograma conservado: {0:F1} ms", capture.Elapsed.TotalMilliseconds);
                    await Task.Delay(550);
                    foreach (var p in Process.GetProcessesByName("ffmpeg")) using (p) {
                        if (!before.Contains(p.Id) && !p.HasExited)
                            Console.WriteLine("FFmpeg RAM residente: {0:F1} MiB; privada: {1:F1} MiB", p.WorkingSet64 / 1048576.0, p.PrivateMemorySize64 / 1048576.0);
                    }
                    timer.Stop(); // Preserve a static frame during the rest of the recording.
                    await Task.Delay(motion ? 1800 : 1700);
                    capture.Stop();
                    video = await capture.Result;
                    if (!audio.Running || audio.Ring.Snapshot().Length != 2 * AudioRing.BytesPerSecond)
                        throw new Exception("El búfer de clips dejó de recibir audio durante la grabación.");
                    player.Stop();
                }
                await Task.Run(() => CheckRecordedFilter(video, folder));
                string trim = SceneFiles.Output(folder, "Recorte", ".mp4");
                await Task.Run(() => SceneFiles.Trim(video, trim, .25, 1.0));
                Console.WriteLine("Clip sin límite de peso: {0} bytes.",new FileInfo(video).Length);
                var controls = new ControlWindow { ShowInTaskbar = false, ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000 };
                var styles = ((FrameworkElement)controls.Content).Resources;
                var editor = new SceneEditor(scene, styles, video) { ShowInTaskbar = false, ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000 };
                var media = (MediaElement)typeof(SceneEditor).GetField("media", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor);
                var opened = new TaskCompletionSource<bool>();
                media.MediaOpened += delegate { opened.TrySetResult(true); };
                media.MediaFailed += delegate(object sender, ExceptionRoutedEventArgs error) { opened.TrySetException(error.ErrorException); };
                editor.Show();
                try {
                    if (await Task.WhenAny(opened.Task, Task.Delay(15000)) != opened.Task || !await opened.Task)
                        throw new Exception("El editor no abrió la vista previa.");
                    var trimEnd = (Slider)typeof(SceneEditor).GetField("end", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor);
                    var watch = Stopwatch.StartNew();
                    while (!trimEnd.IsEnabled && watch.ElapsedMilliseconds < 5500) await Task.Delay(50);
                    Console.WriteLine("Vista previa WPF; recorte hasta {0:F3} s", trimEnd.Maximum);
                    double actual = SceneFiles.Duration(video, 0);
                    if (!trimEnd.IsEnabled || Math.Abs(trimEnd.Maximum - actual) > .01 || actual < 2.1)
                        throw new Exception("El editor no usó la duración real del archivo o se perdió parte de la escena estática.");
                } finally { editor.Close(); }
                if (args.Length > 1 && args[1] == "--recycle") {
                    string discarded = SceneFiles.Output(folder, "Nitidez-prueba-papelera", ".mp4");
                    File.Copy(trim, discarded);
                    SceneFiles.Recycle(hwnd, discarded);
                    if (!File.Exists(trim) || !File.Exists(video)) throw new Exception("Se alteró otro archivo de la prueba.");
                    Console.WriteLine("RECYCLED=" + discarded);
                }
                Console.WriteLine("VIDEO=" + video);
                Console.WriteLine("TRIM=" + trim);
                result = 0;
                }
            } catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { timer.Stop(); if (animate != null) CompositionTarget.Rendering -= animate; scene.Close(); }
        };
        app.Run(scene);
        return result;
    }
}
