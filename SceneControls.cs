using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

sealed class SceneControls : IDisposable {
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern short VkKeyScanEx(char ch, IntPtr layout);
    readonly Window owner;
    readonly FrameworkElement ui;
    readonly Func<SceneLook> look;
    readonly AudioClips audioClips;
    readonly TextBlock status, keys;
    readonly ComboBox resolution, videoFps;
    readonly ComboBoxItem automaticResolution=new ComboBoxItem{Content="Del juego · automática"};
    readonly Button record, screenshot, edit;
    readonly DispatcherTimer timer;
    readonly string[] keyLabels = { "{", "}", "<" };
    readonly bool[] registered = new bool[3];
    readonly ShortcutBinding[] shortcuts=new ShortcutBinding[3];
    Choice target;
    IntPtr hwnd, currentLayout;
    bool attempted, capturing, readyCuePlayed;
    FastSceneRecording recording;
    PreparedScene prepared;
    SceneEditor editor;
    string lastVideo, notice;

    public SceneControls(Window owner, FrameworkElement ui, Func<SceneLook> look, AudioClips audioClips, ShortcutCatalog catalog) {
        this.owner = owner; this.ui = ui; this.look = look;
        this.audioClips = audioClips;
        status = (TextBlock)ui.FindName("SceneStatus"); keys = (TextBlock)ui.FindName("SceneKeys");
        resolution = (ComboBox)ui.FindName("SceneResolution");
        resolution.Items.Add(automaticResolution);resolution.Items.Add("1080p"); resolution.Items.Add("720p"); resolution.SelectedIndex = 0;
        videoFps = (ComboBox)ui.FindName("SceneFps");
        videoFps.Items.Add("60 FPS"); videoFps.Items.Add("30 FPS"); videoFps.SelectedIndex = 0;
        record = (Button)ui.FindName("SceneRecord"); screenshot = (Button)ui.FindName("SceneScreenshot"); edit = (Button)ui.FindName("SceneEdit");
        record.Click += delegate { if (recording != null) Stop(); else Start(); };
        screenshot.Click += delegate { Screenshot(); };
        edit.Click += delegate { Edit(); };
        ((Button)ui.FindName("VideoFolder")).Click+=delegate{try{CaptureFiles.OpenFolder("Clips",target);}catch(Exception ex){notice="No se pudo abrir la carpeta: "+ex.Message;}Refresh();};
        ((Button)ui.FindName("ScreenshotFolder")).Click+=delegate{try{CaptureFiles.OpenFolder("Capturas",target);}catch(Exception ex){notice="No se pudo abrir la carpeta: "+ex.Message;}Refresh();};
        ((Button)ui.FindName("SceneRename")).Click+=delegate{
            try{string renamed=CaptureFiles.RenameDialog(owner,ui.Resources,"Clips");if(renamed!=null){lastVideo=renamed;notice="Clip renombrado: "+renamed;}}
            catch(Exception ex){notice="No se pudo renombrar: "+ex.Message;}Refresh();
        };
        string[] controls={"VideoStartKey","VideoStopKey","ScreenshotKey"};
        string[] actions={"Iniciar clip","Detener clip","Capturar PNG"};
        string symbols="{}<";
        for(int i=0;i<3;i++){
            uint[] symbol=ShortcutBinding.Symbol(symbols[i]);
            shortcuts[i]=catalog.Add(ui,controls[i],actions[i],symbol[0],symbol[1],symbols[i].ToString());
            shortcuts[i].Changed+=delegate{Unregister();UpdateKeyLabels();};
        }
        UpdateKeyLabels();
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += delegate { Refresh(); };
        Refresh();
    }

