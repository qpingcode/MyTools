using System.Diagnostics;
using System.IO;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Common.Config;
using MyTools.Common.Plugins;
using MyTools.Common.Utils;
using MyTools.Plugins.Param;
using SystemDirectory = System.IO.Directory;

namespace MyTools.Plugins;

public class FileSearcher(ILogger<FileSearcher> logger, IMemoryCache cache): PluginBase
{
    private const LuceneVersion LuceneVersion = Lucene.Net.Util.LuceneVersion.LUCENE_48;
    private const string IndexDir = "FileSearcherIndex";
    private IndexWriter? _indexWriter;
    private readonly object _indexLock = new();

    public override string Name => "File Searcher";
    public override string Description => "Search for files and scripts";
    public override List<IActionWithCommand> Actions => [
        WellKnownActions.Execute.WithDefaultCommand(), 
        WellKnownActions.AdminExecute.WithCommand(Commands.Ctrl_Enter), 
        WellKnownActions.OpenInExplorer.WithCommand(Commands.Ctrl_O)
    ];

    public override bool IsGlobalSearchPlugin => true;
    
    public override Task InitializeAsync()
    {
        InitIndex();
        return Task.CompletedTask;
    }

    private void InitIndex()
    {
        lock (_indexLock)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                var indexPath = Path.Combine(ConfigPath.Base, IndexDir);
                if (SystemDirectory.Exists(indexPath))
                {
                    SystemDirectory.Delete(indexPath, true);
                }

                SystemDirectory.CreateDirectory(indexPath);

                var indexDirectory = FSDirectory.Open(indexPath);
                var analyzer = new StandardAnalyzer(LuceneVersion);
                var config = new IndexWriterConfig(LuceneVersion, analyzer);
                _indexWriter = new IndexWriter(indexDirectory, config);

                foreach (var path in ProgramFilesPaths)
                {
                    if (SystemDirectory.Exists(path))
                    {
                        IndexDirectory(path, _indexWriter);
                    }
                }

                _indexWriter.Commit();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "index creation failed");
                throw;
            }
            finally
            {
                stopwatch.Stop();
                logger.LogInformation("index created，cost {costTime} ms", stopwatch.ElapsedMilliseconds);
            }
        }
    }

    private void IndexDirectory(string directoryPath, IndexWriter indexWriter)
    {
        try
        {
            var files = SystemDirectory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || 
                           f.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase));

            foreach (var file in files)
            {
                var fileName =  Path.GetFileNameWithoutExtension(file);
                var doc = new Document
                {
                    new StoredField("path", file),
                    new StoredField("filename", fileName),
                    new StringField("indexedTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Field.Store.YES),
                    new StringField("searchFilename", fileName.ToLower(), Field.Store.NO),
                    new StringField("searchInitials", StringUtils.GetInitialsFromWords(fileName), Field.Store.NO),
                    new TextField("searchPossibles", fileName, Field.Store.NO)
                };
                indexWriter.AddDocument(doc);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "index directory {directoryPath} failed", directoryPath);
        }
    }

    public override Task<Result> SearchAsync(string query, CancellationToken cancellationToken, SearchOptions? searchOptions = null)
    {
        try
        {
            using var reader = _indexWriter?.GetReader(true);
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader), "IndexReader is null");
            }

            query = query.ToLower();
            var searcher = new IndexSearcher(reader);
            PrefixQuery prefixQuery1 = new PrefixQuery(new Term("searchInitials", query));
            prefixQuery1.Boost = 10.0f; 
            PrefixQuery prefixQuery2 = new PrefixQuery(new Term("searchFilename", query));
            prefixQuery2.Boost = 2.0f;
            PrefixQuery prefixQuery3 = new PrefixQuery(new Term("searchPossibles", query));
            prefixQuery3.Boost = 2.0f;
            

            // var fuzzyQueries = new BooleanQuery();
            // var strings = query.Split(" ");
            // foreach (var s in strings)
            // {
            //     if (string.IsNullOrEmpty(s)) continue;
            //     var fuzzyQuery = new FuzzyQuery(new Term("searchFilenameForFuzzy", s), maxEdits: 2);
            //     fuzzyQueries.Add(fuzzyQuery, Occur.MUST);
            //     fuzzyQueries.Boost = 0.3f;
            // }
            
            var combineQuery = new BooleanQuery
            {
                { prefixQuery1, Occur.SHOULD },
                { prefixQuery2, Occur.SHOULD },
                { prefixQuery3, Occur.SHOULD },
            };
            
            var hits = searcher.Search(combineQuery, 30).ScoreDocs;
            var results = new List<ResultItem>();
            foreach (var hit in hits)
            {
                var doc = searcher.Doc(hit.Doc);
                var title = doc.Get("filename");
                var path = doc.Get("path");
                var score = (int)Math.Ceiling(hit.Score * 1000);
                
                results.Add(new ResultItem(GetFileIcon(path), title, path, ActionStringParam.From(path), score));
            }

            return Task.FromResult(Result.CreateSuccessResult(results));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.CreateFailure(ex.Message, ex));
        }
    }

    private Icon GetFileIcon(string path)
    {
        var icon = cache.GetOrCreate(PluginConstants.FileSearcherCachePrefix + path, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            var imageData = FileIconHelper.GetFileIconData(path);
            if (imageData != null)
            {
                return new ImageIcon(imageData);
            }

            return null;
        });
        
        if (icon == null) 
        {
            return new StringIcon("📄");
        }

        return icon;
    }

    private static readonly string[] ProgramFilesPaths =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        Path.Combine("C:\\Users", Environment.UserName, "OneDrive", "Custom Shortcuts", "Scripts"),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft",
            "Windows",
            "Start Menu",
            "Programs"
        ),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
    ];
}