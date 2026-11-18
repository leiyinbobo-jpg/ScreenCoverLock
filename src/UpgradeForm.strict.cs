using System;
using System.Diagnostics;
using System.Windows.Forms;
using ScreenCoverSupport;
using ScreenCoverUpgrade;

namespace ScreenCoverStrict {
 sealed partial class CoverForm {
  Settings _settings;PrefixData _prefix;LockState _state;ActivityMonitor _activity;CoverAssets _assets;
  Timer _upgradeTimer;bool _leaving;double _leaveAt,_lastMouseEvent=-100;bool _attemptErrored;
  readonly Stopwatch _upgradeClock=Stopwatch.StartNew();
  double UpgradeNow {get{return _upgradeClock.Elapsed.TotalSeconds;}}
  void InitializeUpgrade(){
   _settings=Settings.Load();_prefix=PasswordVault.Load();_state=new LockState(()=>UpgradeNow);
   CoverBackground.Configure(_settings);
   _assets=new CoverAssets();_assets.Configure(_settings);
   try{_activity=new ActivityMonitor();}catch(Exception e){
    // Monitoring failure must never open the cover or start a camera.
    MessageBox.Show("本地活动监测不可用，遮屏仍保持。请解锁后检查："+LocalStore.Error(e),"ScreenCoverLock");
   }
   _upgradeTimer=new Timer{Interval=100};_upgradeTimer.Tick+=UpgradeTick;_upgradeTimer.Start();Invalidate();
  }
  void UpgradeTick(object sender,EventArgs args){
   if(_state==null)return;
   if(_leaving){if(ActivityMonitor.KeysReleased()||UpgradeNow-_leaveAt>2){_input.Clear();CompleteExit();}return;}
   if(_activity!=null){
    if(_activity.TakeActivity())_state.Local();
    if(_activity.TakeMouse()&&_settings.MouseRecording&&UpgradeNow-_lastMouseEvent>1){
     _lastMouseEvent=UpgradeNow;RequestRecording(null,"mouse");
    }
   }
   if(_automationPaused)return;
   bool dark=_state.Dark,warning=_state.Warning;string attempt=_state.Attempt;
   _state.Poll(_settings.IdleSeconds,_settings.InputTimeoutSeconds);
   if(attempt!=null&&_state.Attempt==null){_input.Clear();_attemptErrored=false;}
   if(dark!=_state.Dark||warning!=_state.Warning){CoverBackground.Dark=_state.Dark;Invalidate();if(_displayCovers!=null)_displayCovers.RefreshBackground();}
  }
  void UpgradeTextChanged(){
   if(_state==null||_automationPaused||_leaving){Invalidate();return;}
   if(_state.Text(_input.Text,_prefix)){
    _attemptErrored=true;ShowUpgradeWarning();RequestRecording(_state.Attempt,"password_error");
   }Invalidate();
  }
  void ShowUpgradeWarning(){
   if(!_settings.WarnOnError){_state.Warning=false;_state.Dark=CoverBackground.Dark;return;}_state.Wrong();if(_settings.WarnOnError){CoverBackground.Dark=false;if(_displayCovers!=null)_displayCovers.RefreshBackground();}Invalidate();
  }
  void RequestRecording(string attempt,string reason){if(_settings!=null){if(_settings.RecordingEnabled)RecorderClient.SafeSend("trigger",attempt,reason);else RecorderClient.LogDisabled(attempt,reason);}}
  void UpgradeSubmitFailed(){
   if(_state==null)return;
   if(!_attemptErrored)RequestRecording(_state.Attempt,"password_error");
   ShowUpgradeWarning();_state.Cancel();_attemptErrored=false;
  }
  void CancelAttempt(){if(_state!=null)_state.Cancel();_attemptErrored=false;_input.Clear();}
  void PasswordSucceeded(){
   if(_state!=null&&_state.Attempt!=null&&_settings.RecordingEnabled)RecorderClient.SafeSend("success",_state.Attempt,"");
   ExitCover();
  }
  void EmergencyExit(){
   if(_leaving)return;
   // Injected hotkeys retain existing exit behavior but are not local recording triggers.
   if(_activity!=null&&_activity.LocalEmergency)RequestRecording(null,"emergency");
   ExitCover();
  }
  void ExitCover(){
   if(_leaving)return;_leaving=true;_leaveAt=UpgradeNow;CancelAttempt();
   if(_upgradeTimer==null)CompleteExit();
  }
  void UpgradeRestored(){
   if(_state==null)return;CancelAttempt();_state.StartLock();CoverBackground.Dark=false;
  }
  void DisposeUpgrade(){
   if(_upgradeTimer!=null){_upgradeTimer.Stop();_upgradeTimer.Dispose();_upgradeTimer=null;}
   if(_activity!=null){_activity.Dispose();_activity=null;}
   if(_assets!=null){_assets.Dispose();_assets=null;}
  }
 }
}
