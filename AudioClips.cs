using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

sealed class AudioClip {
    public readonly byte[] Pcm;
    public readonly DateTime Time = DateTime.Now;
    public readonly string Game;
    public int Start, End;
    public AudioClip(byte[] pcm, string game) {
        Pcm = pcm; Game = game; End = pcm.Length / AudioRing.FrameBytes;
    }
    public override string ToString() {
        return String.Format("{0:HH:mm:ss.fff} · {1:F2} s · {2}", Time, (double)Pcm.Length / AudioRing.BytesPerSecond, Game);
    }
}

static class AudioFiles {
    public static readonly string Folder = Directory.Exists(@"D:\")
        ? @"D:\Archivos VN All in One"
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Archivos VN All in One");

    public static MemoryStream Preview(AudioClip clip, int start, int end) {
        int length = (end - start) * AudioRing.FrameBytes;
        var stream = new MemoryStream(length + 44);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) {
            writer.Write(0x46464952); writer.Write(length + 36);
            writer.Write(0x45564157); writer.Write(0x20746D66); writer.Write(16);
            writer.Write((short)1); writer.Write((short)2); writer.Write(AudioRing.Rate);
            writer.Write(AudioRing.BytesPerSecond); writer.Write((short)4); writer.Write((short)16);
            writer.Write(0x61746164); writer.Write(length);
            writer.Write(clip.Pcm, start * AudioRing.FrameBytes, length);
        }
        stream.Position = 0;
        return stream;
    }

