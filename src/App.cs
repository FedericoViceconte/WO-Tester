using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Runtime.InteropServices;
[assembly:AssemblyTitle("WO Tester")]
[assembly:AssemblyDescription("Laboratorio independiente de pruebas para WO Definitive")]
[assembly:AssemblyCompany("FRR Games")]
[assembly:AssemblyProduct("WO Tester")]
[assembly:AssemblyVersion("1.0.0.0")]
[assembly:AssemblyFileVersion("1.0.0.0")]
namespace WOTester {
 public static class Program {
  public static string[] Arguments;
  static string Arg(string name,string fallback){int i=Array.IndexOf(Arguments,name);return i>=0&&i+1<Arguments.Length?Arguments[i+1]:fallback;}
  [STAThread] public static int Main(string[] args) {
   Arguments=args;
   string custom=Arg("--home","");if(custom.Length>0)Product.Home=Path.GetFullPath(custom);
   try {
    Directory.CreateDirectory(Product.Home);
    Options options=Options.Load();
    options.Project=Arg("--project",String.IsNullOrEmpty(options.Project)?Scanner.DetectProject():options.Project);
    options.Godot=Arg("--godot",String.IsNullOrEmpty(options.Godot)?Scanner.DetectGodot():options.Godot);
    options.Mode=Arg("--mode",options.Mode);options.Speed=Int32.Parse(Arg("--speed",options.Speed.ToString()));
    options.Speed=Math.Max(1,Math.Min(10,options.Speed));
    if(args.Contains("--cli")){
     Console.OutputEncoding=new System.Text.UTF8Encoding(false);
     if(args.Contains("--help")){Console.WriteLine("wo-tester-cli --project RUTA --godot GODOT.exe --mode quick|complete|failed --speed 1|2|5|10 [--home RUTA]");return 0;}
     var cancel=new CancellationTokenSource();Console.CancelKeyPress+=(s,e)=>{e.Cancel=true;cancel.Cancel();};
     using(var projectLock=Lock(options.Project)){
      var runner=new Runner();runner.OnResult=r=>Console.WriteLine("["+r.Label+"] "+r.Name);
      runner.OnProgress=(t,d,n)=>Console.WriteLine("("+d+"/"+n+") "+t);
      var report=runner.Run(options,cancel.Token);
      Console.WriteLine("REPORT="+Path.Combine(report.Folder,"report.html"));
      Console.WriteLine("PASS="+report.Passed+" FAIL="+report.Failed+" PENDING="+report.Pending+" STATE="+report.State);
      return report.State=="COMPLETED"?(report.Failed>0?1:0):2;
     }
    }
    if(args.Contains("--preview")){
     var preview=new Desktop(options,true,Arg("--load-report",""));
     preview.RenderPreview(Arg("--preview",Path.Combine(Product.Home,"preview.png")),Arg("--preview-page","Dashboard"),Arg("--load-report",""));
     return 0;
    }
    using(var mutex=new Mutex(false,"Local\\FRR.WOTester.Desktop")){
     if(!mutex.WaitOne(0,false)){MessageBox.Show("WO Tester ya está abierto.","WO Tester");return 0;}
     var app=new Application();
     app.DispatcherUnhandledException+=(s,e)=>{e.Handled=true;MessageBox.Show(e.Exception.Message,"WO Tester",MessageBoxButton.OK,MessageBoxImage.Error);};
     return app.Run(new Desktop(options,args.Contains("--no-autorun"),Arg("--load-report","")).Window);
    }
   }catch(Exception e){
    try{File.WriteAllText(Path.Combine(Product.Home,"startup-error.txt"),e.ToString());}catch{}
    if(args.Contains("--cli"))Console.Error.WriteLine(e.Message);else MessageBox.Show(e.Message,"WO Tester",MessageBoxButton.OK,MessageBoxImage.Error);
    return 2;
   }
  }
  public static FileStream Lock(string project) {
   string path=Scanner.NormalizeProject(project).ToLowerInvariant();
   byte[] digest;using(var sha=System.Security.Cryptography.SHA256.Create())digest=sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(path));
   string dir=Path.Combine(Product.Home,"locks");Directory.CreateDirectory(dir);
   try{return new FileStream(Path.Combine(dir,BitConverter.ToString(digest)+".lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
   catch(IOException){throw new IOException("Ya hay una revisión de este proyecto en curso. Esperá a que termine o detenela desde su ventana.");}
  }
 }
 public sealed class Desktop {
  public Window Window; Options options; ObservableCollection<Result> results=new ObservableCollection<Result>();
  Report current; CancellationTokenSource cancellation; Stopwatch clock; DispatcherTimer timer;
  bool running; bool noAutorun; string loadReport; string currentPage="Dashboard";
  T Get<T>(string name) where T:class{return Window.FindName(name) as T;}
  void Text(string name,string value){Get<TextBlock>(name).Text=value;}
  void Click(string name,Action action){Get<Button>(name).Click+=(s,e)=>{try{action();}catch(Exception ex){Error(ex.Message);}};}
  public Desktop(Options selected,bool noAuto,string reportPath) {
   options=selected;noAutorun=noAuto;loadReport=reportPath;
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("WOTester.MainWindow.xaml"))Window=(Window)XamlReader.Load(stream);
   WindowChrome.SetWindowChrome(Window,new WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(0)});
   Window.Width=Math.Min(1320,SystemParameters.WorkArea.Width-35);Window.Height=Math.Min(900,SystemParameters.WorkArea.Height-35);
   Get<Grid>("TitleBar").MouseLeftButtonDown+=(s,e)=>{if(e.ClickCount==2)ToggleMax();else if(e.ButtonState==MouseButtonState.Pressed)Window.DragMove();};
   Click("MinButton",()=>Window.WindowState=WindowState.Minimized);Click("MaxButton",ToggleMax);Click("CloseButton",()=>Window.Close());
   Click("NavDashboard",()=>Page("Dashboard"));Click("NavHistory",()=>{RefreshHistory();Page("HistoryPage");});Click("NavSettings",()=>Page("SettingsPage"));Click("NavAbout",()=>Page("AboutPage"));
   Click("ProjectButton",()=>Page("SettingsPage"));Click("BrowseProject",ChooseProject);Click("BrowseGodot",ChooseGodot);
   Click("SaveSettings",Save);Click("StartButton",()=>Start());Click("StopButton",Stop);
   Click("ReportButton",()=>OpenCurrent());Click("RefreshHistory",RefreshHistory);
   Click("OpenHistoryReport",()=>{var r=Get<DataGrid>("HistoryGrid").SelectedItem as Report;if(r!=null)Open(Path.Combine(r.Folder,"report.html"));});
   Click("LoadHistoryReport",()=>{if(running){Error("Detené la revisión antes de cargar otro informe.");return;}var r=Get<DataGrid>("HistoryGrid").SelectedItem as Report;if(r!=null){ShowReport(r);Page("Dashboard");}});
   Click("OpenRuns",()=>{Directory.CreateDirectory(Path.Combine(Product.Home,"runs"));Open(Path.Combine(Product.Home,"runs"));});
   Click("GithubButton",()=>Open("https://github.com/FedericoViceconte/WO-Tester"));
   Click("DocsButton",()=>{string p=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"GUIA-DE-USO.md");if(File.Exists(p))Open(p);else Open("https://github.com/FedericoViceconte/WO-Tester#readme");});
   Click("CopyButton",()=>{var r=Selected();if(r!=null)Clipboard.SetText(Reports.Markdown(r));});
   Click("LogButton",()=>{var r=Selected();if(r!=null&&current!=null&&!String.IsNullOrEmpty(r.Log))Open(Path.Combine(current.Folder,Path.GetFileName(r.Log)));});
   Get<TextBox>("ProjectPath").Text=options.Project;Get<TextBox>("GodotPath").Text=options.Godot;
   Get<TextBox>("TimeoutBox").Text=options.TimeoutSeconds.ToString();Get<TextBox>("SeedBox").Text=options.Seed.ToString();
   Get<CheckBox>("AutoRunBox").IsChecked=options.AutoRun;Get<CheckBox>("VisibleBox").IsChecked=options.Visible;
   Select("ModeBox",options.Mode);Select("SpeedBox",options.Speed.ToString());Get<ComboBox>("FilterBox").SelectedIndex=0;
   Get<DataGrid>("ResultsGrid").ItemsSource=results;
   Get<DataGrid>("ResultsGrid").SizeChanged+=(s,e)=>SizeColumns();
   CollectionViewSource.GetDefaultView(results).Filter=Filter;
   Get<TextBox>("SearchBox").TextChanged+=(s,e)=>RefreshFilter();
   Get<ComboBox>("FilterBox").SelectionChanged+=(s,e)=>RefreshFilter();
   Get<DataGrid>("ResultsGrid").SelectionChanged+=(s,e)=>Details();
   Window.PreviewKeyDown+=(s,e)=>{if(e.Key==Key.R && Keyboard.Modifiers==ModifierKeys.Control){Start();e.Handled=true;}if(e.Key==Key.F1){Page("AboutPage");e.Handled=true;}};
   timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};timer.Tick+=(s,e)=>{if(clock!=null)Text("ElapsedCaption",clock.Elapsed.ToString(@"mm\:ss"));};
   Window.Closing+=(s,e)=>{if(running){e.Cancel=true;Stop();Text("ProgressCaption","Deteniendo la revisión; esperá a que se guarde el informe.");}};
   Window.Loaded+=async(s,e)=>{
    await RefreshInventory();
    if(!String.IsNullOrEmpty(loadReport)&&File.Exists(loadReport)){ShowReport(Product.Read<Report>(loadReport));return;}
    var previous=Reports.Latest(options.Project);if(previous!=null)ShowReport(previous);
    if(!noAutorun&&options.AutoRun&&File.Exists(options.Godot)&&File.Exists(Path.Combine(options.Project??"","project.godot"))){await Task.Delay(800);Start();}
   };
  }
  public void RenderPreview(string path,string page,string reportPath){
   if(!String.IsNullOrEmpty(reportPath)&&File.Exists(reportPath))ShowReport(Product.Read<Report>(reportPath));
   try{var i=Scanner.Scan(options.Project);Text("GameVersion","VERSIÓN "+i.Version);Text("ProjectCaption",i.Project);Text("InventoryCaption",i.Maps.Count+" mapas  ·  "+i.Weapons.Distinct().Count()+" definiciones de armas  ·  "+i.Pets.Count+" mascotas  ·  "+i.Tests.Count+" escenarios");}catch{}
   Page(page);if(page=="HistoryPage")RefreshHistory();
   var visual=(FrameworkElement)Window.Content;
   visual.Measure(new Size(1320,862));visual.Arrange(new Rect(0,0,1320,862));visual.UpdateLayout();SizeColumns();visual.UpdateLayout();
   var image=new System.Windows.Media.Imaging.RenderTargetBitmap(1320,862,96,96,PixelFormats.Pbgra32);
   image.Render(visual);
   var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
   Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));using(var stream=File.Create(path))encoder.Save(stream);
  }
  void SizeColumns(){var grid=Get<DataGrid>("ResultsGrid");if(grid.ActualWidth>700)grid.Columns[1].Width=new DataGridLength(Math.Max(300,grid.ActualWidth-355));}
  void Select(string name,string tag){var box=Get<ComboBox>(name);foreach(ComboBoxItem i in box.Items)if(Convert.ToString(i.Tag)==tag){box.SelectedItem=i;return;}box.SelectedIndex=0;}
  string Tag(string name){var item=Get<ComboBox>(name).SelectedItem as ComboBoxItem;return item==null?"":Convert.ToString(item.Tag);}
  void ToggleMax(){Window.WindowState=Window.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;}
  void Page(string page){
   currentPage=page;
   foreach(var n in new[]{"Dashboard","HistoryPage","SettingsPage","AboutPage"})Get<FrameworkElement>(n).Visibility=n==page?Visibility.Visible:Visibility.Collapsed;
   string[] names={"NavDashboard","NavHistory","NavSettings","NavAbout"};string[] pages={"Dashboard","HistoryPage","SettingsPage","AboutPage"};
   for(int i=0;i<4;i++)Get<Button>(names[i]).Background=(Brush)new BrushConverter().ConvertFromString(pages[i]==page?"#233858":"Transparent");
  }
  void ChooseProject(){var dialog=new OpenFileDialog{Title="Elegí project.godot de WO Definitive",Filter="Proyecto Godot|project.godot",FileName="project.godot"};if(dialog.ShowDialog(Window)==true)Get<TextBox>("ProjectPath").Text=Path.GetDirectoryName(dialog.FileName);}
  void ChooseGodot(){var dialog=new OpenFileDialog{Title="Elegí el ejecutable de Godot 4",Filter="Ejecutable de Godot|*.exe",FileName="Godot_v4.7.2-stable_win64_console.exe"};if(dialog.ShowDialog(Window)==true)Get<TextBox>("GodotPath").Text=dialog.FileName;}
  void ReadOptions(){
   options.Project=Scanner.NormalizeProject(Get<TextBox>("ProjectPath").Text);
   options.Godot=Get<TextBox>("GodotPath").Text.Trim().Trim('"');
   if(!File.Exists(options.Godot))throw new ArgumentException("Elegí el ejecutable de Godot 4.");
   int timeout,seed;if(!Int32.TryParse(Get<TextBox>("TimeoutBox").Text,out timeout)||timeout<10||timeout>1800)throw new ArgumentException("El tiempo máximo debe estar entre 10 y 1800 segundos.");
   if(!Int32.TryParse(Get<TextBox>("SeedBox").Text,out seed))throw new ArgumentException("La semilla debe ser un número entero.");
   options.TimeoutSeconds=timeout;options.Seed=seed;options.Mode=Tag("ModeBox");options.Speed=Int32.Parse(Tag("SpeedBox"));
   options.AutoRun=Get<CheckBox>("AutoRunBox").IsChecked==true;options.Visible=Get<CheckBox>("VisibleBox").IsChecked==true;
  }
  async void Save(){try{ReadOptions();options.Save();await RefreshInventory();Get<TextBlock>("SettingsFeedback").Text="Configuración guardada. Todo listo para probar."; }catch(Exception e){Error(e.Message);}}
  async Task RefreshInventory(){
   try{
    var i=await Task.Run(()=>Scanner.Scan(options.Project));
    Text("GameVersion","VERSIÓN "+i.Version);Text("ProjectCaption",i.Project);
    Text("InventoryCaption",i.Maps.Count+" mapas  ·  "+i.Weapons.Distinct().Count()+" definiciones de armas  ·  "+i.Pets.Count+" mascotas  ·  "+i.Tests.Count+" escenarios");
   }catch{Text("GameVersion","SIN CONECTAR");Text("ProjectCaption","Elegí el proyecto editable en Configuración.");}
  }
  async void Start(){
   if(running)return;
   FileStream projectLock=null;
   try{
    ReadOptions();projectLock=Program.Lock(options.Project);options.Save();
    running=true;current=null;results.Clear();RefreshCounters();Page("Dashboard");
    Get<Button>("StartButton").IsEnabled=false;Get<Button>("StopButton").IsEnabled=true;Get<Button>("ReportButton").IsEnabled=false;
    Get<ComboBox>("ModeBox").IsEnabled=false;Get<ComboBox>("SpeedBox").IsEnabled=false;Get<Button>("SaveSettings").IsEnabled=false;
    cancellation=new CancellationTokenSource();clock=Stopwatch.StartNew();timer.Start();
    var runner=new Runner();
    runner.OnResult=r=>Window.Dispatcher.Invoke(new Action(()=>{results.Add(r);RefreshCounters();}));
    runner.OnProgress=(t,d,n)=>Window.Dispatcher.Invoke(new Action(()=>{Text("ProgressCaption",t);Get<ProgressBar>("RunProgress").Value=n==0?0:Math.Min(98,100.0*d/n);}));
    current=await Task.Run(()=>runner.Run(options,cancellation.Token));
    RefreshCounters();Get<Button>("ReportButton").IsEnabled=true;
    Get<ProgressBar>("RunProgress").Value=current.State=="COMPLETED"?100:Get<ProgressBar>("RunProgress").Value;
    Text("ProgressCaption",current.State=="COMPLETED"?"Revisión finalizada · "+current.Failed+" errores · "+current.Pending+" pendientes / revisar.":current.State=="CANCELLED"?"Revisión detenida. Los resultados están guardados.":"Revisión incompleta. Revisá los requisitos y registros.");
    RefreshHistory();
   }catch(Exception e){Error(e.Message);Text("ProgressCaption","No se pudo iniciar la revisión.");}
   finally{
    if(projectLock!=null)projectLock.Dispose();
    running=false;timer.Stop();if(clock!=null)clock.Stop();
    Get<Button>("StartButton").IsEnabled=true;Get<Button>("StopButton").IsEnabled=false;Get<Button>("SaveSettings").IsEnabled=true;
    Get<ComboBox>("ModeBox").IsEnabled=true;Get<ComboBox>("SpeedBox").IsEnabled=true;
   }
  }
  void Stop(){if(cancellation!=null)cancellation.Cancel();}
  Result Selected(){return Get<DataGrid>("ResultsGrid").SelectedItem as Result;}
  void Details(){var r=Selected();if(r==null)return;Text("DetailTitle",r.Name.ToUpperInvariant());Get<TextBox>("DetailText").Text=r.Detail+(String.IsNullOrEmpty(r.Suggestion)?"":"\n\nQué revisar: "+r.Suggestion)+(String.IsNullOrEmpty(r.Reproduce)?"":"\n\nRepetir: "+r.Reproduce);}
  bool Filter(object value){var r=value as Result;if(r==null)return false;string q=Get<TextBox>("SearchBox").Text??"",tag=Tag("FilterBox");return (tag=="all"||tag==""||tag==r.Status||(tag=="pending"&&r.Status!="PASS"&&r.Status!="FAIL"))&&(r.Name+" "+r.Category+" "+r.Detail).IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0;}
  void RefreshFilter(){if(Get<DataGrid>("ResultsGrid").ItemsSource!=null)CollectionViewSource.GetDefaultView(results).Refresh();}
  void RefreshCounters(){Text("PassedCount",results.Count(x=>x.Status=="PASS").ToString());Text("FailedCount",results.Count(x=>x.Status=="FAIL").ToString());Text("PendingCount",results.Count(x=>x.Status!="PASS"&&x.Status!="FAIL").ToString());Text("ChecksCount",results.Sum(x=>x.Checks).ToString());Get<StackPanel>("EmptyState").Visibility=results.Count==0?Visibility.Visible:Visibility.Collapsed;}
  void RefreshHistory(){Get<DataGrid>("HistoryGrid").ItemsSource=Reports.History();}
  void ShowReport(Report report){current=report;results.Clear();foreach(var r in report.Results)results.Add(r);RefreshCounters();Get<Button>("ReportButton").IsEnabled=true;Text("ProgressCaption","Informe guardado · "+report.Id+" · "+report.Failed+" errores.");Text("ElapsedCaption",TimeSpan.FromSeconds(report.Seconds).ToString(@"mm\:ss"));Get<ProgressBar>("RunProgress").Value=report.State=="COMPLETED"?100:0;}
  void OpenCurrent(){if(current!=null)Open(Path.Combine(current.Folder,"report.html"));}
  static void Open(string target){Process.Start(new ProcessStartInfo(target){UseShellExecute=true});}
  void Error(string message){MessageBox.Show(Window,message,"WO Tester",MessageBoxButton.OK,MessageBoxImage.Information);}
 }
}
