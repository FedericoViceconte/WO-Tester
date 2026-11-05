using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace WOTester {
 public static class Scanner {
  public static string NormalizeProject(string path) {
   if(String.IsNullOrWhiteSpace(path)) throw new ArgumentException("Elegí la carpeta editable de WO Definitive.");
   path=Path.GetFullPath(path.Trim().Trim('"'));
   if(File.Exists(path) && Path.GetFileName(path).Equals("project.godot",StringComparison.OrdinalIgnoreCase)) path=Path.GetDirectoryName(path);
   if(File.Exists(Path.Combine(path,"VERSION-ACTUAL","project.godot"))) path=Path.Combine(path,"VERSION-ACTUAL");
   if(!File.Exists(Path.Combine(path,"project.godot"))) throw new ArgumentException("No se encontró project.godot. Seleccioná VERSION-ACTUAL o la carpeta que la contiene. El análisis interno necesita el proyecto editable.");
   return path;
  }
  public static string Hash(string path) { using(var sha=SHA256.Create()) using(var f=File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant(); }
  public static string Relative(string root,string path) { return path.Substring(root.TrimEnd('\\').Length+1).Replace('\\','/'); }
  public static bool Inside(string root,string path) { return Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase); }
  public static Inventory Scan(string project) {
   project=NormalizeProject(project);
   var i=new Inventory {Project=project,Version="desconocida",Commit="sin Git"};
   string config=File.ReadAllText(Path.Combine(project,"project.godot"));
   var v=Regex.Match(config,@"config/version=""([^""]+)"""); if(v.Success)i.Version=v.Groups[1].Value;
   var data=Path.Combine(project,"data");
   if(Directory.Exists(data)) i.JsonFiles=Directory.GetFiles(data,"*.json",SearchOption.AllDirectories).OrderBy(x=>x).ToList();
   Catalog(i,"maps_new.json","name",i.Maps); Catalog(i,"weapons.json","type",i.Weapons); Catalog(i,"weapons_extra.json","type",i.Weapons); Catalog(i,"pets.json","type",i.Pets);
   string tests=Path.Combine(project,"tests");
   if(Directory.Exists(tests)) foreach(var scene in Directory.GetFiles(tests,"*_test.tscn").OrderBy(x=>x)) {
    string stem=Path.GetFileNameWithoutExtension(scene); string source=Path.ChangeExtension(scene,".gd");
    string sceneText=File.ReadAllText(scene,Encoding.UTF8);
    var scriptRef=Regex.Match(sceneText,@"type=""Script"" path=""res://([^""]+)""");
    if(scriptRef.Success)source=Path.Combine(project,scriptRef.Groups[1].Value.Replace('/','\\'));
    string text=File.Exists(source)?File.ReadAllText(source,Encoding.UTF8):"";
    var guard=Regex.Match(text,@"ends_with\(""(/\.godot/[^""\r\n]+)""\)");
    string profileGuard=guard.Success?guard.Groups[1].Value.TrimStart('/') : ".godot/wo-tester-user";
    if(text.Contains("\"release-settings-user\" in OS.get_environment"))profileGuard=".godot/release-settings-user";
    if(text.Contains("%s-minipet-user")){
     var caseMatch=Regex.Match(sceneText,@"case_name\s*=\s*""([^""]+)""");
     if(!caseMatch.Success)caseMatch=Regex.Match(text,@"case_name\s*:=\s*""([^""]+)""");
     if(caseMatch.Success)profileGuard=".godot/"+caseMatch.Groups[1].Value+"-minipet-user";
    }
    i.Tests.Add(new TestCase {Id="native:"+stem,Name=Friendly(stem),Scene=Relative(project,scene),Category=Category(stem),Guard=profileGuard,NeedsRender=text.Contains("frame_post_draw") || text.Contains("warp_mouse")});
   }
   var sourceFiles=new List<string>{Path.Combine(project,"project.godot")}; sourceFiles.AddRange(i.JsonFiles);
   foreach(string folder in new[]{"scripts","scenes","tests"}) { string p=Path.Combine(project,folder);if(Directory.Exists(p)) sourceFiles.AddRange(Directory.GetFiles(p,"*",SearchOption.AllDirectories).Where(x=>new[]{".gd",".tscn",".tres"}.Contains(Path.GetExtension(x))));}
   foreach(var f in sourceFiles.OrderBy(x=>x)) i.Content["file:"+Relative(project,f)]=Hash(f);
   foreach(var x in i.Maps) i.Content["map:"+x]="present";
   foreach(var x in i.Weapons.Distinct()) i.Content["weapon:"+x]="present";
   foreach(var x in i.Pets) i.Content["pet:"+x]="present";
   using(var sha=SHA256.Create())i.Fingerprint=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(String.Join("\n",i.Content.OrderBy(x=>x.Key).Select(x=>x.Key+"="+x.Value))))).Replace("-","").ToLowerInvariant();
   try { i.Commit=Git(project,"rev-parse HEAD").Trim();i.Dirty=Git(project,"status --porcelain --untracked-files=no").Length>0; } catch { }
   return i;
  }
  static string Git(string folder,string args) { using(var p=Process.Start(new ProcessStartInfo("git",args) {WorkingDirectory=folder,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true})) { string s=p.StandardOutput.ReadToEnd(); if(!p.WaitForExit(5000)) {p.Kill();throw new IOException("Git no respondió.");}return p.ExitCode==0?s:""; } }
  static void Catalog(Inventory i,string file,string key,List<string> target) {
   string p=Path.Combine(i.Project,"data",file); if(!File.Exists(p)){i.Diagnostics.Add("Falta data/"+file);return;}
   try { foreach(var row in Rows(Product.Json().DeserializeObject(File.ReadAllText(p)))) if(row.ContainsKey(key))target.Add(Convert.ToString(row[key])); }
   catch(Exception e){i.Diagnostics.Add(file+": "+e.Message);}
  }
  public static IEnumerable<Dictionary<string,object>> Rows(object obj) {
   var a=obj as object[]; if(a!=null)foreach(var v in a){var d=v as Dictionary<string,object>;if(d!=null)yield return d;}
  }
  public static string Friendly(string stem) {
   var names=new Dictionary<string,string> {
    {"terrain_integrity_test","Integridad y destrucción de todos los mapas"},{"map_rework_test","Colisiones y rutas de mapas"},
    {"practice_ammo_test","Munición y guardado en práctica"},{"menu_bot_launch_test","Del menú a una partida con bots"},
    {"startup_shutdown_test","Inicio y cierre del juego"},{"battle_controls_test","Movimiento, cámara y controles"},
    {"network_payload_test","Validación de mensajes de red"},{"settings_rewrite_test","Configuración del juego"},
    {"account_gate_test","Acceso y pantalla de cuenta"},{"speed_mode_test","Reglas del modo de velocidad"},
    {"weapon_fit_catalog_test","Catálogo y montaje de armas"},{"scenery_destruction_test","Destrucción del escenario"}};
   string s;if(names.TryGetValue(stem,out s))return s;
   return stem.Replace("_test","").Replace('_',' ');
  }
  public static string Category(string s) {
   if(Regex.IsMatch(s,"map|terrain|scenery"))return "Mapas";
   if(Regex.IsMatch(s,"weapon|ammo|combat|battle_controls|motion|speed_mode"))return "Combate";
   if(Regex.IsMatch(s,"eos|eac|online|network|matchmaking|friends|epic|voice|queue"))return "Red y servicios";
   if(Regex.IsMatch(s,"outfit|skin|pet|panda|dracu|boo|santa|monkey|platypus|rabbit|dog_police|audio|voices"))return "Contenido";
   return "Interfaz y sistemas";
  }
  public static List<string> Resources(object node) {
   var output=new List<string>(); ResourcesInto(node,output); return output.Distinct().ToList();
  }
  static void ResourcesInto(object n,List<string> o) {
   var s=n as string;if(s!=null){if(s.StartsWith("res://",StringComparison.Ordinal) && s.Length>6)o.Add(s);return;}
   var d=n as Dictionary<string,object>;if(d!=null){foreach(var v in d.Values)ResourcesInto(v,o);return;}
   var a=n as object[];if(a!=null)foreach(var v in a)ResourcesInto(v,o);
  }
  public static string DetectProject() {
   string desktop=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
   foreach(string p in new[]{Path.Combine(desktop,"wo-remake","VERSION-ACTUAL"),Path.Combine(desktop,"wo-definitive","VERSION-ACTUAL"),Path.Combine(desktop,"WO Definitive","VERSION-ACTUAL")})if(File.Exists(Path.Combine(p,"project.godot")))return p;
   return "";
  }
  public static string DetectGodot() {
   string desktop=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
   string dir=Path.Combine(desktop,"Godot");
   if(Directory.Exists(dir)) {var exes=Directory.GetFiles(dir,"*console.exe",SearchOption.AllDirectories).OrderByDescending(x=>x).ToArray();if(exes.Length>0)return exes[0];}
   return "";
  }
 }
}
