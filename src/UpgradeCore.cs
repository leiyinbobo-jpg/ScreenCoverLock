using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Web.Script.Serialization;

namespace ScreenCoverUpgrade {
 public static class LocalStore {
#if TESTING
  public static readonly string Root=Environment.GetEnvironmentVariable("SC_UPGRADE_TEST_ROOT");
#else
  public static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ScreenCoverUpgrade");
#endif
  public static string At(string name){return Path.Combine(Root,name);}
  public static void Ensure(){
   Directory.CreateDirectory(Root);
   var acl=new DirectorySecurity(); var sid=WindowsIdentity.GetCurrent().User;
   acl.SetAccessRuleProtection(true,false); acl.SetOwner(sid);
   acl.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
   Directory.SetAccessControl(Root,acl);
  }
  public static T Read<T>(string path) where T:new(){
   try{using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(file,Encoding.UTF8))return new JavaScriptSerializer().Deserialize<T>(reader.ReadToEnd());}
   catch(FileNotFoundException){return new T();}catch(DirectoryNotFoundException){return new T();}
  }
  public static void Write(string path,object value){
   Directory.CreateDirectory(Path.GetDirectoryName(path));
   string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   File.WriteAllText(tmp,new JavaScriptSerializer().Serialize(value),new UTF8Encoding(false));
   if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);
  }
  public static string Id(){return Guid.NewGuid().ToString("N");}
  public static string Error(Exception e){return e.GetBaseException().GetType().Name+": "+e.GetBaseException().Message;}
 }
 public sealed class Settings {
  public string NormalImage="", DarkImage="", DigitIconFolder="", DigitImage="", CompleteImage="", ErrorImage="", InputPosition="Bottom", Camera="", SaveDirectory=LocalStore.At("Recordings");
  public int IdleSeconds=10, ClipSeconds=15, MaxClipSeconds=60, RetentionDays=7, CapacityMB=1024, InputTimeoutSeconds=60;
  public bool WarnOnError=true, ShowErrorImage=false, RecordingEnabled=false, MouseRecording=false;
  public string TestedCamera="";
  public static Settings Load(){try {var s=LocalStore.Read<Settings>(LocalStore.At("settings.json"));s.Check();return s;}catch{return new Settings();}}
  public void Check(){
   IdleSeconds=Math.Max(1,Math.Min(3600,IdleSeconds));ClipSeconds=Math.Max(1,Math.Min(600,ClipSeconds));
   MaxClipSeconds=Math.Max(ClipSeconds,Math.Min(3600,MaxClipSeconds));RetentionDays=Math.Max(1,Math.Min(3650,RetentionDays));CapacityMB=Math.Max(16,Math.Min(1048576,CapacityMB));
   InputTimeoutSeconds=Math.Max(10,Math.Min(3600,InputTimeoutSeconds));
   if(InputPosition!="Bottom"&&InputPosition!="Center"&&InputPosition!="Top")InputPosition="Bottom";
   if(string.IsNullOrWhiteSpace(SaveDirectory)||!Path.IsPathRooted(SaveDirectory))SaveDirectory=LocalStore.At("Recordings");
   if(RecordingEnabled&&(string.IsNullOrEmpty(Camera)||TestedCamera!=Camera))RecordingEnabled=false;
  }
  public void Save(){Check();LocalStore.Write(LocalStore.At("settings.json"),this);}
 }
 public sealed class PrefixData {public string Key;public string[] Checks;}
 public static class PasswordVault {
  static string Hex(byte[] b){return BitConverter.ToString(b).Replace("-","").ToLowerInvariant();}
  public static string Hash(string s){using(var h=SHA256.Create())return Hex(h.ComputeHash(Encoding.UTF8.GetBytes(s)));}
  static bool Equal(string a,string b){if(a==null||b==null||a.Length!=b.Length)return false;int d=0;for(int i=0;i<a.Length;i++)d|=a[i]^b[i];return d==0;}
  public static bool VerifyLegacy(string input){try{return Equal(Hash(input),Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(LocalStore.At("legacy-verifier.dat")),null,DataProtectionScope.CurrentUser)));}catch{return false;}}
  public static PrefixData Load(){try{return new JavaScriptSerializer().Deserialize<PrefixData>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(LocalStore.At("prefix.dat")),null,DataProtectionScope.CurrentUser)));}catch{return null;}}
  static string Check(string text,byte[] key){using(var h=new HMACSHA256(key))return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(text)));}
  public static void Initialize(string password){
   if(!VerifyLegacy(password))throw new InvalidOperationException("原密码验证失败，未更改密码资料。");
   SaveForPassword(password);
  }
  public static void CreateInitial(string password){
   if(File.Exists(LocalStore.At("legacy-verifier.dat")))throw new InvalidOperationException("此账户已有密码，不能覆盖。");
   if(password.Length!=6)throw new InvalidOperationException("请选择6个字符的密码，与现有输入操作保持一致。");
   byte[] bytes=Encoding.UTF8.GetBytes(Hash(password));
   try{File.WriteAllBytes(LocalStore.At("legacy-verifier.dat"),ProtectedData.Protect(bytes,null,DataProtectionScope.CurrentUser));}finally{Array.Clear(bytes,0,bytes.Length);}
   SaveForPassword(password);
  }
  public static PrefixData Create(string password){
   byte[] key=new byte[32];using(var r=RandomNumberGenerator.Create())r.GetBytes(key);
   var data=new PrefixData{Key=Convert.ToBase64String(key),Checks=new string[password.Length]};
   for(int i=0;i<password.Length;i++)data.Checks[i]=Check(password.Substring(0,i+1),key);
   Array.Clear(key,0,key.Length);return data;
  }
  static void SaveForPassword(string password){
   var data=Create(password);byte[] plain=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(data));
   try {File.WriteAllBytes(LocalStore.At("prefix.dat"),ProtectedData.Protect(plain,null,DataProtectionScope.CurrentUser));}finally{Array.Clear(plain,0,plain.Length);}
  }
  public static bool Prefix(string input,PrefixData data){
   if(data==null||input.Length==0)return true;if(input.Length>data.Checks.Length)return false;
   byte[] key=Convert.FromBase64String(data.Key);try{return Equal(Check(input,key),data.Checks[input.Length-1]);}finally{Array.Clear(key,0,key.Length);}
  }
 }
 // Clock is injectable: wallpaper timeout never resets the input attempt.
 public sealed class LockState {
  readonly Func<double> now;double lastLocal,lastInput;
  public bool Dark,Warning; public string Attempt {get;private set;} public string Previous="";
  public LockState(Func<double> clock){now=clock;lastLocal=lastInput=now();}
  public void Local(){lastLocal=now();}
  public void Poll(int idle,int inputTimeout){if(now()-lastLocal>=idle){Dark=true;Warning=false;}if(Attempt!=null&&now()-lastInput>=inputTimeout)Cancel();}
  public void StartLock(){Dark=false;Warning=false;lastLocal=now();Cancel();}
  public bool Text(string value,PrefixData data){
   bool addition=value.Length>=Previous.Length&&value!=Previous;
   if(value.Length>0&&Attempt==null)Attempt=LocalStore.Id();
   lastInput=now();Previous=value;
   if(addition&&value.Length>0&&data!=null&&!PasswordVault.Prefix(value,data)){Dark=false;Warning=true;return true;}return false;
  }
  public void Wrong(){Dark=false;Warning=true;}
  public void Cancel(){Attempt=null;Previous="";}
 }
 public sealed class Trigger {public string Id,Attempt,Reason,Utc;}
 public sealed class Clip {
  public string Id,Path,CreatedUtc,StartedUtc,CompletedUtc,Status="requested",Failure="";
  public List<Trigger> Events=new List<Trigger>();
  public bool AutoDeletable(string attempt){return !string.IsNullOrEmpty(attempt)&&Events.Count>0&&Events.All(e=>e.Attempt==attempt&&e.Reason=="password_error");}
 }
 public sealed class RecorderCommand {public string Kind,Attempt,Reason,Id,Utc;}
 public static class RecorderClient {
  public static void LogDisabled(string attempt,string reason){try{var c=new Clip{Id=LocalStore.Id(),CreatedUtc=DateTime.UtcNow.ToString("o"),CompletedUtc=DateTime.UtcNow.ToString("o"),Status="recording_disabled"};c.Events.Add(new Trigger{Id=LocalStore.Id(),Attempt=attempt,Reason=reason,Utc=c.CreatedUtc});LocalStore.Write(LocalStore.At("Events\\"+c.Id+".json"),c);}catch{}}
  public static void Send(string kind,string attempt,string reason){
   var cmd=new RecorderCommand{Kind=kind,Attempt=attempt,Reason=reason,Id=LocalStore.Id(),Utc=DateTime.UtcNow.ToString("o")};
   string queue=LocalStore.At("Queue");Directory.CreateDirectory(queue);
   LocalStore.Write(Path.Combine(queue,DateTime.UtcNow.Ticks.ToString("D19")+"-"+cmd.Id+".json"),cmd);
   var p=new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ScreenCover.Recorder.exe"));p.UseShellExecute=false;p.CreateNoWindow=true;p.WindowStyle=ProcessWindowStyle.Hidden;
   Process.Start(p).Dispose();
  }
  public static void SafeSend(string kind,string attempt,string reason){try {Send(kind,attempt,reason);}catch(Exception e){
   try {var clip=new Clip{Id=LocalStore.Id(),CreatedUtc=DateTime.UtcNow.ToString("o"),CompletedUtc=DateTime.UtcNow.ToString("o"),Status="start_failed",Failure=LocalStore.Error(e)};clip.Events.Add(new Trigger{Id=LocalStore.Id(),Attempt=attempt,Reason=reason,Utc=clip.CreatedUtc});LocalStore.Write(LocalStore.At("Events\\"+clip.Id+".json"),clip);}catch{}
  }}
 }
 public sealed class ClipClock {
  public double? First;public double Last;public readonly double Duration,Maximum;
  public ClipClock(double duration,double maximum){Duration=duration;Maximum=maximum;}
  public void Frame(double now){if(!First.HasValue){First=now;Last=now;}}
  public void Trigger(double now){Last=now;}
  public bool Done(double now){return First.HasValue&&(now>=First.Value+Maximum||now>=Last+Duration);}
 }
}
