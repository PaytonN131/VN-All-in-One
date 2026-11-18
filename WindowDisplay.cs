using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using SD = System.Drawing;
using WF = System.Windows.Forms;

sealed class WindowCapture : IDisposable {
    [StructLayout(LayoutKind.Sequential)]
    public struct Filter {
        public float Sharp, Brightness, Contrast, Saturation, Warmth, Gamma, StepX, StepY;
    }
    [DllImport("WindowCapture.dll")] static extern int NVCreate(IntPtr window, out IntPtr capture);
    [DllImport("WindowCapture.dll")] static extern int NVRead(IntPtr capture, Native.RECT area, [Out] byte[] pixels, uint width, uint height);
    [DllImport("WindowCapture.dll")] static extern int NVReadCurrent(IntPtr capture, [Out] byte[] pixels, uint width, uint height);
    [DllImport("WindowCapture.dll")] static extern int NVPresent(IntPtr capture, IntPtr host, Native.RECT area,
        uint width, uint height, ref Filter settings);
    [DllImport("WindowCapture.dll")] static extern void NVClose(IntPtr capture);
    [DllImport("WindowCapture.dll")] static extern int NVSnapshot(IntPtr capture,Native.RECT area,[Out] byte[] pixels,uint width,uint height,ref Filter filter);
    IntPtr session;

    public WindowCapture(IntPtr window) { Marshal.ThrowExceptionForHR(NVCreate(window, out session)); }

    public bool Read(SD.Rectangle area, byte[] pixels, int width, int height) {
        var rect = new Native.RECT { L = area.Left, T = area.Top, R = area.Right, B = area.Bottom };
        int hr = NVRead(session, rect, pixels, (uint)width, (uint)height);
        Marshal.ThrowExceptionForHR(hr);
        return hr == 0;
    }

    public bool Present(IntPtr host, SD.Rectangle area, int width, int height, Filter settings) {
        var rect = new Native.RECT { L = area.Left, T = area.Top, R = area.Right, B = area.Bottom };
        int hr = NVPresent(session, host, rect, (uint)width, (uint)height, ref settings);
        Marshal.ThrowExceptionForHR(hr);
        return hr == 0;
    }

    public bool ReadCurrent(byte[] pixels, int width, int height) {
        int hr = NVReadCurrent(session, pixels, (uint)width, (uint)height);
        Marshal.ThrowExceptionForHR(hr);
        return hr == 0;
    }
    public bool Snapshot(SD.Rectangle area,byte[] pixels,int width,int height,Filter settings){
        var rect=new Native.RECT{L=area.Left,T=area.Top,R=area.Right,B=area.Bottom};
        int hr=NVSnapshot(session,rect,pixels,(uint)width,(uint)height,ref settings);
        Marshal.ThrowExceptionForHR(hr);return hr==0;
    }

    public void Dispose() {
        if (session == IntPtr.Zero) return;
        NVClose(session);
        session = IntPtr.Zero;
    }
}

sealed class BorderlessWindow {
    const int StyleIndex = -16, Caption = 0x00C00000, Frame = 0x00040000;
    const int OverlappedWindow = 0x00CF0000, Popup = unchecked((int)0x80000000);
    const uint FrameChanged = 0x20, NoActivate = 0x10, NoZOrder = 0x4;

    [StructLayout(LayoutKind.Sequential)] struct Placement {
        public uint Length, Flags, ShowCommand;
        public Native.POINT Min, Max;
        public Native.RECT Normal;
    }
    [DllImport("user32.dll", SetLastError = true)] static extern bool GetWindowPlacement(IntPtr h, ref Placement placement);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowPlacement(IntPtr h, ref Placement placement);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
    [DllImport("kernel32.dll")] static extern void SetLastError(uint error);

    Choice target;
    Placement previous;
    int previousStyle; SD.Rectangle originalClient;
    [DllImport("user32.dll")] static extern bool AdjustWindowRectExForDpi(ref Native.RECT rect,uint style,bool menu,uint exStyle,uint dpi);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
    public bool Active { get { return target != null; } }

