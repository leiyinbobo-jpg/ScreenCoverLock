using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Security.Cryptography;
using ScreenCoverUpgrade;
class WorkerTests {
 static int checks;static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static Clip[] Clips(){return Directory.GetFiles(LocalStore.At("Events"),"*.json").Select(f=>LocalStore.Read<Clip>(f)).ToArray();}
 static Clip Wait(string attempt,Func<Clip,bool> predicate){for(int i=0;i<150;i++){var c=Clips().LastOrDefault(v=>v.Events.Any(e=>e.Attempt==attempt)&&predicate(v));if(c!=null)return c;Thread.Sleep(100);}throw new Exception("fixture timeout "+attempt);}
 static void Mode(string mode){File.WriteAllText(LocalStore.At("mode.txt"),mode);}
 static void Trigger(string attempt,string reason){RecorderClient.Send("trigger",attempt,reason);}
 static void Main(string[] args){try{if(args.Length>0&&args[0]=="--submit-emergency"){RecorderClient.Send("trigger","parent-exit","emergency");return;}Run();}catch(Exception e){Console.WriteLine("TEST FAILURE "+e.GetType().Name+": "+e.Message);Environment.ExitCode=1;}}
 static void Run(){
  LocalStore.Ensure();Directory.CreateDirectory(LocalStore.At("Events"));
  byte[] plain=Encoding.UTF8.GetBytes(PasswordVault.Hash("8ABC"));File.WriteAllBytes(LocalStore.At("legacy-verifier.dat"),ProtectedData.Protect(plain,null,DataProtectionScope.CurrentUser));Array.Clear(plain,0,plain.Length);
  Check(PasswordVault.VerifyLegacy("8ABC")&&!PasswordVault.VerifyLegacy("wrong"),"DPAPI legacy full verification");
  PasswordVault.Initialize("8ABC");Check(PasswordVault.Prefix("8A",PasswordVault.Load()),"DPAPI prefix round trip");
  var s=new Settings{Camera="fixture",TestedCamera="fixture",RecordingEnabled=true,ClipSeconds=1,MaxClipSeconds=2,SaveDirectory=LocalStore.At("Videos")};s.Save();
  Mode("normal");Trigger("a","password_error");var a=Wait("a",c=>c.Status=="recording");Thread.Sleep(600);Trigger("a","password_error");
  a=Wait("a",c=>c.CompletedUtc!=null);Check(a.Status=="saved"&&a.Events.Count==2,"same camera task absorbs repeat events");
  Check(File.Exists(a.Path),"completed file retained");
  Trigger("b","password_error");var b=Wait("b",c=>c.Status=="recording");RecorderClient.Send("success","b","");
  for(int i=0;i<80&&File.Exists(b.Path);i++)Thread.Sleep(100);
  Check(!File.Exists(b.Path),"successful same attempt stops and deletes own clip");Check(File.Exists(a.Path),"owner cleanup preserves historical clip");
  Trigger("c","password_error");var cclip=Wait("c",c=>c.Status=="recording");Trigger(null,"emergency");Thread.Sleep(200);RecorderClient.Send("success","c","");
  cclip=Wait("c",c=>c.CompletedUtc!=null);Check(cclip.Status=="saved"&&File.Exists(cclip.Path),"emergency attribution prevents auto deletion");
  Mode("start_failure");Trigger("d","password_error");var failed=Wait("d",c=>c.CompletedUtc!=null);Check(failed.Status=="start_failed"&&failed.StartedUtc==null&&failed.Failure.Contains("camera unavailable"),"camera startup failure is recorded, never success");
  Mode("save_failure");Trigger("e","password_error");failed=Wait("e",c=>c.CompletedUtc!=null);Check(failed.Status=="save_failed"&&failed.Failure.Contains("disk write failed"),"encoder write failure has concrete error");
  Mode("normal");string blocked=LocalStore.At("blocked");File.WriteAllText(blocked,"not a directory");s.SaveDirectory=blocked;s.Save();Trigger("f","password_error");failed=Wait("f",c=>c.CompletedUtc!=null);Check(failed.Status=="start_failed"&&failed.Failure.Length>0,"unwritable recording directory fails without hanging");
  s.SaveDirectory=LocalStore.At("Videos");s.Save();Trigger("g","password_error");var g=Wait("g",c=>c.Status=="recording");RecorderClient.Send("delete",g.Id,"");Thread.Sleep(200);Check(File.Exists(g.Path),"record management cannot delete an active write");g=Wait("g",c=>c.CompletedUtc!=null);
  RecorderClient.Send("clear",null,"");for(int i=0;i<100&&Clips().Length!=0;i++)Thread.Sleep(100);Check(Clips().Length==0,"clear removes completed records and files");
  Console.WriteLine("TOTAL "+checks+" worker/DPAPI checks passed; fake encoder only, no real camera.");
 }
}
