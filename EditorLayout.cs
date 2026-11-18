using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

static class EditorLayout {
    static Brush Brush(string color){return (Brush)new BrushConverter().ConvertFromString(color);}
    public static TextBlock Note(string text){return new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Foreground=Brush("#C8B6C4"),LineHeight=21,Margin=new Thickness(0,0,0,12)};}
    public static StackPanel Card(StackPanel parent,string heading){
        var body=new StackPanel();
        body.Children.Add(new TextBlock{Text=heading,FontSize=17,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,14)});
        parent.Children.Add(new Border{Child=body,Background=Brush("#2C2532"),BorderBrush=Brush("#544151"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(18),Margin=new Thickness(0,0,0,14)});
        return body;
    }
    public static void Shell(Window window,string heading,string instruction,out StackPanel main,out StackPanel side){
        window.FontFamily=new FontFamily("Segoe UI");window.FontSize=13;
        var root=new Grid{Margin=new Thickness(24),Background=window.Background};root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());
        root.ColumnDefinitions.Add(new ColumnDefinition());root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(20)});root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(296)});
        var title=new StackPanel();title.Children.Add(new TextBlock{Text=heading,FontSize=27,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6)});title.Children.Add(Note(instruction));Grid.SetColumnSpan(title,3);root.Children.Add(title);
        main=new StackPanel();side=new StackPanel();
        var left=new ScrollViewer{Content=main,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(left,1);root.Children.Add(left);
        var right=new ScrollViewer{Content=side,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(right,1);Grid.SetColumn(right,2);root.Children.Add(right);window.Content=root;
    }
    public static void Range(StackPanel parent,TextBlock firstLabel,Slider first,TextBlock lastLabel,Slider last,Button markFirst=null,Button markLast=null){
        var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(20)});row.ColumnDefinitions.Add(new ColumnDefinition());
        var left=new StackPanel();left.Children.Add(firstLabel);left.Children.Add(first);if(markFirst!=null){markFirst.Margin=new Thickness(0,8,0,0);left.Children.Add(markFirst);}row.Children.Add(left);
        var right=new StackPanel();right.Children.Add(lastLabel);right.Children.Add(last);if(markLast!=null){markLast.Margin=new Thickness(0,8,0,0);right.Children.Add(markLast);}Grid.SetColumn(right,2);row.Children.Add(right);parent.Children.Add(row);
    }
    public static void Buttons(StackPanel parent,params Button[] buttons){var row=new WrapPanel();foreach(var button in buttons){button.Margin=new Thickness(0,0,8,8);row.Children.Add(button);}parent.Children.Add(row);}
    public static void SaveButton(Button button){button.Background=Brush("#B88FA8");button.Foreground=Brush("#211B25");button.FontWeight=FontWeights.SemiBold;button.Margin=new Thickness(0,8,0,10);button.HorizontalAlignment=HorizontalAlignment.Stretch;}
}
