using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Diagnostics;
using System.Text;
namespace WOTester {
 public static class Tests {
  static int count;
  static void Check(bool condition,string message){count++;if(!condition)throw new Exception("FAIL: "+message);}
  [STAThread] public static int Main(string[] args) {
   if(args.Length>0&&args[0]=="--script-fail"){Console.WriteLine("SCRIPT ERROR: fixture");Console.Out.Flush();Thread.Sleep(10000);return 0;}
   if(args.Length>0&&args[0]=="--sleep"){Thread.Sleep(10000);return 0;}
   if(args.Length>0&&args[0]=="--echo"){Console.Write(Convert.ToBase64String(Encoding.UTF8.GetBytes(args[1])));return 0;}
   if(args.Length>0&&args[0]=="--profile"){Console.Write(Environment.GetEnvironmentVariable("APPDATA"));return 0;}
   string root=Path.Combine(Path.GetTempPath(),"wo-tester-selftest-"+Guid.NewGuid().ToString("N"));
   Directory.CreateDirectory(root);
   try {
    Check(Runner.Classify(new ProcessOutcome{Exit=0,Text="PASS"})=="PASS","successful exit");
    Check(Runner.Classify(new ProcessOutcome{Exit=0,Text="SCRIPT ERROR: invalid access"})=="FAIL","zero exit with script error");
    Check(Runner.Classify(new ProcessOutcome{Exit=0,Text="WO_TESTER_DONE {\"failed\":0}\n"})=="PASS","machine protocol is not a false failure");
    Check(Runner.Classify(new ProcessOutcome{Exit=0,Text="ERROR: 1 resources still in use at exit"})=="REVIEW","cleanup leak remains visible as review");
    Check(Runner.Classify(new ProcessOutcome{Exit=0,Text="AUDIO_REPORT {\"failed\":[]}"})=="PASS","structured empty failed list");
    Check(Runner.Classify(new ProcessOutcome{Exit=0,Text="AUDIO_REPORT {\"failed\":[\"bad\"]}"})=="FAIL","structured failed list");
    Check(Runner.Classify(new ProcessOutcome{Exit=2,Text=""})=="BLOCKED","missing prerequisites");
    Check(Runner.Classify(new ProcessOutcome{Exit=0,Timeout=true,Text=""})=="BLOCKED","timeouts never pass");
    Check(Runner.Classify(new ProcessOutcome{Exit=0,Cancelled=true,Text=""})=="CANCELLED","cancellation never passes");
    Check(Runner.Classify(new ProcessOutcome{Exit=0,Text="AUDIT: 10 checks, 2 failures"})=="FAIL","positive failure counter");
    Check(Runner.Classify(new ProcessOutcome{Exit=0,Text="WARNING: Epic friends query failed (result 6); retrying with backoff.\nQUEUE_MODES: 5 checks, 0 failures"})=="PASS","expected service warning is not a failure");
    string secret="access_token=very-secret authorization: Bearer eyJabc.eyJxyz.signature";
    Check(!Runner.Redact(secret).Contains("very-secret"),"credentials redacted");
    Check(!Runner.Redact(secret).Contains("eyJabc"),"JWT redacted");
    Check(!Runner.Redact("Authorization: Bearer opaque-secret").Contains("opaque-secret"),"opaque authorization redacted");
    Check(!Runner.Redact("clientSecret=opaque-secret").Contains("opaque-secret"),"camel case credentials redacted");
    string exe=Assembly.GetExecutingAssembly().Location;
    foreach(string value in new[]{"simple","C:\\folder with spaces\\","a\"b","", "español 日本語"}) {
     var p=Runner.Execute(exe,"--echo "+Runner.Quote(value),root,null,5,CancellationToken.None);
     Check(p.Exit==0&&p.Text.Trim()==Convert.ToBase64String(Encoding.UTF8.GetBytes(value)),"Windows argument round trip");
    }
    var broken=Runner.Execute(exe,"--script-fail",root,null,8,CancellationToken.None);
    Check(Runner.Classify(broken)=="FAIL"&&broken.Seconds<5,"script exception closes broken test promptly");
    Check(Runner.Classify(new ProcessOutcome{Timeout=true,Text="SCRIPT ERROR: fixture"})=="FAIL","script failure retained even if time limit expires");
    string profile=Path.Combine(root,"isolated",".godot","test-user");
    var child=Runner.Execute(exe,"--profile",root,profile,5,CancellationToken.None);
    Check(child.Text.Trim()==profile,"child profile isolation");
    Check(Environment.GetEnvironmentVariable("APPDATA")!=profile,"parent environment unchanged");
    var timeout=Runner.Execute(exe,"--sleep",root,profile,1,CancellationToken.None);
    Check(timeout.Timeout&&timeout.Seconds<7,"timed out child terminated");
    using(var cancel=new CancellationTokenSource()){cancel.Cancel();var p=Runner.Execute(exe,"--sleep",root,profile,5,cancel.Token);Check(p.Cancelled,"cancellation terminates child");}
    Directory.CreateDirectory(Path.Combine(root,"data"));Directory.CreateDirectory(Path.Combine(root,"tests"));
    File.WriteAllText(Path.Combine(root,"project.godot"),"[application]\nconfig/version=\"fixture\"\n");
    File.WriteAllText(Path.Combine(root,"data","maps_new.json"),"[{\"name\":\"map\"}]");
    File.WriteAllText(Path.Combine(root,"data","weapons.json"),"[{\"type\":\"mortar\"}]");
    File.WriteAllText(Path.Combine(root,"data","weapons_extra.json"),"[]");
    File.WriteAllText(Path.Combine(root,"data","pets.json"),"[{\"type\":\"dog\"}]");
    File.WriteAllText(Path.Combine(root,"tests","fresh_test.gd"),"extends Node\n");
    File.WriteAllText(Path.Combine(root,"tests","fresh_test.tscn"),"[gd_scene]\n");
    var first=Scanner.Scan(root);
    Check(first.Maps.Count==1&&first.Weapons.Count==1&&first.Tests.Count==1,"new content and test discovery");
    Check(first.Version=="fixture","game version");
    File.WriteAllText(Path.Combine(root,"data","weapons.json"),"[{\"type\":\"mortar\"},{\"type\":\"new\"}]");
    var second=Scanner.Scan(root);
    Check(second.Weapons.Count==2&&second.Fingerprint!=first.Fingerprint,"content updates change fingerprint");
    string bad=Path.Combine(root,"data","bad.json");File.WriteAllText(bad,"{no");
    Check(Runner.ValidateJson(root,bad).Status=="FAIL","malformed JSON caught");
    File.WriteAllText(bad,"{\"image\":\"res://missing.png\"}");
    Check(Runner.ValidateJson(root,bad).Status=="FAIL","missing asset caught");
    File.WriteAllText(bad,"{\"image\":\"res://../outside.png\"}");
    Check(Runner.ValidateJson(root,bad).Detail.Contains("fuera"),"resource traversal rejected");
    File.WriteAllText(bad,"[{\"type\":\"same\"},{\"type\":\"same\"}]");
    Check(Runner.ValidateJson(root,bad).Status=="FAIL","duplicate IDs caught");
    Check(!Scanner.Inside(root,root+"-other\\asset.png"),"path prefix collision rejected");
    var report=new Report{Folder=Path.Combine(root,"report"),Id="fixture",GameVersion="fixture",State="COMPLETED",StartedUtc=DateTime.UtcNow.ToString("o")};
    report.Results.Add(new Result{Id="fixture",Name="<script>alert(1)</script>",Detail="<img onerror=alert(1)>",Status="FAIL",Category="fixture",Suggestion="=HYPERLINK(\"bad\")",Log="evidence.log",Checks=2});
    Reports.Save(report);
    string html=File.ReadAllText(Path.Combine(report.Folder,"report.html"));
    Check(!html.Contains("<img onerror"),"report escapes untrusted content");
    Check(html.Contains("&lt;script&gt;alert"),"report safely displays markup");
    Check(File.ReadAllText(Path.Combine(report.Folder,"results.csv")).Contains("'=HYPERLINK"),"spreadsheet formula injection prevented");
    Check(Product.Read<Report>(Path.Combine(report.Folder,"report.json")).Failed==1,"report JSON round trip");
    Console.WriteLine("PASS: "+count+" meaningful checks.");return 0;
   } catch(Exception e){Console.Error.WriteLine(e);return 1;}
   finally {
    string allowed=Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\')+"\\wo-tester-selftest-";
    if(Path.GetFullPath(root).StartsWith(allowed,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(root))Directory.Delete(root,true);
   }
  }
 }
}
