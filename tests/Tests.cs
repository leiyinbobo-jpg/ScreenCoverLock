using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using ScreenCoverControl;
class Tests {
 static int count;
 static void Check(bool b,string s){if(!b)throw new Exception(s);count++;}
 static Dictionary<string,object> Req(string a){return new Dictionary<string,object>{{"action",a},{"leaseSeconds",600},{"maxSeconds",1800}};}
 static bool Ok(Dictionary<string,object> r){return (bool)r["ok"];}
 static Dictionary<string,object> Auth(string a,string t){var r=Req(a);r["session"]=t;return r;}
 static void Main(string[] args){
  double time=0; int hidden=0,shown=0;
  var l=new SessionLease(true,"test",()=>hidden++,()=>shown++,()=>time);
  Check(!l.Handle(Req("status")).ContainsKey("session"),"status secrecy");
  var p=l.Handle(Req("pause")); string token=(string)p["session"];
  Check(Ok(p)&&hidden==1,"pause");
  Check(!Ok(l.Handle(Req("pause")))&&hidden==1,"busy");
  Check(!Ok(l.Handle(Auth("resume","wrong")))&&shown==0,"bad credential");
  time=599;Check(Ok(l.Handle(Auth("renew",token))),"renew");
  time=1198;Check(Ok(l.Handle(Auth("renew",token))),"renew twice");
  time=1797;Check(Ok(l.Handle(Auth("renew",token))),"clamp");
  time=1800;l.Poll();Check(shown==1,"hard expiry");
  Check(!Ok(l.Handle(Auth("renew",token))),"cannot revive");
  p=l.Handle(Req("pause"));token=(string)p["session"];l.RestoreManually();
  Check(!Ok(l.Handle(Auth("renew",token))),"manual wins");
  p=l.Handle(Req("pause"));time+=601;l.Poll();Check(shown==3,"lease expiry");
  var disabled=new SessionLease(false,"test",()=>{throw new Exception();},()=>{},()=>time);
  Check(!Ok(disabled.Handle(Req("pause"))),"opt in required");
  var bad=Req("pause");bad["leaseSeconds"]=601;Check(!Ok(l.Handle(bad)),"duration bounds");
  int attempts=0;
  var fail=new SessionLease(true,"test",()=>{throw new Exception();},()=>attempts++,()=>time);
  Check(!Ok(fail.Handle(Req("pause")))&&attempts==1,"rollback");
  bool broken=true;
  var recovery=new SessionLease(true,"test",()=>{},()=>{if(broken)throw new Exception();},()=>time);
  p=recovery.Handle(Req("pause"));time+=601;
  try{recovery.Poll();}catch{} broken=false;recovery.Poll();
  Check((string)recovery.Handle(Req("status"))["state"]=="covered","restore retry");
    p=l.Handle(Req("pause"));token=(string)p["session"];l.RestoreAfterPhysicalInput();
  Check((string)l.Handle(Req("status"))["state"]=="covered","physical restores");
  Check((string)l.Handle(Req("status"))["lastRestoreReason"]=="physical_input","reason visible");
  Check(!Ok(l.Handle(Auth("renew",token))),"keeper cannot undo physical restore");
  Check(!InputGuard.IsInjectedMouse(0)&&InputGuard.IsInjectedMouse(1)&&InputGuard.IsInjectedMouse(2),"mouse flags");
  Check(!InputGuard.IsInjectedKeyboard(0)&&!InputGuard.IsInjectedKeyboard(1)&&InputGuard.IsInjectedKeyboard(0x10)&&InputGuard.IsInjectedKeyboard(2),"keyboard flags");
  int trips=0;
  using(var guard=new InputGuard(()=>trips++)){
   var decision=typeof(InputGuard).GetMethod("StopInput",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
   Check(!(bool)decision.Invoke(guard,new object[]{true})&&trips==0,"injection passes");
   Check((bool)decision.Invoke(guard,new object[]{false})&&trips==1,"physical suppressed and restore queued");
   Check((bool)decision.Invoke(guard,new object[]{true})&&trips==1,"latched block until restored");
  }
  double unlimitedClock=0;int unlimitedRestores=0;
  var unbounded=new SessionLease(true,"test",()=>{},()=>unlimitedRestores++,()=>unlimitedClock);
  var unlimitedRequest=Req("pause");unlimitedRequest["maxSeconds"]=0;
  var unlimitedReply=unbounded.Handle(unlimitedRequest);string unlimitedToken=(string)unlimitedReply["session"];
  Check((bool)unlimitedReply["unlimitedSession"]&&unlimitedReply["sessionSecondsRemaining"]==null,"unlimited status");
  for(int i=0;i<5000;i++){unlimitedClock+=599;if(!Ok(unbounded.Handle(Auth("renew",unlimitedToken))))throw new Exception("Unlimited renewal failed");}
  Check((string)unbounded.Handle(Req("status"))["state"]=="paused"&&unlimitedRestores==0,"renew survives virtual 34 days without hard cutoff");
  unlimitedClock+=601;unbounded.Poll();Check(unlimitedRestores==1,"lease watchdog still expires");
  Check(!Ok(unbounded.Handle(Auth("renew",unlimitedToken))),"expired unlimited cannot reopen");
  unlimitedReply=unbounded.Handle(unlimitedRequest);unlimitedToken=(string)unlimitedReply["session"];
  unbounded.RestoreAfterPhysicalInput();Check(!Ok(unbounded.Handle(Auth("renew",unlimitedToken))),"physical input ends unlimited session");
  unlimitedReply=unbounded.Handle(unlimitedRequest);unlimitedToken=(string)unlimitedReply["session"];
  unbounded.RestoreManually();Check(!Ok(unbounded.Handle(Auth("renew",unlimitedToken))),"manual restore ends unlimited session");
  var tooShort=Req("pause");tooShort["maxSeconds"]=1;Check(!Ok(unbounded.Handle(tooShort)),"invalid finite duration rejected");
  string name="ScreenCover.Test."+Guid.NewGuid().ToString("N");
  using(var pipe=new ControlPipe(name,l.Handle)){
   pipe.Start();
   using(var client=new NamedPipeClientStream(".",name,PipeDirection.InOut)){
    client.Connect(3000);var w=new StreamWriter(client,new UTF8Encoding(false));w.AutoFlush=true;
    w.WriteLine("{\"action\":\"status\"}");var r=new StreamReader(client);Check(r.ReadLine().Contains("covered"),"pipe status");
   }
  }
  Console.WriteLine("PASS "+count+" headless checks; no cover windows created.");
  if(args.Length>0){using(var pipe=new ControlPipe(args[0],l.Handle)){pipe.Start(); Console.WriteLine("FIXTURE READY");while(!File.Exists(args[1]))System.Threading.Thread.Sleep(100);}}
 }
}
