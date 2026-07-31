using System.Text.RegularExpressions;

namespace MyTools.Plugins.Translator;

public static class DevopsTranslator
{
    public static string Translate(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return string.Empty;
        }
        
        if(query.StartsWith("\"") && query.EndsWith("\""))
        {
            return query;
        }
        
        string replacedString = query;
        
        // notest => NOT file:*test*
        if (replacedString.IndexOf("notest", StringComparison.Ordinal) > -1)
        {
            replacedString = Regex.Replace(replacedString, @"notest", "");
            replacedString += " NOT path:*test*";
        }
        
        return replacedString;
    }
}