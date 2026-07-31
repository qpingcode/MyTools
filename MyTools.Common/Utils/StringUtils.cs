using System.Text;
using System.Text.RegularExpressions;

namespace MyTools.Common.Utils;

public static class StringUtils
{
    private static readonly char[] Delimiters = { ' ', '.', '/', '-', '\\', ',', '_', '<', '>', ':', '`', '"', '?', '*' };

    public static string[] GetTokens(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return [];
        
        return input.Split(Delimiters, StringSplitOptions.RemoveEmptyEntries);
    }
    
    /// <summary>
    /// Get the initials from words
    /// example: "Hello World C#" => "hwc"
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public static string GetInitialsFromWords(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        
        var words = input.Split(Delimiters, StringSplitOptions.RemoveEmptyEntries);
        var initials = new StringBuilder();
        foreach (var word in words)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                continue;
            }
            initials.Append(word[0]);
        }
        return initials.ToString().ToLower();
    }
    
    static Regex regex = new Regex(@"(?<!^)(?=[A-Z])", RegexOptions.Compiled);
    
    /// <summary>
    /// Get more possible initials from words
    /// for example: "Hello World C#" => ["HeWC", "HelWC", "HellWC", "HelloWC"]
    /// first word is more important
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public static IEnumerable<string> GetMorePossibleInitialsFromWords(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Enumerable.Empty<string>();
        }

        input = regex.Replace(input, " ");
        var words = input.Split(Delimiters, StringSplitOptions.RemoveEmptyEntries);
        var results = new HashSet<string>();
        var firstWord = words[0];
        var initialsFromSecond =  string.Join(string.Empty, words.Skip(1).Select(w => char.ToLower(w[0])));
        string[] subsequencesForFirstWord = GetAllSubsequences(firstWord.Length > 3 ? firstWord[..3] : firstWord);
        foreach (var s in subsequencesForFirstWord)
        {
            results.Add(s + initialsFromSecond);
        }

        return results;
    }

    public static string[] GetAllSubsequences(string word)
    {
        var result = new HashSet<string>();
        var n = word.Length;
        var total = 1 << n; // 2^n
        for (var i = 0; i < total; i++)
        {
            StringBuilder sb = new StringBuilder();
            for (var j = 0; j < n; j++)
            {
                if ((i & (1 << j)) != 0)
                {
                    sb.Append(char.ToLower(word[j]));
                }
            }
            if (sb.Length > 0)
            {
                result.Add(sb.ToString());
            }
        }
        return result.ToArray();
    }

    /// <summary>
    /// Determine if the pattern is a subsequence of the target
    /// </summary>
    /// <param name="pattern">e.g."AMD", s</param>
    /// <param name="target">e.g. "ABMCDDD"</param>
    /// <returns></returns>
    public static bool IsSubsequence(string pattern, string target)
    {
        if (string.IsNullOrEmpty(pattern)) return true; 
        if (string.IsNullOrEmpty(target)) return false;
        int patternIndex = 0;
        int targetIndex = 0;
    
        while (targetIndex < target.Length && patternIndex < pattern.Length)
        {
            if (CompareCharsIgnoreCase(target[targetIndex], pattern[patternIndex]))
            {
                patternIndex++; 
            }
            targetIndex++;
        }
    
        return patternIndex == pattern.Length;
    }
    
    static bool CompareCharsIgnoreCase(char c1, char c2)
    {
        return char.ToLower(c1) == char.ToLower(c2);
    }
}