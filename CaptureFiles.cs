using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

static class CaptureFiles {
    public static void Initialize(){foreach(string category in new[]{"Capturas","Grabaciones","Videos"})Directory.CreateDirectory(Path.Combine(AudioFiles.Folder,category));}
    public static string Name(string value){
        string name=Regex.Replace(value??"","\\s+"," ").Trim();
        foreach(char c in Path.GetInvalidFileNameChars())name=name.Replace(c,'_');
        name=name.Trim(' ','.');
        if(name.Length>80)name=name.Substring(0,80).TrimEnd(' ','.');
        if(name.Length==0)name="Juego";
        if(Regex.IsMatch(name,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)",RegexOptions.IgnoreCase))name="Juego - "+name;
        return name;
    }
    public static string SuggestedName(string title){
        return Name(Regex.Replace(title??"",@"\s+(?:ver\.?|version|v)\s*\d.*$","",RegexOptions.IgnoreCase));
    }
    public static string Game(Choice target){return Name(String.IsNullOrWhiteSpace(target.GameName)?SuggestedName(target.Title):target.GameName);}
    public static string Folder(string category,string game){
        if(category!="Capturas"&&category!="Grabaciones"&&category!="Videos")throw new ArgumentException("Categoría inválida.");
        string folder=Path.Combine(AudioFiles.Folder,category,Name(game));Directory.CreateDirectory(folder);return folder;
    }
    public static bool Inside(string file,string folder){
        return Path.GetFullPath(file).StartsWith(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    }
    public static FileStream NewScreenshot(string game){
        string name=Name(game),folder=Folder("Capturas",game);
        long next=1;
        foreach(string path in Directory.EnumerateFiles(folder,"*.png")){
            string stem=Path.GetFileNameWithoutExtension(path);long number;
            if(stem.StartsWith(name+"_",StringComparison.OrdinalIgnoreCase)&&Int64.TryParse(stem.Substring(name.Length+1),out number)&&number>=next&&number<Int64.MaxValue)next=number+1;
        }
        for(int attempt=0;attempt<100;attempt++,next++){
            string path=Path.Combine(folder,name+"_"+next.ToString("D2")+".png");
            try{return new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);}
            catch(IOException){if(!File.Exists(path))throw;}
        }
        throw new IOException("No se pudo reservar un nombre de captura libre.");
    }
    public static string Rename(string source,string newName,string category){
        string ext=category=="Grabaciones"?".mp3":category=="Videos"?".mp4":null;
        if(ext==null||!String.Equals(Path.GetExtension(source),ext,StringComparison.OrdinalIgnoreCase)||!Inside(source,Path.Combine(AudioFiles.Folder,category)))throw new IOException("Selecciona un archivo de la carpeta "+category+" del programa.");
        string name=(newName??"").Trim();
        if(name.EndsWith(ext,StringComparison.OrdinalIgnoreCase))name=name.Substring(0,name.Length-ext.Length);
        if(String.IsNullOrWhiteSpace(name)||Name(name)!=name)throw new IOException("El nombre no es válido. Evita rutas, caracteres especiales y nombres reservados de Windows.");
        string dest=Path.Combine(Path.GetDirectoryName(source),name+ext);
        if(String.Equals(source,dest,StringComparison.OrdinalIgnoreCase))return source;
        if(File.Exists(dest))throw new IOException("Ya existe un archivo con ese nombre. Elige otro.");
        File.Move(source,dest);return dest;
    }
    public static string RenameDialog(Window owner,System.Windows.ResourceDictionary style,string category){
        string ext=category=="Grabaciones"?"mp3":"mp4",folder=Path.Combine(AudioFiles.Folder,category);
        Directory.CreateDirectory(folder);
        var file=new Microsoft.Win32.OpenFileDialog{Title="Elegir "+ext.ToUpperInvariant()+" para renombrar",InitialDirectory=folder,Filter=ext.ToUpperInvariant()+" (*."+ext+")|*."+ext};
        if(file.ShowDialog(owner)!=true)return null;
        if(!Inside(file.FileName,folder))throw new IOException("Elige un archivo dentro de "+folder+". Los archivos anteriores no se han movido.");
        var dialog=new Window{Owner=owner,Title="Renombrar "+ext.ToUpperInvariant(),Width=510,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=owner.Background,Foreground=owner.Foreground,FontFamily=owner.FontFamily};
        dialog.Resources.MergedDictionaries.Add(style);
        var root=new StackPanel{Margin=new Thickness(24)};dialog.Content=root;
        root.Children.Add(new TextBlock{Text="Nuevo nombre del archivo",FontSize=20,Margin=new Thickness(0,0,0,14)});
        var input=new TextBox{IsReadOnly=false,Cursor=System.Windows.Input.Cursors.IBeam,Text=Path.GetFileNameWithoutExtension(file.FileName),Margin=new Thickness(0,0,0,12)};root.Children.Add(input);
        root.Children.Add(new TextBlock{Text="Se conserva la carpeta del juego y la extensión ."+ext+". No se reemplaza otro archivo.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)});
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};root.Children.Add(error);
        var save=new Button{Content="Guardar nombre",IsDefault=true,HorizontalAlignment=HorizontalAlignment.Left};root.Children.Add(save);
        string renamed=null;
        save.Click+=delegate{try{renamed=Rename(file.FileName,input.Text,category);dialog.DialogResult=true;}catch(Exception ex){error.Text="No se pudo renombrar: "+ex.Message+" Si está abierto en el editor, ciérralo y vuelve a intentarlo.";}};
        dialog.Loaded+=delegate{input.Focus();input.SelectAll();};dialog.ShowDialog();return renamed;
    }
}
