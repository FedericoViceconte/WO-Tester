using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace WOTester {
 public sealed class ProcessOutcome { public int Exit; public bool Timeout; public bool Cancelled; public string Text; public double Seconds; }
 public sealed class Runner {
  public Action<Result> OnResult; public Action<string,int,int> OnProgress;
  CancellationToken token; Report report; Options options; Inventory inventory;
  static readonly string[] Quick = {"startup_shutdown_test","terrain_integrity_test","practice_ammo_test","menu_bot_launch_test","network_payload_test","speed_mode_test","battle_controls_test","settings_rewrite_test"};
  public static string Quote(string value) {
   if(value==null)return "\"\"";
   var b=new StringBuilder("\"");int slashes=0;
   foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='"'){b.Append('\\',slashes*2+1);b.Append(c);slashes=0;}else{b.Append('\\',slashes);slashes=0;b.Append(c);}}
   b.Append('\\',slashes*2);b.Append('"');return b.ToString();
  }
  public static ProcessOutcome Execute(string exe,string args,string cwd,string profile,int timeout,CancellationToken token) {
   var watch=Stopwatch.StartNew();var output=new StringBuilder();var gate=new object();
   var start=new ProcessStartInfo(exe,args){WorkingDirectory=cwd,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
   if(!String.IsNullOrEmpty(profile)){
    Directory.CreateDirectory(profile);
    start.EnvironmentVariables["APPDATA"]=profile;start.EnvironmentVariables["LOCALAPPDATA"]=profile;
    start.EnvironmentVariables["XDG_DATA_HOME"]=profile;start.EnvironmentVariables["XDG_CONFIG_HOME"]=profile;
    start.EnvironmentVariables["WO_TESTER_ISOLATED"]="1";
   }
   using(var process=new Process {StartInfo=start}){
    DataReceivedEventHandler handler=(s,e)=>{if(e.Data!=null)lock(gate){if(output.Length<12*1024*1024)output.AppendLine(e.Data);}};
    process.OutputDataReceived+=handler;process.ErrorDataReceived+=handler;
    process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
    bool timed=false,cancelled=false;
    while(!process.WaitForExit(100)){
     if(token.IsCancellationRequested || watch.Elapsed.TotalSeconds>timeout){
      cancelled=token.IsCancellationRequested;timed=!cancelled;
      try{process.Kill();}catch{}
      break;
     }
    }
    if(!process.WaitForExit(5000))throw new IOException("No se pudo cerrar el proceso de prueba.");
    process.WaitForExit();
    return new ProcessOutcome{Exit=process.ExitCode,Text=Redact(output.ToString()),Timeout=timed,Cancelled=cancelled,Seconds=watch.Elapsed.TotalSeconds};
   }
  }
  public static string Redact(string text) {
   text=Regex.Replace(text,@"(?i)((?:access_token|refresh_token|id_token|authorization|client_secret|password)\s*[:=]\s*[""']?)[^\s,""'}]+","$1[REDACTADO]");
   text=Regex.Replace(text,@"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b","[TOKEN REDACTADO]");
   return text;
  }
  public static string Classify(ProcessOutcome p) {
   if(p.Cancelled)return "CANCELLED";
   if(p.Timeout)return "BLOCKED";
   if(p.Exit==2)return "BLOCKED";
   if(p.Exit!=0)return "FAIL";
   string text=String.Join("\n",p.Text.Split('\n').Where(x=>!x.StartsWith("WO_TESTER_")));
   bool leak=Regex.IsMatch(text,@"(?im)^\s*ERROR: \d+ resources? still in use at exit|ObjectDB instances were leaked");
   text=Regex.Replace(text,@"(?im)^\s*ERROR: \d+ resources? still in use at exit.*$","");
   if(Regex.IsMatch(text,@"(?im)^\s*(?:SCRIPT ERROR|ERROR:|USER ERROR:)|(?:^|[\s\[])(?:FAIL|FAILED)(?:[\s\]]|$)|\b[1-9]\d* failures\b"))return "FAIL";
   foreach(string line in text.Split('\n')){
    int brace=line.IndexOf('{');if(brace<0)continue;
    try{var obj=Product.Json().DeserializeObject(line.Substring(brace)) as Dictionary<string,object>;if(obj==null)continue;
     foreach(string field in new[]{"failed","failures"}){object value;if(!obj.TryGetValue(field,out value))continue;var array=value as object[];if(array!=null&&array.Length>0)return "FAIL";if(value is int && (int)value>0)return "FAIL";}
    }catch{}
   }
   return leak?"REVIEW":"PASS";
  }
  public Report Run(Options selected,CancellationToken cancellation) {
   options=selected;token=cancellation;
   string project=Scanner.NormalizeProject(options.Project);
   if(!File.Exists(options.Godot))throw new ArgumentException("Seleccioná el ejecutable de Godot 4 que corresponde al juego.");
   inventory=Scanner.Scan(project);
   string id=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6);
   string folder=Path.Combine(Product.Home,"runs",id);Directory.CreateDirectory(folder);
   report=new Report{Id=id,StartedUtc=DateTime.UtcNow.ToString("o"),Project=project,GameVersion=inventory.Version,Fingerprint=inventory.Fingerprint,Commit=inventory.Commit,Dirty=inventory.Dirty,Mode=options.Mode,Speed=options.Speed,Seed=options.Seed,State="RUNNING",Content=inventory.Content,Folder=folder};
   Report previous=Reports.Latest(project);
   if(previous!=null) {
    foreach(var item in inventory.Content)if(!previous.Content.ContainsKey(item.Key))report.Changes.Add("NUEVO "+item.Key);else if(previous.Content[item.Key]!=item.Value)report.Changes.Add("CAMBIÓ "+item.Key);
    foreach(var item in previous.Content)if(!inventory.Content.ContainsKey(item.Key))report.Changes.Add("ELIMINADO "+item.Key);
   } else report.Changes.Add("Primera revisión: se creó la referencia de contenido.");
   var clock=Stopwatch.StartNew();
   Product.Write(Path.Combine(folder,"report.json"),report);
   try {
    var version=Execute(options.Godot,"--version",project,null,10,token);
    report.GodotVersion=version.Text.Trim();
    if(version.Exit!=0 || !report.GodotVersion.StartsWith("4."))throw new ArgumentException("WO Tester necesita Godot 4. Resultado: "+report.GodotVersion);
    var feature=Regex.Match(File.ReadAllText(Path.Combine(project,"project.godot")),@"config/features=PackedStringArray\(""([0-9]+\.[0-9]+)");
    bool matches=!feature.Success || report.GodotVersion.StartsWith(feature.Groups[1].Value+".");
    Add(new Result{Id="environment:godot",Name="Compatibilidad de Godot",Category="Entorno",Status=matches?"PASS":"BLOCKED",Checks=1,Detail="Proyecto "+(feature.Success?feature.Groups[1].Value:"Godot 4")+" · motor "+report.GodotVersion,Suggestion=matches?"":"Seleccioná la misma versión de Godot indicada por el proyecto."});
    if(!matches) { report.State="BLOCKED";return Finish(clock); }
    string cache=Path.Combine(project,".godot","global_script_class_cache.cfg");
    if(!File.Exists(cache)) {
     Add(new Result{Id="environment:import",Name="Recursos importados",Category="Entorno",Status="BLOCKED",Detail="El proyecto todavía no tiene su caché de clases de Godot.",Suggestion="Abrí el proyecto con su versión de Godot y esperá a que termine la importación. Después repetí la revisión."});
     report.State="BLOCKED";return Finish(clock);
    }
    var native=inventory.Tests.Where(t=>options.Mode!="quick" || Quick.Contains(Path.GetFileNameWithoutExtension(t.Scene))).ToList();
    if(options.Mode=="failed" && previous!=null)native=native.Where(t=>previous.Results.Any(r=>r.Id==t.Id && r.Status!="PASS")).ToList();
    int total=inventory.JsonFiles.Count+native.Count+4;int done=0;
    foreach(var file in inventory.JsonFiles) {
     if(token.IsCancellationRequested)break;
     Progress("Validando "+Path.GetFileName(file),done++,total);
     Add(ValidateJson(project,file));
    }
    foreach(string diagnostic in inventory.Diagnostics)Add(new Result{Id="inventory:"+report.Results.Count,Name="Catálogo incompleto",Category="Catálogo",Status="FAIL",Detail=diagnostic,Suggestion="Revisá el archivo de datos indicado."});
    var phases=options.Mode=="quick"?new[]{"catalog","maps"}:new[]{"catalog","maps","weapons","soak"};
    foreach(var phase in phases) {
     if(token.IsCancellationRequested)break;
     Progress(phase=="catalog"?"Resolviendo el catálogo del juego":phase=="maps"?"Probando todos los mapas":phase=="weapons"?"Disparando las armas habilitadas":"Ejecutando una partida prolongada",done++,total);
     RunProbe(phase);
     Checkpoint();
    }
    foreach(var test in native) {
     if(token.IsCancellationRequested)break;
     Progress(test.Name,done++,total);RunNative(test);Checkpoint();
    }
    Add(new Result{Id="coverage:online",Name="Online real, nube y EAC",Category="Cobertura pendiente",Status="SKIP",Detail="Las pruebas locales de red usan escenarios controlados. Esta ejecución no certifica una partida pública, guardado en nube, voz entre equipos ni EAC.",Suggestion="Validar con cuentas de prueba, varios equipos y el lanzador oficial protegido."});
    Add(new Result{Id="coverage:visual",Name="Inspección visual y hardware real",Category="Cobertura pendiente",Status=options.Visible?"REVIEW":"SKIP",Detail=options.Visible?"Se generaron capturas de mapas para revisión humana; no se compararon contra imágenes aprobadas.":"La ejecución sin gráficos no certifica presentación, audio ni rendimiento gráfico.",Suggestion="Activá mapas visibles para capturas. Revisá interfaz, audio y rendimiento en los dispositivos de destino."});
    Add(new Result{Id="coverage:rules",Name="Cobertura de mecánicas nuevas",Category="Cobertura pendiente",Status="REVIEW",Detail="Los mapas y armas nuevos reciben pruebas comunes. Una mecánica nueva necesita reglas específicas. No se recorren todas las combinaciones ni se garantiza ausencia de bugs.",Suggestion="Agregar una escena *_test.tscn en tests/ del juego para cada comportamiento nuevo; se descubrirá automáticamente."});
    report.State=token.IsCancellationRequested?"CANCELLED":"COMPLETED";
   } catch(Exception e) {
    Add(new Result{Id="environment:exception",Name="No se pudo completar la revisión",Category="Entorno",Status="BLOCKED",Detail=e.Message,Suggestion="Revisá las rutas y el entorno. Los resultados anteriores se conservaron."});
    report.State=token.IsCancellationRequested?"CANCELLED":"BLOCKED";
   }
   return Finish(clock);
  }
  void Progress(string text,int done,int total){if(OnProgress!=null)OnProgress(text,done,total);}
  void Add(Result r) { if(r.Status=="FAIL" && String.IsNullOrEmpty(r.Severity))r.Severity="media"; report.Results.Add(r);if(OnResult!=null)OnResult(r); }
  void Checkpoint(){Product.Write(Path.Combine(report.Folder,"report.json"),report);}
  Report Finish(Stopwatch clock) {
   report.Seconds=clock.Elapsed.TotalSeconds;report.FinishedUtc=DateTime.UtcNow.ToString("o");
   try {
    var current=Scanner.Scan(options.Project);
    if(current.Fingerprint!=report.Fingerprint)Add(new Result{Id="environment:changed",Name="El juego cambió durante las pruebas",Category="Entorno",Status="REVIEW",Detail="La huella del código es distinta a la inicial. El informe contiene resultados de contenido que pudo cambiar.",Suggestion="Cerrá los cambios y repetí la revisión sobre una versión estable."});
   } catch {}
   Reports.Save(report);return report;
  }
  public static Result ValidateJson(string project,string file) {
   string rel=Scanner.Relative(project,file);
   var r=new Result{Id="data:"+rel,Name=Path.GetFileName(file),Category="Datos",Status="PASS",Checks=1,Reproduce="Validar "+rel+" con WO Tester."};
   try{
    object data=Product.Json().DeserializeObject(File.ReadAllText(file,Encoding.UTF8));
    var missing=new List<string>();
    foreach(var reference in Scanner.Resources(data)) {
     string path=Path.GetFullPath(Path.Combine(project,reference.Substring(6).Replace('/','\\')));
     if(!Scanner.Inside(project,path)){missing.Add(reference+" (fuera del proyecto)");continue;}
     if(!File.Exists(path) && !Directory.Exists(path))missing.Add(reference);
     r.Checks++;
    }
    var types=new HashSet<string>();foreach(var row in Scanner.Rows(data)){object type;if(row.TryGetValue("type",out type) && !types.Add(Convert.ToString(type)))missing.Add("Identificador repetido: "+type);}
    r.Status=missing.Count>0?"FAIL":"PASS";
    r.Detail=missing.Count>0?String.Join("\n",missing.Take(40)):"JSON válido; referencias locales existentes. "+r.Checks+" comprobaciones.";
    r.Suggestion=missing.Count>0?"Corregí las referencias o identificadores indicados. No se modificó el archivo.":"";
   }catch(Exception e){r.Status="FAIL";r.Detail=e.Message;r.Suggestion="Corregí la sintaxis JSON y repetí la validación.";}
   return r;
  }
  string ProfileFor(string id,string guard){return Path.Combine(report.Folder,"profiles",id,guard.Replace('/','\\'));}
  string ProbeFile() {
   string path=Path.Combine(report.Folder,"adapter.gd");
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("WOTester.probe.gd"))using(var output=File.Create(path))stream.CopyTo(output);
   return path;
  }
  void RunProbe(string phase) {
   string log=Path.Combine(report.Folder,"probe-"+phase+".log");
   string args="--path "+Quote(inventory.Project)+" --script "+Quote(ProbeFile())+" --audio-driver Dummy ";
   if(!options.Visible || phase!="maps")args+="--headless ";else args+="--resolution 1280x720 --position 40,40 ";
   args+="-- --wo-phase="+phase+" --wo-speed="+options.Speed+" --wo-seed="+options.Seed+" --wo-output="+Quote(report.Folder);
   if(options.Visible && phase=="maps")args+=" --wo-visible";
   var p=Execute(options.Godot,args,inventory.Project,ProfileFor("probe-"+phase,".godot/wo-tester-user"),Math.Max(options.TimeoutSeconds,phase=="weapons"?600:phase=="maps"?300:120),token);
   File.WriteAllText(log,p.Text,new UTF8Encoding(false));
   int count=0;
   foreach(string line in p.Text.Split('\n'))if(line.StartsWith("WO_TESTER_EVENT ")) {
    try{var r=Product.Json().Deserialize<Result>(line.Substring(16));r.Log=Path.GetFileName(log);r.Reproduce="Modo "+options.Mode+" · semilla "+options.Seed+" · fase "+phase+" · "+r.Id;r.Seconds=0;Add(r);count++;}catch{}
   }
   bool complete=p.Text.Contains("WO_TESTER_DONE ");
   if(Classify(p)!="PASS" || !complete || count==0) {
    Add(new Result{Id="probe:"+phase,Name="Motor de prueba · "+phase,Category="Entorno",Status=p.Cancelled?"CANCELLED":p.Timeout?"BLOCKED":!complete?"BLOCKED":Classify(p),Severity="alta",Seconds=p.Seconds,Log=Path.GetFileName(log),Detail=Summary(p,!complete?"El adaptador no completó su protocolo.":""),Suggestion="Revisá el registro. Si el juego cambió su estructura, actualizá el adaptador.",Reproduce="Fase "+phase+", semilla "+options.Seed});
   }
  }
  void RunNative(TestCase test) {
   string log=Path.Combine(report.Folder,Path.GetFileNameWithoutExtension(test.Scene)+".log");
   string args=(test.NeedsRender?"--position 40,40 ":"--headless ")+"--resolution 1280x720 --path "+Quote(inventory.Project)+" "+Quote("res://"+test.Scene)+" --audio-driver Dummy --log-file "+Quote(log);
   var p=Execute(options.Godot,args,inventory.Project,ProfileFor(Path.GetFileNameWithoutExtension(test.Scene),test.Guard),options.TimeoutSeconds,token);
   File.WriteAllText(log,p.Text,new UTF8Encoding(false));
   string status=Classify(p);
   int checks=0;foreach(Match match in Regex.Matches(p.Text,@"\b(\d+) checks\b")){int n;if(Int32.TryParse(match.Groups[1].Value,out n))checks=Math.Max(checks,n);}
   foreach(string line in p.Text.Split('\n')){int brace=line.IndexOf('{');if(brace<0)continue;try{var obj=Product.Json().DeserializeObject(line.Substring(brace)) as Dictionary<string,object>;if(obj!=null&&obj.ContainsKey("checks"))checks=Math.Max(checks,Convert.ToInt32(obj["checks"]));}catch{}}
   if(status=="PASS" && checks==0 && !Regex.IsMatch(p.Text,@"(?i)\bPASS\b|\b0 failures\b"))status="REVIEW";
   Add(new Result{Id=test.Id,Name=test.Name,Category=test.Category,Status=status,Severity=status=="FAIL"?"alta":"",Seconds=p.Seconds,Checks=checks,Log=Path.GetFileName(log),Detail=Summary(p,status=="REVIEW"?"La ejecución requiere revisión: no hay resumen verificable o el motor avisó de recursos pendientes al cierre.":""),Suggestion=status=="PASS"?"":status=="BLOCKED"?"Revisá los requisitos de la escena y su registro; no se contabiliza como prueba aprobada.":"Revisá los primeros errores del registro y repetí esta escena.",Reproduce="Godot "+report.GodotVersion+" · "+test.Scene+" · perfil aislado "+test.Guard+" · velocidad nativa 1×"+(test.NeedsRender?" · con gráficos":" · sin gráficos")});
  }
  static string Summary(ProcessOutcome p,string extra) {
   if(p.Cancelled)return "Cancelada por el usuario.";
   if(p.Timeout)return "Se agotó el tiempo máximo. El proceso fue cerrado y el registro se conservó.";
   var lines=p.Text.Split('\n').Where(x=>!x.StartsWith("WO_TESTER_") && Regex.IsMatch(x,@"(?i)SCRIPT ERROR|ERROR:|\bFAIL\b|\bFAILED\b|\bchecks\b|\bPASS\b|\.gd:\d+")).Take(14);
   string text=String.Join("\n",lines).Trim();
   return (extra.Length>0?extra+"\n":"")+(text.Length>0?text:"El proceso terminó con código "+p.Exit+".");
  }
 }
}