    public void Initialize(IntPtr handle) { hwnd = handle; timer.Start(); }
    public void Load(System.Xml.XmlElement node) {
        for(int i=0;i<3;i++)shortcuts[i].Load(node,"atajoEscena"+i);
        UpdateKeyLabels();
        string selected=node.GetAttribute("clipsResolucion45");
        resolution.SelectedIndex=selected=="720p"?2:selected=="1080p"?1:0;
        videoFps.SelectedIndex = node.GetAttribute("clipsFps") == "30" ? 1 : 0;
    }
    public void Save(System.Xml.XmlWriter writer) {
        for(int i=0;i<3;i++)shortcuts[i].Save(writer,"atajoEscena"+i);
        writer.WriteAttributeString("clipsResolucion45",resolution.SelectedIndex==0?"automatica":resolution.SelectedIndex==1?"1080p":"720p");
        writer.WriteAttributeString("clipsFps", videoFps.SelectedIndex == 1 ? "30" : "60");
    }

    public void Select(Choice game) {
        if (target != null && target.H == game.H && target.Pid == game.Pid) return;
        if (recording != null) Stop();
        if(prepared!=null)prepared.Dispose();
        target = game; Unregister(); notice = null;
        prepared=new PreparedScene(game);
        Refresh();
    }

    bool Valid() {
        if (target == null || !Native.IsWindow(target.H)) return false;
        uint pid;
        Native.GetWindowThreadProcessId(target.H, out pid);
        return pid == target.Pid && !Native.IsIconic(target.H);
    }

    void Refresh() {
        bool valid = Valid();
        if(valid){try{var bounds=BorderlessWindow.ClientBounds(target.H);valid=bounds.Width>1&&bounds.Height>1;string label="Del juego · "+bounds.Width+" × "+bounds.Height;if(!Equals(automaticResolution.Content,label))automaticResolution.Content=label;}catch(InvalidOperationException){valid=false;}}
        if (!valid && recording != null) Stop();
        if (valid && Native.GetForegroundWindow() == target.H && hwnd != IntPtr.Zero) {
            uint pid;
            uint thread = Native.GetWindowThreadProcessId(target.H, out pid);
            IntPtr layout = GetKeyboardLayout(thread);
            if (!attempted || currentLayout != layout) Register(layout);
        } else if (attempted) Unregister();

        record.IsEnabled = recording == null ? valid&&prepared!=null&&prepared.Ready&&prepared.Error==null : !recording.Stopping;
        resolution.IsEnabled = recording == null;
        videoFps.IsEnabled = recording == null;
        screenshot.IsEnabled = valid && !capturing&&prepared!=null&&prepared.Ready;
        edit.IsEnabled = recording == null;
        record.Content = recording == null ? "Grabar MP4 con filtro" : recording.Stopping ? "Finalizando…" : "Detener clip";
        if (recording != null && !recording.Stopping && !readyCuePlayed) {
            readyCuePlayed = true;
            System.Media.SystemSounds.Asterisk.Play();
        }
        string state = recording == null ? "" : recording.Stopping ? "Finalizando MP4… " :
            "● Grabando · " + recording.Width+" × "+recording.Height+" · "+recording.RecordedElapsed.ToString(@"mm\:ss") + "  ";
        if (capturing) state += "Capturando PNG… ";
        string message = state + (notice ?? (valid ? prepared!=null&&prepared.Error!=null?prepared.Error:
            prepared!=null&&prepared.Ready?"Captura lista · Se prioriza calidad, sin límite de peso.":"Preparando captura…" : "Selecciona la ventana del juego."));
        if (status.Text != message) status.Text = message;
        ((TextBlock)ui.FindName("ScreenshotStatus")).Text=message;
    }

    void Register(IntPtr layout) {
        Unregister();
        attempted = true; currentLayout = layout;
        for (int i = 0; i < shortcuts.Length; i++) {
            registered[i]=shortcuts[i].Register(hwnd,4+i);
        }
        UpdateKeyLabels();
    }

    void UpdateKeyLabels(){
        for(int i=0;i<3;i++)keyLabels[i]=shortcuts[i].Label+(attempted&&!registered[i]?" (ocupada; cambia en Teclas)":"");
        keys.Text="Grabar: "+keyLabels[0]+"\nDetener: "+keyLabels[1];
        ((TextBlock)ui.FindName("ScreenshotKeys")).Text="Captura PNG: "+keyLabels[2]+" · Cámbiala en Teclas";
    }

