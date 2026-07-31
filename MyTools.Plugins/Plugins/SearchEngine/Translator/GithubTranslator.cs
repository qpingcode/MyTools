using System.Text.RegularExpressions;

namespace MyTools.Plugins.Translator;

public static class GithubTranslator
{
    // support extra search syntax
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
        
        // path:*test*me* => path:/.*test.*me.*/
        string replacedString = Regex.Replace(query, @"path:([\w\*\-_]+)", match =>
        {
            string filePattern = match.Groups[1].Value;
            string regexPattern = filePattern.Replace("*", ".*");
            return "path:/" + regexPattern + "/";
        });
        
        // ext:cs => path:*.cs
        replacedString = Regex.Replace(replacedString, @"ext:(\w+)", "path:*.$1");
        
        // file:*test*me* => path:**/*test*me*
        // file:test => path:**/test.**
        replacedString = Regex.Replace(replacedString, @"file:([\w\*\-_]+)", match =>
        {
            string filePattern = match.Groups[1].Value;
            if (!filePattern.EndsWith('*'))
            {
                filePattern += ".*";
            }
            return "path:**/" + filePattern;
        });
        
        
        // notest => NOT path:/.*test.*/
        if (replacedString.IndexOf("notest", StringComparison.Ordinal) > -1)
        {
            replacedString = Regex.Replace(replacedString, @"notest", "");
            replacedString += " NOT path:/.*test.*/";
        }
        
        return replacedString;
    }
}