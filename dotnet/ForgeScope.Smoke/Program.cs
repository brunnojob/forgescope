using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

if(args.Length!=4)throw new ArgumentException("HTTP smoke: WORKBENCH_DLL REPORT_JSON JAVA_JAR WORKER_DLL");
var workbench=Path.GetFullPath(args[0]);var original=Path.GetFullPath(args[1]);var jar=Path.GetFullPath(args[2]);var worker=Path.GetFullPath(args[3]);
var root=Path.GetFullPath("examples/sample");var temp=Path.Combine(Path.GetTempPath(),"forgescope-http-"+Guid.NewGuid());Directory.CreateDirectory(temp);
var report=Path.Combine(temp,"report.json");File.Copy(original,report);File.Copy(Path.ChangeExtension(original,".sarif"),Path.Combine(temp,"report.sarif"));
var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var a in new[]{workbench,"--report",report,"--repo",root,"--jar",jar,"--worker",worker,"--port",port.ToString(System.Globalization.CultureInfo.InvariantCulture)})start.ArgumentList.Add(a);
using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new Uri($"http://127.0.0.1:{port}"),Timeout=TimeSpan.FromSeconds(60)};int checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception("HTTP check failed: "+label);checks++;}
try
{
    bool ready=false;for(int i=0;i<100;i++){if(process.HasExited)throw new Exception("Workbench failed: "+await stderr);try{var r=await client.GetAsync("/health");if(r.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}await Task.Delay(50);}
    Check(ready,"health readiness");
    string page=await client.GetStringAsync("/");Check(page.Contains("Java 21 + C#/.NET 10",StringComparison.Ordinal),"Java/.NET identity");Check(!page.Contains("<script",StringComparison.OrdinalIgnoreCase),"no custom browser scripts");
    var pt=await client.GetStringAsync("/?lang=pt&theme=light&engine=impact");Check(pt.Contains("Painel local de investigação",StringComparison.Ordinal),"Portuguese interface");Check(pt.Contains("class=\"light\"",StringComparison.Ordinal),"light theme");
    using(var data=JsonDocument.Parse(await client.GetStringAsync("/report.json")))Check(data.RootElement.GetProperty("version").GetString()=="0.3.0","JSON export");
    using(var data=JsonDocument.Parse(await client.GetStringAsync("/report.sarif")))Check(data.RootElement.GetProperty("version").GetString()=="2.1.0","SARIF export");
    using(var denied=await client.PostAsync("/scan",new FormUrlEncodedContent(new Dictionary<string,string>{{"nonce","wrong"},{"base",""}})))Check(denied.StatusCode==HttpStatusCode.Forbidden,"CSRF rejection");
    string token=Regex.Match(page,"name=\"nonce\" value=\"([^\"]+)\"").Groups[1].Value;Check(token.Length==64,"scan nonce present");
    using(var request=new HttpRequestMessage(HttpMethod.Post,"/scan")){request.Headers.Add("Origin","https://external.example");request.Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"nonce",token}});using var response=await client.SendAsync(request);Check(response.StatusCode==HttpStatusCode.Forbidden,"cross-origin scan rejection");}
    using(var response=await client.PostAsync("/scan",new FormUrlEncodedContent(new Dictionary<string,string>{{"nonce",token},{"base",""},{"changed","java/src/shop/Pricing.java"}})))Check(response.StatusCode==HttpStatusCode.Redirect,"real Java scan from C# workbench");
    using(var data=JsonDocument.Parse(await File.ReadAllTextAsync(report)))Check(data.RootElement.GetProperty("impact").GetProperty("engines").GetProperty("csharp").GetProperty("status").GetString()=="ok","scan invokes Roslyn worker");
    Console.WriteLine($"C# workbench HTTP: {checks} end-to-end checks passed");
}
finally
{
    if(!process.HasExited)process.Kill(entireProcessTree:true);await process.WaitForExitAsync();await Task.WhenAll(stdout,stderr);Directory.Delete(temp,true);
}
