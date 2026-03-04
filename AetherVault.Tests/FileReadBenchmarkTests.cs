using System.Diagnostics;
using Xunit.Abstractions;

namespace AetherVault.Tests
{
    public class FileReadBenchmarkTests
    {
        private readonly ITestOutputHelper _output;

        public FileReadBenchmarkTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void BenchmarkFileReadMethods()
        {
            string testFilePath = Path.GetTempFileName();
            File.WriteAllText(testFilePath, "Benchmark data test.");

            int iterations = 10000;

            // 1. Existing method (Exists + Read)
            var sw1 = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                ReadExisting(testFilePath);
            }
            sw1.Stop();

            // 2. New method (Try-Catch)
            var sw2 = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                ReadOptimized(testFilePath);
            }
            sw2.Stop();

            _output.WriteLine($"Baseline (File.Exists): {sw1.ElapsedMilliseconds}ms");
            _output.WriteLine($"Optimized (Try-Catch): {sw2.ElapsedMilliseconds}ms");

            File.Delete(testFilePath);
        }

        private string ReadExisting(string path)
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        }

        private string ReadOptimized(string path)
        {
            try
            {
                return File.ReadAllText(path).Trim();
            }
            catch (IOException)
            {
                return string.Empty;
            }
            catch (UnauthorizedAccessException)
            {
                return string.Empty;
            }
        }
    }
}