    void Unregister() {
        for (int i = 0; i < registered.Length; i++) {
            if (registered[i]) Native.UnregisterHotKey(hwnd, 4 + i);
            registered[i] = false;
        }
        attempted = false;
    }

    public void Hotkey(int id) {
        if (!Valid() || Native.GetForegroundWindow() != target.H) return;
        if (id == 4) Start();
        else if (id == 5) Stop();
        else if (id == 6) Screenshot();
    }

    async void Start() {
        if (recording != null || !Valid()||prepared==null||!prepared.Ready) return;
        try {
            int limit = resolution.SelectedIndex==0?0:resolution.SelectedIndex==2?720:1080;
            int fps = videoFps.SelectedIndex == 1 ? 30 : 60;
            string game=CaptureFiles.Game(target);
            string path = SceneFiles.Output(CaptureFiles.Folder("Clips",game), game, ".mp4");
            SceneLook recordingLook = look();
            readyCuePlayed = false;
            recording = prepared.Start(audioClips.AcquireVideoAudio(),recordingLook,path,limit,fps);
            notice = "";
            Refresh();
            lastVideo = await recording.Result;
            notice = "Clip con filtro guardado en " + lastVideo;
            if(recording.CompletionNotice!=null)notice=recording.CompletionNotice+"\n"+notice;
        } catch (Exception ex) { notice = "No se pudo grabar: " + ex.GetBaseException().Message; }
        finally { audioClips.ReleaseVideoAudio(); recording = null; Refresh(); }
    }

    void Stop() { if (recording != null) recording.Stop(); }

    async void Screenshot() {
        if (!Valid() || capturing) return;
        capturing = true; notice = "";
        Refresh();
        try {
            SceneLook settings = look();
            string game=CaptureFiles.Game(target),path=null;
            if(prepared==null)throw new IOException("Selecciona la ventana primero.");
            SceneFrame frame=await prepared.Photo(settings);
            var image=BitmapSource.Create(frame.Width,frame.Height,96,96,PixelFormats.Bgra32,null,frame.Pixels,frame.Width*4);
            image.Freeze();
            await Task.Run(delegate{
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));
                try{using(var output=CaptureFiles.NewScreenshot(game)){path=output.Name;png.Save(output);}}
                catch{if(path!=null&&File.Exists(path))File.Delete(path);throw;}
            });
            notice = "Captura guardada en " + path;
        } catch (Exception ex) { notice = "No se pudo guardar la captura: " + ex.GetBaseException().Message; }
        finally { capturing = false; Refresh(); }
    }

    void Edit() {
        if (editor != null) { editor.Activate(); return; }
        try {
            Directory.CreateDirectory(SceneFiles.Clips);
            var file = new Microsoft.Win32.OpenFileDialog { Title = "Abrir clip para recortar",
                Filter = "Clip MP4 (*.mp4)|*.mp4", InitialDirectory = File.Exists(lastVideo)?Path.GetDirectoryName(lastVideo):SceneFiles.Clips,
                FileName = File.Exists(lastVideo) ? Path.GetFileName(lastVideo) : "" };
            if (file.ShowDialog(owner) != true) return;
            editor = new SceneEditor(owner, ui.Resources, file.FileName,target==null?null:CaptureFiles.Game(target));
            editor.Recycled += delegate(string path) {
                if (String.Equals(path, lastVideo, StringComparison.OrdinalIgnoreCase)) lastVideo = null;
                notice = "Enviado a la Papelera: " + Path.GetFileName(path);
                Refresh();
            };
            editor.Closed += delegate { editor = null; };
            editor.Show();
        } catch (Exception ex) { notice = "No se pudo abrir el editor: " + ex.Message; Refresh(); }
    }

    public bool CanClose() {
        if (recording != null) {
            Stop(); notice = "El clip se está guardando. Cierra cuando termine.";
            Refresh();
            return false;
        }
        if (capturing || (editor != null && editor.Busy)) {
            notice = "Espera a que termine de guardarse el archivo."; Refresh();
            return false;
        }
        return true;
    }

    public void Dispose() { timer.Stop(); Unregister(); if(prepared!=null)prepared.Dispose(); if (editor != null) editor.Close(); }
}