    public static SD.Rectangle ClientBounds(IntPtr hwnd) {
        Native.RECT rect;
        var point = new Native.POINT();
        if (!Native.GetClientRect(hwnd, out rect) || !Native.ClientToScreen(hwnd, ref point))
            throw new InvalidOperationException("No se pudo obtener el área del juego.");
        return new SD.Rectangle(point.X, point.Y, rect.R - rect.L, rect.B - rect.T);
    }

    static void ChangeStyle(IntPtr h, int style) {
        SetLastError(0);
        int old = Native.SetWindowLong(h, StyleIndex, style);
        int error = Marshal.GetLastWin32Error();
        if (old == 0 && error != 0) throw new Win32Exception(error);
    }

    public void Apply(Choice game) { Apply(game,3); }
    public void Apply(Choice game,int mode) {
        if(mode<1||mode>3)throw new ArgumentOutOfRangeException("mode");
        if(Active&&(target.H!=game.H||target.Pid!=game.Pid))Restore();
        uint pid; Native.GetWindowThreadProcessId(game.H,out pid);
        if(pid!=game.Pid||!Native.IsWindow(game.H)||Native.IsIconic(game.H))throw new InvalidOperationException("La ventana seleccionada no está disponible.");
        if(!Active){
            previous=new Placement{Length=(uint)Marshal.SizeOf(typeof(Placement))};
            if(!GetWindowPlacement(game.H,ref previous))throw new Win32Exception(Marshal.GetLastWin32Error());
            previousStyle=Native.GetWindowLong(game.H,StyleIndex);
            originalClient=ClientBounds(game.H);
            if(originalClient.Width<2||originalClient.Height<2)throw new InvalidOperationException("Área del juego vacía.");
            target=game;
        }
        try {
            if(IsZoomed(game.H))Native.ShowWindow(game.H,9);
            var screen=WF.Screen.FromHandle(game.H);
            var monitor=mode==3?screen.Bounds:screen.WorkingArea;
            int style=mode==1?(previousStyle&~Popup)|OverlappedWindow:(previousStyle&~OverlappedWindow)|Popup;
            Native.RECT frame=new Native.RECT();
            if(mode==1&&!AdjustWindowRectExForDpi(ref frame,(uint)style,false,(uint)Native.GetWindowLong(game.H,-20),GetDpiForWindow(game.H)))throw new Win32Exception(Marshal.GetLastWin32Error());
            int extraWidth=frame.R-frame.L,extraHeight=frame.B-frame.T;
            double factor=Math.Min((monitor.Width-extraWidth)/(double)originalClient.Width,(monitor.Height-extraHeight)/(double)originalClient.Height);
            if(mode!=3)factor=Math.Min(1,factor);
            int width=Math.Max(2,(int)Math.Round(originalClient.Width*factor)),height=Math.Max(2,(int)Math.Round(originalClient.Height*factor));
            int x=mode==3?monitor.Left+(monitor.Width-width)/2:Math.Max(monitor.Left-frame.L,Math.Min(originalClient.X,monitor.Right-width-frame.R));
            int y=mode==3?monitor.Top+(monitor.Height-height)/2:Math.Max(monitor.Top-frame.T,Math.Min(originalClient.Y,monitor.Bottom-height-frame.B));
            ChangeStyle(game.H,style);
            if(!Native.SetWindowPos(game.H,IntPtr.Zero,x+frame.L,y+frame.T,width+extraWidth,height+extraHeight,FrameChanged|NoActivate|NoZOrder))throw new Win32Exception(Marshal.GetLastWin32Error());
            var actual=ClientBounds(game.H);
            if(Math.Abs(actual.Width-width)>2||Math.Abs(actual.Height-height)>2||Math.Abs(actual.X-x)>2||Math.Abs(actual.Y-y)>2)throw new InvalidOperationException("El juego no aceptó este modo. Se restauró la ventana original; cambia el modo desde las opciones del juego.");
        }catch{Restore();throw;}
    }
    public void Restore() {
        if (target == null) return;
        IntPtr hwnd = target.H;
        uint pid;
        Native.GetWindowThreadProcessId(hwnd, out pid);
        if (pid != target.Pid || !Native.IsWindow(hwnd)) { target = null; return; }
        ChangeStyle(hwnd, previousStyle);
        if (!SetWindowPlacement(hwnd, ref previous)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!Native.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, FrameChanged | NoActivate | NoZOrder | 0x3))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        target = null;
    }
}
