using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using NUnit.Framework;
using Directory = System.IO.Directory;

namespace MyTools.Plugins.Test;

public class LuceneTest
{
    [TestCase("", ExpectedResult = "")]
    [TestCase("The quick brown fox jumps over the lazy dog.", ExpectedResult = "quick brown fox jumps over lazy dog")]
    [TestCase("hi-boy", ExpectedResult = "hi boy")]
    public string TestStandardAnalyzer(string input)
    {
        using var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        using var tokenStream = analyzer.GetTokenStream("field", new StringReader(input));
        var termAttr = tokenStream.AddAttribute<ICharTermAttribute>();
        tokenStream.Reset();
        var result = new List<string>();
        while (tokenStream.IncrementToken())
        {
            result.Add(termAttr.ToString());
        }
        tokenStream.End();
        return string.Join(" ", result);
    }
    
    [TestCase("", ExpectedResult = "")]
    [TestCase("The quick brown fox jumps over the lazy dog.", ExpectedResult = "The quick brown fox jumps over the lazy dog.")]
    [TestCase("hi-boy", ExpectedResult = "hi-boy")]
    public string TestWhiteSpaceAnalyzer(string input)
    {
        using var analyzer = new WhitespaceAnalyzer(LuceneVersion.LUCENE_48);
        using var tokenStream = analyzer.GetTokenStream("field", new StringReader(input));
        var termAttr = tokenStream.AddAttribute<ICharTermAttribute>();
        tokenStream.Reset();
        var result = new List<string>();
        while (tokenStream.IncrementToken())
        {
            result.Add(termAttr.ToString());
        }
        tokenStream.End();
        return string.Join(" ", result);
    }
    
    [TestCase("ilspy", 1, ExpectedResult = 1)]
    [TestCase("ilsp", 1, ExpectedResult = 1)]
    [TestCase("lspy", 1, ExpectedResult = 1)]
    [TestCase("spy", 1, ExpectedResult = 0)]
    [TestCase("spy", 2, ExpectedResult = 1)]
    [TestCase("visual", 2, ExpectedResult = 1)]
    [TestCase("studio", 2, ExpectedResult = 1)]
    [TestCase("code", 2, ExpectedResult = 1)]
    [TestCase("tudio", 2, ExpectedResult = 1)]
    [TestCase("stio", 2, ExpectedResult = 1)]
    [TestCase("Visual Studio Code", 2, ExpectedResult = 0)]
    public int TestFuzzyQuery(string search, int maxEdits)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDirectory);
        
        var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        using var directory = FSDirectory.Open(tempDirectory);
        using var indexWriter = new IndexWriter(directory, new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer));

        AddDocument(indexWriter, "ilspy");
        AddDocument(indexWriter, "Visual Studio Code");
        indexWriter.Commit();

        using var indexReader = DirectoryReader.Open(directory);
        var indexSearcher = new IndexSearcher(indexReader);

        var query = new FuzzyQuery(new Term("content", search), maxEdits);
        var results = indexSearcher.Search(query, 10);
        return results.TotalHits;
    }

    static void AddDocument(IndexWriter writer, string content)
    {
        var doc = new Document();
        doc.Add(new TextField("content", content, Field.Store.YES));
        writer.AddDocument(doc);
    }
}