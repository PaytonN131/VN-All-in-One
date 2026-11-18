using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SD = System.Drawing;

static class RecordingOverlayProbe {
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Pump(int ms) {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    static object Field(ControlWindow controls, string name) {
        return typeof(ControlWindow).GetField(name, Fields).GetValue(controls);
    }

    [STAThread] static int Main(string[] args) {
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
        new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        ControlWindow controls = null;
        FastSceneRecording recording = null;
        AudioClips audio = null;
        Overlay overlay = null;
        try {
            using (var game = Process.GetProcessById(int.Parse(args[0]))) {
                IntPtr hwnd = game.MainWindowHandle;
                if (Native.GetForegroundWindow() != hwnd) throw new Exception("La ventana sintética debe estar al frente.");
                controls = new ControlWindow { ShowInTaskbar = false, ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
                controls.Show();
                var ui = (FrameworkElement)controls.Content;
                ((ComboBox)ui.FindName("CaptureQuality")).SelectedIndex = 2;
                ((ComboBox)ui.FindName("Fps")).SelectedItem = 60;
                ((Slider)ui.FindName("Brightness")).Value = -.25;
                var list = (ComboBox)ui.FindName("Windows");
                var choice = new Choice { H = hwnd, Pid = (uint)game.Id, Title = "Prueba de grabación simultánea" };
                list.Items.Add(choice); list.SelectedItem = choice;
                ((Button)ui.FindName("Start")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var status = (TextBlock)ui.FindName("Status");
                var wait = Stopwatch.StartNew();
                while (!status.Text.StartsWith("Filtro aplicado") && wait.ElapsedMilliseconds < 5000) Pump(50);
                if (!status.Text.StartsWith("Filtro aplicado")) throw new Exception(status.Text);
                ((Button)ui.FindName("Borderless")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump(400);
                overlay = (Overlay)Field(controls, "overlay");
                var capture = Field(controls, "gpuCapture");
                var bounds = BorderlessWindow.ClientBounds(hwnd);
                if (!Native.SetWindowDisplayAffinity(overlay.Hwnd, 0)) throw new Exception("No se pudo medir la imagen visible.");
                audio = (AudioClips)Field(controls, "audioClips");
                string file = SceneFiles.Output(Path.GetFullPath(args[1]), "Simultaneo", ".mp4");
                var scenes = (SceneControls)Field(controls,"sceneControls");
                var prepared=(PreparedScene)typeof(SceneControls).GetField("prepared",Fields).GetValue(scenes);
                var preparation=Stopwatch.StartNew();
                while(!prepared.Ready&&preparation.ElapsedMilliseconds<5000)Pump(20);
                recording = prepared.Start(audio.AcquireVideoAudio(),new SceneLook(.7, -.25, 1, 1, 0, 1),file,1080,60);
                int samples = 0, changes = 0;
                long maxFrozen = 0, lastChanged = 0;
                ulong lastHash = 0;
                var clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < 12500) {
                    Pump(100);
                    if (clock.ElapsedMilliseconds > 9000) recording.Stop();
                    if (recording.Result.IsFaulted) recording.Result.GetAwaiter().GetResult();
                    if (!Object.ReferenceEquals(capture, Field(controls, "gpuCapture")) || !overlay.Visible ||
                        (int)Field(controls, "captureFailures") != 0)
                        throw new Exception("El filtro se reinició o desapareció durante la grabación.");
                    using (var bitmap = new SD.Bitmap(600, 100))
                    using (var graphics = SD.Graphics.FromImage(bitmap)) {
                        graphics.CopyFromScreen(bounds.X + 35, bounds.Y + 340, 0, 0, bitmap.Size);
                        ulong hash = 1469598103934665603UL;
                        for (int y = 0; y < bitmap.Height; y += 2)
                            for (int x = 0; x < bitmap.Width; x += 2)
                                hash = unchecked((hash ^ (uint)bitmap.GetPixel(x,y).ToArgb()) * 1099511628211UL);
                        if (hash != lastHash) { changes++; lastHash = hash; lastChanged = clock.ElapsedMilliseconds; }
                        else maxFrozen = Math.Max(maxFrozen, clock.ElapsedMilliseconds - lastChanged);
                    }
                    using (var pixel = new SD.Bitmap(1, 1))
                    using (var graphics = SD.Graphics.FromImage(pixel)) {
                        graphics.CopyFromScreen(bounds.X + bounds.Width * 9 / 10, bounds.Y + bounds.Height * 9 / 10,
                            0, 0, pixel.Size);
                        int red = pixel.GetPixel(0,0).R;
                        if (red < 175 || red > 205) throw new Exception("El filtro desapareció o mostró negro, rojo=" + red);
                    }
                    samples++;
                }
                recording.Stop();
                while (!recording.Result.IsCompleted) Pump(50);
                string video = recording.Result.GetAwaiter().GetResult();
                bool staticScene = args.Length > 2 && args[2] == "--static";
                if (!staticScene && (changes < 15 || maxFrozen > 1200)) throw new Exception("La salida quedó congelada durante la prueba.");
                if (Native.GetForegroundWindow() != hwnd) throw new Exception("Se perdió el foco de la ventana de prueba.");
                Console.WriteLine("PASS {0} muestras con filtro, {1} cambios visibles, pausa máxima {2} ms, 0 reinicios.", samples, changes, maxFrozen);
                Console.WriteLine("VIDEO=" + video);
                return 0;
            }
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally {
            if (recording != null && !recording.Result.IsCompleted) {
                recording.Stop();
                var wait = Stopwatch.StartNew();
                while (!recording.Result.IsCompleted && wait.ElapsedMilliseconds < 14000) Pump(50);
            }
            if (overlay != null) Native.SetWindowDisplayAffinity(overlay.Hwnd, 0x11);
            if (audio != null) audio.ReleaseVideoAudio();
            if (controls != null) controls.Close();
        }
    }
}
