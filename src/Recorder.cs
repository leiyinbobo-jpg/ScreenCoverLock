using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace ScreenCoverUpgrade {
 public static class CameraTools {
  public static string Encoder {get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tools","ffmpeg.exe");}}
  public static string Quote(string s){return "\""+Regex.Replace(Regex.Replace(s,"(\\\\*)\"","$1$1\\\""),"(\\\\+)$","$1$1")+"\"";}
  public static ProcessStartInfo Info(string args){return new ProcessStartInfo(Encoder,args){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardError=true,RedirectStandardOutput=true,RedirectStandardInput=true};}
  public static string[] Devices(){
   if(!File.Exists(Encoder))throw new FileNotFoundException("缺少 tools/ffmpeg.exe");
   var names=new List<string>();using(var p=new Process()){p.StartInfo=Info("-hide_banner -list_devices true -f dshow -i dummy");
    p.ErrorDataReceived+=(s,e)=>{if(e.Data==null)return;var m=Regex.Match(e.Data,"\"(.+)\" \\(video\\)");if(m.Success)lock(names)names.Add(m.Groups[1].Value);};
    p.Start();p.BeginErrorReadLine();p.BeginOutputReadLine();if(!p.WaitForExit(6000)){p.Kill();p.WaitForExit();throw new TimeoutException("枚举摄像头超时");}p.WaitForExit();
   }return names.Distinct().ToArray();
  }
 }
 public static class RecorderProgram {
  static Clip active;static Process encoder;static Settings settings;static ClipClock clock;static double requested;
  static readonly Stopwatch watch=Stopwatch.StartNew();static readonly object linesLock=new object();
  static string lastError="";static int frames;static bool test;
  static readonly HashSet<string> successful=new HashSet<string>();
  static readonly string events=LocalStore.At("Events"),queue=LocalStore.At("Queue");
  static double Now {get{return watch.Elapsed.TotalSeconds;}}
  static void Save(Clip clip){LocalStore.Write(Path.Combine(events,clip.Id+".json"),clip);}
  public static int Main(string[] args){
   try {LocalStore.Ensure();Directory.CreateDirectory(events);Directory.CreateDirectory(queue);
    bool created;string key="Local\\ScreenCoverUpgrade.Recorder."+WindowsIdentity.GetCurrent().User.Value+"."+Process.GetCurrentProcess().SessionId;
#if TESTING
    key+=".test."+LocalStore.Root.GetHashCode();
#endif
    using(var mutex=new Mutex(true,key,out created)){if(!created)return 0;
     try {Recover();double lastWork=Now;
      while(true){
       foreach(string file in Directory.GetFiles(queue,"*.json").OrderBy(f=>f,StringComparer.Ordinal)){
        try {var cmd=LocalStore.Read<RecorderCommand>(file);Handle(cmd);File.Delete(file);lastWork=Now;}
        catch(Exception e){try{LocalStore.Write(file+".failed",new{failure=LocalStore.Error(e)});File.Delete(file);}catch{} }
       }
       Poll();if(active!=null)lastWork=Now;
       if(active==null&&Now-lastWork>30){Cleanup();if(Directory.GetFiles(queue,"*.json").Length==0)break;}
       Thread.Sleep(50);
      }
     }finally{if(active!=null)Finish("save_failed","录像工作进程退出，片段可能不完整。");mutex.ReleaseMutex();}
    }return 0;
   }catch{return 1;}
  }
  static void Recover(){foreach(string f in Directory.GetFiles(events,"*.json")){try{var c=LocalStore.Read<Clip>(f);if(c.CompletedUtc==null){c.Status="interrupted";c.Failure="先前录像工作进程异常结束；保留已有文件供恢复。";c.CompletedUtc=DateTime.UtcNow.ToString("o");Save(c);}}catch{}}Cleanup();}
  static void Handle(RecorderCommand cmd){
   if(cmd.Kind=="success"){
    successful.Add(cmd.Attempt??"");
    if(active!=null&&active.AutoDeletable(cmd.Attempt))Finish("owner_cancelled","");
    foreach(string f in Directory.GetFiles(events,"*.json")){var c=LocalStore.Read<Clip>(f);if(c.CompletedUtc!=null&&c.AutoDeletable(cmd.Attempt)){DeleteFiles(c);c.Status="owner_deleted";c.Path=null;c.Events.Clear();c.Failure="本次误输录像已由同一尝试的正确密码解锁清理。";Save(c);}}
    return;
   }
   if(cmd.Kind=="delete"||cmd.Kind=="clear"){
    foreach(string f in Directory.GetFiles(events,"*.json")){var c=LocalStore.Read<Clip>(f);if(c.CompletedUtc!=null&&(cmd.Kind=="clear"||c.Id==cmd.Id||c.Id==cmd.Attempt)){DeleteFiles(c);File.Delete(f);}}return;
   }
   if(cmd.Kind!="trigger"&&cmd.Kind!="test")return;
   settings=Settings.Load();bool isTest=cmd.Kind=="test";
   if(!isTest&&!settings.RecordingEnabled)return;
   // Drain frame/deadline first: a new event after a hard limit gets a new segment.
   Poll();
   var ev=new Trigger{Id=cmd.Id,Attempt=cmd.Attempt,Reason=cmd.Reason,Utc=cmd.Utc};
   if(active!=null){
    if(isTest){var busy=new Clip{Id=LocalStore.Id(),CreatedUtc=cmd.Utc,CompletedUtc=DateTime.UtcNow.ToString("o"),Status="start_failed",Failure="已有录像正在进行，请完成后再测试摄像头。"};busy.Events.Add(ev);Save(busy);return;}
    active.Events.Add(ev);clock.Trigger(Now);Save(active);return;
   }
   active=new Clip{Id=LocalStore.Id(),CreatedUtc=cmd.Utc,Status="starting"};active.Events.Add(ev);test=isTest;
   try {
    if(string.IsNullOrEmpty(settings.Camera))throw new InvalidOperationException("未选择摄像头。");
    if(!File.Exists(CameraTools.Encoder))throw new FileNotFoundException("缺少摄像头编码程序 tools/ffmpeg.exe。");
    Directory.CreateDirectory(settings.SaveDirectory);
    active.Path=Path.Combine(settings.SaveDirectory,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+active.Id+".mkv");Save(active);
    frames=0;lastError="";clock=new ClipClock(isTest?3:settings.ClipSeconds,isTest?3:settings.MaxClipSeconds);requested=Now;
    string args="-hide_banner -loglevel warning -stats_period 0.25 -progress pipe:1 -stdin -f dshow -rtbufsize 64M -i "+CameraTools.Quote("video="+settings.Camera)+" -an -vf scale=1280:-2 -r 15 -c:v libx264 -preset ultrafast -crf 25 -pix_fmt yuv420p -f matroska -cluster_time_limit 1000 -flush_packets 1 -y "+CameraTools.Quote(active.Path);
    encoder=new Process{StartInfo=CameraTools.Info(args)};
    encoder.OutputDataReceived+=(s,e)=>{int n;if(e.Data!=null&&e.Data.StartsWith("frame=")&&int.TryParse(e.Data.Substring(6).Trim(),out n)&&n>0)Interlocked.Exchange(ref frames,n);};
    encoder.ErrorDataReceived+=(s,e)=>{if(!string.IsNullOrEmpty(e.Data))lock(linesLock){lastError=(lastError+e.Data+"\n");if(lastError.Length>6000)lastError=lastError.Substring(lastError.Length-6000);}};
    encoder.Start();encoder.BeginErrorReadLine();encoder.BeginOutputReadLine();
   }catch(Exception e){Finish("start_failed",LocalStore.Error(e));}
  }
  static void Poll(){
   if(active==null||encoder==null)return;
   if(Interlocked.CompareExchange(ref frames,0,0)>0&&!clock.First.HasValue){clock.Frame(Now);active.StartedUtc=DateTime.UtcNow.ToString("o");active.Status="recording";Save(active);}
   if(encoder.HasExited){encoder.WaitForExit();string err;lock(linesLock)err=lastError;Finish(clock.First.HasValue?"save_failed":"start_failed","编码器提前退出，退出码 "+encoder.ExitCode+"。"+err);return;}
   if(!clock.First.HasValue&&Now-requested>20){Finish("start_failed","20秒内未获得首帧。权限、占用或设备格式可能不兼容。 "+lastError);return;}
   if(clock.Done(Now))Finish("saved","");
  }
  static void Finish(string status,string failure){
   if(active==null)return;var clip=active;active=null;
   if(encoder!=null){try{
    if(!encoder.HasExited){try{encoder.StandardInput.WriteLine("q");encoder.StandardInput.Flush();}catch{}
     if(!encoder.WaitForExit(5000)){encoder.Kill();encoder.WaitForExit();if(status=="saved"){status="save_failed";failure="编码器未及时完成保存，已结束本次采集，保留部分文件。";}}
    }encoder.WaitForExit();if(status=="saved"&&encoder.ExitCode!=0){status="save_failed";failure=lastError;}
   }catch(Exception e){status="save_failed";failure=LocalStore.Error(e);}finally{encoder.Dispose();encoder=null;}}
   if(status=="saved"&&(clip.Path==null||!File.Exists(clip.Path)||new FileInfo(clip.Path).Length==0)){status="save_failed";failure="未产生有效视频文件。";}
   clip.Status=status;clip.Failure=failure;clip.CompletedUtc=DateTime.UtcNow.ToString("o");
   if(status=="owner_cancelled"){DeleteFiles(clip);clip.Path=null;}
   // Mixed or otherwise ambiguous attribution is never auto-deleted.
   if(clip.Events.Count>0&&clip.Events.All(e=>e.Reason=="password_error")&&clip.Events.Select(e=>e.Attempt).Distinct().Count()==1&&successful.Contains(clip.Events[0].Attempt)){
    DeleteFiles(clip);clip.Path=null;clip.Status="owner_deleted";clip.Events.Clear();
   }
   Save(clip);
   if(test&&clip.Status=="saved")Preview(clip);
   Cleanup();
  }
  static void Preview(Clip c){try{using(var p=new Process()){p.StartInfo=CameraTools.Info("-hide_banner -loglevel error -i "+CameraTools.Quote(c.Path)+" -frames:v 1 -y "+CameraTools.Quote(c.Path+".jpg"));p.Start();p.BeginOutputReadLine();p.BeginErrorReadLine();if(!p.WaitForExit(6000)){p.Kill();p.WaitForExit();}p.WaitForExit();}}catch{}}
  static void DeleteFiles(Clip c){if(!string.IsNullOrEmpty(c.Path)){if(File.Exists(c.Path))File.Delete(c.Path);if(File.Exists(c.Path+".jpg"))File.Delete(c.Path+".jpg");}}
  static void Cleanup(){
   var s=Settings.Load();var clips=new List<Clip>();
   foreach(var f in Directory.GetFiles(events,"*.json")){try{var c=LocalStore.Read<Clip>(f);if(c.CompletedUtc!=null)clips.Add(c);}catch{}}
   long size=clips.Where(c=>c.Path!=null&&File.Exists(c.Path)).Sum(c=>new FileInfo(c.Path).Length);
   foreach(var c in clips.OrderBy(c=>c.CreatedUtc,StringComparer.Ordinal)){
    DateTime date;if(!DateTime.TryParse(c.CompletedUtc,null,System.Globalization.DateTimeStyles.RoundtripKind,out date))continue;
    if(date.ToUniversalTime()<DateTime.UtcNow.AddDays(-s.RetentionDays)||size>(long)s.CapacityMB*1024*1024){
     try{if(c.Path!=null&&File.Exists(c.Path))size-=new FileInfo(c.Path).Length;DeleteFiles(c);File.Delete(Path.Combine(events,c.Id+".json"));}catch{}
    }
   }
  }
 }
}