    public static string FindFfmpeg() {
        string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(local)) return local;
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')) {
            if (String.IsNullOrWhiteSpace(directory)) continue;
            string exe = Path.Combine(directory.Trim().Trim('"'), "ffmpeg.exe");
            if (File.Exists(exe)) return exe;
        }
        string packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(packages)) {
            foreach (string package in Directory.EnumerateDirectories(packages, "*FFmpeg*"))
                foreach (string exe in Directory.EnumerateFiles(package, "ffmpeg.exe", SearchOption.AllDirectories))
                    return exe;
        }
        throw new FileNotFoundException("No se encontró FFmpeg. Coloca ffmpeg.exe junto a NitidezVN.exe o añádelo al PATH.");
    }

    public static void Export(AudioClip clip, int start, int end, string file) {
        if (start < 0 || end <= start || end > clip.Pcm.Length / AudioRing.FrameBytes)
            throw new ArgumentOutOfRangeException("start", "El tramo de audio no es válido.");
        string temp = Path.Combine(Path.GetDirectoryName(file), ".nitidez-" + Guid.NewGuid().ToString("N") + ".part");
        try {
            var info = new ProcessStartInfo(FindFfmpeg(),
                "-hide_banner -loglevel error -n -f s16le -ar 44100 -ac 2 -i pipe:0 -c:a libmp3lame -b:a 192k -f mp3 \"" + temp + "\"") {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true
            };
            using (var encoder = Process.Start(info)) {
                var errors = encoder.StandardError.ReadToEndAsync();
                var input = encoder.StandardInput.BaseStream;
                var write = input.WriteAsync(clip.Pcm, start * AudioRing.FrameBytes, (end - start) * AudioRing.FrameBytes);
                try {
                    if (!write.Wait(20000)) throw new TimeoutException("FFmpeg no recibió el audio a tiempo.");
                    encoder.StandardInput.Close();
                    if (!encoder.WaitForExit(20000)) throw new TimeoutException("FFmpeg tardó demasiado en exportar.");
                    if (encoder.ExitCode != 0) throw new IOException("FFmpeg: " + errors.Result.Trim());
                } finally {
                    if (!encoder.HasExited) { encoder.Kill(); encoder.WaitForExit(3000); }
                }
            }
            File.Move(temp, file);
        } finally {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}

sealed class AudioWave : FrameworkElement {
    const int Columns = 640;
    const double Inset = 14, HandleSize = 9;
    readonly short[] min = new short[Columns], max = new short[Columns];
    AudioClip clip;
    int? playhead;
    bool dragStart;
    double grabOffset;
    public event Action<bool, double> TrimRequested;
    static readonly Brush Back = new SolidColorBrush(Color.FromRgb(44,37,50));
    static readonly Brush Selected = new SolidColorBrush(Color.FromRgb(91,62,86));
    static readonly Pen Active = new Pen(new SolidColorBrush(Color.FromRgb(232,183,211)), 1);
    static readonly Pen Muted = new Pen(new SolidColorBrush(Color.FromRgb(101, 95, 125)), 1);
    static readonly Pen StartMarker = new Pen(new SolidColorBrush(Color.FromRgb(145, 231, 202)), 2);
    static readonly Pen EndMarker = new Pen(new SolidColorBrush(Color.FromRgb(247, 193, 143)), 2);
    static readonly Pen PlaybackMarker = new Pen(Brushes.White, 2);

    double TrackWidth { get { return Math.Max(0, ActualWidth - 2 * Inset); } }
    int Frames { get { return clip.Pcm.Length / AudioRing.FrameBytes; } }
    double XForFrame(int frame) { return Inset + TrackWidth * frame / Frames; }

    public void SetClip(AudioClip value) {
        if (IsMouseCaptured) ReleaseMouseCapture();
        clip = value;
        playhead = null;
        if (clip != null) {
            int frames = clip.Pcm.Length / AudioRing.FrameBytes;
            for (int column = 0; column < Columns; column++) {
                short low = 0, high = 0;
                int first = column * frames / Columns;
                int end = Math.Min(frames, Math.Max(first + 1, (column + 1) * frames / Columns));
                for (int i = first * 4; i < end * 4; i += 2) {
                    short sample = BitConverter.ToInt16(clip.Pcm, i);
                    low = Math.Min(low, sample); high = Math.Max(high, sample);
                }
                min[column] = low; max[column] = high;
            }
        }
        InvalidateVisual();
    }

    public void SetPlayhead(int? frame) {
        if (playhead == frame) return;
        playhead = frame;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) {
        base.OnMouseLeftButtonDown(e);
        if (clip == null || TrackWidth <= 0 || !CaptureMouse()) return;
        Point p = e.GetPosition(this);
        double toStart = Math.Abs(p.X - XForFrame(clip.Start));
        double toEnd = Math.Abs(p.X - XForFrame(clip.End));
        // Use the top and bottom handles to disambiguate a very narrow selection.
        dragStart = toStart <= HandleSize && toEnd <= HandleSize ? p.Y < ActualHeight / 2 : toStart <= toEnd;
        grabOffset = (dragStart ? toStart : toEnd) <= HandleSize ? p.X - XForFrame(dragStart ? clip.Start : clip.End) : 0;
        MoveBoundary(p.X - grabOffset);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        if (!IsMouseCaptured) return;
        if (e.LeftButton == MouseButtonState.Pressed) MoveBoundary(e.GetPosition(this).X - grabOffset);
        else ReleaseMouseCapture();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) {
        base.OnMouseLeftButtonUp(e);
        if (!IsMouseCaptured) return;
        MoveBoundary(e.GetPosition(this).X - grabOffset);
        ReleaseMouseCapture();
        e.Handled = true;
    }

    void MoveBoundary(double x) {
        if (clip == null || TrackWidth <= 0 || TrimRequested == null) return;
        double fraction = Math.Max(0, Math.Min(1, (x - Inset) / TrackWidth));
        TrimRequested(dragStart, fraction * Frames / AudioRing.Rate);
    }

    protected override void OnRender(DrawingContext dc) {
        double width = ActualWidth, height = ActualHeight, center = height / 2;
        dc.DrawRectangle(Back, null, new Rect(0, 0, width, height));
        if (clip == null || TrackWidth <= 0) return;
        double left = XForFrame(clip.Start), right = XForFrame(clip.End);
        dc.DrawRectangle(Selected, null, new Rect(left, 0, right - left, height));
        dc.DrawLine(Muted, new Point(Inset, center), new Point(width - Inset, center));
        for (int i = 0; i < Columns; i++) {
            double x = Inset + TrackWidth * (i + 0.5) / Columns;
            var pen = x >= left && x <= right ? Active : Muted;
            dc.DrawLine(pen, new Point(x, center - max[i] * center / 32768),
                new Point(x, center - min[i] * center / 32768));
        }
        dc.DrawLine(StartMarker, new Point(left, 0), new Point(left, height));
        dc.DrawLine(EndMarker, new Point(right, 0), new Point(right, height));
        dc.DrawRoundedRectangle(StartMarker.Brush, null, new Rect(left - HandleSize, 2, 2 * HandleSize, 16), 3, 3);
        dc.DrawRoundedRectangle(EndMarker.Brush, null, new Rect(right - HandleSize, height - 18, 2 * HandleSize, 16), 3, 3);
        if (playhead.HasValue) {
            double x = XForFrame(playhead.Value);
            dc.DrawLine(PlaybackMarker, new Point(x, 18), new Point(x, height - 18));
            dc.DrawEllipse(Brushes.White, null, new Point(x, 18), 3, 3);
        }
    }
}

sealed class ClipEditor : Window {
    readonly ObservableCollection<AudioClip> clips;
    readonly ComboBox list = new ComboBox();
    readonly Slider start = new Slider(), end = new Slider();
    readonly TextBlock startText = new TextBlock(), endText = new TextBlock(), status = new TextBlock { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock playbackTime = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 6) };
    readonly AudioWave wave = new AudioWave { Height = 142, Margin = new Thickness(0, 6, 0, 12), Cursor = Cursors.SizeWE };
    readonly Button play = new Button { Content = "▶", ToolTip = "Reproducir tramo seleccionado", FontSize = 18, Width = 50 },
        stop = new Button { Content = "■", ToolTip = "Detener reproducción", FontSize = 18, Width = 50 },
        save = new Button { Content = "Guardar MP3" }, discard = new Button { Content = "Descartar clip" };
    SoundPlayer player;
    MemoryStream playback;
    readonly Stopwatch playbackClock = new Stopwatch();
    readonly DispatcherTimer playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
    bool updating;
    public bool Exporting { get; private set; }
    AudioClip Current { get { return list.SelectedItem as AudioClip; } }
    public string LastSaved { get; private set; }

    public ClipEditor(Window owner, ResourceDictionary style, ObservableCollection<AudioClip> clips) {
        Owner = owner; this.clips = clips;
        Resources.MergedDictionaries.Add(style);
        Title = "Nitidez VN · Clips de audio"; Width = 740; MinWidth = 610; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.CanResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(33,29,38)); Foreground = Brushes.White;
        var root = new StackPanel { Margin = new Thickness(22) };
        Content = root;
        root.Children.Add(new TextBlock { Text = "Recorta tu clip", FontSize = 24, Margin = new Thickness(0,0,0,12) });
        list.ItemsSource = clips; root.Children.Add(list);
        root.Children.Add(new TextBlock { Text = "Arrastra las marcas de inicio y final sobre la onda.", Margin = new Thickness(0,12,0,0) });
        root.Children.Add(wave);
        root.Children.Add(startText); root.Children.Add(start);
        root.Children.Add(endText); root.Children.Add(end);
        var actions = new WrapPanel { Margin = new Thickness(0,10,0,10) };
        foreach (var button in new[] { play, stop, save, discard }) {
            button.Margin = new Thickness(0,0,8,6);
            actions.Children.Add(button);
        }
        actions.Children.Add(playbackTime);
        root.Children.Add(actions); root.Children.Add(status);
        System.Windows.Automation.AutomationProperties.SetName(play, "Reproducir tramo seleccionado");
        System.Windows.Automation.AutomationProperties.SetName(stop, "Detener reproducción");
        list.SelectionChanged += delegate { SelectClip(); };
        start.ValueChanged += delegate { UpdateTrim(true); };
        end.ValueChanged += delegate { UpdateTrim(false); };
        wave.TrimRequested += delegate(bool movedStart, double seconds) { (movedStart ? start : end).Value = seconds; };
        play.Click += delegate { Listen(); };
        stop.Click += delegate { Stop(); };
        playbackTimer.Tick += delegate { UpdatePlayhead(); };
        save.Click += async delegate { await Save(); };
        discard.Click += delegate { var clip = Current; if (clip != null) clips.Remove(clip); SelectLast(); };
        Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (Exporting) e.Cancel = true; };
        Closed += delegate { Stop(); };
        Deactivated += delegate { Stop(); };
        SelectLast();
    }

    public void SelectLast() {
        list.SelectedIndex = clips.Count - 1;
        SelectClip();
    }

    void SelectClip() {
        Stop();
        var clip = Current;
        updating = true;
        if (clip != null) {
            start.Maximum = end.Maximum = (double)clip.Pcm.Length / AudioRing.BytesPerSecond;
            start.Value = (double)clip.Start / AudioRing.Rate; end.Value = (double)clip.End / AudioRing.Rate;
            start.SmallChange = end.SmallChange = .01; start.LargeChange = end.LargeChange = .25;
        }
        updating = false;
        wave.SetClip(clip);
        UpdateControls();
    }

    void UpdateControls() {
        var clip = Current;
        start.IsEnabled = end.IsEnabled = play.IsEnabled = save.IsEnabled = discard.IsEnabled = clip != null && !Exporting;
        wave.IsEnabled = start.IsEnabled;
        stop.IsEnabled = player != null;
        list.IsEnabled = !Exporting;
        startText.Text = clip == null ? "Inicio" : String.Format("Inicio   {0:F2} s", start.Value);
        endText.Text = clip == null ? "Final" : String.Format("Final   {0:F2} s     ·     Selección {1:F2} s", end.Value, end.Value-start.Value);
        if (clip == null && String.IsNullOrEmpty(status.Text)) status.Text = "Captura un clip mientras juegas.";
    }

    void UpdateTrim(bool movedStart) {
        var clip = Current;
        if (updating || clip == null) return;
        Stop();
        int first = (int)Math.Round(start.Value * AudioRing.Rate), last = (int)Math.Round(end.Value * AudioRing.Rate);
        int minFrames = Math.Min(AudioRing.Rate / 50, clip.Pcm.Length / AudioRing.FrameBytes);
        if (movedStart) first = Math.Min(first, last - minFrames);
        else last = Math.Max(last, first + minFrames);
        clip.Start = first; clip.End = last;
        updating = true;
        start.Value = (double)first / AudioRing.Rate; end.Value = (double)last / AudioRing.Rate;
        updating = false;
        wave.InvalidateVisual();
        UpdateControls();
    }

    void Listen() {
        Stop();
        var clip = Current;
        if (clip == null) return;
        try {
            playback = AudioFiles.Preview(clip, clip.Start, clip.End);
            player = new SoundPlayer(playback);
            player.Load();
            player.Play();
            playbackClock.Restart();
            stop.IsEnabled = true;
            playbackTimer.Start();
            UpdatePlayhead();
        } catch (Exception ex) { Stop(); status.Text = "No se pudo reproducir: " + ex.Message; }
    }

    void UpdatePlayhead() {
        var clip = Current;
        if (player == null || clip == null) { Stop(); return; }
        int frame = Math.Min(clip.End, clip.Start + (int)(playbackClock.Elapsed.TotalSeconds * AudioRing.Rate));
        wave.SetPlayhead(frame);
        playbackTime.Text = String.Format("{0:F2} s", (double)frame / AudioRing.Rate);
        if (frame >= clip.End) ReleasePlayback();
    }

    void ReleasePlayback() {
        playbackTimer.Stop();
        playbackClock.Reset();
        if (player != null) { player.Stop(); player.Dispose(); player = null; }
        if (playback != null) { playback.Dispose(); playback = null; }
        stop.IsEnabled = false;
    }

    void Stop() {
        ReleasePlayback();
        wave.SetPlayhead(null);
        playbackTime.Text = "";
    }

    async Task Save() {
        var clip = Current;
        if (clip == null || Exporting) return;
        Stop();
        try {
            string folder=CaptureFiles.Folder("Grabaciones",clip.Game);
            var dialog = new Microsoft.Win32.SaveFileDialog {
                Title = "Guardar tramo de audio", Filter = "Audio MP3 (*.mp3)|*.mp3",
                DefaultExt = ".mp3", AddExtension = true, InitialDirectory = folder,
                FileName = CaptureFiles.Name(clip.Game)+"_" + clip.Time.ToString("yyyy-MM-dd_HH-mm-ss-fff") + ".mp3"
            };
            if (dialog.ShowDialog(this) != true) return;
            if (File.Exists(dialog.FileName)) throw new IOException("Ese archivo ya existe. Elige un nombre nuevo.");
            int first = clip.Start, last = clip.End;
            Exporting = true; UpdateControls(); status.Text = "Exportando MP3…";
            await Task.Run(() => AudioFiles.Export(clip, first, last, dialog.FileName));
            LastSaved = dialog.FileName;
            status.Text = "Guardado en " + LastSaved;
            clips.Remove(clip);
            SelectLast();
        } catch (Exception ex) { status.Text = "No se pudo guardar: " + ex.GetBaseException().Message; }
        finally { Exporting = false; UpdateControls(); }
    }
}

