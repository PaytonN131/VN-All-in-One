using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SD = System.Drawing;
using WF = System.Windows.Forms;

static class WindowDisplayProbe {
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);




    static void WaitForOverlay(TextBlock status) {
        var frame = new DispatcherFrame();
        var limit = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += delegate {
            if (status.Text.StartsWith("Filtro aplicado") || limit.ElapsedMilliseconds > 5000) frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        if (!status.Text.StartsWith("Filtro aplicado")) throw new Exception("Estado de la superposición: " + status.Text);
    }

    static void Pump(int ms) {
        var frame = new DispatcherFrame();
        var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        delay.Tick += delegate { delay.Stop(); frame.Continue = false; };
        delay.Start();
        Dispatcher.PushFrame(frame);
    }

    static void VerifyPresentation(ControlWindow controls, IntPtr hwnd, string label) {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(ControlWindow);
        var overlay = (Overlay)type.GetField("overlay", fields).GetValue(controls);
        if (!overlay.DirectVideo || overlay.Image.Source != null ||
            type.GetField("pixels", fields).GetValue(controls) != null)
            throw new Exception("La ruta activa todavía pasa por el búfer de píxeles de la interfaz.");
        var area = SD.Rectangle.Intersect(BorderlessWindow.ClientBounds(hwnd), WF.Screen.FromHandle(hwnd).Bounds);
        int width = (int)type.GetField("bw", fields).GetValue(controls), height = (int)type.GetField("bh", fields).GetValue(controls);
        if (width != area.Width || height != area.Height)
            throw new Exception("No se conservó la resolución nativa.");
        var native = (WindowCapture)type.GetField("gpuCapture", fields).GetValue(controls);
        var raw = new byte[area.Width * area.Height * 4];
        if (!native.ReadCurrent(raw, area.Width, area.Height)) throw new Exception("No hubo fotograma crudo para comparar.");
        int x = area.Width * 9 / 10, y = area.Height * 9 / 10;
        int at = (y * area.Width + x) * 4;
        int before = raw[at + 2];
        if (!Native.SetWindowDisplayAffinity(overlay.Hwnd, 0)) throw new Exception("No se pudo leer la presentación de prueba.");
        try {
            Pump(220);
            Native.DwmFlush();
            using (var pixel = new SD.Bitmap(1, 1))
            using (var g = SD.Graphics.FromImage(pixel)) {
                g.CopyFromScreen(area.X + x, area.Y + y, 0, 0, new SD.Size(1, 1));
                var filtered = pixel.GetPixel(0, 0);
                Console.WriteLine("{0}: {1}x{2}, píxel crudo={3}, presentado={4}", label, width, height, before, filtered.R);
                if (before - filtered.R < 30 || filtered.R < 20) throw new Exception("No se vio el ajuste de brillo en la salida de GPU.");
            }
        } finally { Native.SetWindowDisplayAffinity(overlay.Hwnd, 0x11); }
    }

    static void TestOverlay(Process game, IntPtr hwnd, SD.Rectangle before) {
        if (Native.GetForegroundWindow() != hwnd && !SetForegroundWindow(hwnd))
            throw new Exception("Windows no dio foco a la ventana de prueba.");
        new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var controls = new ControlWindow { ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
        controls.Show();
        try {
            var ui = (FrameworkElement)controls.Content;
            var list = (ComboBox)ui.FindName("Windows");
            var item = new Choice { H = hwnd, Pid = (uint)game.Id, Title = "Ventana de prueba" };
            list.Items.Add(item);
            list.SelectedItem = item;
            var status = (TextBlock)ui.FindName("Status");
            ((ComboBox)ui.FindName("CaptureQuality")).SelectedIndex = 2;
            ((ComboBox)ui.FindName("Fps")).SelectedItem = 60;
            ((Slider)ui.FindName("Brightness")).Value = -.25;
            ((Button)ui.FindName("Start")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForOverlay(status);
            VerifyPresentation(controls, hwnd, "GPU nativa en ventana");

            ((Button)ui.FindName("Borderless")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitForOverlay(status);
            VerifyPresentation(controls, hwnd, "GPU nativa sin bordes");
            var area = BorderlessWindow.ClientBounds(hwnd);
            var point = new Native.POINT { X = area.X + area.Width / 2, Y = area.Y + area.Height / 2 };
            if (Native.GetAncestor(Native.WindowFromPoint(point), 2) != hwnd || Native.GetForegroundWindow() != hwnd)
                throw new Exception("El filtro alteró el destino del ratón o el foco.");
            Console.WriteLine("Superposición activa sin bordes; foco y destino del ratón conservados.");
        } finally { controls.Close(); }
        if (BorderlessWindow.ClientBounds(hwnd) != before)
            throw new Exception("La ventana no recuperó su geometría al cerrar Nitidez.");
        var reopened = new ControlWindow { ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
        reopened.Show();
        try {
            var ui = (FrameworkElement)reopened.Content;
            WaitForOverlay((TextBlock)ui.FindName("Status"));
            var selected = (Choice)((ComboBox)ui.FindName("Windows")).SelectedItem;
            if (selected.H != hwnd || selected.Pid != (uint)game.Id)
                throw new Exception("La selección automática no recuperó la aplicación esperada.");
            Console.WriteLine("La última aplicación se reconoció al reabrir Nitidez.");
        } finally { reopened.Close(); }
    }
    static void Inspect(IntPtr hwnd, string label) {
        var client = BorderlessWindow.ClientBounds(hwnd);
        var area = SD.Rectangle.Intersect(client, WF.Screen.FromHandle(hwnd).Bounds);
        int height = Math.Min(720, area.Height), width = (int)Math.Round(area.Width * (double)height / area.Height);
        var data = new byte[width * height * 4];
        int frames = 0, nonBlack = 0;
        var elapsed = Stopwatch.StartNew();
        double copyMs = 0;
        using (var capture = new WindowCapture(hwnd)) {
            while (elapsed.ElapsedMilliseconds < 1800) {
                long before = Stopwatch.GetTimestamp();
                if (capture.Read(area, data, width, height)) {
                    copyMs += (Stopwatch.GetTimestamp() - before) * 1000.0 / Stopwatch.Frequency;
                    frames++;
                    for (int p = 0; p < data.Length; p += 4 * 97)
                        if (data[p] > 16 || data[p + 1] > 16 || data[p + 2] > 16) nonBlack++;
                }
                Thread.Sleep(33);
            }
        }
        Console.WriteLine("{0}: cliente={1}; visible={2}; búfer={3}x{4}; frames={5}; muestras no negras={6}; lectura media={7:F2} ms",
            label, client, area, width, height, frames, nonBlack, frames == 0 ? 0 : copyMs / frames);
        if (frames == 0 || nonBlack == 0) throw new Exception("La captura no entregó una imagen útil.");
    }

    [STAThread] static int Main(string[] args) {
        try {
            Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
            if (args.Length == 1 && args[0] == "--static") {
                new Application().Run(new Window { Title = "Prueba estática de Nitidez", Width = 900, Height = 620,
                    Background = System.Windows.Media.Brushes.White });
                return 0;
            }

            using (var game = Process.GetProcessById(int.Parse(args[0]))) {
                var hwnd = game.MainWindowHandle;
                if (hwnd == IntPtr.Zero) throw new Exception("No se encontró una ventana principal.");
                var before = BorderlessWindow.ClientBounds(hwnd);
                int style = Native.GetWindowLong(hwnd, -16);
                if (args.Length > 2 && args[2] == "--clip-test") {
                    var screen = WF.Screen.FromHandle(hwnd).Bounds;
                    Native.SetWindowPos(hwnd, IntPtr.Zero, screen.Left - 10, screen.Top + 50,
                        screen.Width + 20, screen.Height + 20, 0x14);
                    Thread.Sleep(300);
                    before = BorderlessWindow.ClientBounds(hwnd);
                    if (screen.Contains(before)) throw new Exception("La prueba no dejó la ventana fuera del monitor.");
                }
                Inspect(hwnd, "Ventana original");
                if (args.Length > 1 && args[1] == "--borderless") {
                    var mode = new BorderlessWindow();
                    try {
                        foreach(int selectedMode in new[]{2,3,1}){
                            mode.Apply(new Choice { H = hwnd, Pid = (uint)game.Id },selectedMode);
                            Thread.Sleep(300);
                            Inspect(hwnd, "Modo " + selectedMode);
                            var area=BorderlessWindow.ClientBounds(hwnd);
                            if(Math.Abs(area.Width/(double)area.Height-before.Width/(double)before.Height)>.005)
                                throw new Exception("El modo alteró la proporción del área cliente.");
                            bool caption=(Native.GetWindowLong(hwnd,-16)&0x00C00000)!=0;
                            if(caption!=(selectedMode==1))throw new Exception("No se aplicaron los bordes esperados.");
                        }
                    } finally { mode.Restore(); }
                    Thread.Sleep(250);
                    var restored = BorderlessWindow.ClientBounds(hwnd);
                    Console.WriteLine("Ventana restaurada: " + restored);
                    if (restored != before || style != Native.GetWindowLong(hwnd, -16))
                        throw new Exception("La posición o el estilo no coincidieron tras restaurar.");
                }
                if (args.Length > 3 && args[3] == "--overlay-test") TestOverlay(game, hwnd, before);
            }
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
