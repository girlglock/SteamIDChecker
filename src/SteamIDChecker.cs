/*
Copyright (C) 2025 Dea Brcka

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.
This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.
You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using System.Net;
using System.Text;
using System.Xml.Linq;

partial class SteamIDChecker
{
    private static readonly HttpClient client = new();
    private static int requestCount = 0;
    private static DateTime lastRequestTime = DateTime.Now;
    private static readonly object lockObj = new();
    private static StreamWriter? logWriter;
    private static string? logFilePath;

    static async Task Main(string[] args)
    {
        try
        {
            bool checkThreeLetterCombinations = args.Length > 0 && args[0] == "-3";

            string executableDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string letterCount = checkThreeLetterCombinations ? "3letter" : "2letter";
            logFilePath = Path.Combine(executableDir, $"steam_ids_{letterCount}_{timestamp}.txt");

            logWriter = new StreamWriter(logFilePath, false, Encoding.UTF8);
            await logWriter.WriteLineAsync($"Steam ID Checker Results - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            await logWriter.WriteLineAsync($"Checking {(checkThreeLetterCombinations ? "3 letter" : "2 letter")} combinations");
            await logWriter.WriteLineAsync("=" + new string('=', 50));
            await logWriter.FlushAsync();

            var combinations = GenerateCombinations(checkThreeLetterCombinations);
            int totalCombinations = checkThreeLetterCombinations ? 46656 : 1296;

            Console.WriteLine($"Steam ID Checker - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Checking {(checkThreeLetterCombinations ? "3 letter" : "2 letter")} Steam IDs...");
            Console.WriteLine($"Total combinations to check: {totalCombinations:N0}");
            Console.WriteLine($"Results will be saved to: {logFilePath}");
            Console.WriteLine("Press Ctrl+C to stop at any time.");
            Console.WriteLine("Starting check...\n");

            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            client.Timeout = TimeSpan.FromSeconds(30);

            int checkedCount = 0;
            int free = 0;
            int taken = 0;
            var startTime = DateTime.Now;

            foreach (var id in combinations)
            {
                try
                {
                    var result = await CheckSteamID(id);
                    checkedCount++;

                    string status = result.IsFree ? "Free" : "Taken";
                    string colorStatus = result.IsFree ? "\u001b[32mFree\u001b[0m" : "\u001b[91mTaken\u001b[0m";

                    Console.WriteLine($"{id} - {colorStatus}");

                    await logWriter.WriteLineAsync($"{id} - {status}");

                    if (result.IsFree)
                    {
                        free++;
                        Console.WriteLine($"  -> FREE ID FOUND: {id}");
                    }
                    else taken++;

                    if (checkedCount % 10 == 0)
                    {
                        await logWriter.FlushAsync();
                    }

                    if (checkedCount % 50 == 0)
                    {
                        var elapsed = DateTime.Now - startTime;
                        var rate = checkedCount / elapsed.TotalMinutes;
                        var remaining = totalCombinations - checkedCount;
                        var eta = remaining / rate;

                        string progressLine = $"--- Progress: {checkedCount:N0}/{totalCombinations:N0} ({checkedCount * 100.0 / totalCombinations:F1}%) ---";
                        string statsLine = $"Free: {free:N0} | Taken: {taken:N0} | Rate: {rate:F1}/min | ETA: {eta:F0} min";

                        Console.WriteLine($"\n{progressLine}");
                        Console.WriteLine($"{statsLine}\n");

                        await logWriter.WriteLineAsync($"\n{progressLine}");
                        await logWriter.WriteLineAsync($"{statsLine}\n");
                        await logWriter.FlushAsync();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{id} - \u001b[93mError: {ex.Message}\u001b[0m");
                    await logWriter.WriteLineAsync($"{id} - Error: {ex.Message}");
                }
            }

            var totalTime = DateTime.Now - startTime;
            string completionSummary = $@"
=== COMPLETED ===
Total checked: {checkedCount:N0}
Free IDs found: {free:N0}
Taken IDs: {taken:N0}
Total time: {totalTime:hh\:mm\:ss}
Average rate: {checkedCount / totalTime.TotalMinutes:F1} checks/minute
Completed at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

            Console.WriteLine(completionSummary);
            await logWriter.WriteLineAsync(completionSummary);
            await logWriter.FlushAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error: {ex.Message}");
            if (logWriter != null)
            {
                await logWriter.WriteLineAsync($"Fatal error: {ex.Message}");
            }
        }
        finally
        {
            if (logWriter != null)
            {
                await logWriter.FlushAsync();
                logWriter.Close();
                logWriter.Dispose();
            }
            client?.Dispose();
            Console.WriteLine($"\nResults saved to: {logFilePath}");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }

    static async Task<(bool IsFree, string Response)> CheckSteamID(string id)
    {
        const int maxRetries = 5;
        int retryCount = 0;

        while (retryCount < maxRetries)
        {
            try
            {
                await EnforceRateLimit();

                string url = $"https://steamcommunity.com/id/{id}/?xml=1";

                lock (lockObj)
                {
                    requestCount++;
                    lastRequestTime = DateTime.Now;
                }

                var response = await client.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();

                if (IsRateLimited(response, content))
                {
                    var waitTime = GetWaitTimeFromHeaders(response) ?? TimeSpan.FromMinutes(1);
                    Console.WriteLine($"\u001b[93mRate limited (HTTP {(int)response.StatusCode})! Waiting {waitTime.TotalSeconds:F0} seconds...\u001b[0m");
                    await Task.Delay(waitTime);
                    retryCount++;
                    continue;
                }

                if (!response.IsSuccessStatusCode && !IsRateLimited(response, content))
                {
                    if (retryCount < maxRetries - 1)
                    {
                        Console.WriteLine($"\u001b[93mHTTP {(int)response.StatusCode} for {id}, retrying...\u001b[0m");
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)));
                        retryCount++;
                        continue;
                    }
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                }

                bool isFree = IsProfileNotFound(content);
                return (isFree, content);
            }
            catch (TaskCanceledException)
            {
                if (retryCount < maxRetries - 1)
                {
                    Console.WriteLine($"\u001b[93mTimeout for {id}, retrying...\u001b[0m");
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)));
                    retryCount++;
                    continue;
                }
                throw new TimeoutException($"Request timed out for {id}");
            }
            catch (HttpRequestException ex)
            {
                if (retryCount < maxRetries - 1)
                {
                    Console.WriteLine($"\u001b[93mNetwork error for {id}, retrying...\u001b[0m");
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)));
                    retryCount++;
                    continue;
                }
                throw;
            }
        }

        throw new Exception($"Failed to check {id} after {maxRetries} attempts");
    }

    static async Task EnforceRateLimit()
    {
        lock (lockObj)
        {
            var timeSinceLastRequest = DateTime.Now - lastRequestTime;
            var minInterval = TimeSpan.FromMilliseconds(250);

            if (timeSinceLastRequest < minInterval)
            {
                var waitTime = minInterval - timeSinceLastRequest;
                Task.Delay(waitTime).Wait();
            }
        }

        if (requestCount > 100)
        {
            Console.WriteLine("\u001b[96mTaking a longer break to be nice to valve servers...\u001b[0m");
            await Task.Delay(TimeSpan.FromSeconds(15));
            lock (lockObj)
            {
                requestCount = 0;
            }
        }
    }

    static bool IsRateLimited(HttpResponseMessage response, string content)
    {
        var rateLimitStatusCodes = new[]
        {
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.NotFound,
            HttpStatusCode.Forbidden,
            HttpStatusCode.MethodNotAllowed,
            HttpStatusCode.BadGateway,
            HttpStatusCode.GatewayTimeout
        };

        if (rateLimitStatusCodes.Contains(response.StatusCode))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(content))
        {
            var rateLimitKeywords = new[]
            {
                "rate limit",
                "too many requests",
                "temporarily unavailable",
                "service unavailable",
                "try again later",
                "throttled"
            };

            return rateLimitKeywords.Any(keyword =>
                content.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    static TimeSpan? GetWaitTimeFromHeaders(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter != null)
        {
            if (response.Headers.RetryAfter.Delta.HasValue)
                return response.Headers.RetryAfter.Delta.Value;

            if (response.Headers.RetryAfter.Date.HasValue)
                return response.Headers.RetryAfter.Date.Value - DateTimeOffset.Now;
        }

        return null;
    }

    static IEnumerable<string> GenerateCombinations(bool checkThreeLetterCombinations)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        if (checkThreeLetterCombinations)
        {
            foreach (char first in chars)
            {
                foreach (char second in chars)
                {
                    foreach (char third in chars)
                    {
                        yield return $"{first}{second}{third}";
                    }
                }
            }
        }
        else
        {
            foreach (char first in chars)
            {
                foreach (char second in chars)
                {
                    yield return $"{first}{second}";
                }
            }
        }
    }

    static bool IsProfileNotFound(string response)
    {
        try
        {
            var xml = XDocument.Parse(response);
            var errorElement = xml.Root?.Element("error");

            if (errorElement != null)
            {
                string errorText = errorElement.Value;
                return errorText.Contains("The specified profile could not be found") ||
                       errorText.Contains("No profile exists");
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
