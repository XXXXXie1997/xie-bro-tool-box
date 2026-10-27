using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Management;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

internal sealed class InstallerWindow : Window
{
    static readonly Brush Ink = Brush("#292D33"), Muted = Brush("#69717C"), Blue = Brush("#2B588D"), Canvas = Brush("#EEEEEE"), Surface = Brush("#F5F5F5"), Line = Brush("#D5D7DA");
#if UNINSTALLER
    const bool IsUninstaller = true;
#else
    const bool IsUninstaller = false;
#endif
    static string UninstallTarget;
    StackPanel body; TextBox location; TextBlock notice; Button action, cancel; CheckBox desktop, launch; bool busy, complete; string target, logPath;
    static Brush Brush(string value) { return (Brush)new BrushConverter().ConvertFromString(value); }
    [STAThread] static int Main(string[] args)
    {
        if(IsUninstaller && !(args.Length==2 && args[0]=="--render"))
        {
            if(args.Length==2 && args[0]=="--target") UninstallTarget=Path.GetFullPath(args[1]);
            else
            {
                var source=Assembly.GetExecutingAssembly().Location;
                var install=Directory.GetParent(Path.GetDirectoryName(source)).FullName;
                var scratch=Path.Combine(Path.GetTempPath(),"XieBox-Uninstall-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scratch);
                var copy=Path.Combine(scratch,"Uninstall.exe"); File.Copy(source,copy);
                Process.Start(new ProcessStartInfo(copy,"--target "+Quote(install)){UseShellExecute=true});return 0;
            }
        }
        var app = new Application(); var window = new InstallerWindow();
        if (args.Length == 2 && args[0] == "--render")
        {
            Directory.CreateDirectory(args[1]);
            window.Snapshot(Path.Combine(args[1], "install.png")); window.ShowComplete(); window.Snapshot(Path.Combine(args[1], "complete.png")); return 0;
        }
        return app.Run(window);
    }
    void Snapshot(string path)
    {
        var view = (FrameworkElement)Content; view.Measure(new Size(Width, Height)); view.Arrange(new Rect(0, 0, Width, Height)); view.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32); bmp.Render(view);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp)); using(var file=File.Create(path)) png.Save(file);
    }
    static TextBlock Text(string text, double size, Brush color)
    { return new TextBlock { Text=text, FontSize=size, Foreground=color, TextWrapping=TextWrapping.Wrap }; }
    static Button Button(string label, bool primary)
    {
        var b = new Button { Content=label, Padding=new Thickness(20,10,20,10), MinHeight=40, MinWidth=90, Cursor=Cursors.Hand, FocusVisualStyle=null, Background=primary?Blue:Surface, Foreground=primary?Brushes.White:Ink, BorderBrush=Line, BorderThickness=new Thickness(primary?0:1) };
        var border=new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty,new CornerRadius(8));
        border.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background") { RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderBrushProperty,new System.Windows.Data.Binding("BorderBrush") { RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderThicknessProperty,new System.Windows.Data.Binding("BorderThickness") { RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.PaddingProperty,new System.Windows.Data.Binding("Padding") { RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        var content=new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center); content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center); border.AppendChild(content);
        b.Template=new ControlTemplate(typeof(Button)) { VisualTree=border };
        b.MouseEnter+=(s,e)=>b.Opacity=.86; b.MouseLeave+=(s,e)=>b.Opacity=b.IsEnabled?1:.5; b.IsEnabledChanged+=(s,e)=>b.Opacity=b.IsEnabled?1:.5;
        return b;
    }
    public InstallerWindow()
    {
        Title=IsUninstaller?"蟹小箱 · 卸载":"蟹小箱 · 安装"; Width=660; Height=420; MaxHeight=SystemParameters.WorkArea.Height-24; MaxWidth=SystemParameters.WorkArea.Width-24;
        WindowStartupLocation=WindowStartupLocation.CenterScreen; WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize; AllowsTransparency=true; Background=Brushes.Transparent; FontFamily=new FontFamily("Microsoft YaHei UI"); FontSize=13;
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Logo")) { var bitmap=new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption=BitmapCacheOption.OnLoad; bitmap.StreamSource=stream; bitmap.EndInit(); Icon=bitmap; }
        var frame=new Border { Background=Canvas,BorderBrush=Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12) }; Content=frame;
        var root=new Grid(); frame.Child=root; root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition());
        var titlebar=new Grid { Height=42, Margin=new Thickness(18,0,10,0) }; titlebar.MouseLeftButtonDown+=(s,e)=>{if(e.ButtonState==MouseButtonState.Pressed)DragMove();};
        var title=Text(IsUninstaller?"蟹小箱卸载":"蟹小箱安装",12,Muted); title.VerticalAlignment=VerticalAlignment.Center; titlebar.Children.Add(title);
        var close=Button("×",false); close.MinWidth=32; close.MinHeight=30; close.Padding=new Thickness(8,2,8,2); close.HorizontalAlignment=HorizontalAlignment.Right; close.VerticalAlignment=VerticalAlignment.Center; close.Background=Brushes.Transparent; close.BorderThickness=new Thickness(0); close.Click+=(s,e)=>{if(!busy)Close();}; titlebar.Children.Add(close); root.Children.Add(titlebar);
        var layout=new Grid { Margin=new Thickness(30,12,30,26) }; Grid.SetRow(layout,1); root.Children.Add(layout);
        layout.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition()); layout.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        var heading=new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(0,0,0,24) }; heading.Children.Add(new Image { Source=Icon,Width=64,Height=64,Margin=new Thickness(0,0,18,0) });
        var names=new StackPanel { VerticalAlignment=VerticalAlignment.Center }; names.Children.Add(Text("蟹小箱",28,Ink)); var sub=Text("Windows 系统与文件管理工具箱  ·  v1.0.0",12,Muted); sub.Margin=new Thickness(0,5,0,0); names.Children.Add(sub); heading.Children.Add(names); layout.Children.Add(heading);
        body=new StackPanel(); var scroll=new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled }; Grid.SetRow(scroll,1); layout.Children.Add(scroll);
        var footer=new Grid { Margin=new Thickness(0,18,0,0) }; Grid.SetRow(footer,2); layout.Children.Add(footer);
        notice=Text("安装时会请求管理员权限",12,Muted); notice.VerticalAlignment=VerticalAlignment.Center; notice.MaxWidth=350; notice.HorizontalAlignment=HorizontalAlignment.Left; footer.Children.Add(notice);
        var buttons=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right }; cancel=Button("取消",false); cancel.Margin=new Thickness(0,0,10,0); cancel.Click+=(s,e)=>Close(); buttons.Children.Add(cancel); action=Button(IsUninstaller?"卸载蟹小箱":"开始安装",true); action.Click+=async(s,e)=>{if(complete){if(IsUninstaller)Close();else Finish();}else if(IsUninstaller)await Uninstall();else await Install();}; buttons.Children.Add(action); footer.Children.Add(buttons);
        target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"xieBroToolBox");
        using(var registry=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64)) using(var key=registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{AE006223-8CC9-49D5-A806-887CDFBFC7B5}_is1")) { if(key!=null) target=key.GetValue("InstallLocation",target).ToString(); }
        if(IsUninstaller){target=UninstallTarget??target;ShowUninstallOptions();}else ShowOptions(); Closing+=(s,e)=>{if(busy)e.Cancel=true;};
    }
    void ShowUninstallOptions()
    {
        body.Children.Clear();body.Children.Add(Text("确定卸载蟹小箱？",22,Ink));
        var explanation=Text("将移除程序文件和快捷方式。\n用户设置、日志和文件操作恢复记录会保留。",13,Muted);explanation.Margin=new Thickness(0,16,0,16);body.Children.Add(explanation);
        var path=Text(target,12,Muted);body.Children.Add(path);notice.Text="卸载时会请求管理员权限";
    }
    async Task Uninstall()
    {
        try
        {
            var root=Path.GetFullPath(target).TrimEnd('\\')+"\\";
            using(var search=new ManagementObjectSearcher("SELECT ExecutablePath FROM Win32_Process"))
            using(var processes=search.Get())
                foreach(ManagementObject process in processes)
                using(process)
                {
                    var path=process["ExecutablePath"] as string;
                    if(path!=null && path.StartsWith(root,StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(Path.GetFileName(path),"Uninstall.exe",StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(Path.GetFileName(path),"unins000.exe",StringComparison.OrdinalIgnoreCase))
                    {notice.Text="蟹小箱仍在运行，请关闭应用后重试。";notice.Foreground=Brush("#9B7629");return;}
                }
        }
        catch {notice.Text="无法确认后台进程是否退出，暂不能卸载。请重试。";notice.Foreground=Brush("#B9515A");return;}
        var engine=Path.Combine(target,"uninstall","unins000.exe");
        if(!File.Exists(engine)){notice.Text="未找到卸载程序，请重新安装后再卸载。";return;}
        var manifest=Path.Combine(target,"app","installed-files.txt");
        var installedFiles=File.Exists(manifest)?File.ReadAllLines(manifest):new string[0];
        busy=true;action.IsEnabled=false;cancel.IsEnabled=false;body.Children.Clear();body.Children.Add(Text("正在卸载蟹小箱",22,Ink));body.Children.Add(new ProgressBar { IsIndeterminate=true,Height=6,Margin=new Thickness(0,24,0,0),Foreground=Blue,Background=Line });
        try
        {
            var result=await Task.Run(()=>{using(var proc=Process.Start(new ProcessStartInfo(engine,"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART"){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden})){proc.WaitForExit();return proc.ExitCode;}});
            if(result!=0 && result!=3010)throw new IOException("请确认蟹小箱已经退出，并允许管理员授权后重试。");
            var shortcut=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"蟹小箱.lnk");
            if(File.Exists(shortcut))
            {
                try
                {
                    var shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                    var link=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{shortcut});
                    var linkTarget=(string)link.GetType().InvokeMember("TargetPath",BindingFlags.GetProperty,null,link,null);
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
                    if(string.Equals(linkTarget,Path.Combine(target,"XieBox.exe"),StringComparison.OrdinalIgnoreCase))File.Delete(shortcut);
                }
                catch { throw new IOException("桌面快捷方式未能删除。"); }
            }
            using(var registry=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64))
            using(var key=registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{AE006223-8CC9-49D5-A806-887CDFBFC7B5}_is1"))
                if(key!=null)throw new IOException("卸载注册表仍然存在。");
            foreach(var relative in installedFiles)
            {
                var path=Path.GetFullPath(Path.Combine(target,relative));
                if(!path.StartsWith(Path.GetFullPath(target).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))continue;
                if(File.Exists(path))throw new IOException("仍有程序文件未删除："+relative);
            }
            busy=false;ShowComplete();
        }
        catch(Exception error) {busy=false;ShowUninstallOptions();notice.Text="卸载未完成。"+error.Message;notice.Foreground=Brush("#B9515A");action.IsEnabled=true;cancel.IsEnabled=true;}
    }
    void ShowOptions()
    {
        body.Children.Clear(); var card=new StackPanel(); card.Children.Add(Text("安装位置",15,Ink));
        var row=new Grid { Margin=new Thickness(0,12,0,12) }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        location=new TextBox { Text=target,Padding=new Thickness(10,9,10,9),Background=Surface,Foreground=Ink,BorderThickness=new Thickness(0),VerticalContentAlignment=VerticalAlignment.Center };
        var input=new Border { Child=location,CornerRadius=new CornerRadius(7),BorderBrush=Line,BorderThickness=new Thickness(1),Margin=new Thickness(0,0,10,0) }; row.Children.Add(input);
        var browse=Button("浏览…",false); Grid.SetColumn(browse,1); row.Children.Add(browse); browse.Click+=(s,e)=>{using(var picker=new Forms.FolderBrowserDialog { Description="选择安装位置",SelectedPath=location.Text }){if(picker.ShowDialog()==Forms.DialogResult.OK)location.Text=Normalize(picker.SelectedPath);}};
        card.Children.Add(row); card.Children.Add(Text("选择磁盘根目录时，会自动创建 xieBroToolBox 文件夹。",12,Muted));
        body.Children.Add(new Border { Child=card,Background=Surface,CornerRadius=new CornerRadius(10),Padding=new Thickness(20),BorderBrush=Line,BorderThickness=new Thickness(1) });
        var hint=Text("已包含全部工具与运行环境，无需额外下载。",12,Muted); hint.Margin=new Thickness(0,14,0,0); body.Children.Add(hint);
    }
    static string Normalize(string path)
    {
        path=path.Trim(); if(path.Length==2 && path[1]==':')path+="\\";
        path=Path.GetFullPath(path); var root=Path.GetPathRoot(path);
        return string.Equals(path.TrimEnd('\\'),root.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)?Path.Combine(root,"xieBroToolBox"):path;
    }
    async Task Install()
    {
        try { if(string.IsNullOrWhiteSpace(location.Text))throw new IOException(); target=Normalize(location.Text); }
        catch { notice.Text="请选择有效的安装目录。"; notice.Foreground=Brush("#B9515A"); return; }
        busy=true; action.IsEnabled=false; cancel.IsEnabled=false; notice.Text="请在 Windows 授权提示中选择“是”"; notice.Foreground=Muted;
        body.Children.Clear(); var label=Text("正在安装蟹小箱",20,Ink); label.Margin=new Thickness(0,14,0,18); body.Children.Add(label);
        var progress=new ProgressBar { Minimum=0,Maximum=100,Value=0,Height=6,Foreground=Blue,Background=Line };body.Children.Add(progress);
        var percentage=Text("0% · 正在准备安装",12,Muted);percentage.Margin=new Thickness(0,12,0,0);body.Children.Add(percentage);
        var dest=Text(target,12,Muted); dest.Margin=new Thickness(0,18,0,0); body.Children.Add(dest);
        try
        {
            var exit=await Task.Run(()=>RunEngine(value=>Dispatcher.Invoke(new Action(()=>{progress.Value=value;percentage.Text=value+"% · "+(value<100?"正在安装":"正在完成安装");}))));
            if(exit!=0 && exit!=3010)throw new IOException("安装程序退出代码："+exit);
            busy=false; ShowComplete();
        }
        catch(Exception error)
        {
            busy=false; ShowOptions(); notice.Text=error is System.ComponentModel.Win32Exception && ((System.ComponentModel.Win32Exception)error).NativeErrorCode==1223?"已取消管理员授权，可以重新安装。":"安装未完成，请查看安装日志。"; notice.Foreground=Brush("#B9515A");
            if(logPath!=null && File.Exists(logPath)) { var logs=Button("查看安装日志",false); logs.Margin=new Thickness(0,8,0,0); logs.Click+=(s,e)=>Process.Start(new ProcessStartInfo(logPath){UseShellExecute=true}); body.Children.Add(logs); }
            action.IsEnabled=true; cancel.IsEnabled=true;
        }
    }
    int RunEngine(Action<int> reportProgress)
    {
        var folder=Path.Combine(Path.GetTempPath(),"XieBox-Setup-"+Guid.NewGuid().ToString("N"));
        var acl=new DirectorySecurity(); acl.SetAccessRuleProtection(true,false);
        foreach(var sid in new[]{WindowsIdentity.GetCurrent().User,new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null)}) acl.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        Directory.CreateDirectory(folder,acl); var engine=Path.Combine(folder,"Engine.exe");
        var logs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"XieBox","InstallerLogs"); Directory.CreateDirectory(logs); logPath=Path.Combine(logs,"setup-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".log");
        try
        {
            using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("Engine")) using(var output=new FileStream(engine,FileMode.CreateNew,FileAccess.Write,FileShare.None)) input.CopyTo(output);
            var progressFile=Path.Combine(folder,"progress.txt");
            var start=new ProcessStartInfo(engine,"/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR="+Quote(target)+" /LOG="+Quote(logPath)+" /PROGRESSFILE="+Quote(progressFile)){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};
            using(var process=Process.Start(start))
            {
                var last=-1;
                while(!process.WaitForExit(200))
                {
                    try {int value;if(File.Exists(progressFile) && int.TryParse(File.ReadAllText(progressFile),out value) && value>last){last=Math.Min(100,value);reportProgress(last);}}catch(IOException){}
                }
                if(process.ExitCode==0 || process.ExitCode==3010)reportProgress(100);
                File.Delete(progressFile);return process.ExitCode;
            }
        }
        finally { try{File.Delete(engine);Directory.Delete(folder);}catch(IOException){}catch(UnauthorizedAccessException){} }
    }
    static string Quote(string value){return "\""+value.TrimEnd('\\').Replace("\"","")+"\"";}
    void ShowComplete()
    {
        complete=true; body.Children.Clear();
        if(IsUninstaller){body.Children.Add(Text("卸载完成",22,Ink));var message=Text("程序已移除，用户数据已保留。",13,Muted);message.Margin=new Thickness(0,12,0,0);body.Children.Add(message);notice.Text="感谢使用蟹小箱";cancel.Visibility=Visibility.Collapsed;action.Content="完成";action.IsEnabled=true;return;}
        body.Children.Add(Text("安装完成",22,Ink));
        var hint=Text("蟹小箱已经准备好了。",13,Muted); hint.Margin=new Thickness(0,8,0,18); body.Children.Add(hint);
        desktop=new CheckBox { Content="创建桌面快捷方式",IsChecked=true,Margin=new Thickness(0,0,0,14),Foreground=Ink }; launch=new CheckBox { Content="打开蟹小箱",IsChecked=true,Foreground=Ink }; body.Children.Add(desktop); body.Children.Add(launch);
        notice.Text="感谢使用蟹小箱"; cancel.Visibility=Visibility.Collapsed; action.Content="完成"; action.IsEnabled=true;
    }
    void Finish()
    {
        try
        {
            if(desktop.IsChecked==true)
            {
                var helper=Path.Combine(target,"app","SetupActions.exe"); var link=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"蟹小箱.lnk");
                using(var p=Process.Start(new ProcessStartInfo(helper,"--shortcut "+Quote(link)+" "+Quote(Path.Combine(target,"XieBox.exe"))){UseShellExecute=false,CreateNoWindow=true})){p.WaitForExit();if(p.ExitCode!=0)throw new IOException();}
                desktop.IsChecked=false;
            }
            if(launch.IsChecked==true) Process.Start(new ProcessStartInfo(Path.Combine(target,"XieBox.exe")){UseShellExecute=true,WorkingDirectory=target});
            Close();
        }
        catch { notice.Text="安装已完成，但后续操作未完成，可从开始菜单打开。"; notice.Foreground=Brush("#B9515A"); }
    }
}