sealed class SceneEditor : Window {
    readonly string source;
    readonly string outputFolder;
    readonly TextBox fileName=new TextBox{IsReadOnly=false,Cursor=System.Windows.Input.Cursors.IBeam,Margin=new Thickness(0,5,0,8)};
    readonly Button folderButton=new Button{Content="Abrir carpeta"};
    readonly MediaElement media = new MediaElement { LoadedBehavior = MediaState.Manual,
        UnloadedBehavior = MediaState.Close, ScrubbingEnabled = true, Stretch = Stretch.Uniform };
    readonly Slider position = new Slider(), start = new Slider(), end = new Slider();
    readonly TextBlock positionTime=new TextBlock{Margin=new Thickness(0,0,0,10)};
    readonly TextBlock trimInfo = new TextBlock(), status = new TextBlock { TextWrapping = TextWrapping.Wrap };
    readonly Button play = new Button { Content = "▶", ToolTip = "Reproducir tramo", FontSize = 18 },
        stop = new Button { Content = "■", ToolTip = "Detener", FontSize = 18 },
        save = new Button { Content = "Guardar recorte MP4" },
        recycle = new Button { Content = "Enviar a la Papelera", ToolTip = "Quita de Clips el MP4 abierto" },
        markStart = new Button { Content = "Marcar inicio aquí" },
        markEnd = new Button { Content = "Marcar final aquí" };
    readonly DispatcherTimer timer;
    readonly System.Threading.CancellationTokenSource previewCancellation=new System.Threading.CancellationTokenSource();
    string previewPath;
    bool preparingPreview;
    bool ready, updating, playing, closed, exporting, removing;
    public bool Busy { get { return exporting || removing; } }
    public event Action<string> Recycled;