sealed class AudioClips : IDisposable {
    const int Limit = 5, HotkeyId = 3;
    readonly Window owner;
    readonly FrameworkElement ui;
    readonly TextBlock status;
    readonly ComboBox length;
    readonly ShortcutBinding key;
    readonly CheckBox enabled;
    readonly Button edit;
    readonly DispatcherTimer timer;
    readonly ObservableCollection<AudioClip> clips = new ObservableCollection<AudioClip>();
    readonly string[] keys = { ".", "Punto del teclado numérico", "F2", "F3", "F4", "F6", "F7", "F9", "F11", "F12" };
    readonly uint[] virtualKeys = { 0xBE, 0x6E, 0x71, 0x72, 0x73, 0x75, 0x76, 0x78, 0x7A, 0x7B };
    Choice target;
    GameAudio audio;
    GameAudio videoAudio;
    ClipEditor editor;
    IntPtr hwnd;
    bool registered, hotkeyAttempted;
    string notice;

    public AudioClips(Window owner, FrameworkElement ui, ShortcutCatalog shortcuts) {
        this.owner = owner; this.ui = ui;
        length = (ComboBox)ui.FindName("AudioLength");
        uint[] symbol=ShortcutBinding.Symbol('.');
        key=shortcuts.Add(ui,"AudioKey","Capturar audio",symbol[0],symbol[1],".");
        enabled = (CheckBox)ui.FindName("AudioEnabled"); status = (TextBlock)ui.FindName("AudioStatus");
        edit = (Button)ui.FindName("AudioEdit");
        for (int i = 1; i <= 30; i++) length.Items.Add(i);
        length.SelectedItem = 8;
        length.SelectionChanged += delegate { if (audio != null && audio.Ring != null) audio.Ring.Resize((int)length.SelectedItem); };
        key.Changed += delegate { Unregister(); notice = null; };
        enabled.Checked += delegate { Start(); };
        enabled.Unchecked += delegate {
            if (audio != null && audio == videoAudio) { audio.Ring = null; Unregister(); }
            else StopCapture();
        };
        edit.Click += delegate { OpenEditor(); };
        ((Button)ui.FindName("AudioRename")).Click+=delegate{
            try{string renamed=CaptureFiles.RenameDialog(owner,ui.Resources,"Grabaciones");if(renamed!=null)notice="Audio renombrado: "+renamed;}
            catch(Exception ex){notice="No se pudo renombrar: "+ex.Message;}UpdateStatus();
        };
        clips.CollectionChanged += delegate { UpdateStatus(); };
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += delegate { UpdateStatus(); };
        UpdateStatus();
    }

