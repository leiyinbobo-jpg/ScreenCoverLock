using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ScreenCoverUpgrade;
class CodecTest {
 static int Main(string[] args){try{
  using(var p=new Process()){
   p.StartInfo=CameraTools.Info("-hide_banner -loglevel error -re -f lavfi -i testsrc2=size=640x480:rate=15 -progress pipe:1 -stdin -an -vf scale=1280:-2 -r 15 -c:v libx264 -preset ultrafast -crf 25 -pix_fmt yuv420p -f matroska -cluster_time_limit 1000 -flush_packets 1 -y "+CameraTools.Quote(args[0]));
   p.Start();p.BeginErrorReadLine();bool frame=false;var watch=Stopwatch.StartNew();
   while(watch.Elapsed.TotalSeconds<10){var line=Task.Factory.StartNew(()=>p.StandardOutput.ReadLine());if(!line.Wait(3000))break;int count;if(line.Result==null)break;if(line.Result.StartsWith("frame=")&&int.TryParse(line.Result.Substring(6).Trim(),out count)&&count>0){frame=true;break;}}
   if(!frame){p.Kill();throw new Exception("No real encoder progress frame");}
   Thread.Sleep(1000);p.StandardInput.WriteLine("q");p.StandardInput.Flush();
   if(!p.WaitForExit(5000)){p.Kill();throw new Exception("Encoder did not release on graceful q");}p.WaitForExit();
   if(p.ExitCode!=0||!File.Exists(args[0]))throw new Exception("Codec save failed");
   Console.WriteLine("PASS real FFmpeg first-frame progress, stdin graceful stop and file finalization; synthetic video, no camera.");return 0;
  }
 }catch(Exception e){Console.WriteLine(e.Message);return 1;}}
}
