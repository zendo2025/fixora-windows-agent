using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using LibreHardwareMonitor.Hardware;

internal static class FixoraAgent
{
    private const int Port = 38471;
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    private static string token;

    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs eventArgs) {
            string name = new AssemblyName(eventArgs.Name).Name + ".dll";
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib", name);
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Console.Title = "Fixora Windows Agent";
        token = CreateToken();
        string siteUrl = args.Length > 0 ? args[0] : "http://localhost/electronix/public/diagnostic-pc";
        var listener = new HttpListener();
        listener.Prefixes.Add("http://127.0.0.1:" + Port + "/");
        try { listener.Start(); }
        catch (Exception ex) { Console.Error.WriteLine("Impossible de démarrer l'agent : " + ex.Message); Console.ReadKey(); return 1; }

        string pairedUrl = siteUrl + (siteUrl.Contains("?") ? "&" : "?") + "agent_token=" + Uri.EscapeDataString(token);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Fixora Agent 2.0 actif sur 127.0.0.1:" + Port);
        Console.ResetColor();
        Console.WriteLine("Gardez cette fenêtre ouverte pendant le diagnostic.");
        Process.Start(new ProcessStartInfo(pairedUrl) { UseShellExecute = true });

        while (listener.IsListening)
        {
            try { Handle(listener.GetContext()); }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); }
        }
        return 0;
    }

    private static void Handle(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Headers"] = "X-Fixora-Token, Content-Type";
        response.Headers["Access-Control-Allow-Methods"] = "GET, OPTIONS";
        response.Headers["Cache-Control"] = "no-store";
        if (request.HttpMethod == "OPTIONS") { response.StatusCode = 204; response.Close(); return; }

        object payload;
        if (request.Headers["X-Fixora-Token"] != token) { response.StatusCode = 401; payload = new { error = "invalid_token" }; }
        else if (request.Url.AbsolutePath == "/health") payload = new { ok = true, agent = "Fixora Windows Agent", version = "2.0.0" };
        else if (request.Url.AbsolutePath == "/hardware") payload = BuildReport();
        else { response.StatusCode = 404; payload = new { error = "not_found" }; }

        byte[] bytes = Encoding.UTF8.GetBytes(Json.Serialize(payload));
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.Close();
    }

    private static object BuildReport()
    {
        return new Dictionary<string, object> {
            { "agent", "Fixora Windows Agent" }, { "version", "2.0.0" },
            { "timestamp", DateTime.UtcNow.ToString("o") }, { "computer", Environment.MachineName },
            { "cpu", QueryWmi("SELECT Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed FROM Win32_Processor") },
            { "memoryBytes", QueryFirstLong("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem", "TotalPhysicalMemory") },
            { "video", QueryWmi("SELECT Name,AdapterRAM,DriverVersion,Status FROM Win32_VideoController") },
            { "nvidia", ReadNvidia() }, { "sensors", ReadSensors() }
        };
    }

    private static List<Dictionary<string, object>> QueryWmi(string query)
    {
        var rows = new List<Dictionary<string, object>>();
        using (var searcher = new ManagementObjectSearcher(query))
            foreach (ManagementObject item in searcher.Get()) {
                var row = new Dictionary<string, object>();
                foreach (PropertyData property in item.Properties) row[property.Name] = property.Value;
                rows.Add(row);
            }
        return rows;
    }

    private static long QueryFirstLong(string query, string field)
    {
        var rows = QueryWmi(query); return rows.Count == 0 || rows[0][field] == null ? 0 : Convert.ToInt64(rows[0][field]);
    }

    private static List<Dictionary<string, object>> ReadNvidia()
    {
        var result = new List<Dictionary<string, object>>();
        try {
            var process = new Process { StartInfo = new ProcessStartInfo("nvidia-smi.exe", "--query-gpu=name,temperature.gpu,utilization.gpu,memory.total,memory.used,power.draw,fan.speed,driver_version --format=csv,noheader,nounits") { UseShellExecute=false, RedirectStandardOutput=true, CreateNoWindow=true } };
            process.Start(); string output = process.StandardOutput.ReadToEnd(); process.WaitForExit(5000);
            foreach (string line in output.Split(new[] {'\r','\n'}, StringSplitOptions.RemoveEmptyEntries)) {
                string[] p = line.Split(',').Select(x => x.Trim()).ToArray();
                result.Add(new Dictionary<string, object> { {"name",p[0]}, {"temperature",Number(p[1])}, {"load",Number(p[2])}, {"memoryTotalMb",Number(p[3])}, {"memoryUsedMb",Number(p[4])}, {"powerWatts",Number(p[5])}, {"fanPercent",Number(p[6])}, {"driver",p[7]} });
            }
        } catch { }
        return result;
    }

    private static object ReadSensors()
    {
        var list = new List<Dictionary<string, object>>();
        try {
            var computer = new Computer { IsCpuEnabled=true, IsGpuEnabled=true, IsMemoryEnabled=true, IsStorageEnabled=true, IsMotherboardEnabled=true };
            computer.Open(); foreach (IHardware hardware in computer.Hardware) ReadHardware(hardware, list); computer.Close();
            return new Dictionary<string, object> { {"available",true}, {"hardware",list} };
        } catch (Exception ex) { return new Dictionary<string, object> { {"available",false}, {"error",ex.Message} }; }
    }

    private static void ReadHardware(IHardware hardware, List<Dictionary<string, object>> list)
    {
        hardware.Update(); var sensors = new List<Dictionary<string, object>>();
        foreach (ISensor sensor in hardware.Sensors) if (sensor.Value.HasValue) sensors.Add(new Dictionary<string, object> { {"name",sensor.Name}, {"type",sensor.SensorType.ToString()}, {"value",Math.Round(sensor.Value.Value,2)} });
        list.Add(new Dictionary<string, object> { {"name",hardware.Name}, {"type",hardware.HardwareType.ToString()}, {"sensors",sensors} });
        foreach (IHardware sub in hardware.SubHardware) ReadHardware(sub, list);
    }

    private static double? Number(string value) { double n; return double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out n) ? (double?)n : null; }
    private static string CreateToken() { byte[] data=new byte[24]; using(var rng=RandomNumberGenerator.Create()) rng.GetBytes(data); return Convert.ToBase64String(data).TrimEnd('=').Replace('+','-').Replace('/','_'); }
}
