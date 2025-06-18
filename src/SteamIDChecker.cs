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

class SteamIDChecker
{
    private static readonly HttpClient client = new();
    private static int requestCount = 0;
    private static readonly object lockObject = new();
    private static DateTime lastRateLimitTime = DateTime.MinValue;
    private static readonly object rateLimitLock = new();

    static async Task Main(string[] args)
    {
        int length = GetLength(args);
        string startFrom = GetStartFrom(args);
        var logFile = CreateLogFile(length, startFrom);

        WriteColorLine($"steam ID Checker - Checking {length} combinations", ConsoleColor.Cyan);
        if (!string.IsNullOrEmpty(startFrom))
            WriteColorLine($"starting from: {startFrom}", ConsoleColor.Yellow);
        WriteColorLine($"results will be saved to: {logFile}", ConsoleColor.Gray);

        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        client.Timeout = TimeSpan.FromSeconds(15);

        int count = 0, free = 0;
        var start = DateTime.Now;

        using var writer = new StreamWriter(logFile, false, Encoding.UTF8);
        await writer.WriteLineAsync($"steam ID Check - {DateTime.Now}");
        await writer.WriteLineAsync($"length: {length} characters");
        if (!string.IsNullOrEmpty(startFrom))
            await writer.WriteLineAsync($"Starting from: {startFrom}");
        await writer.WriteLineAsync();

        var allIds = GenerateIds(length, startFrom).ToList();
        int totalIds = allIds.Count;

        foreach (var id in allIds)
        {
            try
            {
                bool isFree = await CheckId(id);
                count++;

                string status = isFree ? "FREE" : "TAKEN";
                ConsoleColor color = isFree ? ConsoleColor.Green : ConsoleColor.Red;

                WriteColor($"{id} - ", ConsoleColor.White);
                WriteColorLine(status, color);
                await writer.WriteLineAsync($"{id} - {status}");

                if (isFree) free++;
            }
            catch (Exception ex)
            {
                count++;
                WriteColor($"{id} - ", ConsoleColor.White);
                WriteColorLine($"ERROR: {ex.Message}", ConsoleColor.Magenta);
            }

            if (count % 100 == 0)
            {
                var elapsed = DateTime.Now - start;
                var rate = count / elapsed.TotalMinutes;
                var remaining = totalIds - count;
                var estimatedMinutesLeft = remaining / Math.Max(rate, 1);
                var estimatedTimeLeft = TimeSpan.FromMinutes(estimatedMinutesLeft);

                WriteColorLine($"progress: {count:N0}/{totalIds:N0} checked, {free} free, {rate:F0}/min, ETA: {estimatedTimeLeft:hh\\:mm\\:ss}\n", ConsoleColor.Yellow);
                await writer.FlushAsync();
            }
        }

        var total = DateTime.Now - start;
        string summary = $"\ncompleted: {count:N0} checked, {free} free IDs found in {total:hh\\:mm\\:ss}";
        WriteColorLine(summary, ConsoleColor.Cyan);
        await writer.WriteLineAsync(summary);
    }

    static void WriteColor(string text, ConsoleColor color)
    {
        lock (lockObject)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ForegroundColor = originalColor;
        }
    }

    static void WriteColorLine(string text, ConsoleColor color)
    {
        lock (lockObject)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = originalColor;
        }
    }

    static int GetLength(string[] args)
    {
        if (args.Length > 0 && args[0].StartsWith("-") &&
            int.TryParse(args[0][1..], out int length) &&
            length >= 1 && length <= 8)
        {
            return length;
        }
        return 2;
    }

    static string GetStartFrom(string[] args)
    {
        if (args.Length > 1)
        {
            return args[1].ToUpper();
        }
        return string.Empty;
    }

    static string CreateLogFile(int length, string startFrom)
    {
        string dir = AppContext.BaseDirectory;
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string startSuffix = string.IsNullOrEmpty(startFrom) ? "" : $"_from_{startFrom}";
        return Path.Combine(dir, $"steam_ids_{length}char{startSuffix}_{timestamp}.txt");
    }

    static IEnumerable<string> GenerateIds(int length, string startFrom = "")
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_";
        var allIds = Generate("", length, chars);
        
        if (string.IsNullOrEmpty(startFrom))
            return allIds;
        
        return allIds.SkipWhile(id => string.Compare(id, startFrom, StringComparison.OrdinalIgnoreCase) < 0);
    }

    static IEnumerable<string> Generate(string prefix, int remaining, string chars)
    {
        if (remaining == 0)
            yield return prefix;
        else
            foreach (char c in chars)
                foreach (string id in Generate(prefix + c, remaining - 1, chars))
                    yield return id;
    }

    static async Task<bool> CheckId(string id)
    {
        TimeSpan rateLimitWaitTime;
        bool needToWait = false;
        
        lock (rateLimitLock)
        {
            var timeSinceLastRateLimit = DateTime.Now - lastRateLimitTime;
            if (timeSinceLastRateLimit.TotalSeconds < 35)
            {
                rateLimitWaitTime = TimeSpan.FromSeconds(35) - timeSinceLastRateLimit;
                if (rateLimitWaitTime.TotalMilliseconds > 0)
                {
                    needToWait = true;
                }
            }
            else
            {
                rateLimitWaitTime = TimeSpan.Zero;
            }
        }
        
        if (needToWait)
        {
            await ShowCountdownAsync("waiting for rate limit", rateLimitWaitTime);
        }

        await RateLimit();

        int attempt = 0;
        while (true)
        {
            var response = await client.GetAsync($"https://steamcommunity.com/id/{id}/?xml=1");
            
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                lock (rateLimitLock)
                {
                    lastRateLimitTime = DateTime.Now;
                }
                
                attempt++;
                TimeSpan waitTime = GetRetryDelay(attempt);
                
                WriteColorLine($"rate limited #{attempt} - waiting {waitTime.TotalMinutes:F0}m {waitTime.Seconds}s...", ConsoleColor.DarkYellow);
                await ShowCountdownAsync($"Rate limited (#{attempt})", waitTime);
                
                continue;
            }

            var content = await response.Content.ReadAsStringAsync();

            return IsNotFound(content);
        }
    }

    static async Task RateLimit()
    {
        await Task.Delay(1);

        lock (lockObject)
        {
            requestCount++;
        }
    }

    static TimeSpan GetRetryDelay(int attempt)
    {
        return attempt switch
        {
            1 => TimeSpan.FromSeconds(30),
            2 => TimeSpan.FromMinutes(3),
            3 => TimeSpan.FromMinutes(5),
            _ => TimeSpan.FromMinutes(10)
        };
    }

    static async Task ShowCountdownAsync(string message, TimeSpan duration)
    {
        var endTime = DateTime.Now.Add(duration);
        
        while (DateTime.Now < endTime)
        {
            var remaining = endTime - DateTime.Now;
            if (remaining.TotalSeconds <= 0) break;
            
            lock (lockObject)
            {
                Console.Write($"\r{message} - resuming in: {remaining.TotalSeconds:F0}s");
            }
            
            await Task.Delay(1000);
        }
        
        lock (lockObject)
        {
            Console.Write($"\r{new string(' ', Console.WindowWidth - 1)}\r");
        }
    }

    static bool IsNotFound(string xml)
    {
        if (xml.Contains("The specified profile could not be found"))
            return true;
        
        if (xml.Contains("Failed loading profile data") || xml.Contains("<steamID64>"))
            return false;
        
        return true;
    }
}