    public void Initialize(IntPtr handle) { hwnd = handle; timer.Start(); }
    public void Load(System.Xml.XmlElement node) {
        int seconds;
        if (int.TryParse(node.GetAttribute("audioSegundos"), out seconds) && length.Items.Contains(seconds)) length.SelectedItem = seconds;
        string selected = node.GetAttribute("audioTecla");
        int legacy=Array.IndexOf(keys,selected);
        if(!node.HasAttribute("atajoAudio")&&legacy>=0)key.Legacy(virtualKeys[legacy],0,selected);
        key.Load(node,"atajoAudio");
        enabled.IsChecked = node.GetAttribute("audioActivo") != "false";
    }
    public void Save(System.Xml.XmlWriter writer) {
        writer.WriteAttributeString("audioSegundos", length.SelectedItem.ToString());
        key.Save(writer,"atajoAudio");
        writer.WriteAttributeString("audioActivo", enabled.IsChecked == true ? "true" : "false");
    }
    public void Select(Choice game) {
        if (target != null && target.H == game.H && target.Pid == game.Pid && audio != null && audio.Error == null) return;
        StopCapture(); target = game; notice = null; Start();
    }
    void Start() {
        if (target == null || enabled.IsChecked != true) return;
        if (audio == null) audio = new GameAudio(target.Pid, (int)length.SelectedItem);
        else if (audio.Ring == null) audio.Ring = new AudioRing((int)length.SelectedItem);
    }
    public GameAudio AcquireVideoAudio() {
        if (audio != null && audio.Error != null) StopCapture();
        if (audio == null) audio = new GameAudio(target.Pid, enabled.IsChecked == true ? (int)length.SelectedItem : 0);
        return videoAudio = audio;
    }
    public void ReleaseVideoAudio() {
        var session = videoAudio;
        videoAudio = null;
        if (session == null) return;
        if (session != audio) session.Dispose();
        else if (enabled.IsChecked != true) StopCapture();
    }
    void StopCapture() {
        Unregister();
        if (audio != null && audio != videoAudio) audio.Dispose();
        audio = null;
    }
    void Unregister() {
        if (registered) Native.UnregisterHotKey(hwnd, HotkeyId);
        registered = hotkeyAttempted = false;
    }
    void UpdateStatus() {
        if (target != null) {
            uint pid;
            Native.GetWindowThreadProcessId(target.H, out pid);
            if (pid != target.Pid || !Native.IsWindow(target.H)) { StopCapture(); target = null; notice = "La ventana del juego se cerró."; }
        }
        bool focused = target != null && Native.GetForegroundWindow() == target.H;
        bool ready = enabled.IsChecked == true && audio != null && audio.Running;
        if (!focused || !ready) Unregister();
        else if (!hotkeyAttempted && hwnd != IntPtr.Zero) {
            hotkeyAttempted = true;
            registered = key.Register(hwnd,HotkeyId);
        }
        string message;
        if (enabled.IsChecked != true) message = "Captura de audio desactivada.";
        else if (audio == null) message = "Selecciona la ventana de tu novela.";
        else if (audio.Error != null) message = "No se pudo capturar audio de esta aplicación: " + audio.Error;
        else if (!ready) message = "Iniciando audio…";
        else if (focused && !registered) message = "El atajo está ocupado. Elige otra tecla.";
        else message = String.Format("Audio listo · últimos {0} s · pulsa {1} con el juego al frente.", length.SelectedItem, key.Label);
        ((TextBlock)ui.FindName("AudioShortcutHint")).Text="Tecla de audio: "+key.Label+" · Cámbiala en Teclas";
        string caption = String.Format("Editar clips ({0}/{1})", clips.Count, Limit);
        if (!Equals(edit.Content, caption)) edit.Content = caption;
        edit.IsEnabled = clips.Count != 0;
        string combined = notice == null ? message : message + "\n" + notice;
        if (status.Text != combined) status.Text = combined;
    }

