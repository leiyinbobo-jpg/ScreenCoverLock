using System;
using System.IO;
using System.Reflection;
using System.IO.Compression;
using System.Windows.Forms;
[assembly:AssemblyVersion("1.0.0.0")]
class Installer {
 const string Version="__VERSION__";
 [STAThread]static int Main(string[] args){
  try{
   bool extract=args.Length==2&&args[0]=="--extract-to";
   bool silent=args.Length==1&&args[0]=="--install-no-launch";
   if(!extract&&!silent&&MessageBox.Show("安装 ScreenCoverLock "+Version+"？\n将安装到当前用户目录，并创建两个桌面快捷方式。\n不自动开启摄像头，也不写入开机启动项。","ScreenCoverLock 安装",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return 0;
   string root=extract?Path.GetFullPath(args[1]):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ScreenCoverUpgradeApp",Version);
   if(Directory.Exists(root)&&Directory.GetFiles(root,"*",SearchOption.AllDirectories).Length>0)throw new IOException("安装目录已存在且非空，请使用新构建版本或先移走该目录。");
   Directory.CreateDirectory(root);string prefix=root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))using(var archive=new ZipArchive(stream,ZipArchiveMode.Read)){
    foreach(var entry in archive.Entries){string path=Path.GetFullPath(Path.Combine(root,entry.FullName));if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException("安装包路径不合法。");
     Directory.CreateDirectory(Path.GetDirectoryName(path));entry.ExtractToFile(path,false);
    }
   }
   if(!extract){
    Shortcut(root,"ScreenCoverLock (严格模式).lnk",false);
    Shortcut(root,"ScreenCoverLock 设置与记录.lnk",true);
    if(!silent)MessageBox.Show("已安装。请先打开设置界面设置或验证机主密码，按需配置自定义壁纸与摄像头测试（录像默认关闭）。","安装完成");
   }return 0;
  }catch(Exception e){if(args.Length==0)MessageBox.Show(e.Message,"安装未完成");else File.WriteAllText(Path.Combine(Path.GetTempPath(),"ScreenCover-install-error.txt"),e.GetType().Name+": "+e.Message);return 1;}
 }
 static void Shortcut(string root,string name,bool settings){
  string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),name);
  dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));dynamic link=shell.CreateShortcut(path);
  link.TargetPath=Path.Combine(root,"ScreenCover-Strict.exe");
  link.Arguments=settings?"--settings":"--allow-automation";link.WorkingDirectory=root;
  link.IconLocation=Path.Combine(root,"ScreenCover-Strict.exe")+",0";link.Description="ScreenCoverLock (camera recording disabled by default)";link.Save();
 }
}
