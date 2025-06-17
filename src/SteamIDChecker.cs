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

class SteamIDChecker
{
    private static readonly HttpClient client = new();
    private static int requestCount = 0;
    
    static async Task Main(string[] args)
    {
        int length = GetLength(args);
        var logFile = CreateLogFile(length);
        
        WriteColorLine($"Steam ID Checker - Checking {length} character combinations", ConsoleColor.Cyan);
        WriteColorLine($"Results saved to: {logFile}", ConsoleColor.Gray);

        client.DefaultRequestHeaders.Add("User-Agent", 
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        client.Timeout = TimeSpan.FromSeconds(10);

        int count = 0, free = 0;
        var start = DateTime.Now;

        using var writer = new StreamWriter(logFile, false, Encoding.UTF8);
        await writer.WriteLineAsync($"Steam ID Check Results - {DateTime.Now}");
        await writer.WriteLineAsync($"Length: {length} characters\n");

        foreach (var id in GenerateIds(length))
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

                if (count % 100 == 0)
                {
                    var elapsed = DateTime.Now - start;
                    var rate = count / elapsed.TotalMinutes;
                    WriteColorLine($"Progress: {count:N0} checked, {free} free, {rate:F0}/min\n", ConsoleColor.Yellow);
                    await writer.FlushAsync();
                }
            }
            catch (Exception ex)
            {
                WriteColor($"{id} - ", ConsoleColor.White);
                WriteColorLine($"ERROR: {ex.Message}", ConsoleColor.Magenta);
            }
        }

        var total = DateTime.Now - start;
        string summary = $"\nCompleted: {count:N0} checked, {free} free IDs found in {total:hh\\:mm\\:ss}";
        WriteColorLine(summary, ConsoleColor.Cyan);
        await writer.WriteLineAsync(summary);
    }

    static void WriteColor(string text, ConsoleColor color)
    {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = originalColor;
    }

    static void WriteColorLine(string text, ConsoleColor color)
    {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = originalColor;
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

    static string CreateLogFile(int length)
    {
        string dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        return Path.Combine(dir, $"steam_ids_{length}char_{timestamp}.txt");
    }

    static IEnumerable<string> GenerateIds(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_";
        return Generate("", length, chars);
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
        await RateLimit();

        var response = await client.GetAsync($"https://steamcommunity.com/id/{id}/?xml=1");
        var content = await response.Content.ReadAsStringAsync();

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            WriteColorLine("Rate limited - waiting 30s...", ConsoleColor.DarkYellow);
            await Task.Delay(30000);
            return await CheckId(id);
        }

        return IsNotFound(content);
    }

    static async Task RateLimit()
    {
        await Task.Delay(50);
        
        requestCount++;
        if (requestCount % 50 == 0)
        {
            WriteColorLine("Taking break...", ConsoleColor.DarkGray);
            await Task.Delay(5000);
        }
    }

    static bool IsNotFound(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var error = doc.Root?.Element("error")?.Value;
            return error?.Contains("could not be found") == true;
        }
        catch
        {
            return false;
        }
    }
}
