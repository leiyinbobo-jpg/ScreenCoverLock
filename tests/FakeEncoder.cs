using System;
using System.IO;
using System.Threading;
// Test-only encoder: never opens any hardware. Production builds do not reference it.
class FakeEncoder {
 static int Main(string[] args){
  string root=Environment.GetEnvironmentVariable("SC_UPGRADE_TEST_ROOT"),mode=File.ReadAllText(Path.Combine(root,"mode.txt"));
  if(mode=="start_failure"){Console.Error.WriteLine("fixture: camera unavailable");return 8;}
  string file=args[args.Length-1];using(var stream=new FileStream(file,FileMode.Create,FileAccess.Write,FileShare.Read)){
   stream.Write(new byte[2048],0,2048);stream.Flush();
   Console.WriteLine("frame=1");Console.Out.Flush();
   if(mode=="save_failure"){Thread.Sleep(100);Console.Error.WriteLine("fixture: disk write failed");return 9;}
   Console.ReadLine();
  }return 0;
 }
}
