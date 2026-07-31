using MyTools.Common.Utils;
using NUnit.Framework;

namespace MyTools.Common.Test;

public class StringUtilsTest
{
    [Test]
    public void TestGetInitialsFromWords()
    {
        Assert.That(StringUtils.GetInitialsFromWords("Hello World"), Is.EqualTo("hw"));
    }

    [Test]
    public void TestGetInitialsFromWords_Empty()
    {
        Assert.That(StringUtils.GetInitialsFromWords(""), Is.EqualTo(""));
        Assert.That(StringUtils.GetInitialsFromWords(" "), Is.EqualTo(""));
    }
    
    [Test]
    public void TestIsSubsequence()
    {
        Assert.That(StringUtils.IsSubsequence("dk", "DeepSeek"), Is.True);
    }
    
    [Test]
    public void TestGetAllSubsequences()
    {
        Assert.That(StringUtils.GetAllSubsequences("abc"), Is.EquivalentTo(new[] {  "b", "a","c", "ab", "bc", "ac", "abc" }));
    }
    
    [Test]
    public void TestGetAllSubsequences_Duplicate()
    {
        Assert.That(StringUtils.GetAllSubsequences("aabb"), Is.EquivalentTo(new[] { "a", "aa", "b", "ab", "aab", "bb", "abb", "aabb"}));
    }
    
    [Test]
    public void TestGetAllSubsequences_Case()
    {
        Assert.That(StringUtils.GetAllSubsequences("AB"), Is.EquivalentTo(new[] {  "a", "b", "ab"}));
    }
    
    [Test]
    public void TestGetMorePossibleInitialsFromWords_Empty()
    {
        Assert.That(StringUtils.GetMorePossibleInitialsFromWords(""), Is.EquivalentTo(Array.Empty<string>()));
    }
    
    [Test]
    public void TestGetMorePossibleInitialsFromWords_OnlyOneWord()
    {
        Assert.That(StringUtils.GetMorePossibleInitialsFromWords("Abc"), Is.EquivalentTo(new[] { "b", "a","c", "ab", "bc", "ac", "abc" }));
    }
    
    [Test]
    public void TestGetMorePossibleInitialsFromWords_OnlyOneWord_WithWhiteSpace()
    {
        Assert.That(StringUtils.GetMorePossibleInitialsFromWords(" Abc "), Is.EquivalentTo(new[] { "b", "a","c", "ab", "bc", "ac", "abc" }));
    }
    
    [Test]
    public void TestGetMorePossibleInitialsFromWords_MoreWord()
    {
        Assert.That(StringUtils.GetMorePossibleInitialsFromWords("Abc Hello Word"), Is.EquivalentTo(new[] { "bhw", "ahw","chw", "abhw", "bchw", "achw", "abchw" }));
    }
    
    [Test]
    public void TestGetMorePossibleInitialsFromWords_MoreWord_WithWhiteSpace()
    {
        Assert.That(StringUtils.GetMorePossibleInitialsFromWords(" Abc  Hello     Word     "), Is.EquivalentTo(new[] { "bhw", "ahw","chw", "abhw", "bchw", "achw", "abchw" }));
    }
    
    [Test]
    public void TestGetMorePossibleInitialsFromWords_CaseCamel()
    {
        Assert.That(StringUtils.GetMorePossibleInitialsFromWords("HelloWord"), Is.EquivalentTo(new[] { "hw", "ew", "hew", "lw", "hlw", "elw", "helw" }));
    }
    
    [Test]
    public void TestGetMorePossibleInitialsFromWords_CaseCamel_MoreWord()
    {
        Assert.That(StringUtils.GetMorePossibleInitialsFromWords("Hi Test MyWorld"), Is.EquivalentTo(new[] { "htmw", "itmw", "hitmw" }));
    }
    
    [Test]
    public void TestGetMorePossibleInitialsFromWords_CaseCamel_AllUpperCase()
    {
        Assert.That(StringUtils.GetMorePossibleInitialsFromWords("VERY IMPORTANT"), Is.EquivalentTo(new[] { "veryimportant" }));
    }
}