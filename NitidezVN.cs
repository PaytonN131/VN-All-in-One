using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SD = System.Drawing;
using WF = System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Nitidez VN 4.5")]
[assembly: System.Reflection.AssemblyVersion("4.5.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("4.5.0.0")]

static class Native {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr x);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll",SetLastError=true)] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x,int y,int w,int ht,uint flags);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h,int index);
    [DllImport("user32.dll",SetLastError=true)] public static extern int SetWindowLong(IntPtr h,int index,int value);
    [DllImport("user32.dll",SetLastError=true)] public static extern bool SetWindowDisplayAffinity(IntPtr h,uint a);
    [DllImport("user32.dll",SetLastError=true)] public static extern bool SetLayeredWindowAttributes(IntPtr h,uint color,byte alpha,uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowDisplayAffinity(IntPtr h,out uint a);
    [DllImport("user32.dll",SetLastError=true)] public static extern bool RegisterHotKey(IntPtr h,int id,uint mods,uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h,int id);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int cmd);
    [DllImport("dwmapi.dll")] public static extern int DwmFlush();
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h,int attribute,ref int value,int size);
    [DllImport("d3dcompiler_47.dll",CharSet=CharSet.Ansi)] public static extern int D3DCompile(byte[] src,UIntPtr size,string name,IntPtr defines,IntPtr inc,string entry,string profile,uint f1,uint f2,out IntPtr blob,out IntPtr errors);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr BlobPointer(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate UIntPtr BlobSize(IntPtr self);
    public static byte[] Compile(string code) {
        byte[] src=Encoding.ASCII.GetBytes(code); IntPtr b,e;
        int hr=D3DCompile(src,(UIntPtr)src.Length,null,IntPtr.Zero,IntPtr.Zero,"main","ps_2_0",1<<15,0,out b,out e);
        try {
            if(hr<0) throw new Exception("Shader GPU: " + (e==IntPtr.Zero ? hr.ToString() : Marshal.PtrToStringAnsi(Pointer(e))));
            IntPtr table=Marshal.ReadIntPtr(b);
            var size=(BlobSize)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(table,4*IntPtr.Size),typeof(BlobSize));
            byte[] bytes=new byte[(int)size(b).ToUInt64()]; Marshal.Copy(Pointer(b),bytes,0,bytes.Length); return bytes;
        } finally { if(b!=IntPtr.Zero) Marshal.Release(b); if(e!=IntPtr.Zero) Marshal.Release(e); }
    }
    static IntPtr Pointer(IntPtr b) { return ((BlobPointer)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(b),3*IntPtr.Size),typeof(BlobPointer)))(b); }
}

