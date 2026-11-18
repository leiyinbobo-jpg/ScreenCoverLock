using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using ScreenCoverUpgrade;
using ScreenCoverSupport;
using ScreenCoverControl;

class UpgradeTests {
 static int count;
 static void Check(bool value,string name){if(!value)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
 static Dictionary<string,object> Req(string action){return new Dictionary<string,object>{{"action",action},{"leaseSeconds",600},{"maxSeconds",0}};}
 static void Main(){
  double now=0;var state=new LockState(()=>now);var prefix=PasswordVault.Create("8ABC");
  Check(!state.Dark,"normal startup");now=9.99;state.Poll(10,60);Check(!state.Dark,"no early dim");now=10;state.Poll(10,60);Check(state.Dark,"dim at ten seconds");
  state.Local();Check(state.Dark,"mouse activity does not brighten");Check(!state.Text("8",prefix)&&state.Dark,"correct first character remains dark");
  Check(!state.Text("8A",prefix)&&state.Dark,"correct later prefix remains dark");
  string attempt=state.Attempt;Check(state.Text("8AX",prefix)&&!state.Dark&&state.Warning,"wrong later character brightens and warns");
  Check(!state.Text("8A",prefix)&&state.Attempt==attempt,"backspace preserves attempt and is not error");
  Check(state.Warning,"backspace does not erase event warning");Check(!state.Text("8ABC",prefix),"correct full value");
  state.Local();now=20;state.Poll(10,60);Check(state.Dark&&!state.Warning&&state.Attempt==attempt,"dim hides warning without cancelling attempt");
  now=71;state.Poll(10,60);Check(state.Attempt==null,"independent attempt timeout");
  state.StartLock();Check(!state.Dark&&!state.Warning&&state.Attempt==null,"relock resets wallpaper and input");
  Check(state.Text("9",prefix)&&state.Warning,"wrong first character immediate");
  state.Text("",prefix);Check(state.Attempt!=null,"erase all permits same attempt correction");state.Cancel();Check(state.Attempt==null,"explicit cancellation ends attempt");
  Check(!state.Text("",prefix),"empty text not error");Check(!state.Text("x",null),"uninitialized verifier does not flag prefixes");
  Check(PasswordVault.Prefix("8",prefix),"prefix verifier first");Check(PasswordVault.Prefix("8ABC",prefix),"prefix verifier complete");
  Check(!PasswordVault.Prefix("8ABc",prefix),"case sensitive");Check(!PasswordVault.Prefix("8ABCD",prefix),"longer than password rejected");
  Check(prefix.Checks[0]!="8"&&prefix.Checks[3]!="8ABC","no plaintext prefix persisted");
  var clock=new ClipClock(15,60);clock.Trigger(0);Check(!clock.Done(100),"recording duration waits for actual frame");clock.Frame(100);Check(!clock.Done(114.9)&&clock.Done(115),"fifteen seconds after first frame");
  clock=new ClipClock(15,60);clock.Frame(0);clock.Trigger(12);Check(!clock.Done(20)&&clock.Done(27),"repeat trigger extends deadline");
  for(int i=0;i<60;i++)clock.Trigger(i);Check(clock.Done(60),"hard maximum beats extensions");
  state.Wrong();now+=11;state.Poll(10,60);Check(clock.Done(60),"wallpaper state independent of clip clock");
  var clip=new Clip();clip.Events.Add(new Trigger{Attempt="a",Reason="password_error"});Check(clip.AutoDeletable("a"),"same attempt cleanup allowed");
  Check(!clip.AutoDeletable("b"),"historical other attempt retained");clip.Events.Add(new Trigger{Attempt="b",Reason="password_error"});Check(!clip.AutoDeletable("a"),"mixed attempt clip retained");
  clip.Events.RemoveAt(1);clip.Events.Add(new Trigger{Attempt=null,Reason="emergency"});Check(!clip.AutoDeletable("a"),"emergency mixed clip retained");
  Check(!clip.AutoDeletable(null),"unknown attribution retained");
  int covered=0,paused=0;double leaseTime=0;var lease=new SessionLease(true,"upgrade-test",()=>paused++,()=>covered++,()=>leaseTime);
  var reply=lease.Handle(Req("pause"));string token=(string)reply["session"];
  Check(paused==1&&(bool)reply["unlimitedSession"],"authorized unlimited session retained");
  Check(!(bool)lease.Handle(Req("pause"))["ok"],"busy cannot steal pause");
  Check(!lease.Handle(Req("status")).ContainsKey("session"),"status does not expose credential");
  lease.RestoreAfterPhysicalInput();Check(covered==1&&(string)lease.Handle(Req("status"))["lastRestoreReason"]=="physical_input","physical restoration retained");
  var renew=Req("renew");renew["session"]=token;Check(!(bool)lease.Handle(renew)["ok"],"stale automation cannot revive");
  var disabled=new SessionLease(false,"upgrade-test",()=>{},()=>{},()=>0);Check(!(bool)disabled.Handle(Req("pause"))["ok"],"injected input does not grant authorization");
  Check(InputGuard.IsInjectedMouse(1)&&!InputGuard.IsInjectedMouse(0),"mouse injection classification retained");Check(InputGuard.IsInjectedKeyboard(0x10)&&!InputGuard.IsInjectedKeyboard(0),"keyboard classification retained");
  var settings=new Settings();Check(!settings.RecordingEnabled&&!settings.MouseRecording,"camera defaults off");Check(settings.IdleSeconds==10&&settings.ClipSeconds==15&&settings.MaxClipSeconds==60,"timing defaults");
  settings.RecordingEnabled=true;settings.Camera="test";settings.Check();Check(!settings.RecordingEnabled,"first enable requires user camera test");
  settings.TestedCamera="test";settings.RecordingEnabled=true;settings.Check();Check(settings.RecordingEnabled,"tested camera can be enabled");
  settings.Camera="other";settings.Check();Check(!settings.RecordingEnabled,"changed camera requires new test");

  // Custom UI image & layout tests
  settings.InputPosition="Invalid";settings.Check();Check(settings.InputPosition=="Bottom","invalid input position falls back to Bottom");
  settings.InputPosition="Center";settings.Check();Check(settings.InputPosition=="Center","Center input position preserved");
  var bounds=new Rectangle(0,0,1920,1080);
  var topBox=CoverBackground.DialogueBounds(bounds,"Top");
  var centerBox=CoverBackground.DialogueBounds(bounds,"Center");
  var bottomBox=CoverBackground.DialogueBounds(bounds,"Bottom");
  Check(topBox.Top<centerBox.Top&&centerBox.Top<bottomBox.Top,"dialogue bounds obey Top < Center < Bottom");

  string tempDir=Path.Combine(Path.GetTempPath(),"sc-img-test-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(tempDir);
  try{
   string customPng=Path.Combine(tempDir,"custom.png");
   using(var b=new Bitmap(200,100))using(var g=Graphics.FromImage(b)){g.Clear(Color.FromArgb(255,200,100,50));b.Save(customPng,System.Drawing.Imaging.ImageFormat.Png);}
   var customSettings=new Settings{NormalImage=customPng,DarkImage="",DigitImage=customPng};
   CoverBackground.Configure(customSettings);
   using(var sample=new Bitmap(customPng))using(var dimmed=(Bitmap)CoverBackground.DimCopy(sample,0.28f)){
    Color orig=sample.GetPixel(10,10),dim=dimmed.GetPixel(10,10);
    Check(dim.R<orig.R&&dim.G<orig.G,"auto dim reduces brightness of custom normal wallpaper");
   }
   string folderDir=Path.Combine(tempDir,"icons");Directory.CreateDirectory(folderDir);
   string d2=Path.Combine(folderDir,"2.png");File.Copy(customPng,d2);
   string c6=Path.Combine(folderDir,"complete.png");File.Copy(customPng,c6);
   var folderSettings=new Settings{DigitIconFolder=folderDir,DigitImage=customPng};
   Check(CoverAssets.ResolveDigitPath(folderSettings,2)==d2,"folder digit overrides single digit icon");
   Check(CoverAssets.ResolveDigitPath(folderSettings,1)==customPng,"missing folder digit falls back to single digit icon");
   Check(CoverAssets.ResolveCompletePath(folderSettings)==c6,"complete icon resolved from folder");
   using(var assets=new CoverAssets()){
    assets.Configure(folderSettings);
    Check(assets.Digits[0]!=null&&assets.CompleteIcon!=null&&assets.ErrorIcon!=null,"cover assets load cleanly with embedded fallback");
   }
  }finally{
   try{Directory.Delete(tempDir,true);}catch{}
  }

  Console.WriteLine("TOTAL "+count+" checks passed. No camera, hooks or live control used.");
 }
}
