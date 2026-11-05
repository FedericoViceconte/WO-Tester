using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace WOTester {
 public static class Product {
  public const string Version = "1.0.0";
  public static string Home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WO Tester");
  public static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024, RecursionLimit = 128 }; }
  public static T Read<T>(string path) { return Json().Deserialize<T>(File.ReadAllText(path, Encoding.UTF8)); }
  public static void Write(string path, object value) { Directory.CreateDirectory(Path.GetDirectoryName(path)); string temp = path + ".tmp"; File.WriteAllText(temp, Json().Serialize(value), new UTF8Encoding(false)); if(File.Exists(path)) File.Delete(path); File.Move(temp,path); }
  public static string StatusLabel(string s) { switch(s) { case "PASS": return "CORRECTO"; case "FAIL": return "ERROR"; case "BLOCKED": return "BLOQUEADA"; case "REVIEW": return "REVISAR"; case "CANCELLED": return "CANCELADA"; default: return "PENDIENTE"; } }
 }
 public sealed class Options {
  public string Project {get;set;}
  public string Godot {get;set;}
  public string Mode {get;set;}
  public int Speed {get;set;}
  public int TimeoutSeconds {get;set;}
  public int Seed {get;set;}
  public bool AutoRun {get;set;}
  public bool Visible {get;set;}
  public Options() { Mode="complete"; Speed=10; TimeoutSeconds=120; Seed=20261105; AutoRun=true; }
  public static Options Load() { try { return Product.Read<Options>(Path.Combine(Product.Home,"settings.json")); } catch { return new Options(); } }
  public void Save() { Product.Write(Path.Combine(Product.Home,"settings.json"),this); }
 }
 public sealed class TestCase {
  public string Id {get;set;} public string Name {get;set;} public string Category {get;set;}
  public string Scene {get;set;} public string Guard {get;set;} public bool NeedsRender {get;set;}
 }
 public sealed class Inventory {
  public string Project {get;set;} public string Version {get;set;} public string Fingerprint {get;set;}
  public string Commit {get;set;} public bool Dirty {get;set;}
  public List<string> JsonFiles = new List<string>();
  public List<TestCase> Tests = new List<TestCase>();
  public Dictionary<string,string> Content = new Dictionary<string,string>();
  public List<string> Maps = new List<string>(); public List<string> Weapons = new List<string>(); public List<string> Pets = new List<string>();
  public List<string> Diagnostics = new List<string>();
 }
 public sealed class Result {
  public string Id {get;set;} public string Name {get;set;} public string Category {get;set;}
  public string Status {get;set;} public string Severity {get;set;} public string Detail {get;set;}
  public string Suggestion {get;set;} public string Reproduce {get;set;} public string Log {get;set;}
  public double Seconds {get;set;} public int Checks {get;set;}
  public string Label { get { return Product.StatusLabel(Status); } }
  public string Duration { get { return Seconds.ToString("0.0")+" s"; } }
  public string Color { get { return Status=="PASS" ? "#63D7B5" : Status=="FAIL" ? "#FF838D" : "#F3C67A"; } }
 }
 public sealed class Report {
  public string Schema {get;set;} public string TesterVersion {get;set;}
  public string Id {get;set;} public string StartedUtc {get;set;} public string FinishedUtc {get;set;}
  public string Project {get;set;} public string GameVersion {get;set;} public string Fingerprint {get;set;}
  public string Commit {get;set;} public bool Dirty {get;set;}
  public string GodotVersion {get;set;} public string Mode {get;set;} public int Speed {get;set;} public int Seed {get;set;}
  public string State {get;set;} public double Seconds {get;set;}
  public Dictionary<string,string> Content {get;set;}
  public List<string> Changes {get;set;} public List<Result> Results {get;set;}
  public string Folder {get;set;}
  public int Passed { get { return Results.Count(x=>x.Status=="PASS"); } }
  public int Failed { get { return Results.Count(x=>x.Status=="FAIL"); } }
  public int Pending { get { return Results.Count(x=>x.Status!="PASS" && x.Status!="FAIL"); } }
  public int CheckCount { get { return Results.Sum(x=>x.Checks); } }
  public Report() { Schema="wo-tester.report/1";TesterVersion=Product.Version;Results=new List<Result>();Content=new Dictionary<string,string>();Changes=new List<string>(); }
 }
}