class Sharpen : ShaderEffect {
    public static readonly DependencyProperty InputProperty=RegisterPixelShaderSamplerProperty("Input",typeof(Sharpen),0);
    public static readonly DependencyProperty AmountProperty=DependencyProperty.Register("Amount",typeof(double),typeof(Sharpen),new UIPropertyMetadata(0.7,PixelShaderConstantCallback(0)));
    public static readonly DependencyProperty StepProperty=DependencyProperty.Register("Step",typeof(Point),typeof(Sharpen),new UIPropertyMetadata(new Point(1.0/800,1.0/600),PixelShaderConstantCallback(1)));
    public static readonly DependencyProperty BrightnessProperty=DependencyProperty.Register("Brightness",typeof(double),typeof(Sharpen),new UIPropertyMetadata(0.0,PixelShaderConstantCallback(2)));
    public static readonly DependencyProperty ContrastProperty=DependencyProperty.Register("Contrast",typeof(double),typeof(Sharpen),new UIPropertyMetadata(1.0,PixelShaderConstantCallback(3)));
    public static readonly DependencyProperty SaturationProperty=DependencyProperty.Register("Saturation",typeof(double),typeof(Sharpen),new UIPropertyMetadata(1.0,PixelShaderConstantCallback(4)));
    public static readonly DependencyProperty WarmthProperty=DependencyProperty.Register("Warmth",typeof(double),typeof(Sharpen),new UIPropertyMetadata(0.0,PixelShaderConstantCallback(5)));
    public static readonly DependencyProperty GammaProperty=DependencyProperty.Register("Gamma",typeof(double),typeof(Sharpen),new UIPropertyMetadata(1.0,PixelShaderConstantCallback(6)));
    public Brush Input {get{return (Brush)GetValue(InputProperty);} set{SetValue(InputProperty,value);}}
    public double Amount {get{return (double)GetValue(AmountProperty);}set{SetValue(AmountProperty,value);}}
    public Point Step {get{return (Point)GetValue(StepProperty);}set{SetValue(StepProperty,value);}}
    public double Brightness {get{return (double)GetValue(BrightnessProperty);}set{SetValue(BrightnessProperty,value);}}
    public double Contrast {get{return (double)GetValue(ContrastProperty);}set{SetValue(ContrastProperty,value);}}
    public double Saturation {get{return (double)GetValue(SaturationProperty);}set{SetValue(SaturationProperty,value);}}
    public double Warmth {get{return (double)GetValue(WarmthProperty);}set{SetValue(WarmthProperty,value);}}
    public double Gamma {get{return (double)GetValue(GammaProperty);}set{SetValue(GammaProperty,value);}}
    public Sharpen() {
        var shader=new PixelShader();
        using(var ms=new MemoryStream(Native.Compile(@"sampler2D s:register(s0); float amount:register(c0); float2 stepUV:register(c1);
float brightness:register(c2); float contrast:register(c3); float saturation:register(c4); float warmth:register(c5); float gammaValue:register(c6);
float4 main(float2 uv:TEXCOORD):COLOR {
 float4 c=tex2D(s,uv);
 float3 n=tex2D(s,uv+float2(stepUV.x,0)).rgb+tex2D(s,uv-float2(stepUV.x,0)).rgb+tex2D(s,uv+float2(0,stepUV.y)).rgb+tex2D(s,uv-float2(0,stepUV.y)).rgb;
 float3 rgb=saturate(c.rgb+amount*(c.rgb-n*0.25));
 rgb=saturate((rgb-0.5)*contrast+0.5+brightness);
 float lum=dot(rgb,float3(0.2126,0.7152,0.0722));
 rgb=lerp(lum.xxx,rgb,saturation)+warmth*float3(1,0.15,-1);
 rgb=pow(max(saturate(rgb),0.00001),1.0/gammaValue);
 return float4(saturate(rgb),c.a);
}"))) shader.SetStreamSource(ms);
        PixelShader=shader; UpdateShaderValue(InputProperty); UpdateShaderValue(AmountProperty); UpdateShaderValue(StepProperty);
        UpdateShaderValue(BrightnessProperty);UpdateShaderValue(ContrastProperty);UpdateShaderValue(SaturationProperty);UpdateShaderValue(WarmthProperty);UpdateShaderValue(GammaProperty);
    }
}

// Read only discovery: no hooks, changes to Luna, or game access.
class LunaWindows {
    readonly HashSet<uint> processes=new HashSet<uint>();
    readonly Stopwatch refresh=Stopwatch.StartNew();
    bool initialized;
    public readonly List<SD.Rectangle> Rectangles=new List<SD.Rectangle>();
    public bool Owns(IntPtr h){uint pid;Native.GetWindowThreadProcessId(h,out pid);return processes.Contains(pid);}
    public void Refresh(){
        if(!initialized||refresh.ElapsedMilliseconds>=1000){
            processes.Clear();
            foreach(string name in new[]{"LunaTranslator","LunaTranslator_admin"})
                foreach(var process in Process.GetProcessesByName(name))using(process)processes.Add((uint)process.Id);
            initialized=true;refresh.Restart();
        }
        Rectangles.Clear();if(processes.Count==0)return;
        Native.EnumWindows(delegate(IntPtr h,IntPtr unused){
            Native.RECT r;
            if(Owns(h)&&Native.IsWindowVisible(h)&&!Native.IsIconic(h)&&Native.GetWindowRect(h,out r)&&r.R>r.L&&r.B>r.T){
                var rect=SD.Rectangle.FromLTRB(r.L,r.T,r.R,r.B);rect.Inflate(2,2);Rectangles.Add(rect);
            }
            return true;
        },IntPtr.Zero);
    }
}

// DXGI owns this child window's pixels. WinForms must not erase it with GDI.
sealed class VideoPanel : WF.Panel {
    public VideoPanel(){SetStyle(WF.ControlStyles.UserPaint|WF.ControlStyles.AllPaintingInWmPaint|WF.ControlStyles.Opaque,true);}
    protected override void OnPaintBackground(WF.PaintEventArgs e){}
    protected override void OnPaint(WF.PaintEventArgs e){}
}

class Overlay : WF.Form {
    public readonly Image Image=new Image {Stretch=Stretch.Fill,SnapsToDevicePixels=true};
    readonly System.Windows.Forms.Integration.ElementHost wpfHost;
    readonly WF.Panel videoHost;
    public IntPtr VideoHwnd { get { return videoHost.Handle; } }
    public bool DirectVideo { get { return videoHost.Visible; } }
    public IntPtr Hwnd;
    string regionKey;
    public int LunaAreas {get;private set;}
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override WF.CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x80000|0x20|0x08000000|0x80;return p;}}
    public bool IsVisible {get{return Visible;}}
    public Overlay() {
        FormBorderStyle=WF.FormBorderStyle.None; ShowInTaskbar=false;TopMost=true;AutoScaleMode=WF.AutoScaleMode.None;
        StartPosition=WF.FormStartPosition.Manual;SetBounds(-20000,-20000,1,1);
        wpfHost=new System.Windows.Forms.Integration.ElementHost {Dock=WF.DockStyle.Fill,Child=Image,BackColor=SD.Color.Black};
        videoHost=new VideoPanel {Dock=WF.DockStyle.Fill,Enabled=false,Visible=false};
        Controls.Add(wpfHost);Controls.Add(videoHost);
        Hwnd=Handle;
        if(!Native.SetLayeredWindowAttributes(Hwnd,0,255,2))throw new Exception("No se pudo habilitar paso de clics: "+Marshal.GetLastWin32Error());
        uint affinity;
        if(!Native.SetWindowDisplayAffinity(Hwnd,0x11)||!Native.GetWindowDisplayAffinity(Hwnd,out affinity)||affinity!=0x11){Dispose();throw new Exception("Windows no permite excluir la superposición (Win32 "+Marshal.GetLastWin32Error()+"). Se cancela para evitar espejo.");}
    }
    public void UseDirectVideo(bool direct) {
        if(direct){wpfHost.Hide();videoHost.Show();videoHost.BringToFront();}
        else{videoHost.Hide();wpfHost.Show();}
    }
    protected override void WndProc(ref WF.Message m){if(m.Msg==0x21){m.Result=new IntPtr(3);return;}if(m.Msg==0x84){m.Result=new IntPtr(-1);return;}base.WndProc(ref m);}
    public void ProtectLuna(SD.Rectangle bounds,IEnumerable<SD.Rectangle> windows){
        var holes=new List<SD.Rectangle>();var signature=new StringBuilder(bounds.Width+","+bounds.Height+";");
        foreach(var window in windows){var hole=SD.Rectangle.Intersect(bounds,window);if(hole.IsEmpty)continue;
            hole.Offset(-bounds.X,-bounds.Y);holes.Add(hole);signature.Append(hole.ToString());}
        LunaAreas=holes.Count;string next=signature.ToString();if(next==regionKey)return;
        SD.Region region=null;
        if(holes.Count>0){region=new SD.Region(new SD.Rectangle(0,0,bounds.Width,bounds.Height));foreach(var hole in holes)region.Exclude(hole);}
        var old=Region;try{Region=region;regionKey=next;}catch{if(region!=null)region.Dispose();throw;}finally{if(old!=null)old.Dispose();}
    }
    public void Place(int x,int y,int w,int h) {
        if(Bounds.X!=x||Bounds.Y!=y||Bounds.Width!=w||Bounds.Height!=h)
            Native.SetWindowPos(Hwnd,IntPtr.Zero,x,y,w,h,0x10|0x4);
        if(!Visible)Show();
    }
}

class Choice {public IntPtr H; public uint Pid; public string Title; public string GameName; public override string ToString(){return Title;}}

class ControlWindow : Window {
    ComboBox windows,fps,preset,captureQuality;
    readonly ShortcutCatalog shortcuts; readonly ShortcutBinding filterKey,closeKey;
    Slider amount,brightness,contrast,saturation,warmth,gamma;
    TextBlock status,hotkeyStatus,effectState,metrics;
    Button toggle;
    bool applyingPreset;
    readonly LunaWindows luna=new LunaWindows();
    readonly AudioClips audioClips;
    readonly SceneControls sceneControls;

