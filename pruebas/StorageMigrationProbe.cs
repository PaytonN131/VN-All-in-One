using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

static class StorageMigrationProbe {
    static string Hash(string file){using(var stream=File.OpenRead(file))using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(stream));}
    static int Main(string[] args){
        try{
            if(args.Length>1&&args[1]=="--apply"){
                string storage=Path.GetFullPath(args[0]);string oldFolder=Path.Combine(storage,"Videos"),newFolder=Path.Combine(storage,"Clips");
                var before=(Directory.Exists(oldFolder)?Directory.GetFiles(oldFolder,"*",SearchOption.AllDirectories):new string[0]).Concat(Directory.Exists(newFolder)?Directory.GetFiles(newFolder,"*",SearchOption.AllDirectories):new string[0]).Select(Hash).OrderBy(v=>v).ToArray();
                CaptureFiles.MigrateVideosToClips(storage);
                var after=(Directory.Exists(newFolder)?Directory.GetFiles(newFolder,"*",SearchOption.AllDirectories):new string[0]).Select(Hash).OrderBy(v=>v).ToArray();
                if(!before.SequenceEqual(after)||Directory.Exists(oldFolder))throw new Exception("No se completó la verificación de la carpeta renombrada.");
                Console.WriteLine("PASS: "+after.Length+" archivos conservados con SHA-256 idéntico; Videos renombrada a Clips.");return 0;
            }
            string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
            string atomic=Path.Combine(root,"renombrado");Directory.CreateDirectory(Path.Combine(atomic,"Videos","Novela"));
            string original=Path.Combine(atomic,"Videos","Novela","Escena.mp4");File.WriteAllText(original,"Conservar todo el contenido");string expected=Hash(original);
            CaptureFiles.MigrateVideosToClips(atomic);
            if(Directory.Exists(Path.Combine(atomic,"Videos"))||Hash(Path.Combine(atomic,"Clips","Novela","Escena.mp4"))!=expected)throw new Exception("El renombrado no conservó el archivo.");
            string merge=Path.Combine(root,"union");Directory.CreateDirectory(Path.Combine(merge,"Videos","Novela"));Directory.CreateDirectory(Path.Combine(merge,"Clips","Novela"));
            File.WriteAllText(Path.Combine(merge,"Videos","Novela","Escena.mp4"),"Version anterior");File.WriteAllText(Path.Combine(merge,"Clips","Novela","Escena.mp4"),"Version existente");
            CaptureFiles.MigrateVideosToClips(merge);
            var contents=Directory.GetFiles(Path.Combine(merge,"Clips","Novela")).Select(File.ReadAllText).OrderBy(v=>v).ToArray();
            if(contents.Length!=2||!contents.Contains("Version anterior")||!contents.Contains("Version existente"))throw new Exception("Se perdió un archivo al resolver el nombre repetido.");
            CaptureFiles.MigrateVideosToClips(merge);
            if(Directory.GetFiles(Path.Combine(merge,"Clips","Novela")).Length!=2)throw new Exception("La migración repetida duplicó archivos.");
            string conflict=Path.Combine(root,"conflicto");Directory.CreateDirectory(Path.Combine(conflict,"Videos"));File.WriteAllText(Path.Combine(conflict,"Clips"),"No reemplazar");
            bool refused=false;try{CaptureFiles.MigrateVideosToClips(conflict);}catch(IOException){refused=true;}
            if(!refused||!Directory.Exists(Path.Combine(conflict,"Videos"))||File.ReadAllText(Path.Combine(conflict,"Clips"))!="No reemplazar")throw new Exception("No se protegió el destino existente.");
            Console.WriteLine("PASS: Videos → Clips, hashes conservados, unión sin sobrescritura, operación repetible y conflicto protegido.");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
