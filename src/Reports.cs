using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
namespace WOTester {
 public static class Reports {
  public static List<Report> History() {
   var list=new List<Report>();string root=Path.Combine(Product.Home,"runs");
   if(!Directory.Exists(root))return list;
   foreach(string folder in Directory.GetDirectories(root).OrderByDescending(x=>x).Take(120)) {
    try{var r=Product.Read<Report>(Path.Combine(folder,"report.json"));r.Folder=folder;list.Add(r);}catch{}
   }return list;
  }
  public static Report Latest(string project){return History().FirstOrDefault(r=>String.Equals(r.Project,project,StringComparison.OrdinalIgnoreCase)&&r.State!="RUNNING");}
  public static string Markdown(Result r){return "### "+r.Name+"\n\nEstado: "+r.Label+"\n\n"+r.Detail+"\n\n**Cómo repetirlo**\n"+r.Reproduce+"\n\n**Qué revisar**\n"+r.Suggestion+"\n\nRegistro: "+r.Log;}
  static string H(string s){return HttpUtility.HtmlEncode(s??"");}
  static string Csv(string s){s=s??"";if(s.Length>0 && "=+-@\t\r".Contains(s[0]))s="'"+s;return "\""+s.Replace("\"","\"\"")+"\"";}
  public static void Save(Report r){
   Product.Write(Path.Combine(r.Folder,"report.json"),r);
   var md=new StringBuilder("# WO Tester · informe "+r.Id+"\n\n");
   md.AppendFormat("Juego: {0} · modo: {1} · semilla: {2}\n\n",r.GameVersion,r.Mode,r.Seed);
   md.AppendFormat("{0} correctas · {1} errores · {2} pendientes. Estado: {3}.\n\n",r.Passed,r.Failed,r.Pending,r.State);
   foreach(var x in r.Results.Where(x=>x.Status!="PASS"))md.AppendLine(Markdown(x)+"\n");
   File.WriteAllText(Path.Combine(r.Folder,"issues.md"),md.ToString(),new UTF8Encoding(false));
   var csv=new StringBuilder("Estado,Categoria,Prueba,Detalle,Reproducir,Sugerencia,Registro\r\n");
   foreach(var x in r.Results)csv.AppendLine(String.Join(",",new[]{x.Label,x.Category,x.Name,x.Detail,x.Reproduce,x.Suggestion,x.Log}.Select(Csv)));
   File.WriteAllText(Path.Combine(r.Folder,"results.csv"),csv.ToString(),new UTF8Encoding(true));
   File.WriteAllText(Path.Combine(r.Folder,"report.html"),Html(r),new UTF8Encoding(false));
  }
  static string Html(Report r){
   var b=new StringBuilder(@"<!doctype html><html lang='es'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>WO Tester · informe</title><style>
:root{color-scheme:dark}*{box-sizing:border-box}body{margin:0;background:#0c1018;color:#eaf0fc;font:15px/1.65 system-ui,sans-serif}main{max-width:1100px;margin:auto;padding:54px 28px}small,.muted{color:#9ba8bd}.brand{color:#83a9ff;letter-spacing:.18em;font-size:12px;font-weight:700}h1{font-size:38px;letter-spacing:-1px;line-height:1.2}h2{font-size:22px;margin-top:36px}.stats{display:flex;gap:14px;flex-wrap:wrap}.stat{background:#151d2b;border:1px solid #273349;border-radius:16px;padding:20px 28px;min-width:190px}.number{font-size:32px;font-weight:700}.PASS{color:#68d9b5}.FAIL{color:#ff8792}.BLOCKED,.REVIEW,.SKIP,.CANCELLED{color:#f2c77c}details{border:1px solid #263249;background:#121a27;border-radius:12px;padding:18px 22px;margin:12px 0}summary{cursor:pointer;font-weight:650}.pill{font-size:11px;letter-spacing:.07em;margin-right:12px}pre{white-space:pre-wrap;word-break:break-word;background:#0c121d;padding:15px;border-radius:8px;font:13px/1.6 ui-monospace,monospace}input,select{background:#182234;color:#edf2ff;border:1px solid #34435c;border-radius:8px;padding:12px;font:inherit;margin:12px 12px 12px 0}input{width:min(470px,100%)}a{color:#9abaff}.meta{overflow-wrap:anywhere}.hidden{display:none}.notice{border-left:3px solid #7c9dff;padding-left:18px}.changes{max-height:220px;overflow:auto}footer{margin-top:40px;color:#8796ac;font-size:12px}@media print{body{background:white;color:#111}details{break-inside:avoid}input,select{display:none}pre,.stat,details{background:#f2f4f8;color:#111}}</style><main><div class='brand'>WO TESTER / QUALITY LAB</div><h1>Informe de pruebas</h1>");
   b.Append("<p class='meta muted'>WO Definitive · versión "+H(r.GameVersion)+" · "+H(r.StartedUtc)+"<br>WO Tester "+H(r.TesterVersion)+" · "+H(r.Id)+"</p>");
   b.Append("<div class='stats'><div class='stat'><div class='number PASS'>"+r.Passed+"</div>Correctas</div><div class='stat'><div class='number FAIL'>"+r.Failed+"</div>Errores</div><div class='stat'><div class='number REVIEW'>"+r.Pending+"</div>Pendientes / revisar</div><div class='stat'><div class='number'>"+r.CheckCount+"</div>Comprobaciones declaradas</div></div>");
   b.Append("<p class='notice'>"+H(r.State)+" · "+r.Seconds.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+" s reales. Las pruebas aprobadas describen únicamente los escenarios ejecutados. Revisá la cobertura pendiente.</p>");
   b.Append("<details><summary>Versión y reproducción</summary><pre>Modo: "+H(r.Mode)+"\nAceleración del adaptador: "+r.Speed+"×; pruebas nativas: 1×\nSemilla: "+r.Seed+"\nGodot: "+H(r.GodotVersion)+"\nCommit: "+H(r.Commit)+"\nCambios locales: "+r.Dirty+"\nHuella del código: "+H(r.Fingerprint)+"</pre></details>");
   b.Append("<details><summary>Cambios desde la revisión anterior · "+r.Changes.Count+"</summary><pre class='changes'>"+H(String.Join("\n",r.Changes))+"</pre></details>");
   b.Append("<h2>Resultados y evidencia</h2><input id='q' aria-label='Buscar prueba' placeholder='Buscar mapa, arma, error…'><select id='status' aria-label='Filtrar estado'><option value=''>Todos los estados</option><option value='FAIL'>Errores</option><option value='PASS'>Correctas</option><option value='pending'>Pendientes / revisar</option></select>");
   foreach(var x in r.Results.OrderBy(x=>x.Status=="FAIL"?0:x.Status=="PASS"?2:1)){
    b.Append("<details class='result' data-status='"+H(x.Status)+"'><summary><span class='pill "+H(x.Status)+"'>"+H(x.Label)+"</span>"+H(x.Name)+" <small> / "+H(x.Category)+"</small></summary><pre>"+H(x.Detail)+"</pre>");
    if(!String.IsNullOrEmpty(x.Reproduce))b.Append("<b>Cómo repetirlo</b><p>"+H(x.Reproduce)+"</p>");
    if(!String.IsNullOrEmpty(x.Suggestion))b.Append("<b>Qué revisar</b><p>"+H(x.Suggestion)+"</p>");
    if(!String.IsNullOrEmpty(x.Log)&&Path.GetFileName(x.Log)==x.Log)b.Append("<a href='"+H(Uri.EscapeDataString(x.Log))+"'>Abrir registro</a>");
    b.Append("</details>");
   }
   b.Append(@"<footer>Generado localmente por WO Tester. Los informes no se envían a ningún servidor. Cada error conserva su evidencia.</footer></main><script>
function filter(){const q=document.getElementById('q').value.toLocaleLowerCase(),s=document.getElementById('status').value;document.querySelectorAll('.result').forEach(e=>{const v=e.dataset.status;e.classList.toggle('hidden',!e.textContent.toLocaleLowerCase().includes(q)||(s==='pending'?v==='PASS'||v==='FAIL':s&&v!==s));})}
document.getElementById('q').addEventListener('input',filter);document.getElementById('status').addEventListener('change',filter);
</script></html>");
   return b.ToString();
  }
 }
}
