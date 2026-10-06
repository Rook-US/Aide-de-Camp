using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace AideDeCamp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException+=(_,args)=>Services.ErrorLog.Write("Unhandled background error",args.ExceptionObject as Exception??new Exception(args.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException+=(_,args)=>Services.ErrorLog.Write("Unobserved task error",args.Exception);
        base.OnStartup(e);
        if(e.Args.Contains("--smoke-test")) {
            try {
                var probe=new MainWindow(true);MainWindow=probe;probe.Show();
                Dispatcher.BeginInvoke(new Action(()=>{
                    try {
                        probe.UpdateLayout();
                        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"smoke-test.json"),System.Text.Json.JsonSerializer.Serialize(new {
                            success=true,title=probe.Title,runtime=System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
                            framework=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,architecture=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString()
                        }));Shutdown(0);
                    }catch(Exception ex){SmokeFailure(ex);}
                }),DispatcherPriority.ApplicationIdle);
            }catch(Exception ex){SmokeFailure(ex);}return;
        }
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if(Environment.GetCommandLineArgs().Contains("--smoke-test")){e.Handled=true;SmokeFailure(e.Exception);return;}
        var logPath=Services.ErrorLog.Write("Unhandled UI error",e.Exception);
        var details=BuildExceptionText(e.Exception)+"\nError log: "+(logPath??"Could not write log.");

        MessageBox.Show(
            details,
            "Aide-de-Camp — Unhandled error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }
    private void SmokeFailure(Exception ex) {
        Services.ErrorLog.Write("Packaged startup verification",ex);
        try{File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"smoke-test.json"),System.Text.Json.JsonSerializer.Serialize(new {success=false,error=ex.ToString()}));}catch{}
        Shutdown(1);
    }

    private static string BuildExceptionText(Exception ex)
    {
        var sb = new StringBuilder();
        var current = ex;
        var level = 0;
        while (current is not null)
        {
            sb.AppendLine($"Exception level {level}: {current.GetType().FullName}");
            sb.AppendLine(current.Message);
            sb.AppendLine();
            current = current.InnerException;
            level++;
        }
        sb.AppendLine("Stack trace:");
        sb.AppendLine(ex.StackTrace ?? "(none)");
        return sb.ToString();
    }
}
