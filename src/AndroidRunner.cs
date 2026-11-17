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
  public const string GamePackage="com.frrentertainment.wodefinitive";
  public const string TestPackage=GamePackage+".test";
  public const string QaPackage=GamePackage+".qa";
  const int ObservationSeconds=10;
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
   package=String.IsNullOrWhiteSpace(options.AndroidPackage)?GamePackage:options.AndroidPackage;
   report.AndroidPackage=package;
   report.GameVersion="desconocida";
   try {
    Begin("adb","Conexión por ADB",0);
    if(package!=GamePackage&&package!=TestPackage&&package!=QaPackage)
     throw new ArgumentException("Elegí el paquete del juego, de pruebas o de QA de WO.");
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
     return Observe();
    }
   } catch(OperationCanceledException) {
    Result("Prueba Android detenida","CANCELLED","Se conservó la evidencia obtenida hasta la cancelación.");
    return "CANCELLED";
   } catch(Exception e) {
    Result("Paso Android incompleto","BLOCKED",Runner.Redact(e.Message));
    return "BLOCKED";
   } finally {
    add(new Result{Id="android:coverage",Name="Cobertura Android pendiente",Category="Cobertura pendiente",Status="SKIP",
     Detail=(package==QaPackage?"Las capturas de la escena QA requieren inspección visual. ":"Los controles táctiles y mapas se prueban con el APK interno de QA. ")+
      "El inicio de sesión Epic, las partidas entre dispositivos, la voz, el aspecto visual y el rendimiento requieren pruebas en los equipos de destino."});
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
   if(!String.IsNullOrWhiteSpace(options.Apk)) {
    string apk=Path.GetFullPath(options.Apk);
    if(!File.Exists(apk)||!String.Equals(Path.GetExtension(apk),".apk",StringComparison.OrdinalIgnoreCase))
     throw new IOException("Elegí un archivo APK existente.");
    string aapt=FindAapt(adb);
    if(aapt==null)throw new IOException("Para instalar un APK, instalá Android SDK Build-Tools en el mismo SDK que adb.exe.");
    var badging=Require(Exec(aapt,"dump","badging",apk));
    string metadata=badging.Text.Split('\n').FirstOrDefault(x=>x.StartsWith("package:"))??"";
    SaveLog(metadata);
    var identity=Regex.Match(metadata,@"^package: name='([^']+)'");
    if(!identity.Success||identity.Groups[1].Value!=package)
     throw new IOException("El APK corresponde a "+(identity.Success?identity.Groups[1].Value:"un paquete desconocido")+". Elegí su paquete antes de instalarlo.");
    report.Fingerprint=Scanner.Hash(apk);
    var install=Device("install","-r",apk);
    SaveLog(install.Text);
    Require(install);
    if(!Regex.IsMatch(install.Text,@"(?m)^Success\s*$"))
     throw new IOException("ADB no confirmó la instalación. Revisá android-apk.log.");
   }
   var installed=Device("shell","pm","path",package);
   SaveLog(installed.Text);
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

  string marker, initialPid;
  bool Launch() {
   Begin("launch","Inicio de WO en Android",2);
   marker="wo-"+Guid.NewGuid().ToString("N");
   Require(Device("shell","log","-t","WOTester",marker));
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
   bool qa=package==QaPackage;
   string name=qa?"QA de menús, mapas y controles táctiles":"Estabilidad durante el arranque";
   Begin("runtime",name,3);
   int seconds=qa?options.TimeoutSeconds:ObservationSeconds;
   var watch=Stopwatch.StartNew();
   string status,detail;
   while(true) {
    if(!qa&&watch.Elapsed.TotalSeconds<seconds)Wait(250);
    else {
     string pid=CurrentPid();
     var dump=Require(Device("logcat","-b","main","-b","system","-b","crash","-b","events","-d","-v","brief","-t","4000",
      "WOTester:I","Godot:V","godot:V","AndroidRuntime:E","am_proc_start:I","am_crash:I","am_anr:I","*:S"));
     var evidence=CollectLogs(dump.Text,marker,package,initialPid,pid);
     File.WriteAllText(log,Runner.Redact(String.Join(Environment.NewLine,evidence)),new UTF8Encoding(false));
     var fault=evidence.Select(x=>LogLine.Match(x)).FirstOrDefault(IsFault);
     var summary=evidence.Select(x=>QaSummary.Match(x)).LastOrDefault(x=>x.Success);
     if(fault!=null) { status="FAIL";detail=fault.Groups["message"].Value;break; }
     if(qa&&summary!=null) {
      status=summary.Groups[2].Value=="0"&&summary.Groups[1].Value!="0"?"PASS":"FAIL";
      detail="La escena QA informó "+summary.Groups[1].Value+" capturas y "+summary.Groups[2].Value+" fallos.";
      break;
     }
     if(pid.Length==0||(!qa&&initialPid.Length>0&&pid!=initialPid)) {
      status="FAIL";detail="El proceso de WO se cerró o reinició antes de completar la prueba.";break;
     }
     if(watch.Elapsed.TotalSeconds>=seconds) {
      status=qa?"BLOCKED":"PASS";
      detail=qa?"Se agotó el tiempo sin recibir ANDROID_QA_DONE.":"WO siguió activo después de "+ObservationSeconds+" segundos sin errores de Godot, cierres ni ANR registrados.";
      break;
     }
     Wait(2000);
    }
   }
   Result(name,status,Runner.Redact(detail));
   if(!qa&&status=="PASS")Capture();
   return status=="BLOCKED"?"BLOCKED":"COMPLETED";
  }

  string CurrentPid() {
   var p=Device("shell","pidof",package);
   if(p.Exit==1&&String.IsNullOrWhiteSpace(p.Text)&&!p.Timeout&&!p.Cancelled)return "";
   return Require(p).Text.Trim();
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
  ProcessOutcome Device(params string[] args) { return Exec(adb,new[]{"-s",serial}.Concat(args).ToArray()); }
  ProcessOutcome Exec(string exe,params string[] args) { return RunCommand(exe,options.TimeoutSeconds,token,args); }
  ProcessOutcome RunCommand(string exe,int timeout,CancellationToken cancellation,params string[] args) {
   return Runner.Execute(exe,String.Join(" ",args.Select(Runner.Quote)),report.Folder,null,timeout,cancellation,false);
  }
  void Begin(string id,string name,int index) {
   step=id;
   log=Path.Combine(report.Folder,"android-"+id+".log");
   progress(name,index,5);
  }
  void SaveLog(string text) { File.AppendAllText(log,text+Environment.NewLine,new UTF8Encoding(false)); }
  void Result(string name,string status,string detail,string screenshot=null) {
   add(new Result{Id="android:"+step,Name=name,Category="Android",Status=status,Detail=detail,
    Checks=status=="PASS"||status=="FAIL"?1:0,Log=File.Exists(log)?Path.GetFileName(log):null,
    Screenshot=screenshot,Reproduce="Revisión Android · "+package+(serial==null?"":" · "+serial)});
  }
 }
}