    readonly DispatcherTimer restoreTargetTimer=new DispatcherTimer(DispatcherPriority.Background);
    string rememberedExe="",rememberedTitle="",rememberedGame="";
    TextBox gameName;
    FrameworkElement ui;
    Overlay overlay; Sharpen shader; DispatcherTimer timer; Choice target; IntPtr hwnd; bool enabled; bool closing;
    SD.Bitmap bitmap; byte[] pixels; WriteableBitmap wb; int bw,bh; long frames,sampleFrames; Stopwatch rate=new Stopwatch(); double workMs;
    WindowCapture gpuCapture;
    Stopwatch firstFrame;
    bool hasFrame;
    long nextMetrics;
    readonly Stopwatch recoveryClock=Stopwatch.StartNew();
    long retryCaptureAt,lastCaptureFailure=-30000;
    int captureFailures;
    SD.Rectangle displayBounds;
    string logPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"sesion.log");
    public ControlWindow() {
        Title="Nitidez VN 4.5 — controles"; Width=Math.Min(1080,SystemParameters.WorkArea.Width-35); Height=Math.Min(860,SystemParameters.WorkArea.Height-35);MinWidth=880;MinHeight=620;Background=new SolidColorBrush(Color.FromRgb(33,29,38));Foreground=Brushes.White;FontFamily=new FontFamily("Segoe UI");FontSize=13;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        using(var x=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Interfaz.xaml"))ui=(FrameworkElement)System.Windows.Markup.XamlReader.Load(x);Content=ui;
        var sections=(TabControl)ui.FindName("Sections");
        sections.SelectionChanged+=delegate(object sender,SelectionChangedEventArgs e){if(e.OriginalSource==sections)Dispatcher.BeginInvoke(new Action(delegate{var scroll=sections.Template.FindName("PanelScroller",sections) as ScrollViewer;if(scroll!=null)scroll.ScrollToTop();}),DispatcherPriority.Loaded);};
        windows=(ComboBox)ui.FindName("Windows");fps=(ComboBox)ui.FindName("Fps");preset=(ComboBox)ui.FindName("Preset");
        amount=(Slider)ui.FindName("Sharp");brightness=(Slider)ui.FindName("Brightness");contrast=(Slider)ui.FindName("Contrast");saturation=(Slider)ui.FindName("Saturation");warmth=(Slider)ui.FindName("Warmth");gamma=(Slider)ui.FindName("Gamma");
        status=(TextBlock)ui.FindName("Status");hotkeyStatus=(TextBlock)ui.FindName("HotkeyStatus");effectState=(TextBlock)ui.FindName("EffectState");toggle=(Button)ui.FindName("Toggle");
        metrics=(TextBlock)ui.FindName("CaptureMetrics");
        var refresh=(Button)ui.FindName("Refresh");var start=(Button)ui.FindName("Start");var diag=(Button)ui.FindName("Diagnostic");
        captureQuality=(ComboBox)ui.FindName("CaptureQuality");
        foreach(string quality in new[]{"GPU · 540p","GPU · 720p","GPU · Nativa","Escritorio · Compatibilidad"})captureQuality.Items.Add(quality);
        captureQuality.SelectedIndex=2;
        gameName=(TextBox)ui.FindName("GameName");
        windows.SelectionChanged+=delegate{var choice=windows.SelectedItem as Choice;if(choice!=null)gameName.Text=CaptureFiles.Game(choice);};
        gameName.TextChanged+=delegate{var choice=windows.SelectedItem as Choice;if(choice!=null){choice.GameName=CaptureFiles.Name(gameName.Text);if(target!=null&&target.H==choice.H&&target.Pid==choice.Pid){target.GameName=choice.GameName;rememberedGame=choice.GameName;}}};
        foreach(int n in new[]{10,15,20,30,60})fps.Items.Add(n);fps.SelectedItem=30;
        shortcuts=new ShortcutCatalog(ui);filterKey=shortcuts.Add(ui,"Key","Alternar filtro",0x77,3,"Ctrl + Alt + F8");closeKey=shortcuts.Add(ui,"CloseKey","Cerrar Nitidez",0x79,3,"Ctrl + Alt + F10");
        ((Button)ui.FindName("ResetKeys")).Click+=delegate{shortcuts.Reset();};
        foreach(string s in new[]{"Natural","Noche suave","Luminoso","Colores vivos","Cálido","Blanco y negro","Personalizado"})preset.Items.Add(s);preset.SelectedIndex=0;
        string pic=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","NitidezVN.ico");if(File.Exists(pic)){var frames=BitmapDecoder.Create(new Uri(pic),BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames;BitmapFrame best=frames[0];foreach(var frame in frames)if(frame.PixelWidth>best.PixelWidth)best=frame;((Image)ui.FindName("Mascot")).Source=best;}
        string ico=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","NitidezVN.ico");if(File.Exists(ico))Icon=BitmapFrame.Create(new Uri(ico));
        refresh.Click+=delegate{RefreshWindows();};start.Click+=delegate{SelectTarget();};toggle.Click+=delegate{Toggle();};
        foreach(var slider in new[]{amount,brightness,contrast,saturation,warmth,gamma})slider.ValueChanged+=delegate{if(!applyingPreset)preset.SelectedIndex=6;UpdateFilters();};
        preset.SelectionChanged+=delegate{if(!applyingPreset&&preset.SelectedIndex<6)ApplyPreset(preset.SelectedIndex);};
        ((Button)ui.FindName("Reset")).Click+=delegate{ApplyPreset(0);};
        audioClips=new AudioClips(this,ui,shortcuts);
        sceneControls=new SceneControls(this,ui,()=>new SceneLook(amount.Value,brightness.Value,contrast.Value,saturation.Value,warmth.Value,gamma.Value),audioClips,shortcuts);
        LoadSettings();UpdateFilters();
        restoreTargetTimer.Interval=TimeSpan.FromSeconds(1);
        restoreTargetTimer.Tick+=delegate{RestoreTarget();};

        captureQuality.SelectionChanged+=delegate{ReleaseFrame();if(target!=null)Tick();};
        fps.SelectionChanged+=delegate{if(timer!=null)timer.Interval=TimeSpan.FromMilliseconds(1000.0/(int)fps.SelectedItem);};
        filterKey.Changed+=delegate{if(hwnd!=IntPtr.Zero)RegisterToggle();};
        closeKey.Changed+=delegate{if(hwnd!=IntPtr.Zero)RegisterClose();};
        diag.Click+=delegate {var delay=new DispatcherTimer {Interval=TimeSpan.FromSeconds(3)};delay.Tick+=delegate{delay.Stop();if(!closing)Diagnostic();};delay.Start();};
        SourceInitialized+=delegate {
            hwnd=new WindowInteropHelper(this).Handle;
            int dark=1;Native.DwmSetWindowAttribute(hwnd,20,ref dark,4);
            audioClips.Initialize(hwnd);
            sceneControls.Initialize(hwnd);
            HwndSource.FromHwnd(hwnd).AddHook(delegate(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool done){
                if(msg==0x312){
                    done=true;
                    switch(w.ToInt32()){case 1: Toggle(); break; case 2: Close(); break; case 3: audioClips.CaptureClip(); break; default: sceneControls.Hotkey(w.ToInt32()); break;}
                }
                return IntPtr.Zero;
            });
            RegisterToggle();RegisterClose();
        };
        Loaded+=delegate {
            try{CaptureFiles.Initialize();}catch(Exception ex){Log("Carpetas de archivos: "+ex.Message);}
            RefreshWindows();
            Log("Inicio v4.5 (captura preparada y clips compatibles). Windows="+Environment.OSVersion+" GPU WPF tier="+(RenderCapability.Tier>>16));
            if(rememberedExe.Length!=0){restoreTargetTimer.Start();RestoreTarget();}
            Dispatcher.BeginInvoke(new Action(SaveUiPreview),DispatcherPriority.ApplicationIdle);
        };
        Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){
            if(!sceneControls.CanClose()||!audioClips.CanClose()){e.Cancel=true;return;}

            closing=true;restoreTargetTimer.Stop();SaveSettings();sceneControls.Dispose();audioClips.Dispose();if(timer!=null)timer.Stop();Native.UnregisterHotKey(hwnd,1);Native.UnregisterHotKey(hwnd,2);ReleaseFrame();if(overlay!=null)overlay.Close();Log("Cierre; recursos liberados.");
        };
    }
    void UpdateFilters(){
        ((System.Windows.Documents.Run)ui.FindName("SharpValue")).Text=amount.Value.ToString("F2");
        ((System.Windows.Documents.Run)ui.FindName("BrightnessValue")).Text=brightness.Value.ToString("+0%;-0%;0%");
        ((System.Windows.Documents.Run)ui.FindName("ContrastValue")).Text=contrast.Value.ToString("F2");
        ((System.Windows.Documents.Run)ui.FindName("SaturationValue")).Text=saturation.Value.ToString("F2");
        ((System.Windows.Documents.Run)ui.FindName("WarmthValue")).Text=warmth.Value.ToString("+0%;-0%;0%");
        ((System.Windows.Documents.Run)ui.FindName("GammaValue")).Text=gamma.Value.ToString("F2");
        if(shader!=null){shader.Amount=amount.Value;shader.Brightness=brightness.Value;shader.Contrast=contrast.Value;shader.Saturation=saturation.Value;shader.Warmth=warmth.Value;shader.Gamma=gamma.Value;}
    }
    void ApplyPreset(int index){if(index<0||index>5)return;applyingPreset=true;try{
        double[][] p={new[]{.7,0,1,1,0,1},new[]{.5,-.12,.95,.85,.035,1},new[]{.6,.07,1.02,1.02,0,1.08},new[]{.8,0,1.1,1.25,0,1},new[]{.6,0,1.02,1,.06,1},new[]{.7,0,1.08,0,0,1}};
        Slider[] sliders={amount,brightness,contrast,saturation,warmth,gamma};for(int i=0;i<6;i++)sliders[i].Value=p[index][i];preset.SelectedIndex=index;
    }finally{applyingPreset=false;}UpdateFilters();Log("Estilo: "+preset.SelectedItem);}
    void LoadSettings(){try{string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ajustes.xml");if(!File.Exists(file))return;var doc=new System.Xml.XmlDocument();doc.Load(file);var node=doc.DocumentElement;applyingPreset=true;
        Slider[] sliders={amount,brightness,contrast,saturation,warmth,gamma};string[] names={"nitidez","brillo","contraste","saturacion","temperatura","gamma"};
        for(int i=0;i<6;i++){double v;if(double.TryParse(node.GetAttribute(names[i]),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out v)&&!double.IsNaN(v)&&!double.IsInfinity(v))sliders[i].Value=Math.Max(sliders[i].Minimum,Math.Min(sliders[i].Maximum,v));}
        int n;if(int.TryParse(node.GetAttribute("fps"),out n)&&fps.Items.Contains(n))fps.SelectedItem=n;if(!node.HasAttribute("atajoFiltro")){string oldKey=node.GetAttribute("tecla");int oldNumber;if(oldKey.StartsWith("F")&&int.TryParse(oldKey.Substring(1),out oldNumber)&&oldNumber>=1&&oldNumber<=12)filterKey.Legacy((uint)(0x6F+oldNumber),3,"Ctrl + Alt + "+oldKey);}filterKey.Load(node,"atajoFiltro");closeKey.Load(node,"atajoCerrar");string style=node.GetAttribute("estilo");if(preset.Items.Contains(style))preset.SelectedItem=style;else preset.SelectedIndex=6;
        audioClips.Load(node);
        sceneControls.Load(node);
        int quality;if(int.TryParse(node.GetAttribute("capturaCalidad"),out quality)&&quality>=0&&quality<captureQuality.Items.Count)captureQuality.SelectedIndex=quality;
        rememberedExe=node.GetAttribute("ultimaAplicacion");
        rememberedTitle=node.GetAttribute("ultimaVentana");rememberedGame=node.GetAttribute("nombreJuego");
    }catch(Exception ex){Log("Ajustes no cargados: "+ex.Message);}finally{applyingPreset=false;}}
    void SaveSettings(){try{var opts=new System.Xml.XmlWriterSettings{Indent=true};using(var writer=System.Xml.XmlWriter.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ajustes.xml"),opts)){writer.WriteStartElement("ajustes");
        Slider[] sliders={amount,brightness,contrast,saturation,warmth,gamma};string[] names={"nitidez","brillo","contraste","saturacion","temperatura","gamma"};for(int i=0;i<6;i++)writer.WriteAttributeString(names[i],sliders[i].Value.ToString(System.Globalization.CultureInfo.InvariantCulture));writer.WriteAttributeString("fps",fps.SelectedItem.ToString());filterKey.Save(writer,"atajoFiltro");closeKey.Save(writer,"atajoCerrar");writer.WriteAttributeString("estilo",preset.SelectedItem.ToString());writer.WriteAttributeString("capturaCalidad",captureQuality.SelectedIndex.ToString());
        writer.WriteAttributeString("ultimaAplicacion",rememberedExe);
        writer.WriteAttributeString("ultimaVentana",rememberedTitle);writer.WriteAttributeString("nombreJuego",rememberedGame);
        audioClips.Save(writer);sceneControls.Save(writer);writer.WriteEndElement();}
    }catch(Exception ex){Log("No se pudieron guardar ajustes: "+ex.Message);}}
    void SaveUiPreview(){try{var bmp=new RenderTargetBitmap((int)ui.ActualWidth,(int)ui.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(ui);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"diagnostico"));using(var f=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"diagnostico","interfaz.png")))png.Save(f);}catch(Exception ex){Log("Vista de interfaz: "+ex.Message);}}
    void UpdateState(string text, bool active){if(effectState.Text==text)return;effectState.Text=text;effectState.Foreground=new SolidColorBrush(active?Color.FromRgb(143,224,190):Color.FromRgb(168,166,186));toggle.Background=new SolidColorBrush(active?Color.FromRgb(56,106,91):Color.FromRgb(66,54,94));toggle.ToolTip=active?"Desactivar efecto":"Activar efecto";}
    void Toggle(){if(target==null){SelectTarget();return;}enabled=!enabled;Log("Efecto "+(enabled?"activado":"desactivado"));Tick();}
    void RegisterToggle(){Native.UnregisterHotKey(hwnd,1);bool ok=filterKey.Register(hwnd,1);hotkeyStatus.Text=ok?"Filtro: "+filterKey.Label+" registrado.":"El atajo del filtro está ocupado o repetido. Elige otro.";Log(hotkeyStatus.Text);}
    void RegisterClose(){Native.UnregisterHotKey(hwnd,2);if(!closeKey.Register(hwnd,2))shortcuts.Notice("El atajo de cerrar está ocupado o repetido. Puedes usar la X.");}
    void Log(string s){try{File.AppendAllText(logPath,DateTime.Now.ToString("s")+" "+s+Environment.NewLine);}catch{}}
    static List<Choice> OpenWindows(HashSet<uint> only=null) {
        var list=new List<Choice>();
        uint own=(uint)Process.GetCurrentProcess().Id;
        Native.EnumWindows(delegate(IntPtr h,IntPtr p){
            uint pid;Native.GetWindowThreadProcessId(h,out pid);
            if(pid==own||(only!=null&&!only.Contains(pid))||!Native.IsWindowVisible(h)||(Native.GetWindowLong(h,-20)&0x80)!=0)return true;
            var text=new StringBuilder(512);Native.GetWindowText(h,text,512);
            Native.RECT r;
            if(text.Length>0&&Native.GetClientRect(h,out r)&&r.R>1&&r.B>1)list.Add(new Choice{H=h,Pid=pid,Title=text.ToString()});
            return true;
        },IntPtr.Zero);
        return list;
    }
    void RefreshWindows() {
        var selected=windows.SelectedItem as Choice ?? target;
        windows.Items.Clear();
        foreach(var item in OpenWindows()) {
            windows.Items.Add(item);
            if(selected!=null&&item.H==selected.H&&item.Pid==selected.Pid){item.GameName=selected.GameName;windows.SelectedItem=item;}
        }
        if(windows.SelectedItem==null&&rememberedExe.Length==0&&windows.Items.Count>0)windows.SelectedIndex=0;
    }
    static string ProcessPath(Process process) {
        try { return process.MainModule.FileName; }
        catch(System.ComponentModel.Win32Exception) { return ""; }
        catch(InvalidOperationException) { return ""; }
    }
    void RestoreTarget() {
        if(closing||target!=null||rememberedExe.Length==0){restoreTargetTimer.Stop();return;}
        var pids=new HashSet<uint>();
        foreach(var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(rememberedExe)))using(process) {
            if(String.Equals(ProcessPath(process),rememberedExe,StringComparison.OrdinalIgnoreCase))pids.Add((uint)process.Id);
        }
        var choices=pids.Count==0?new List<Choice>():OpenWindows(pids);
        var exact=choices.FindAll(item=>String.Equals(item.Title,rememberedTitle,StringComparison.OrdinalIgnoreCase));
        var match=exact.Count==1?exact[0]:choices.Count==1?choices[0]:null;
        if(match==null) {
            string waiting=choices.Count>1?"Hay varias ventanas de la última aplicación. Selecciona una.":"Esperando a "+rememberedTitle+"…";
            if(status.Text!=waiting)status.Text=waiting;
            return;
        }
        restoreTargetTimer.Stop();
        foreach(Choice item in windows.Items)if(item.H==match.H&&item.Pid==match.Pid){match=item;break;}
        match.GameName=String.IsNullOrWhiteSpace(rememberedGame)?CaptureFiles.SuggestedName(match.Title):rememberedGame;
        if(!windows.Items.Contains(match))windows.Items.Add(match);
        windows.SelectedItem=match;
        gameName.Text=CaptureFiles.Game(match);
        SelectTarget(true);
    }
    void SelectTarget(bool automatic=false){var c=windows.SelectedItem as Choice;if(c==null){status.Text="Selecciona una ventana.";return;}try{
        c.GameName=CaptureFiles.Name(gameName.Text);
        if(target==null||target.H!=c.H||target.Pid!=c.Pid){ReleaseFrame();}
        audioClips.Select(c);sceneControls.Select(c);
        if(overlay==null){if((RenderCapability.Tier>>16)<2)throw new Exception("WPF no ofrece aceleración GPU de nivel 2 en esta sesión.");shader=new Sharpen();UpdateFilters();overlay=new Overlay();overlay.Image.Effect=shader;overlay.Show();overlay.Hide();}
        target=c;restoreTargetTimer.Stop();
        if(!automatic){
            try{using(var process=Process.GetProcessById((int)c.Pid))rememberedExe=ProcessPath(process);}
            catch(ArgumentException){rememberedExe="";}
            rememberedTitle=c.Title;rememberedGame=CaptureFiles.Game(c);SaveSettings();
        }
        enabled=true;frames=0;sampleFrames=0;workMs=0;rate.Reset();if(timer==null){timer=new DispatcherTimer(DispatcherPriority.Background);timer.Tick+=delegate{Tick();};}timer.Interval=TimeSpan.FromMilliseconds(1000.0/(int)fps.SelectedItem);timer.Start();Log((automatic?"Reconocida: ":"Seleccionada: ")+c.Title);Tick();
    }catch(Exception ex){enabled=false;if(overlay!=null)overlay.Hide();status.Text="No se pudo iniciar: "+ex.Message;Log(status.Text);}}
    void ReleaseGpu(){
        if(gpuCapture==null)return;
        if(overlay!=null)overlay.Image.Source=null;
        gpuCapture.Dispose();gpuCapture=null;
    }
    void ReleaseFrame(){
        ReleaseGpu();
        if(bitmap!=null){bitmap.Dispose();bitmap=null;}
        wb=null;pixels=null;bw=bh=0;hasFrame=false;displayBounds=SD.Rectangle.Empty;
        if(overlay!=null){overlay.Hide();overlay.Image.Source=null;}
    }
    void Pause(string text){if(overlay!=null)overlay.Hide();ReleaseGpu();hasFrame=false;rate.Reset();sampleFrames=0;nextMetrics=0;if(status.Text!=text)status.Text=text;UpdateState(enabled?"EN ESPERA":"DESACTIVADO",false);}
    void Tick(){if(closing||target==null)return;try{
        uint pid;Native.GetWindowThreadProcessId(target.H,out pid);
        if(!Native.IsWindow(target.H)||pid!=target.Pid){Pause("La ventana seleccionada dejó de estar disponible.");enabled=false;ReleaseFrame();target=null;if(rememberedExe.Length!=0)restoreTargetTimer.Start();return;}
        if(!enabled){Pause("Efecto desactivado. El juego sigue mostrando su imagen original.");return;}
        if(Native.IsIconic(target.H)){Pause("Captura pausada: ventana minimizada.");return;}
        luna.Refresh();
        var fg=Native.GetForegroundWindow();if(Native.GetAncestor(fg,2)!=target.H&&!luna.Owns(fg)){Pause("En espera: vuelve al juego.");return;}
        if(recoveryClock.ElapsedMilliseconds<retryCaptureAt)return;
        var client=BorderlessWindow.ClientBounds(target.H);
        var bounds=SD.Rectangle.Intersect(client,WF.Screen.FromHandle(target.H).Bounds);
        if(bounds.Width<=1||bounds.Height<=1){Pause("El área del juego está fuera del monitor.");return;}
        bool desktop=captureQuality.SelectedIndex==3;
        int limit=captureQuality.SelectedIndex==0?540:captureQuality.SelectedIndex==1?720:bounds.Height;
        double factor=desktop?1:Math.Min(1,limit/(double)bounds.Height);
        int w=Math.Max(2,(int)Math.Round(bounds.Width*factor)),h=Math.Max(2,(int)Math.Round(bounds.Height*factor));
        var watch=Stopwatch.StartNew();
        if(!rate.IsRunning)rate.Start();
        if(w!=bw||h!=bh){
            ReleaseFrame();bw=w;bh=h;
            if(desktop){
                overlay.UseDirectVideo(false);
                bitmap=new SD.Bitmap(w,h,SD.Imaging.PixelFormat.Format32bppArgb);
                pixels=new byte[checked(w*h*4)];
                wb=new WriteableBitmap(w,h,96,96,PixelFormats.Bgra32,null);
                shader.Step=new Point(1.0/w,1.0/h);
                overlay.Image.Effect=shader;overlay.Image.Source=wb;
            }
            Log("Área física="+bounds.Width+"x"+bounds.Height+"; búfer="+w+"x"+h+"; posición="+bounds.X+","+bounds.Y);
        }
        displayBounds=bounds;
        bool fresh;
        if(desktop) {
            uint affinity;if(!Native.GetWindowDisplayAffinity(overlay.Hwnd,out affinity)||affinity!=0x11)throw new Exception("Se perdió la exclusión de captura.");
            using(var g=SD.Graphics.FromImage(bitmap))g.CopyFromScreen(bounds.X,bounds.Y,0,0,bounds.Size,SD.CopyPixelOperation.SourceCopy);
            var data=bitmap.LockBits(new SD.Rectangle(0,0,w,h),SD.Imaging.ImageLockMode.ReadOnly,SD.Imaging.PixelFormat.Format32bppArgb);
            try{Marshal.Copy(data.Scan0,pixels,0,pixels.Length);}finally{bitmap.UnlockBits(data);}
            for(int i=3;i<pixels.Length;i+=4)pixels[i]=255;
            wb.WritePixels(new Int32Rect(0,0,w,h),pixels,w*4,0);
            fresh=true;
        } else {
            if(gpuCapture==null){
                gpuCapture=new WindowCapture(target.H);
                overlay.UseDirectVideo(true);
                overlay.Image.Effect=null;
                firstFrame=Stopwatch.StartNew();
            }
            var filter=new WindowCapture.Filter {
                Sharp=(float)amount.Value, Brightness=(float)brightness.Value, Contrast=(float)contrast.Value,
                Saturation=(float)saturation.Value, Warmth=(float)warmth.Value, Gamma=(float)gamma.Value
            };
            fresh=gpuCapture.Present(overlay.VideoHwnd,bounds,w,h,filter);
        }
        if(fresh){
            hasFrame=true;frames++;sampleFrames++;workMs+=watch.Elapsed.TotalMilliseconds;
        }
        if(!hasFrame) {
            if(firstFrame!=null&&firstFrame.ElapsedMilliseconds>3500)
                throw new Exception("La ventana no entregó imagen por GPU. Prueba Escritorio · Compatibilidad.");
            status.Text="Esperando el primer fotograma…";return;
        }
        overlay.ProtectLuna(bounds,luna.Rectangles);
        overlay.Place(bounds.X,bounds.Y,bounds.Width,bounds.Height);
        UpdateState("ACTIVO",true);
        const string active="Filtro aplicado a la ventana seleccionada.";
        if(status.Text!=active)status.Text=active;
        if(fresh&&rate.ElapsedMilliseconds>=nextMetrics){
            nextMetrics=rate.ElapsedMilliseconds+250;
            metrics.Text=String.Format("{0} · {1}×{2} px · {3:F1} presentaciones/s · {4:F1} ms por presentación en el hilo de la interfaz.\nLuna Translator: {5} áreas protegidas.",desktop?"Escritorio":"GPU directa",w,h,sampleFrames/Math.Max(.001,rate.Elapsed.TotalSeconds),workMs/frames,overlay.LunaAreas);
        }
    }catch(Exception ex){
        long now=recoveryClock.ElapsedMilliseconds;
        captureFailures=now-lastCaptureFailure>30000?1:captureFailures+1;
        lastCaptureFailure=now;
        ReleaseFrame();
        if(captureQuality.SelectedIndex!=3&&captureFailures<=3){
            retryCaptureAt=now+Math.Min(2000,captureFailures*400);
            status.Text="Recuperando el filtro…";
            UpdateState("RECUPERANDO",false);
            Log("Recuperación automática: reiniciar superficie GPU; intento="+captureFailures+"; "+ex.Message);
        }else{
            enabled=false;
            status.Text="FALLO DE CAPTURA: "+ex.Message+" — vuelve a activar el filtro.";
            UpdateState("DESACTIVADO",false);
            Log(status.Text);
        }
    }}
    void Diagnostic(){if(overlay==null||!overlay.IsVisible||!hasFrame){status.Text="Diagnóstico: el juego debe estar al frente al terminar los 3 segundos.";Log(status.Text);return;}timer.Stop();try{
        string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"diagnostico");Directory.CreateDirectory(dir);
        byte[] original=pixels;
        if(gpuCapture!=null){
            original=new byte[checked(bw*bh*4)];
            if(!gpuCapture.ReadCurrent(original,bw,bh))throw new Exception("No hay un fotograma disponible para el diagnóstico.");
        }
        var raw=BitmapSource.Create(bw,bh,96,96,PixelFormats.Bgra32,null,original,bw*4);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(raw));
        using(var output=File.Create(Path.Combine(dir,"original.png")))png.Save(output);
        // Temporarily remove exclusion ONLY with capture stopped; never feed this image back.
        if(!Native.SetWindowDisplayAffinity(overlay.Hwnd,0))throw new Exception("No se pudo preparar diagnóstico.");
        Native.DwmFlush();System.Threading.Thread.Sleep(100);var area=displayBounds;
        using(var b=new SD.Bitmap(area.Width,area.Height)) {using(var g=SD.Graphics.FromImage(b))g.CopyFromScreen(area.X,area.Y,0,0,area.Size);b.Save(Path.Combine(dir,"pantalla-filtrada.png"),SD.Imaging.ImageFormat.Png);}
        var probe=new Native.POINT{X=area.X+area.Width/2,Y=area.Y+area.Height/2};var hit=Native.WindowFromPoint(probe);var hitRoot=Native.GetAncestor(hit,2);
        File.WriteAllText(Path.Combine(dir,"estado.txt"),status.Text+"\n"+hotkeyStatus.Text+"\nEstilo visual="+preset.SelectedItem+"\nNitidez="+amount.Value+" Brillo="+brightness.Value+" Contraste="+contrast.Value+" Saturación="+saturation.Value+" Temperatura="+warmth.Value+" Gamma="+gamma.Value+"\nGPU tier="+(RenderCapability.Tier>>16)+"\nDPI="+Native.GetDpiForWindow(target.H)+"\nFrames="+frames+"\nForeground="+Native.GetForegroundWindow()+"\nTarget="+target.H+"\nOverlay="+overlay.Hwnd+"\nRaíz receptora bajo cursor de prueba="+hitRoot+"\nPaso de entrada calculado="+(hitRoot==target.H)+" (no sustituye un clic real)\nEstilo="+Native.GetWindowLong(overlay.Hwnd,-20).ToString("X"));Log("Diagnóstico guardado; frames="+frames);
    }catch(Exception e){Log("Diagnóstico: "+e.Message);}finally{if(!Native.SetWindowDisplayAffinity(overlay.Hwnd,0x11)){enabled=false;overlay.Hide();Log("Se detuvo: no se pudo restaurar exclusión.");}timer.Start();}}
}

