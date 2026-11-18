using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

static class StorageProbe {
    [STAThread] static int Main(){
        string game="_Prueba Nitidez 41 "+Guid.NewGuid().ToString("N").Substring(0,8);
        try{
            CaptureFiles.Initialize();
            if(CaptureFiles.SuggestedName("Wagamama Ver1.02 R18")!="Wagamama")throw new Exception("No se limpió la versión del título.");
            var choices=new[]{new Choice{Title="Wagamama Ver1.02 R18"},new Choice{Title="Título técnico",GameName="Hatsuyuki Sakura"}};
            if(CaptureFiles.Game(choices[0])!="Wagamama"||CaptureFiles.Game(choices[1])!="Hatsuyuki Sakura")throw new Exception("Nombre de juego incorrecto.");
            var shots=Enumerable.Range(0,8).Select(i=>Task.Run(()=>{using(var file=CaptureFiles.NewScreenshot(game)){file.WriteByte(42);return file.Name;}})).ToArray();
            Task.WaitAll(shots);
            if(shots.Select(t=>t.Result).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=8)throw new Exception("Se repitió un nombre de captura.");
            if(!shots.Any(t=>Path.GetFileName(t.Result)==game+"_01.png"))throw new Exception("La numeración no empezó en 01.");
            foreach(string category in new[]{"Grabaciones","Clips"}){
                string ext=category=="Clips"?".mp4":".mp3";
                string folder=CaptureFiles.Folder(category,game),source=Path.Combine(folder,"Original"+ext),existing=Path.Combine(folder,"Existente"+ext);
                File.WriteAllText(source,"original");File.WriteAllText(existing,"conservar");
                bool rejected=false;
                try{CaptureFiles.Rename(source,"Existente",category);}catch(IOException){rejected=true;}
                if(!rejected||File.ReadAllText(existing)!="conservar")throw new Exception("Se reemplazó un archivo existente.");
                string renamed=CaptureFiles.Rename(source,"Mi escena",category);
                if(File.Exists(source)||File.ReadAllText(renamed)!="original"||Path.GetDirectoryName(renamed)!=folder)throw new Exception("Renombrado incorrecto.");
                rejected=false;
                try{CaptureFiles.Rename(renamed,"..\\fuera",category);}catch(IOException){rejected=true;}
                if(!rejected)throw new Exception("Se permitió una ruta en el nombre.");
            }
            Console.WriteLine("PASS: carpetas por juego, títulos, 8 capturas concurrentes numeradas, renombrado MP3/MP4, extensión conservada y rechazo de sobrescritura/rutas.");
            Console.WriteLine("RAIZ="+AudioFiles.Folder);
            return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally{
            foreach(string category in new[]{"Capturas","Grabaciones","Clips"}){
                string folder=Path.Combine(AudioFiles.Folder,category,game);
                if(Directory.Exists(folder)){foreach(string file in Directory.GetFiles(folder))File.Delete(file);Directory.Delete(folder);}
            }
        }
    }
}
