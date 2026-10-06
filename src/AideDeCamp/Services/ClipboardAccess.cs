using System.Runtime.InteropServices;
using System.Windows;

namespace AideDeCamp.Services;
public static class ClipboardAccess
{
    private static T Retry<T>(Func<T> action) {
        for(int attempt=0;;attempt++)try{return action();}catch(COMException) when(attempt<4){Thread.Sleep(30);}
    }
    public static void SetText(string text)=>Retry(()=>{Clipboard.SetText(text);return true;});
    public static string GetText()=>Retry(Clipboard.GetText);
}
