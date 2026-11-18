using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

static class QualityEncodingProbe {
    static double Similarity(string encoded,string reference){
        var info=new ProcessStartInfo(AudioFiles.FindFfmpeg(),"-hide_banner -i "+SceneFiles.Quote(encoded)+" -loop 1 -framerate 60 -i "+SceneFiles.Quote(reference)+" -filter_complex "+SceneFiles.Quote("[0:v]scale=out_color_matrix=bt709:out_range=tv,format=yuv420p[a];[1:v]scale=out_color_matrix=bt709:out_range=tv,format=yuv420p[b];[a][b]ssim=shortest=1")+" -an -f null NUL"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
        using(var process=Process.Start(info)){
            var error=process.StandardError.ReadToEndAsync();if(!process.WaitForExit(20000)){process.Kill();throw new Exception("No terminó la comparación de calidad.");}
            var match=Regex.Match(error.Result,@"All:([0-9.]+)");if(process.ExitCode!=0||!match.Success)throw new Exception(error.Result);
            return Double.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture);
        }
    }
    static int Main(string[] args){
        try{
            string folder=Path.GetFullPath(args[0]);Directory.CreateDirectory(folder);string reference=Path.Combine(folder,"Referencia.png");
            using(var image=new Bitmap(1280,720))using(var graphics=Graphics.FromImage(image)){
                using(var brush=new LinearGradientBrush(new Rectangle(0,0,1280,720),Color.FromArgb(42,36,68),Color.FromArgb(225,189,196),35))graphics.FillRectangle(brush,0,0,1280,720);
                for(int x=0;x<1280;x+=3)using(var pen=new Pen(Color.FromArgb(50,220,220,245)))graphics.DrawLine(pen,x,0,1280-x,719);
                using(var small=new Font("Segoe UI",11))using(var large=new Font("Segoe UI",27)){
                    graphics.DrawString("Novela visual — detalle, texto y color",large,Brushes.White,35,25);
                    for(int row=0;row<26;row++)graphics.DrawString("Texto pequeño: La lluvia dibuja líneas sobre la ventana. 0123456789 — ABCDEFG",small,row%2==0?Brushes.White:Brushes.Black,35,100+row*22);
                }
                image.Save(reference,System.Drawing.Imaging.ImageFormat.Png);
            }
            string old=Path.Combine(folder,"Anterior-10Mbps.mp4"),current=Path.Combine(folder,"Calidad-alta.mp4");
            string input="-loop 1 -framerate 60 -i "+SceneFiles.Quote(reference)+" -t 1 -an ";
            SceneFiles.RunFile(input+"-c:v h264_nvenc -preset p3 -tune ll -rc cbr -b:v 10M -maxrate 10M -bufsize 10M -bf 0 -pix_fmt yuv420p -f mp4",old,20000);
            SceneFiles.RunFile(input+SceneFiles.VideoQualityEncoding+" -g 120 -surfaces 6 -zerolatency 1 -f mp4",current,20000);
            double baseline=Similarity(old,reference),updated=Similarity(current,reference);
            if(updated<=baseline)throw new Exception("El perfil nuevo no mejoró la referencia controlada.");
            Console.WriteLine("PASS: misma referencia de texto y líneas; SSIM anterior {0:F6}, nuevo {1:F6}. MP4 H.264 compatible, sin cambiar resolución ni FPS.",baseline,updated);return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
