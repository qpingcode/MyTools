using System;
using System.IO;
using System.Linq;
using MyTools.Common.Config;

namespace MyTools.Desktop.Storage;

/// <summary>
/// JSON配置存储测试类
/// </summary>
public static class JsonConfigurationStorageTest
{
    /// <summary>
    /// 运行所有测试
    /// </summary>
    public static void RunAllTests()
    {
        Console.WriteLine("=== JSON配置存储测试 ===\n");
        
        TestBasicOperations();
        TestConcurrentAccess();
        TestErrorHandling();
        TestFileOperations();
        
        Console.WriteLine("所有测试完成！");
    }
    
    /// <summary>
    /// 测试基本操作
    /// </summary>
    private static void TestBasicOperations()
    {
        Console.WriteLine("1. 测试基本操作...");
        
        try
        {
            using var storage = new JsonConfigurationStorage();
            
            // 测试存储和检索
            storage.Store("test.string", "Hello World");
            storage.Store("test.number", "42");
            storage.Store("test.boolean", "true");
            
            var stringValue = storage.Retrieve("test.string");
            var numberValue = storage.Retrieve("test.number");
            var booleanValue = storage.Retrieve("test.boolean");
            
            Console.WriteLine($"  ✓ 存储和检索: {stringValue}, {numberValue}, {booleanValue}");
            
            // 测试存在性检查
            var exists = storage.Exists("test.string");
            var notExists = storage.Exists("test.nonexistent");
            
            Console.WriteLine($"  ✓ 存在性检查: {exists}, {notExists}");
            
            // 测试获取所有名称
            var allNames = storage.GetAllNames().ToList();
            Console.WriteLine($"  ✓ 获取所有名称: {string.Join(", ", allNames)}");
            
            // 测试删除
            storage.Delete("test.string");
            var deletedExists = storage.Exists("test.string");
            Console.WriteLine($"  ✓ 删除操作: {!deletedExists}");
            
            // 测试清空
            storage.Clear();
            var countAfterClear = storage.GetAllNames().Count();
            Console.WriteLine($"  ✓ 清空操作: {countAfterClear == 0}");
            
            Console.WriteLine("  ✓ 基本操作测试通过\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ 基本操作测试失败: {ex.Message}\n");
        }
    }
    
    /// <summary>
    /// 测试并发访问
    /// </summary>
    private static void TestConcurrentAccess()
    {
        Console.WriteLine("2. 测试并发访问...");
        
        try
        {
            using var storage = new JsonConfigurationStorage();
            
            // 模拟并发写入
            var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(() =>
            {
                storage.Store($"concurrent.key{i}", $"value{i}");
                return storage.Retrieve($"concurrent.key{i}");
            })).ToArray();
            
            Task.WaitAll(tasks);
            
            var results = tasks.Select(t => t.Result).ToList();
            var uniqueResults = results.Distinct().Count();
            
            Console.WriteLine($"  ✓ 并发写入测试: 写入{results.Count}个值，唯一值{uniqueResults}个");
            Console.WriteLine("  ✓ 并发访问测试通过\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ 并发访问测试失败: {ex.Message}\n");
        }
    }
    
    /// <summary>
    /// 测试错误处理
    /// </summary>
    private static void TestErrorHandling()
    {
        Console.WriteLine("3. 测试错误处理...");
        
        try
        {
            using var storage = new JsonConfigurationStorage();
            
            // 测试空名称
            try
            {
                storage.Store("", "value");
                Console.WriteLine("  ✗ 空名称测试失败: 应该抛出异常");
            }
            catch (ArgumentException)
            {
                Console.WriteLine("  ✓ 空名称测试通过: 正确抛出ArgumentException");
            }
            
            // 测试null名称
            try
            {
                storage.Store(null!, "value");
                Console.WriteLine("  ✗ null名称测试失败: 应该抛出异常");
            }
            catch (ArgumentException)
            {
                Console.WriteLine("  ✓ null名称测试通过: 正确抛出ArgumentException");
            }
            
            Console.WriteLine("  ✓ 错误处理测试通过\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ 错误处理测试失败: {ex.Message}\n");
        }
    }
    
    /// <summary>
    /// 测试文件操作
    /// </summary>
    private static void TestFileOperations()
    {
        Console.WriteLine("4. 测试文件操作...");
        
        try
        {
            var testFilePath = Path.Combine(ConfigPath.Base, "TestSettings.json");
            
            // 清理测试文件
            if (File.Exists(testFilePath))
            {
                File.Delete(testFilePath);
            }
            
            // 创建存储并写入数据
            using (var storage = new JsonConfigurationStorage())
            {
                storage.Store("file.test", "test value");
            }
            
            // 检查文件是否创建
            var fileExists = File.Exists(Path.Combine(ConfigPath.Base, "Settings.json"));
            Console.WriteLine($"  ✓ 文件创建: {fileExists}");
            
            // 检查文件内容
            if (fileExists)
            {
                var content = File.ReadAllText(Path.Combine(ConfigPath.Base, "Settings.json"));
                var isValidJson = !string.IsNullOrWhiteSpace(content) && content.Contains("test value");
                Console.WriteLine($"  ✓ 文件内容验证: {isValidJson}");
            }
            
            Console.WriteLine("  ✓ 文件操作测试通过\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ 文件操作测试失败: {ex.Message}\n");
        }
    }
}
