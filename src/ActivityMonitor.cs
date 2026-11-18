using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Threading;

namespace ScreenCoverUpgrade {
 public sealed class ActivityMonitor:IDisposable {
  delegate IntPtr Hook(int c,IntPtr m,IntPtr d);
  Hook keyboard,mouse;IntPtr kh,mh;long activity,mouseAction,emergency;int lastX,lastY;bool havePoint;
  public ActivityMonitor(){keyboard=Key;mouse=Mouse;kh=SetWindowsHookEx(13,keyboard,GetModuleHandle(null),0);if(kh==IntPtr.Zero)throw new Win32Exception();mh=SetWindowsHookEx(14,mouse,GetModuleHandle(null),0);if(mh==IntPtr.Zero){Dispose();throw new Win32Exception();}}
  public bool TakeActivity(){return Interlocked.Exchange(ref activity,0)!=0;}
  public bool TakeMouse(){return Interlocked.Exchange(ref mouseAction,0)!=0;}
  public bool LocalEmergency {get{return (Stopwatch.GetTimestamp()-Interlocked.Read(ref emergency))/(double)Stopwatch.Frequency<1.5;}}
  IntPtr Key(int code,IntPtr message,IntPtr data){
   if(code>=0&&!ScreenCoverControl.InputGuard.IsInjectedKeyboard(unchecked((uint)Marshal.ReadInt32(data,8)))){
    Interlocked.Exchange(ref activity,1);
    int vk=Marshal.ReadInt32(data);long msg=message.ToInt64();
    if((msg==0x100||msg==0x104)&&vk==0x51&&(GetAsyncKeyState(0x11)&0x8000)!=0&&(GetAsyncKeyState(0x10)&0x8000)!=0)Interlocked.Exchange(ref emergency,Stopwatch.GetTimestamp());
   }return CallNextHookEx(kh,code,message,data);
  }
  IntPtr Mouse(int code,IntPtr message,IntPtr data){
   if(code>=0&&!ScreenCoverControl.InputGuard.IsInjectedMouse(unchecked((uint)Marshal.ReadInt32(data,12)))){
    Interlocked.Exchange(ref activity,1);int x=Marshal.ReadInt32(data),y=Marshal.ReadInt32(data,4);int msg=message.ToInt32();
    bool deliberate=msg==0x201||msg==0x204||msg==0x207||msg==0x20b||msg==0x20a||msg==0x20e;
    if(!havePoint){lastX=x;lastY=y;havePoint=true;}
    if(Math.Abs(x-lastX)+Math.Abs(y-lastY)>=12){deliberate=true;lastX=x;lastY=y;}
    if(deliberate)Interlocked.Exchange(ref mouseAction,1);
   }return CallNextHookEx(mh,code,message,data);
  }
  public static bool KeysReleased(){for(int i=8;i<256;i++)if((GetAsyncKeyState(i)&0x8000)!=0)return false;return true;}
  public void Dispose(){if(kh!=IntPtr.Zero){UnhookWindowsHookEx(kh);kh=IntPtr.Zero;}if(mh!=IntPtr.Zero){UnhookWindowsHookEx(mh);mh=IntPtr.Zero;}}
  [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SetWindowsHookEx(int t,Hook cb,IntPtr module,uint thread);
  [DllImport("user32.dll")]static extern bool UnhookWindowsHookEx(IntPtr h);
  [DllImport("user32.dll")]static extern IntPtr CallNextHookEx(IntPtr h,int c,IntPtr m,IntPtr d);
  [DllImport("kernel32.dll",CharSet=CharSet.Auto)]static extern IntPtr GetModuleHandle(string n);
  [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
 }
}
