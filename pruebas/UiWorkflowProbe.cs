using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Diagnostics;

static class UiWorkflowProbe {
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
    static void Press(TextBox box,ControlWindow window,Key key){
        box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,key){RoutedEvent=Keyboard.PreviewKeyDownEvent});
    }
    [STAThread] static int Main(string[] args){
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
        new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        ControlWindow window=null;
        Window source=null;
        string shotFolder=null;
        try{
            window=new ControlWindow{ShowActivated=false,ShowInTaskbar=false};window.Show();Pump();
            var ui=(FrameworkElement)window.Content;
            var tabs=(TabControl)ui.FindName("Sections");
            if(tabs.Items.Count!=6)throw new Exception("Faltan paneles laterales.");
            var list=(ComboBox)ui.FindName("Windows");list.Items.Clear();list.Items.Add(new Choice{Title="Novela visual — ejemplo"});list.SelectedIndex=0;
            string folder=Path.GetFullPath(args[0]);Directory.CreateDirectory(folder);
            for(int compact=0;compact<2;compact++){
                window.Width=compact==0?1080:880;window.Height=compact==0?860:620;
                for(int i=0;i<6;i++){
                    tabs.SelectedIndex=i;Pump();ui.UpdateLayout();
                    var tab=(TabItem)tabs.Items[i];var toggle=(Button)ui.FindName("Toggle");
                    var navRect=new Rect(tab.TranslatePoint(new Point(),ui),new Size(tab.ActualWidth,tab.ActualHeight));
                    var toggleRect=new Rect(toggle.TranslatePoint(new Point(),ui),new Size(toggle.ActualWidth,toggle.ActualHeight));
                    if(navRect.IntersectsWith(toggleRect))throw new Exception("El menú se superpone al botón +.");
                    var bmp=new RenderTargetBitmap((int)ui.ActualWidth,(int)ui.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(ui);
                    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));
                    using(var output=File.Create(Path.Combine(folder,(compact==0?"panel-":"compacto-")+i+".png")))encoder.Save(output);
                }
            }
            var start=(TextBox)ui.FindName("VideoStartKey");var photo=(TextBox)ui.FindName("ScreenshotKey");
            string before=photo.Text;
            Press(start,window,Key.F6);
            if(!start.Text.Contains("F6"))throw new Exception("No se capturó la tecla.");
            Press(photo,window,Key.F6);
            if(photo.Text!=before||!((TextBlock)ui.FindName("ShortcutStatus")).Text.Contains("ya se usa"))throw new Exception("No se rechazó el atajo duplicado.");
            typeof(ControlWindow).GetMethod("SaveSettings",Private).Invoke(window,null);
            window.Close();window=new ControlWindow{ShowActivated=false,ShowInTaskbar=false};window.Show();Pump();ui=(FrameworkElement)window.Content;
            if(!((TextBox)ui.FindName("VideoStartKey")).Text.Contains("F6"))throw new Exception("No se conservó el atajo al reabrir.");
            ((Button)ui.FindName("ResetKeys")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(((TextBox)ui.FindName("VideoStartKey")).Text!="{")throw new Exception("No se restauraron las teclas originales.");
            source=new Window{Title="Wagamama Ver1.0",Width=420,Height=300,ShowActivated=false,ShowInTaskbar=false,Background=System.Windows.Media.Brushes.Gray};source.Show();Pump();
            var choice=new Choice{Title=source.Title,H=new WindowInteropHelper(source).Handle,Pid=(uint)Process.GetCurrentProcess().Id};
            list=(ComboBox)ui.FindName("Windows");list.Items.Clear();list.Items.Add(choice);list.SelectedItem=choice;
            if(((TextBox)ui.FindName("GameName")).Text!="Wagamama")throw new Exception("No se propuso el nombre del juego.");
            string game="_Prueba integrada 41 "+Guid.NewGuid().ToString("N").Substring(0,8);
            ((TextBox)ui.FindName("GameName")).Text=game;
            ((Button)ui.FindName("Start")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var scenes=typeof(ControlWindow).GetField("sceneControls",Private).GetValue(window);
            var prepared=(PreparedScene)typeof(SceneControls).GetField("prepared",Private).GetValue(scenes);
            var clock=Stopwatch.StartNew();while(!prepared.Ready&&clock.ElapsedMilliseconds<5000){Pump();System.Threading.Thread.Sleep(10);}
            if(!prepared.Ready)throw new Exception("No se preparó la captura integrada.");
            shotFolder=Path.Combine(AudioFiles.Folder,"Capturas",game);
            ((Button)ui.FindName("SceneScreenshot")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            clock.Restart();while((bool)typeof(SceneControls).GetField("capturing",Private).GetValue(scenes)&&clock.ElapsedMilliseconds<5000){Pump();System.Threading.Thread.Sleep(10);}
            string shot=Path.Combine(shotFolder,game+"_01.png");
            if(!File.Exists(shot)||new FileInfo(shot).Length<100)throw new Exception("No se guardó la captura integrada por juego: "+((TextBlock)ui.FindName("SceneStatus")).Text);
            Console.WriteLine("PASS: 6 paneles a 1080 y 880 px, menú sin superposición, atajos y persistencia; captura PNG real por juego, título limpio y nombre corregible.");
            return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally{if(source!=null)source.Close();if(window!=null){typeof(ControlWindow).GetField("rememberedExe",Private).SetValue(window,"");typeof(ControlWindow).GetField("rememberedTitle",Private).SetValue(window,"");window.Close();}if(shotFolder!=null&&Directory.Exists(shotFolder)){foreach(string file in Directory.GetFiles(shotFolder))File.Delete(file);Directory.Delete(shotFolder);}}
    }
}
