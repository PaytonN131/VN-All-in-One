using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

sealed class ShortcutCatalog {
    readonly List<ShortcutBinding> bindings=new List<ShortcutBinding>();
    readonly TextBlock status;
    public ShortcutCatalog(FrameworkElement ui){status=(TextBlock)ui.FindName("ShortcutStatus");}
    public ShortcutBinding Add(FrameworkElement ui,string control,string action,uint key,uint mods,string label){
        var binding=new ShortcutBinding(this,(TextBox)ui.FindName(control),action,key,mods,label);
        bindings.Add(binding);return binding;
    }
    public bool Available(ShortcutBinding current,uint key,uint mods){
        foreach(var b in bindings)if(b!=current&&b.Key==key&&b.Modifiers==mods){
            status.Text="Esta combinación ya se usa para «"+b.Action+"». Elige otra.";return false;
        }
        return true;
    }
    public void Notice(string message){status.Text=message;}
    public void Reset(){foreach(var b in bindings)b.Reset();foreach(var b in bindings)b.Notify();Notice("Atajos originales restaurados. Los cambios se guardan al cerrar.");}
}

sealed class ShortcutBinding {
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern short VkKeyScanEx(char value,IntPtr layout);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code,uint type);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetKeyNameText(int code,StringBuilder text,int size);
    readonly ShortcutCatalog catalog;
    readonly TextBox box;
    readonly uint defaultKey,defaultMods;
    readonly string defaultLabel;
    public readonly string Action;
    public uint Key {get;private set;}
    public uint Modifiers {get;private set;}
    public string Label {get;private set;}
    public event System.Action Changed;
    public ShortcutBinding(ShortcutCatalog catalog,TextBox box,string action,uint key,uint mods,string label){
        this.catalog=catalog;this.box=box;Action=action;defaultKey=key;defaultMods=mods;defaultLabel=label;
        Reset();
        box.GotKeyboardFocus+=delegate{box.SelectAll();catalog.Notice("Pulsa la tecla o combinación para «"+Action+"». Tab pasa al siguiente campo; Esc cancela.");};
        box.PreviewKeyDown+=Capture;
    }
    public static uint[] Symbol(char symbol){
        short code=VkKeyScanEx(symbol,GetKeyboardLayout(0));
        if(code==-1)return new uint[]{0x77,3};
        int shift=(code>>8)&255;
        return new uint[]{(uint)(code&255),(uint)(((shift&1)<<2)|(shift&2)|((shift&4)>>2))};
    }
    void Set(uint key,uint mods,string label){Key=key;Modifiers=mods;Label=label;box.Text=label;if(Changed!=null)Changed();}
    public void Reset(){Set(defaultKey,defaultMods,defaultLabel);}
    public void Notify(){if(Changed!=null)Changed();}
    public void Legacy(uint key,uint mods,string label){Set(key,mods,label);}
    void Capture(object sender,KeyEventArgs e){
        var k=e.Key==System.Windows.Input.Key.System?e.SystemKey:e.Key;
        if(k==System.Windows.Input.Key.Tab)return;
        e.Handled=true;
        if(k==System.Windows.Input.Key.Escape){Keyboard.ClearFocus();catalog.Notice("Sin cambios.");return;}
        if(k==System.Windows.Input.Key.LeftCtrl||k==System.Windows.Input.Key.RightCtrl||k==System.Windows.Input.Key.LeftAlt||k==System.Windows.Input.Key.RightAlt||k==System.Windows.Input.Key.LeftShift||k==System.Windows.Input.Key.RightShift)return;
        if((Keyboard.Modifiers&ModifierKeys.Windows)!=0||k==System.Windows.Input.Key.LWin||k==System.Windows.Input.Key.RWin||k==System.Windows.Input.Key.None){catalog.Notice("Elige una combinación sin la tecla Windows.");return;}
        uint vk=(uint)KeyInterop.VirtualKeyFromKey(k);
        uint mods=(uint)(((Keyboard.Modifiers&ModifierKeys.Control)!=0?2:0)|((Keyboard.Modifiers&ModifierKeys.Alt)!=0?1:0)|((Keyboard.Modifiers&ModifierKeys.Shift)!=0?4:0));
        if(!catalog.Available(this,vk,mods))return;
        var name=new StringBuilder(128);
        uint scan=MapVirtualKey(vk,0);
        GetKeyNameText((int)(scan<<16)|((vk>=0x21&&vk<=0x2E)?1<<24:0),name,name.Capacity);
        string label=((mods&2)!=0?"Ctrl + ":"")+((mods&1)!=0?"Alt + ":"")+((mods&4)!=0?"Shift + ":"")+(name.Length>0?name.ToString():k.ToString());
        Set(vk,mods,label);catalog.Notice("Asignado: "+Action+" → "+Label+". Se guardará al cerrar.");Keyboard.ClearFocus();
    }
    public bool Register(IntPtr hwnd,int id){return catalog.Available(this,Key,Modifiers)&&Native.RegisterHotKey(hwnd,id,0x4000|Modifiers,Key);}
    public void Save(System.Xml.XmlWriter writer,string name){writer.WriteAttributeString(name,Key+","+Modifiers+","+Label);}
    public void Load(System.Xml.XmlElement node,string name){
        string[] data=node.GetAttribute(name).Split(new[]{','},3);
        uint key,mods;
        if(data.Length==3&&uint.TryParse(data[0],out key)&&key>0&&key<256&&uint.TryParse(data[1],out mods)&&mods<8)Set(key,mods,data[2]);
    }
}