class TestWindow : Window {
    int clicks,keys,ticks; TextBlock info; DispatcherTimer timer; bool full; WindowStyle savedStyle; Rect saved;
    public TestWindow(){Title="Prueba VN — imagen dinámica y entrada";Width=900;Height=620;Left=90;Top=90;Background=Brushes.White;
        var root=new StackPanel{Margin=new Thickness(35)};Content=root;
        root.Children.Add(new TextBlock{Text="Escena de prueba de nitidez",FontSize=34,Foreground=Brushes.DarkSlateBlue});
        root.Children.Add(new TextBlock{Text="Texto pequeño: La lluvia dibuja líneas sobre la ventana. 0123456789",FontSize=13});
        var pattern=new SD.Bitmap(780,180);using(var g=SD.Graphics.FromImage(pattern)){g.Clear(SD.Color.Beige);for(int x=0;x<780;x++){int c=(int)(125+60*Math.Sin(x*.6));using(var pen=new SD.Pen(SD.Color.FromArgb(c,c,c)))g.DrawLine(pen,x,0,x,180);}g.DrawString("Novela visual · bordes y textura",new SD.Font("Segoe UI",24),SD.Brushes.SteelBlue,20,65);}
        using(var ms=new MemoryStream()){pattern.Save(ms,SD.Imaging.ImageFormat.Png);ms.Position=0;var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=ms;image.EndInit();root.Children.Add(new Image{Source=image,Width=780,Height=180,Stretch=Stretch.None,HorizontalAlignment=HorizontalAlignment.Left});}pattern.Dispose();
        var button=new Button{Content="Avanzar diálogo (clic aquí)",Height=50,Margin=new Thickness(0,20,0,10)};button.Click+=delegate{clicks++;Update();};root.Children.Add(button);
        info=new TextBlock{FontSize=19};root.Children.Add(info);root.Children.Add(new TextBlock{Text="Espacio: avanzar con teclado · F11: alternar sin bordes · Esc: salir de sin bordes"});
        KeyDown+=delegate(object s,System.Windows.Input.KeyEventArgs e){if(e.Key==System.Windows.Input.Key.F11)Borderless();else if(e.Key==System.Windows.Input.Key.Escape&&full)Borderless();else {keys++;Update();}};
        timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};timer.Tick+=delegate{ticks++;Update();};timer.Start();Closed+=delegate{timer.Stop();};Update();
    }
    void Update(){info.Text="Clics: "+clicks+" · Teclas: "+keys+" · Fotograma: "+ticks+"\n"+new string('▰',ticks%24+1);}
    void Borderless(){if(!full){saved=new Rect(Left,Top,Width,Height);savedStyle=WindowStyle;var h=new WindowInteropHelper(this).Handle;var b=WF.Screen.FromHandle(h).Bounds;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;Native.SetWindowPos(h,IntPtr.Zero,b.X,b.Y,b.Width,b.Height,0x14);full=true;}else{WindowStyle=savedStyle;ResizeMode=ResizeMode.CanResize;Left=saved.X;Top=saved.Y;Width=saved.Width;Height=saved.Height;full=false;}}
}

static class Program {
    [STAThread] public static void Main(string[] args){
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
        var app=new Application();app.ShutdownMode=ShutdownMode.OnMainWindowClose;
        app.DispatcherUnhandledException+=delegate(object s,DispatcherUnhandledExceptionEventArgs e){MessageBox.Show(e.Exception.Message,"Nitidez VN — error");e.Handled=true;app.Shutdown(1);};
        app.Run((args.Length>0&&args[0]=="--test")||Process.GetCurrentProcess().ProcessName=="PruebaVN"?(Window)new TestWindow():new ControlWindow());
    }
}