    public void CaptureClip() {
        if (audio == null || audio.Ring == null || !audio.Running || target == null || Native.GetForegroundWindow() != target.H) return;
        if (clips.Count >= Limit) notice = "Hay 5 clips pendientes. Guarda o descarta uno para seguir capturando.";
        else {
            byte[] samples = audio.Ring.Snapshot();
            if (samples.Length < AudioRing.BytesPerSecond / 50) notice = "Aún no hay audio disponible.";
            else {
                clips.Add(new AudioClip(samples, CaptureFiles.Game(target)));
                notice = String.Format("Clip capturado · {0:F2} s · abre Editar clips al terminar.", (double)samples.Length / AudioRing.BytesPerSecond);
            }
        }
        UpdateStatus();
    }

    void OpenEditor() {
        if (editor != null) { editor.Activate(); return; }
        editor = new ClipEditor(owner, ui.Resources, clips);
        editor.Closed += delegate {
            if (editor.LastSaved != null) notice = "Guardado en " + editor.LastSaved;
            editor = null;
        };
        editor.Show();
    }

    public bool CanClose() {
        if (editor != null && editor.Exporting) {
            MessageBox.Show(owner, "Espera a que termine de guardarse el MP3.", "Nitidez VN");
            return false;
        }
        return clips.Count == 0 || MessageBox.Show(owner, "¿Cerrar y descartar los clips pendientes de guardar?",
            "Nitidez VN", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    }
    public void Dispose() { timer.Stop(); StopCapture(); if (editor != null) editor.Close(); }
}
