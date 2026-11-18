using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Snapshot the six effect controls. The hook uses the same order, kernel and
// coefficients as Sharpen, before scaling or encoding the final image.
sealed class SceneLook {
    readonly double sharp, brightness, contrast, saturation, warmth, gamma;
    public WindowCapture.Filter NativeFilter {
        get { return new WindowCapture.Filter{Sharp=(float)sharp,Brightness=(float)brightness,Contrast=(float)contrast,
            Saturation=(float)saturation,Warmth=(float)warmth,Gamma=(float)gamma}; }
    }
    public SceneLook(double sharp, double brightness, double contrast, double saturation, double warmth, double gamma) {
        this.sharp = sharp; this.brightness = brightness; this.contrast = contrast;
        this.saturation = saturation; this.warmth = warmth; this.gamma = gamma;
    }
    public string Filter(bool video) {
        string hook = String.Format(CultureInfo.InvariantCulture, @"//!HOOK MAIN
//!BIND HOOKED
vec4 hook() {{
    vec3 c = HOOKED_tex(HOOKED_pos).rgb;
    vec3 n = HOOKED_texOff(vec2(1,0)).rgb + HOOKED_texOff(vec2(-1,0)).rgb
           + HOOKED_texOff(vec2(0,1)).rgb + HOOKED_texOff(vec2(0,-1)).rgb;
    vec3 rgb = clamp(c + float({0:R}) * (c - n * 0.25), 0.0, 1.0);
    rgb = clamp((rgb - 0.5) * float({2:R}) + 0.5 + float({1:R}), 0.0, 1.0);
    float lum = dot(rgb, vec3(0.2126, 0.7152, 0.0722));
    rgb = mix(vec3(lum), rgb, float({3:R})) + float({4:R}) * vec3(1.0, 0.15, -1.0);
    rgb = pow(max(clamp(rgb, 0.0, 1.0), vec3(0.00001)), vec3(1.0 / float({5:R})));
    return vec4(clamp(rgb, 0.0, 1.0), 1.0);
}}
", sharp, brightness, contrast, saturation, warmth, gamma);
        string hex = BitConverter.ToString(Encoding.UTF8.GetBytes(hook)).Replace("-", "");
        string format = video ? "yuv420p:colorspace=bt709:color_primaries=bt709:range=tv" : "bgra:range=pc";
        return "libplacebo=custom_shader_bin=" + hex + ":format=" + format;
    }
}

static class SceneFiles {
    public const string CompatibleEncoding="-c:v h264_nvenc -preset p3 -tune ll -bf 0 -rc vbr -cq 18 -b:v 0 -pix_fmt yuv420p -c:a aac -b:a 160k";
    public const long PreferredVideoBytes = 30000000;
    public static readonly string Videos = Path.Combine(AudioFiles.Folder, "Videos");
    public static readonly string Screenshots = Path.Combine(AudioFiles.Folder, "Capturas");
    [DllImport("WindowCapture.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int NVRecycle(IntPtr owner, string path);

    public static void Recycle(IntPtr owner, string file) {
        if (!String.Equals(Path.GetExtension(file), ".mp4", StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
            throw new IOException("No se encontró el MP4 seleccionado.");
        if (new DriveInfo(Path.GetPathRoot(file)).DriveType != DriveType.Fixed)
            throw new IOException("El vídeo debe estar en un disco local con Papelera.");
        Marshal.ThrowExceptionForHR(NVRecycle(owner, file));
        if (File.Exists(file)) throw new IOException("Windows dejó el archivo en su carpeta.");
    }

    public static string Output(string folder, string kind, string extension) {
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, kind + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + "_" +
            Guid.NewGuid().ToString("N").Substring(0, 6) + extension);
    }
    public static string Quote(string value) { return "\"" + value + "\""; }
    public static string Number(double value) { return value.ToString("0.######", CultureInfo.InvariantCulture); }

    public static string Source(IntPtr window, int limit, int captureFps = 30) {
        string source = "gfxcapture=hwnd=" + unchecked((ulong)window.ToInt64()) +
            ":capture_border=0:capture_cursor=0:display_border=0:max_framerate=" + captureFps;
        if (limit != 0) {
            Native.RECT rect;
            if (!Native.GetClientRect(window, out rect)) throw new IOException("La ventana ya no está disponible.");
            int width = rect.R - rect.L, height = rect.B - rect.T;
            if (width < 2 || height < 2) throw new IOException("El área del juego está vacía.");
            double factor = Math.Min(1, Math.Min(limit / (double)height, (limit * 16.0 / 9) / width));
            int w = Math.Max(2, (int)(width * factor) & ~1), h = Math.Max(2, (int)(height * factor) & ~1);
            source += ":width=" + w + ":height=" + h + ":resize_mode=scale_aspect";
        }
        return source + ",hwdownload,format=bgra";
    }

    public static Process Start(string args, bool stdin) {
        var start = new ProcessStartInfo(AudioFiles.FindFfmpeg(),
            "-hide_banner -nostats -loglevel error -abort_on empty_output+empty_output_stream -filter_threads 2 -filter_complex_threads 2 -n " + args) {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true,
            RedirectStandardOutput = true, RedirectStandardInput = stdin
        };
        return Process.Start(start);
    }

    public static void FinishFile(string temp, string file) {
        if (!File.Exists(temp) || new FileInfo(temp).Length < 256) throw new IOException("No se recibieron fotogramas.");
        File.Move(temp, file);
    }

    public static void RunFile(string args, string file, int timeout,CancellationToken cancellation=default(CancellationToken)) {
        string temp = file + ".part";
        try {
            using (var process = Start("-nostdin " + args + " " + Quote(temp), false)) {
                var error = process.StandardError.ReadToEndAsync();
                process.BeginOutputReadLine();
                try {
                    var clock=Stopwatch.StartNew();
                    while(!process.WaitForExit(100)){cancellation.ThrowIfCancellationRequested();if(clock.ElapsedMilliseconds>timeout)throw new TimeoutException("FFmpeg no terminó a tiempo.");}
                    cancellation.ThrowIfCancellationRequested();
                    if (process.ExitCode != 0) throw Failure(error.Result);
                } finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }
            }
            FinishFile(temp, file);
        } finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static IOException Failure(string message) {
        return new IOException(String.IsNullOrWhiteSpace(message) ? "FFmpeg no pudo completar la captura." : message.Trim());
    }

    public static void Screenshot(string source, SceneLook look, string path) {
        RunFile("-filter_complex " + Quote(source + "," + look.Filter(false) + ",format=rgb24") +
            " -frames:v 1 -threads:v 1 -c:v png -f image2 -update 1", path, 12000);
    }

    public static void Trim(string source, string dest, double start, double end) {
        if(end<=start)throw new IOException("El tramo elegido está vacío.");
        RunFile("-ss "+Number(start)+" -i "+Quote(source)+" -t "+Number(end-start)+" -map 0:v:0 -map 0:a:0? "+CompatibleEncoding+" -fps_mode passthrough -movflags +faststart -f mp4",dest,1800000);
    }
    public static void Preview(string source,string dest,CancellationToken cancellation){
        RunFile("-i "+Quote(source)+" -map 0:v:0 -map 0:a:0? -vf "+Quote("scale=-2:'min(ih,540)'")+" -c:v libx264 -preset ultrafast -crf 26 -pix_fmt yuv420p -threads:v 2 -c:a aac -b:a 96k -movflags +faststart -f mp4",dest,1800000,cancellation);
    }

    public static double Duration(string source, double fallback) {
        try {
            string ffprobe = Path.Combine(Path.GetDirectoryName(AudioFiles.FindFfmpeg()), "ffprobe.exe");
            var info = new ProcessStartInfo(ffprobe,
                "-v error -show_entries format=duration -of default=nw=1:nk=1 " + Quote(source)) {
                CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true
            };
            using (var process = Process.Start(info)) {
                var output = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(4000)) { process.Kill(); return fallback; }
                double seconds;
                if (process.ExitCode == 0 && Double.TryParse(output.Result.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)
                    && seconds > 0 && !Double.IsInfinity(seconds)) return seconds;
            }
        } catch (IOException) {} catch (System.ComponentModel.Win32Exception) {}
        return fallback;
    }
}
