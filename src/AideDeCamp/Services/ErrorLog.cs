using System.IO;
using System.Runtime.InteropServices;

namespace AideDeCamp.Services;
public static class ErrorLog
{
    private static readonly object Gate=new();
    public static string DirectoryPath=>Path.Combine(AppPaths.Root,"Logs");
    public static string? Write(string operation,Exception exception,string? directory=null)
    {
        try {
            lock(Gate) {
                var folder=directory??DirectoryPath;Directory.CreateDirectory(folder);
                var file=Path.Combine(folder,$"Aide-de-Camp-{DateTime.Now:yyyy-MM-dd}-{Environment.ProcessId}.log");
                var details=$"[{DateTimeOffset.Now:O}] Aide-de-Camp {typeof(ErrorLog).Assembly.GetName().Version} | {operation}\n{RuntimeInformation.FrameworkDescription} | {RuntimeInformation.OSDescription}\n{exception}\n\n";
                if(File.Exists(file) && new FileInfo(file).Length>4*1024*1024)File.Move(file,file+".previous",true);
                File.AppendAllText(file,details);return file;
            }
        }catch{return null;} // Reporting an error must not throw a second one.
    }
}
