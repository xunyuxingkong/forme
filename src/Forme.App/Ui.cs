using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Forme.App;

internal static class Ui
{
    public static readonly Brush Ink=Brush("#34463E"),Muted=Brush("#626E65"),Sage=Brush("#527A62"),Cream=Brush("#F7F5EE"),Line=Brush("#E4E8DF"),Gold=Brush("#DCA859");
    public static TextBlock Text(string text,double size=14,Brush? color=null,bool bold=false) => new(){Text=text,FontSize=size,Foreground=color??Ink,TextWrapping=TextWrapping.Wrap,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,Margin=new Thickness(0,0,0,8)};
    public static Button Button(string text,Action action,bool primary=false)
    {
        var b=new Button{Content=text,Margin=new Thickness(0,0,8,8),Padding=new Thickness(14,9,14,9),MinHeight=36,Cursor=System.Windows.Input.Cursors.Hand,Background=primary?Sage:Brush("#EFF2EB"),Foreground=primary?(SystemParameters.HighContrast?SystemColors.HighlightTextBrush:Brush("#FFFFFF")):Ink,BorderThickness=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Left};
        System.Windows.Automation.AutomationProperties.SetName(b,text);
        b.Click+=(_,_)=>Guard(action);return b;
    }
    public static Button AsyncButton(string text,Func<Task> action,bool primary=false)
    {
        var b=Button(text,()=>{},primary);b.Click+=async(_,_)=>{b.IsEnabled=false;try{await action();}catch(Exception ex) when(Forme.Core.OperationErrors.Expected(ex)){Error(Forme.Core.OperationErrors.Message(ex));}finally{b.IsEnabled=true;}};return b;
    }
    public static void Guard(Action action){try{action();}catch(Exception ex) when(Forme.Core.OperationErrors.Expected(ex)){Error(Forme.Core.OperationErrors.Message(ex));}}
    public static void Error(string text)=>MessageBox.Show(text,"Forme",MessageBoxButton.OK,MessageBoxImage.Warning);
    public static bool Confirm(string text)=>MessageBox.Show(text,"Forme",MessageBoxButton.OKCancel,MessageBoxImage.Information)==MessageBoxResult.OK;
    public static TextBox Input(string value="",bool multiline=false,int max=2000,string automationName="文本输入框")
    {
        var input=new TextBox{Text=value,MaxLength=max,FontSize=14,Padding=new Thickness(12),Margin=new Thickness(0,0,0,12),Background=Brush("#FFFFFF"),Foreground=Ink,BorderBrush=Line,BorderThickness=new Thickness(1),AcceptsReturn=multiline,TextWrapping=TextWrapping.Wrap,MinHeight=multiline?85:40,VerticalScrollBarVisibility=multiline?ScrollBarVisibility.Auto:ScrollBarVisibility.Hidden};
        System.Windows.Automation.AutomationProperties.SetName(input,automationName);return input;
    }
    public static StackPanel Stack(params UIElement[] items){var p=new StackPanel();foreach(var i in items)p.Children.Add(i);return p;}
    public static WrapPanel Row(params UIElement[] items){var p=new WrapPanel();foreach(var i in items)p.Children.Add(i);return p;}
    public static Border Card(UIElement child,Thickness? padding=null)=>new(){Child=child,Background=Brush("#FFFFFF"),CornerRadius=new CornerRadius(16),BorderBrush=Line,BorderThickness=new Thickness(1),Padding=padding??new Thickness(20),Margin=new Thickness(0,0,0,14)};
    public static CheckBox Check(string label,bool value){var check=new CheckBox{Content=label,IsChecked=value,Margin=new Thickness(0,0,0,12),Foreground=Ink,FontSize=14};System.Windows.Automation.AutomationProperties.SetName(check,label);return check;}
    public static ComboBox Select(IEnumerable<string> options,string value,string automationName="选项")
    {var c=new ComboBox{MinHeight=36,Margin=new Thickness(0,0,0,12),Padding=new Thickness(8),FontSize=14};foreach(var x in options)c.Items.Add(x);c.SelectedItem=value;System.Windows.Automation.AutomationProperties.SetName(c,automationName);return c;}
    public static SolidColorBrush Brush(string hex)
    {
        if(SystemParameters.HighContrast)
        {
            var color=(Color)ColorConverter.ConvertFromString(hex);double luminance=(.2126*color.R+.7152*color.G+.0722*color.B)/255;
            if(luminance>.88)return SystemColors.WindowBrush as SolidColorBrush??new(SystemColors.WindowColor);
            if(luminance<.32)return SystemColors.WindowTextBrush as SolidColorBrush??new(SystemColors.WindowTextColor);
            return SystemColors.HighlightBrush as SolidColorBrush??new(SystemColors.HighlightColor);
        }
        var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));brush.Freeze();return brush;
    }
    public static void InstallStyles(Application app)
    {
        app.Resources[typeof(Window)]=new Style(typeof(Window)){Setters={new Setter(Control.FontFamilyProperty,new FontFamily("Microsoft YaHei UI")),new Setter(Control.ForegroundProperty,Ink)}};
        var buttonStyle=new Style(typeof(Button));
        var template=new ControlTemplate(typeof(Button));
        var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(10));border.SetValue(Border.BorderBrushProperty,Brush("#8A958C"));border.SetValue(Border.BorderThicknessProperty,new Thickness(1));
        border.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=new(System.Windows.Data.RelativeSourceMode.TemplatedParent)});
        border.SetBinding(Border.PaddingProperty,new System.Windows.Data.Binding("Padding"){RelativeSource=new(System.Windows.Data.RelativeSourceMode.TemplatedParent)});
        var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(content);template.VisualTree=border;
        var focus=new Trigger{Property=UIElement.IsKeyboardFocusedProperty,Value=true};focus.Setters.Add(new Setter(Border.BorderBrushProperty,Gold));focus.Setters.Add(new Setter(Border.BorderThicknessProperty,new Thickness(3)));template.Triggers.Add(focus);
        buttonStyle.Setters.Add(new Setter(Control.TemplateProperty,template));
        var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(UIElement.OpacityProperty,0.82));buttonStyle.Triggers.Add(hover);
        var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(UIElement.OpacityProperty,0.45));buttonStyle.Triggers.Add(disabled);
        app.Resources[typeof(Button)]=buttonStyle;
    }
}
