using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace WOTester {
 sealed class AndroidRunner {
  public const string TestPackage="com.frrentertainment.wodefinitive.test";
  const string CaseIndex="assets/tests/mobile_native_index.json";
  const int ObservationSeconds=10;
  static readonly Regex DeviceDisconnected=new Regex(@"(?im)(?:device (?:'.*?' )?not found|device offline|no devices/emulators found|device disconnected)");
  static readonly Regex SafeSerial=new Regex(@"\A[A-Za-z0-9_.:\[\]-]+\z");
  static readonly Regex LogLine=new Regex(@"^(?<level>[A-Z])/(?<tag>[^\s(]+)\s*\(\s*(?<pid>\d+)\):\s*(?<message>.*)$");
  static readonly Regex QaSummary=new Regex(@"\bANDROID_QA_DONE:\s*screenshots=(\d+)\s+failures=(\d+)\s*$");
  readonly Options options;
  readonly Report report;
  readonly CancellationToken token;
  readonly Action<Result> add;
  readonly Action<string,int,int> progress;
  string adb, serial, package, log, step;

  public AndroidRunner(Options options,Report report,CancellationToken token,Action<Result> add,Action<string,int,int> progress) {
   this.options=options;this.report=report;this.token=token;this.add=add;this.progress=progress;
  }

  public static string DetectAdb(string project) {
   var roots=new List<string>{Environment.GetEnvironmentVariable("ANDROID_HOME"),Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"),
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Android","Sdk")};
   if(!String.IsNullOrWhiteSpace(project)) {
    try { roots.Add(Path.Combine(Scanner.NormalizeProject(project),".godot","android-online","sdk")); }
    catch(ArgumentException) {}
   }
   return roots.Where(x=>!String.IsNullOrWhiteSpace(x)).Select(x=>Path.Combine(x,"platform-tools","adb.exe"))
    .Concat((Environment.GetEnvironmentVariable("PATH")??"").Split(';').Where(x=>x.Length>0).Select(x=>x.Trim('"')+"\\adb.exe"))
    .FirstOrDefault(File.Exists)??"";
  }

  public string Run() {
   package=TestPackage;
   report.AndroidPackage=package;
   report.GameVersion="desconocida";
   try {
    Begin("adb","Conexión por ADB",0);
    adb=String.IsNullOrWhiteSpace(options.Adb)?DetectAdb(options.Project):options.Adb;
    if(!File.Exists(adb))throw new IOException("No se encontró adb.exe. Elegilo en Configuración o instalá Android SDK Platform-Tools.");
    adb=Path.GetFullPath(adb);
    var devices=Require(Exec(adb,"devices","-l"));
    serial=SelectDevice(devices.Text,options.AndroidSerial);
    using(Program.LockTarget("android:"+serial)) {
     string model=Require(Device("shell","getprop","ro.product.model")).Text.Trim();
     string api=Require(Device("shell","getprop","ro.build.version.sdk")).Text.Trim();
     if(model.Length==0||!Regex.IsMatch(api,@"\A[0-9]+\z"))throw new IOException("ADB no devolvió el modelo o la versión de Android.");
     report.AndroidDevice=model+" · API "+api+" · "+serial;
     SaveLog(report.AndroidDevice);
     Result("Conexión por ADB","PASS",report.AndroidDevice);
     Install();
     if(!Launch())return "COMPLETED";
     string startup=Observe();
     if(startup!="COMPLETED")return startup;
     return RunSuite();
    }
   } catch(OperationCanceledException) {
    Result("Prueba Android detenida","CANCELLED","Se conservó la evidencia obtenida hasta la cancelación.");
    return "CANCELLED";
   } catch(Exception e) {
    Result("Paso Android incompleto","BLOCKED",Runner.Redact(e.Message));
    return "BLOCKED";
   } finally {
    add(new Result{Id="android:coverage",Name="Cobertura Android pendiente",Category="Cobertura pendiente",Status="SKIP",
     Detail="El inicio de sesión Epic, las partidas entre dispositivos, la voz, el aspecto visual y el rendimiento requieren pruebas específicas. Las capturas de QA quedan en el perfil aislado del dispositivo."});
   }
  }

  public static string SelectDevice(string output,string requested) {
   var devices=Regex.Matches(output,@"(?m)^([^\s]+)\s+(device|offline|unauthorized|recovery|sideload|bootloader|no permissions)(?=\s|$)")
    .Cast<Match>().Where(x=>SafeSerial.IsMatch(x.Groups[1].Value)).ToDictionary(x=>x.Groups[1].Value,x=>x.Groups[2].Value);
   if(!String.IsNullOrWhiteSpace(requested)) {
    string state;
    if(!devices.TryGetValue(requested,out state)||state!="device")
     throw new IOException("El dispositivo indicado no está autorizado o conectado. Consultá adb devices -l.");
    return requested;
   }
   var ready=devices.Where(x=>x.Value=="device").Select(x=>x.Key).ToArray();
   if(ready.Length==1)return ready[0];
   if(ready.Length>1)throw new IOException("Hay varios dispositivos disponibles. Elegí uno por su número de serie (adb devices -l).");
   throw new IOException("No hay dispositivos autorizados. Conectá un teléfono con depuración USB y aceptá la autorización, o iniciá un emulador.");
  }

  void Install() {
   Begin("apk","Paquete Android",1);
   if(String.IsNullOrWhiteSpace(options.Apk))throw new IOException("Elegí el APK jugable de WO con pruebas integradas en Configuración.");
   string apk=Path.GetFullPath(options.Apk);
   if(!File.Exists(apk)||!String.Equals(Path.GetExtension(apk),".apk",StringComparison.OrdinalIgnoreCase))
    throw new IOException("Elegí un archivo APK existente.");
   string aapt=FindAapt(adb);
   if(aapt==null)throw new IOException("Para instalar el APK, instalá Android SDK Build-Tools en el mismo SDK que adb.exe.");
   var badging=Require(Exec(aapt,"dump","badging",apk));
   string metadata=badging.Text.Split('\n').FirstOrDefault(x=>x.StartsWith("package:"))??"";
   SaveLog(metadata);
   var identity=Regex.Match(metadata,@"^package: name='([^']+)'");
   if(!identity.Success||identity.Groups[1].Value!=package)
    throw new IOException("Elegí la edición jugable de WO con pruebas integradas.");
   var archive=Require(Exec(aapt,"list",apk));
   if(!archive.Text.Split('\n').Any(x=>x.Trim()==CaseIndex))
    throw new IOException("El APK no incluye la revisión completa. Actualizá el archivo APK de WO Definitive.");
   report.Fingerprint=Scanner.Hash(apk);
   var install=Device("install","-r",apk);
   SaveLog(install.Text);
   Require(install);
   if(!Regex.IsMatch(install.Text,@"(?m)^Success\s*$"))
    throw new IOException("ADB no confirmó la instalación. Revisá android-apk.log.");
   var installed=Device("shell","pm","path",package);
   SaveLog(installed.Text);
   if(!installed.Cancelled&&!installed.Timeout&&installed.Exit==1&&String.IsNullOrWhiteSpace(installed.Text))
    throw new IOException("El paquete "+package+" no está instalado. Elegí en Configuración la edición que tiene el dispositivo o seleccioná su APK.");
   Require(installed);
   if(!Regex.IsMatch(installed.Text,@"(?m)^package:/.+\.apk\s*$"))
    throw new IOException("El paquete "+package+" no está instalado para el usuario activo.");
   var info=Require(Device("shell","dumpsys","package",package));
   var version=Regex.Match(info.Text,@"(?m)^\s*versionName=(\S[^\r\n]*)");
   if(version.Success)report.GameVersion=version.Groups[1].Value.Trim();
   Result("Paquete Android","PASS",package+" · versión "+report.GameVersion+
    (String.IsNullOrEmpty(report.Fingerprint)?"":"\nSHA-256 del APK: "+report.Fingerprint));
  }

  static string FindAapt(string adb) {
   var platformTools=Path.GetDirectoryName(Path.GetFullPath(adb));
   string buildTools=Path.Combine(Path.GetDirectoryName(platformTools)??platformTools,"build-tools");
   if(!Directory.Exists(buildTools))return null;
   return Directory.GetDirectories(buildTools).OrderByDescending(x=>{Version v;return Version.TryParse(Path.GetFileName(x),out v)?v:new Version(0,0);})
    .Select(x=>Path.Combine(x,"aapt.exe")).FirstOrDefault(File.Exists);
  }

  string marker, logSince, initialPid;
  bool Launch() {
   Begin("launch","Inicio de WO en Android",2);
   MarkLogStart();
   var launch=Device("shell","am","start","-W","-S","-a","android.intent.action.MAIN","-c","android.intent.category.LAUNCHER","-p",package);
   SaveLog(launch.Text);
   if(launch.Cancelled||launch.Timeout)Require(launch);
   if(Regex.IsMatch(launch.Text,@"(?im)^\s*Error(?:\s+type\s+\d+)?\s*:|unable to resolve Intent|^\s*Status:\s*(?:timeout|error)")) {
    Result("Inicio de WO en Android","FAIL","Android no pudo iniciar la actividad de WO. Revisá android-launch.log.");
    return false;
   }
   Require(launch);
   if(!Regex.IsMatch(launch.Text,@"(?m)^Status:\s*ok\s*$")||!Regex.IsMatch(launch.Text,@"(?m)^Activity:\s*"+Regex.Escape(package)+@"/"))
    throw new IOException("ADB no confirmó la actividad principal de WO.");
   initialPid=CurrentPid();
   Result("Inicio de WO en Android","PASS","Android inició la actividad principal de "+package+".");
   return true;
  }

  string Observe() {
   string name="Estabilidad durante el arranque";
   Begin("runtime",name,3);
   Wait(ObservationSeconds*1000);
   string pid=CurrentPid();
   var evidence=ReadEvidence(pid);
   var fault=evidence.Select(x=>LogLine.Match(x)).FirstOrDefault(IsFault);
   if(fault!=null) {
    Result(name,"FAIL",fault.Groups["message"].Value);
    return "FAILED";
   }
   if(pid.Length==0||(initialPid.Length>0&&pid!=initialPid)) {
    Result(name,"FAIL","El proceso de WO se cerró o reinició antes de completar la prueba.");
    return "FAILED";
   }
   Result(name,"PASS","WO siguió activo después de "+ObservationSeconds+" segundos sin errores de Godot, cierres ni ANR registrados.");
   Capture();
   return "COMPLETED";
  }

  void MarkLogStart() {
   logSince=LogcatStartTime(Require(Device("shell","date","+%m-%d_%H:%M:%S")).Text.Trim());
   marker="wo-"+Guid.NewGuid().ToString("N");
   Require(Device("shell","log","-t","WOTester",marker));
  }

  List<string> ReadEvidence(string pid) {
   var dump=Require(Device("logcat","-b","main","-b","system","-b","crash","-b","events","-d","-v","brief","-T",logSince,
    "WOTester:I","Godot:V","godot:V","GODOT:V","AndroidRuntime:E","am_proc_start:I","am_crash:I","am_anr:I","*:S"));
   var evidence=CollectLogs(dump.Text,marker,package,initialPid,pid);
   File.WriteAllText(log,Runner.Redact(String.Join(Environment.NewLine,evidence)),new UTF8Encoding(false));
   return evidence;
  }

  string RunSuite() {
   int position=5,total=0;
   var native=new List<Dictionary<string,string>>();
   string[] phases={"data","catalog","maps","weapons","soak","touch"};
   string[] names={"Datos del APK","Catálogo en Android","Mapas en Android","Armas en Android","Estabilidad prolongada en Android","Menús y controles táctiles"};
   for(int i=0;i<phases.Length;i++){
    string phase=phases[i];
    int limit=Math.Max(options.TimeoutSeconds,phase=="weapons"?600:phase=="maps"?300:120);
    var evidence=RunQa(phase,"wo-tester-user",names[i],position++,total,limit);
    bool complete=ReportQa(phase,names[i],evidence);
    if(phase=="data"){
     if(!complete)return "BLOCKED";
     foreach(string message in evidence.Where(x=>x.StartsWith("WO_TESTER_NATIVE "))){
      var entry=Product.Json().Deserialize<Dictionary<string,string>>(message.Substring("WO_TESTER_NATIVE ".Length));
      if(entry!=null&&entry.ContainsKey("id")&&Regex.IsMatch(entry["id"],@"\A[a-z0-9_]+_test\z"))native.Add(entry);
     }
     total=position+phases.Length-1+native.Count;
    }
   }
   foreach(var entry in native){
    string id=entry["id"];
    string title=Scanner.Friendly(id);
    string reason,guard;
    if(entry.TryGetValue("reason",out reason)&&!String.IsNullOrEmpty(reason)){
     Begin(id,title,position++,total);
     Result(title,"SKIP",reason);
     continue;
    }
    if(!entry.TryGetValue("guard",out guard))guard="wo-tester-user";
    var evidence=RunQa("native:"+id,guard,title,position++,total,options.TimeoutSeconds);
    ReportNative(id,title,evidence);
   }
   return report.Results.Any(x=>x.Status=="BLOCKED")?"BLOCKED":"COMPLETED";
  }

  List<string> RunQa(string phase,string guard,string title,int index,int total,int timeout) {
   Begin(phase.Replace(':','-'),title,index,total);
   MarkLogStart();
   initialPid="";
   string requests="files/wo-tester/requests";
   string request=requests+"/"+marker;
   Require(Device("shell","run-as",package,"mkdir","-p",requests));
   Require(Device("shell","run-as",package,"touch",request));
   bool stop=false;
   try{
    var launch=Require(Device("shell","am","start","-S","-a","android.intent.action.MAIN","-c","android.intent.category.LAUNCHER","-p",package,
     "--es","wo_tester_token",marker,"--es","wo_tester_phase",phase,"--es","wo_tester_guard",guard,
     "--es","wo_tester_seed",options.Seed.ToString(),"--es","wo_tester_speed",options.Speed.ToString()));
    SaveLog(launch.Text);
    if(Regex.IsMatch(launch.Text,@"(?im)^\s*Error|unable to resolve Intent"))throw new IOException(launch.Text.Trim());
    initialPid=CurrentPid();
    var watch=Stopwatch.StartNew();
    var evidence=new List<string>();
    int waitLimit=phase=="data"?Math.Min(timeout,30):timeout;
    while(watch.Elapsed.TotalSeconds<waitLimit){
     Wait(1500);
     string pid=CurrentPid();
     if(initialPid.Length==0)initialPid=pid;
     evidence=ReadEvidence(pid).Select(x=>LogLine.Match(x)).Where(x=>x.Success)
      .Select(x=>IsFault(x)?"ERROR: "+x.Groups["message"].Value:x.Groups["message"].Value).ToList();
     if(evidence.Any(x=>x.StartsWith("WO_TESTER_DONE ")||QaSummary.IsMatch(x)))return evidence;
     if(evidence.Any(x=>x.Contains("SCRIPT ERROR:")||x.StartsWith("ERROR: FATAL EXCEPTION"))){
      stop=true;
      return evidence;
     }
     if(pid.Length==0&&watch.Elapsed.TotalSeconds>=5)return evidence;
    }
    stop=true;
    evidence.Add("WO_TESTER_TIMEOUT");
    return evidence;
   } finally {
    try {
     if(stop||token.IsCancellationRequested)RunCommand(adb,10,CancellationToken.None,"-s",serial,"shell","am","force-stop",package);
     RunCommand(adb,10,CancellationToken.None,"-s",serial,"shell","run-as",package,"rm","-f",request);
    } catch(IOException) {}
    catch(System.ComponentModel.Win32Exception) {}
   }
  }

  bool ReportQa(string phase,string title,List<string> evidence) {
   int declared=0;
   foreach(string message in evidence.Where(x=>x.StartsWith("WO_TESTER_EVENT "))){
    var result=Product.Json().Deserialize<Result>(message.Substring("WO_TESTER_EVENT ".Length));
    if(result==null||String.IsNullOrWhiteSpace(result.Id))continue;
    result.Id="android:"+result.Id;
    result.Log=Path.GetFileName(log);
    result.Reproduce="Revisión Android · "+title;
    add(result);
    declared++;
   }
   string summary=evidence.LastOrDefault(x=>x.StartsWith("WO_TESTER_DONE ")||QaSummary.IsMatch(x));
   if(summary==null){
    bool failed=Runner.Classify(new ProcessOutcome{Text=String.Join("\n",evidence)})=="FAIL";
    Result(title,failed?"FAIL":"BLOCKED",failed?"El escenario Android registró un error. Revisá el registro.":"El APK no devolvió el resultado de "+phase+". Revisá el registro.");
    return false;
   }
   var touch=QaSummary.Match(summary);
   if(touch.Success){
    Result(title,touch.Groups[2].Value=="0"&&touch.Groups[1].Value!="0"?"PASS":"FAIL",
     "La escena QA informó "+touch.Groups[1].Value+" capturas y "+touch.Groups[2].Value+" fallos.");
   } else {
    var done=Product.Json().Deserialize<Dictionary<string,object>>(summary.Substring("WO_TESTER_DONE ".Length));
    if(done==null||!done.ContainsKey("phase")||Convert.ToString(done["phase"])!=phase||
     !done.ContainsKey("results")||Convert.ToInt32(done["results"])!=declared||declared==0){
     Result(title,"BLOCKED","Logcat devolvió un resumen incompleto. Revisá el registro.");
     return false;
    }
    if(Runner.Classify(new ProcessOutcome{Text=String.Join("\n",evidence)})=="FAIL")
     Result(title,"FAIL","Se registró un error de ejecución además de las comprobaciones declaradas.");
   }
   return true;
  }

  void ReportNative(string id,string title,List<string> evidence) {
   string text=String.Join("\n",evidence);
   string status=Runner.Classify(new ProcessOutcome{Text=text});
   if(evidence.Contains("WO_TESTER_TIMEOUT"))status=status=="FAIL"?"FAIL":"BLOCKED";
   if(!evidence.Contains("WO_TESTER_NATIVE_START "+id))status=status=="FAIL"?"FAIL":"BLOCKED";
   else if(status=="PASS"&&!Regex.IsMatch(text,@"(?i)\b0 failures\b|\bPASS\b"))status="REVIEW";
   var summaries=evidence.Where(x=>Regex.IsMatch(x,@"(?i)\bchecks\b|\bPASS\b|\bfailures\b|ERROR:")).Take(10);
   string detail=String.Join("\n",summaries);
   int checks=0;
   foreach(Match match in Regex.Matches(text,@"\b(\d+) checks\b")){
    int count;
    if(Int32.TryParse(match.Groups[1].Value,out count))checks=Math.Max(checks,count);
   }
   Result(title,status,detail.Length>0?detail:"La escena terminó o se detuvo sin un resumen compatible. Revisá el registro.",checks:checks);
  }

  string CurrentPid() {
   var p=Device("shell","pidof",package);
   if(p.Exit==1&&String.IsNullOrWhiteSpace(p.Text)&&!p.Timeout&&!p.Cancelled)return "";
   return Require(p).Text.Trim();
  }

  public static string LogcatStartTime(string deviceTime) {
   if(!Regex.IsMatch(deviceTime,@"\A[0-9]{2}-[0-9]{2}_[0-9]{2}:[0-9]{2}:[0-9]{2}\z"))
    throw new IOException("ADB no devolvió la hora del dispositivo en el formato que requiere Logcat.");
   return deviceTime.Replace('_',' ')+".000";
  }

  public static List<string> CollectLogs(string output,string marker,string package,params string[] knownPids) {
   var lines=output.Split('\n').Select(x=>x.TrimEnd('\r')).ToList();
   int start=lines.FindIndex(x=>{var m=LogLine.Match(x);return m.Success&&m.Groups["tag"].Value=="WOTester"&&m.Groups["message"].Value==marker;});
   if(start<0)throw new IOException("Logcat no conservó el marcador de inicio. Repetí la prueba para obtener evidencia de esta ejecución.");
   var session=lines.Skip(start+1).Select(x=>LogLine.Match(x)).Where(x=>x.Success).ToList();
   var pids=new HashSet<string>(knownPids.SelectMany(x=>x.Split(' ')).Where(x=>x.Length>0));
   var processStart=new Regex(@"^\[\d+,\s*(\d+),\s*\d+,\s*"+Regex.Escape(package)+@",");
   var processEvent=new Regex(@"^\[\d+,\s*\d+,\s*"+Regex.Escape(package)+@",");
   foreach(var line in session.Where(x=>x.Groups["tag"].Value=="am_proc_start")) {
    var process=processStart.Match(line.Groups["message"].Value);
    if(process.Success)pids.Add(process.Groups[1].Value);
   }
   return session.Where(x=>{
    string tag=x.Groups["tag"].Value;
    if(tag=="am_anr"||tag=="am_crash")return processEvent.IsMatch(x.Groups["message"].Value);
    return (tag=="AndroidRuntime"||String.Equals(tag,"godot",StringComparison.OrdinalIgnoreCase))&&pids.Contains(x.Groups["pid"].Value);
   }).Select(x=>x.Value).ToList();
  }

  static bool IsFault(Match line) {
   string tag=line.Groups["tag"].Value;
   string message=line.Groups["message"].Value;
   if(tag=="am_anr"||tag=="am_crash")return true;
   if(tag=="AndroidRuntime")return message.StartsWith("FATAL EXCEPTION");
   return message.StartsWith("SCRIPT ERROR:")||message.StartsWith("ERROR:")||message.StartsWith("USER ERROR:")||
    message.StartsWith("ANDROID_QA:")||line.Groups["level"].Value=="F";
  }

  void Capture() {
   Begin("screen","Captura de arranque",4);
   string remote="/data/local/tmp/wo-tester-"+Guid.NewGuid().ToString("N")+".png";
   string local=Path.Combine(report.Folder,"android-startup.png");
   bool captureStarted=false;
   try {
    var window=Require(Device("shell","dumpsys","window"));
    if(!Regex.IsMatch(window.Text,@"(?m)^\s*mCurrentFocus=Window\{[^\r\n]*\s"+Regex.Escape(package)+@"/"))
     throw new IOException("La ventana de WO no está en primer plano para la captura.");
    captureStarted=true;
    Require(Device("shell","screencap","-p",remote));
    Require(Device("pull",remote,local));
    var png=new byte[]{137,80,78,71,13,10,26,10};
    using(var file=File.OpenRead(local)) {
     var header=new byte[png.Length];
     if(file.Length<=24||file.Read(header,0,header.Length)!=header.Length||!header.SequenceEqual(png))
      throw new IOException("ADB no devolvió una captura PNG válida.");
    }
    Result("Captura de arranque","REVIEW","Se guardó la pantalla para inspección visual.",Path.GetFileName(local));
   } catch(OperationCanceledException) { throw; }
   catch(Exception e) { Result("Captura de arranque","REVIEW",Runner.Redact(e.Message)); }
   finally {
    if(captureStarted) {
     try { RunCommand(adb,10,CancellationToken.None,"-s",serial,"shell","rm","-f",remote); }
     catch(IOException) {}
     catch(System.ComponentModel.Win32Exception) {}
    }
   }
  }

  void Wait(int milliseconds) {
   if(token.WaitHandle.WaitOne(milliseconds))token.ThrowIfCancellationRequested();
  }
  static ProcessOutcome Require(ProcessOutcome result) {
   if(result.Cancelled)throw new OperationCanceledException();
   if(result.Timeout)throw new IOException("Se agotó el tiempo máximo del comando Android.");
   if(result.Exit!=0)throw new IOException(String.IsNullOrWhiteSpace(result.Text)?"El comando Android terminó con código "+result.Exit+".":result.Text.Trim());
   return result;
  }
  ProcessOutcome Device(params string[] args) {
   var command=new[]{"-s",serial}.Concat(args).ToArray();
   var result=Exec(adb,command);
   if(result.Exit==0||result.Cancelled||!DeviceDisconnected.IsMatch(result.Text))return result;
   var watch=Stopwatch.StartNew();
   while(watch.Elapsed.TotalSeconds<20){
    Wait(1500);
    var devices=Exec(adb,"devices","-l");
    try {
     if(SelectDevice(Require(devices).Text,serial)==serial)return Exec(adb,command);
    } catch(IOException) {}
   }
   return result;
  }
  ProcessOutcome Exec(string exe,params string[] args) { return RunCommand(exe,options.TimeoutSeconds,token,args); }
  ProcessOutcome RunCommand(string exe,int timeout,CancellationToken cancellation,params string[] args) {
   return Runner.Execute(exe,String.Join(" ",args.Select(Runner.Quote)),report.Folder,null,timeout,cancellation,false);
  }
  void Begin(string id,string name,int index,int total=5) {
   step=id;
   log=Path.Combine(report.Folder,"android-"+id+".log");
   progress(name,index,total);
  }
  void SaveLog(string text) { File.AppendAllText(log,text+Environment.NewLine,new UTF8Encoding(false)); }
  void Result(string name,string status,string detail,string screenshot=null,int checks=0) {
   add(new Result{Id="android:"+step,Name=name,Category="Android",Status=status,Detail=detail,
    Checks=checks>0?checks:status=="PASS"||status=="FAIL"?1:0,Log=File.Exists(log)?Path.GetFileName(log):null,
    Screenshot=screenshot,Reproduce="Revisión Android · "+package+(serial==null?"":" · "+serial)});
  }
 }
}
