using Microsoft.Win32;

namespace MyTools.Plugins;

public class DefaultBrowserHelper
{
    public static string? GetBrowserExecutePath()
    {
        const string userChoice = @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice";
        using var userChoiceKey = Registry.CurrentUser.OpenSubKey( userChoice );
        var progIdValue = userChoiceKey?.GetValue( "Progid" );
        if ( progIdValue == null )
        {
            return null;
        }
        
        const string exeSuffix = ".exe";
        string? path = progIdValue + @"\shell\open\command";
        using var pathKey = Registry.ClassesRoot.OpenSubKey( path );
        try
        {
            path = pathKey?.GetValue(null)?.ToString()?.ToLower().Replace( "\"", "" );
            if (path?.EndsWith(exeSuffix) == false)
            {
                var endIndex = path.LastIndexOf(exeSuffix, StringComparison.Ordinal) + exeSuffix.Length;
                path = path[..endIndex];
            }
            return path;
        }
        catch
        {
            // Assume the registry value is set incorrectly, or some funky browser is used which currently is unknown.
        }
        return null;
    }
}
