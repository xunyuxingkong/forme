using System;
using System.IO;
using System.IO.Compression;
using System.Drawing;
using System.Reflection;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace Forme.Setup
{
    internal static class Program
    {
        internal static readonly string Programs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs");
        internal static readonly string Target=Path.Combine(Programs,"Forme");
        internal static readonly string Version=typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion??"0.0.0";
        private const string RegistryKeyName=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Forme";
        [STAThread]
        private static int Main(string[] args)
        {
            if(args.Length>0&&args[0]=="--self-test")
            {
                try
                {
                    string destination=Path.GetFullPath(Path.Combine("artifacts","installer-test",Guid.NewGuid().ToString("N")));
                    Extract(destination);if(!File.Exists(Path.Combine(destination,"Forme.exe")))throw new Exception("Payload executable missing");
                    var linkPath=Path.Combine(destination,"test-shortcut.lnk");Shortcut(linkPath,Path.Combine(destination,"Forme.exe"),"");
                    if(!File.Exists(linkPath))throw new Exception("Shortcut creation failed");
                    if(!File.Exists(Path.Combine(destination,"uninstall.ps1"))||!File.Exists(Path.Combine(destination,".forme-install")))throw new Exception("Uninstall or ownership marker missing");
                    File.WriteAllText("artifacts/installer-result.txt","PASS: embedded payload safely extracted, executable and runtime present, local shortcut created, uninstall script and ownership marker present. No user install or registry changes.\n"+destination);return 0;
                }
                catch(Exception ex){Directory.CreateDirectory("artifacts");File.WriteAllText("artifacts/installer-error.txt",ex.ToString());return 1;}
            }
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new InstallForm());return 0;
        }
        private static void CheckDirectory(string path)
        {
            if(Directory.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("安装目录是链接，请改用普通目录后重试。");
        }
        internal static void Extract(string destination)
        {
            CheckDirectory(destination);Directory.CreateDirectory(destination);string root=Path.GetFullPath(destination)+Path.DirectorySeparatorChar;
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Forme.Payload"))
            using(var archive=new ZipArchive(stream,ZipArchiveMode.Read))
            {
                foreach(var entry in archive.Entries)
                {
                    string path=Path.GetFullPath(Path.Combine(root,entry.FullName));
                    if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new IOException("安装包路径无效。");
                    if(string.IsNullOrEmpty(entry.Name)){Directory.CreateDirectory(path);continue;}
                    Directory.CreateDirectory(Path.GetDirectoryName(path));entry.ExtractToFile(path,true);
                }
            }
            File.WriteAllText(Path.Combine(destination,".forme-install"),"Forme "+Version);
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Forme.Uninstall"))
            using(var reader=new StreamReader(stream,System.Text.Encoding.UTF8))File.WriteAllText(Path.Combine(destination,"uninstall.ps1"),reader.ReadToEnd(),new System.Text.UTF8Encoding(true));
        }
        internal static void Install()
        {
            CheckDirectory(Programs);Directory.CreateDirectory(Programs);CheckDirectory(Target);
            foreach(var process in Process.GetProcessesByName("Forme"))
            {
                using(process){try{if(string.Equals(process.MainModule.FileName,Path.Combine(Target,"Forme.exe"),StringComparison.OrdinalIgnoreCase))throw new IOException("请先从托盘退出已安装的 Forme，再升级。");}catch(System.ComponentModel.Win32Exception){}}
            }
            if(Directory.Exists(Target)&&!File.Exists(Path.Combine(Target,".forme-install")))throw new IOException("安装位置已存在其他内容，未覆盖。请先检查："+Target);
            string stage=Path.Combine(Programs,"Forme.installing-"+Guid.NewGuid().ToString("N"));string previous=Path.Combine(Programs,"Forme.previous-"+Guid.NewGuid().ToString("N"));bool old=false,moved=false;
            try
            {
                Extract(stage);
                if(Directory.Exists(Target)){Directory.Move(Target,previous);old=true;}
                Directory.Move(stage,Target);moved=true;
                string start=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Forme");Directory.CreateDirectory(start);
                Shortcut(Path.Combine(start,"Forme.lnk"),Path.Combine(Target,"Forme.exe"),"");
                string arguments="-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \""+Path.Combine(Target,"uninstall.ps1")+"\"";
                Shortcut(Path.Combine(start,"卸载 Forme.lnk"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe"),arguments);
                Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"Forme.lnk"),Path.Combine(Target,"Forme.exe"),"");
                using(var key=Registry.CurrentUser.CreateSubKey(RegistryKeyName))
                {
                    key.SetValue("DisplayName","Forme 陪伴小屋");key.SetValue("DisplayVersion",Version);key.SetValue("InstallLocation",Target);key.SetValue("DisplayIcon",Path.Combine(Target,"Forme.exe"));
                    key.SetValue("UninstallString","\""+Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe")+"\" "+arguments);
                    key.SetValue("NoModify",1);key.SetValue("NoRepair",1);
                }
                if(old)SafeRemove(previous);
            }
            catch
            {
                if(moved&&Directory.Exists(Target))SafeRemove(Target);
                if(old&&Directory.Exists(previous)&&!Directory.Exists(Target))Directory.Move(previous,Target);
                throw;
            }
            finally{if(Directory.Exists(stage))SafeRemove(stage);}
        }
        private static void SafeRemove(string path)
        {
            string absolute=Path.GetFullPath(path),root=Path.GetFullPath(Programs)+Path.DirectorySeparatorChar;
            if(!absolute.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(absolute).StartsWith("Forme",StringComparison.Ordinal)||!File.Exists(Path.Combine(absolute,".forme-install")))throw new IOException("无法确认安装目录所有权，未删除。");
            CheckDirectory(absolute);Directory.Delete(absolute,true);
        }
        private static void Shortcut(string path,string target,string arguments)
        {
            var type=Type.GetTypeFromProgID("WScript.Shell");dynamic shell=Activator.CreateInstance(type);dynamic link=shell.CreateShortcut(path);
            try{link.TargetPath=target;link.Arguments=arguments;link.WorkingDirectory=Target;link.IconLocation=Path.Combine(Target,"Forme.exe");link.Save();}
            finally{Marshal.FinalReleaseComObject(link);Marshal.FinalReleaseComObject(shell);}
        }
    }
    internal sealed class InstallForm:Form
    {
        private readonly Button _install=new Button();private readonly Label _status=new Label();private readonly CheckBox _launch=new CheckBox();private readonly ProgressBar _progress=new ProgressBar();private bool _busy,_installed;
        internal InstallForm()
        {
            Text="Forme · 安装陪伴小屋";ClientSize=new Size(530,340);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(247,245,238);Font=new Font("Microsoft YaHei UI",10);
            Controls.Add(new Label{Text="给桌面留一间小屋",Font=new Font("Microsoft YaHei UI",20,FontStyle.Bold),Location=new Point(30,28),AutoSize=true,ForeColor=Color.FromArgb(52,70,62)});
            Controls.Add(new Label{Text="桌面伙伴 · 一起专注 · 放松与心情记录\n本地活动离线可用，AI 聊天需自行配置密钥。",Location=new Point(32,84),Size=new Size(470,52)});
            Controls.Add(new Label{Text="安装至当前用户目录，无需管理员权限：\n"+Program.Target+"\n升级和卸载保留个人数据，可在应用中先清除。",Location=new Point(32,145),Size=new Size(470,66)});
            _launch.Text="安装后打开 Forme";_launch.Checked=true;_launch.Location=new Point(32,214);_launch.Size=new Size(230,26);Controls.Add(_launch);
            _progress.Location=new Point(32,246);_progress.Size=new Size(466,5);_progress.Style=ProgressBarStyle.Marquee;_progress.Visible=false;Controls.Add(_progress);
            _status.Location=new Point(32,267);_status.Size=new Size(315,52);Controls.Add(_status);
            _install.Text="安装 / 升级";_install.Location=new Point(365,270);_install.Size=new Size(132,40);_install.BackColor=Color.FromArgb(82,122,98);_install.ForeColor=Color.White;_install.FlatStyle=FlatStyle.Flat;Controls.Add(_install);
            _install.Click+=async delegate
            {
                if(_installed){Close();return;}
                _busy=true;_install.Enabled=false;_progress.Visible=true;_status.Text="正在安装…";
                try{await Task.Run((Action)Program.Install);_installed=true;_status.Text="安装完成。";if(_launch.Checked)Process.Start(Path.Combine(Program.Target,"Forme.exe"));_install.Text="关闭";}
                catch(Exception ex){_status.Text="安装未完成。";MessageBox.Show(ex.Message,"Forme",MessageBoxButtons.OK,MessageBoxIcon.Warning);_install.Enabled=true;}
                finally{_busy=false;_progress.Visible=false;_install.Enabled=true;}
            };
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(_busy)e.Cancel=true;};
        }
    }
}