    public SceneEditor(Window owner, ResourceDictionary style, string source,string game=null) {
        Owner = owner; this.source = source; Resources.MergedDictionaries.Add(style);
        outputFolder=CaptureFiles.Inside(source,SceneFiles.Clips)?Path.GetDirectoryName(source):CaptureFiles.Folder("Clips",game??"Otros");
        Title = "Nitidez VN · Editar clip"; Width = Math.Min(1880,SystemParameters.WorkArea.Width-30);
        Height = Math.Min(1020, SystemParameters.WorkArea.Height - 25);
        MinWidth = 940; MinHeight = 680; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(33,29,38)); Foreground = Brushes.White;
        Grid main;StackPanel side;EditorLayout.ClipShell(this,"Editar clip",Path.GetFileName(source),out main,out side);
        media.SnapsToDevicePixels=true;media.UseLayoutRounding=true;
        var preview=new Border{Background=Brushes.Black,Child=media,CornerRadius=new CornerRadius(8),Margin=new Thickness(0,0,0,12)};main.Children.Add(preview);
        var transport=new StackPanel();Grid.SetRow(transport,1);main.Children.Add(transport);
        transport.Children.Add(position);transport.Children.Add(positionTime);play.Content="▶ Ver selección";stop.Content="■ Detener";EditorLayout.Buttons(transport,play,stop);
        var trim=EditorLayout.Card(side,"1. Elige qué conservar");trim.Children.Add(EditorLayout.Note("Busca el momento en la vista previa y marca el inicio y el final."));
        trim.Children.Add(new TextBlock{Text="Inicio"});trim.Children.Add(start);markStart.Margin=new Thickness(0,4,0,14);trim.Children.Add(markStart);
        trim.Children.Add(new TextBlock{Text="Final"});trim.Children.Add(end);markEnd.Margin=new Thickness(0,4,0,8);trim.Children.Add(markEnd);trimInfo.Margin=new Thickness(0,8,0,0);trimInfo.TextWrapping=TextWrapping.Wrap;trim.Children.Add(trimInfo);        var output=EditorLayout.Card(side,"2. Guarda el recorte");output.Children.Add(EditorLayout.Note("Nombre del nuevo archivo, sin .mp4. El clip original se conserva."));fileName.Text="Recorte_"+DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");output.Children.Add(fileName);EditorLayout.SaveButton(save);output.Children.Add(save);folderButton.Margin=new Thickness(0,0,0,14);output.Children.Add(folderButton);output.Children.Add(EditorLayout.Note("Se guarda en la carpeta del juego, sin límite de peso."));
        var message=EditorLayout.Card(side,"Estado");message.Children.Add(status);
        recycle.Content="Enviar original a la Papelera";recycle.Margin=new Thickness(0,0,0,10);side.Children.Add(recycle);
        side.Children.Add(EditorLayout.Note("Esta opción retira el clip que abriste. Guardar un recorte crea otro archivo."));
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        timer.Tick += delegate {
            if (media.Position.TotalSeconds >= end.Value) { Pause(); Seek(end.Value); }
            else SetPosition(media.Position.TotalSeconds);
        };
        media.MediaOpened += async delegate {
            double fallback = media.NaturalDuration.HasTimeSpan ? media.NaturalDuration.TimeSpan.TotalSeconds : 0;
            Pause();
            double duration = await Task.Run(() => SceneFiles.Duration(source, fallback));
            if (closed || removing) return;
            if (duration <= 0) { status.Text = "El clip no informó su duración."; return; }
            updating = true;
            start.Maximum = end.Maximum = position.Maximum = duration;
            end.Value = duration; start.Value = 0;
            updating = false; ready = duration > 0;
            var dpi=VisualTreeHelper.GetDpi(this);media.MaxWidth=media.NaturalVideoWidth/dpi.DpiScaleX;media.MaxHeight=media.NaturalVideoHeight/dpi.DpiScaleY;
            Pause(); Seek(0); UpdateControls();
        };
        media.MediaFailed += delegate(object sender, ExceptionRoutedEventArgs e) {
            if(previewPath==null&&!preparingPreview){LoadFallbackPreview();return;}
            ready = false; Pause(); status.Text = "No se pudo reproducir el clip: " + e.ErrorException.Message; UpdateControls();
        };
        media.MediaEnded += delegate { Pause(); Seek(end.Value); };
        position.ValueChanged += delegate { if (ready && !updating) { Pause(); media.Position = TimeSpan.FromSeconds(position.Value);positionTime.Text=String.Format("Posición {0:F2} s / {1:F2} s",position.Value,end.Maximum); } };
        start.ValueChanged += delegate { Adjust(true); };
        end.ValueChanged += delegate { Adjust(false); };
        play.Click += delegate { if (playing) Pause(); else { Seek(start.Value); media.Play(); playing = true; timer.Start(); } };
        stop.Click += delegate { Pause(); Seek(start.Value); };
        markStart.Click += delegate { start.Value = position.Value; };
        markEnd.Click += delegate { end.Value = position.Value; };
        save.Click += async delegate { await Export(); };
        folderButton.Click+=delegate{try{CaptureFiles.OpenPath(outputFolder);}catch(Exception ex){status.Text="No se pudo abrir la carpeta: "+ex.Message;}};
        recycle.Click += async delegate { await RecycleVideo(); };
        Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (Busy) e.Cancel = true; };
        Closed += delegate { closed = true; previewCancellation.Cancel();timer.Stop(); media.Close();DeletePreview(); };
        Deactivated += delegate { Pause(); };
        UpdateControls();
        media.Source=new Uri(source);media.Play();
    }

    async void LoadFallbackPreview(){await LoadPreview();}
    void DeletePreview(){try{if(previewPath!=null&&File.Exists(previewPath))File.Delete(previewPath);}catch(IOException){}}
    async Task LoadPreview(){
        preparingPreview=true;UpdateControls();status.Text="Preparando vista previa… El clip original conserva su calidad.";
        previewPath=Path.Combine(Path.GetTempPath(),"Nitidez-previa-"+Guid.NewGuid().ToString("N")+".mp4");
        try{
            await Task.Run(()=>SceneFiles.Preview(source,previewPath,previewCancellation.Token));
            if(closed){DeletePreview();return;}
            media.Source=new Uri(previewPath);status.Text="Vista previa lista. El recorte se guarda desde el original, sin reducir su calidad.";media.Play();
        }catch(OperationCanceledException){DeletePreview();}
        catch(Exception ex){if(!closed)status.Text="No se pudo preparar la vista previa: "+ex.GetBaseException().Message;DeletePreview();}
        finally{preparingPreview=false;if(!closed)UpdateControls();}
    }

    void SetPosition(double seconds) {
        updating = true; position.Value = seconds; updating = false;positionTime.Text=String.Format("Posición {0:F2} s / {1:F2} s",seconds,end.Maximum);
    }
    void Seek(double seconds) { if (ready) { media.Position = TimeSpan.FromSeconds(seconds); SetPosition(seconds); } }
    void Pause() { timer.Stop(); media.Pause(); playing = false; }

    void Adjust(bool movedStart) {
        if (updating || !ready) return;
        Pause();
        updating = true;
        double minimum = Math.Min(1.0 / 30, end.Maximum);
        if (movedStart) start.Value = Math.Min(start.Value, end.Value - minimum);
        else end.Value = Math.Max(end.Value, start.Value + minimum);
        updating = false;
        Seek(movedStart ? start.Value : end.Value);
        UpdateControls();
    }

    void UpdateControls() {
        position.IsEnabled = start.IsEnabled = end.IsEnabled = play.IsEnabled = stop.IsEnabled =
            markStart.IsEnabled = markEnd.IsEnabled = save.IsEnabled = ready && !Busy;
        recycle.IsEnabled = !Busy && !preparingPreview && IsManagedVideo() && File.Exists(source);
        fileName.IsEnabled=ready&&!Busy;
        trimInfo.Text = String.Format("Inicio {0:F2} s  ·  Final {1:F2} s  ·  Recorte {2:F2} s", start.Value, end.Value, end.Value - start.Value);
    }

    async Task Export() {
        if (!ready || Busy) return;
        Pause(); exporting = true; UpdateControls(); status.Text = "Exportando el recorte…";
        try {
            double first = start.Value, last = end.Value;
            string file=CaptureFiles.AvailableOutput(outputFolder,fileName.Text,".mp4","Recorte_"+DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            await Task.Run(() => SceneFiles.Trim(source, file, first, last));
            status.Text = "Guardado en " + file;
        } catch (Exception ex) { status.Text = "No se pudo guardar: " + ex.GetBaseException().Message; }
        finally { exporting = false; UpdateControls(); }
    }

    bool IsManagedVideo() {
        return CaptureFiles.Inside(source,SceneFiles.Clips);
    }

    async Task RecycleVideo() {
        if (Busy || !IsManagedVideo()) return;
        if (MessageBox.Show(this, "¿Enviar este clip a la Papelera?\n\n" + Path.GetFileName(source),
            "Nitidez VN", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        removing = true; ready = false; Pause(); media.Close(); UpdateControls();
        try {
            await Task.Delay(150); // Let the preview release its file handle.
            SceneFiles.Recycle(new WindowInteropHelper(this).Handle, source);
        } catch (Exception ex) {
            status.Text = "No se pudo enviar a la Papelera: " + ex.GetBaseException().Message;
            removing = false; UpdateControls();
            media.Source = new Uri(previewPath??source); media.Play();
            return;
        }
        removing = false;
        var handler = Recycled;
        if (handler != null) handler(source);
        Close();
    }
}
