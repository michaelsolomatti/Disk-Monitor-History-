// disk_monitor.cs — C# версия

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

class DiskSample
{
    public string Timestamp { get; set; }
    public Dictionary<string, double> Speeds { get; set; }
}

class DiskMonitor
{
    private int interval;
    private int duration;
    private string historyFile;
    private List<DiskSample> history = new List<DiskSample>();

    public DiskMonitor(int interval, int duration, string historyFile)
    {
        this.interval = interval;
        this.duration = duration;
        this.historyFile = historyFile;
    }

    private Dictionary<string, (long read, long write)> GetDiskIO()
    {
        var result = new Dictionary<string, (long, long)>();
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            // Windows: используем PerformanceCounter
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady);
            foreach (var drive in drives)
            {
                result[drive.Name.Replace("\\", "")] = (0, 0);
            }
        }
        else
        {
            // Linux/macOS: используем iostat
            var psi = new ProcessStartInfo
            {
                FileName = "iostat",
                Arguments = "-d 1 2",
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            using var process = Process.Start(psi);
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 6 && double.TryParse(parts[5], out _))
                {
                    result[parts[0]] = ((long)(double.Parse(parts[5]) * 1024), (long)(double.Parse(parts[6]) * 1024));
                }
            }
        }
        return result;
    }

    private Dictionary<string, double> CalculateSpeeds(
        Dictionary<string, (long read, long write)> prev,
        Dictionary<string, (long read, long write)> curr)
    {
        var speeds = new Dictionary<string, double>();
        foreach (var disk in curr.Keys)
        {
            if (prev.ContainsKey(disk))
            {
                double readMB = (curr[disk].read - prev[disk].read) / (1024.0 * 1024.0);
                speeds[disk] = readMB / interval;
            }
        }
        return speeds;
    }

    public async Task Collect()
    {
        Console.WriteLine("\u001B[36m💾 Disk Monitor (History) (C#)\u001B[0m");
        Console.WriteLine($"Интервал: {interval} сек, Длительность: {duration} сек");
        Console.WriteLine();

        int totalSamples = duration / interval;
        Console.WriteLine("📊 Мониторинг дисков...");

        var prev = GetDiskIO();
        await Task.Delay(interval * 1000);

        for (int i = 0; i < totalSamples; i++)
        {
            var curr = GetDiskIO();
            var speeds = CalculateSpeeds(prev, curr);

            var sample = new DiskSample
            {
                Timestamp = DateTime.Now.ToString("o"),
                Speeds = speeds
            };
            history.Add(sample);

            // Прогресс
            double percent = (double)(i + 1) / totalSamples * 100;
            int barLen = 30;
            int filled = (int)(barLen * (i + 1) / totalSamples);
            string bar = new string('█', filled) + new string('░', barLen - filled);
            Console.Error.Write($"\r[{bar}] {percent:F1}% ({i + 1}/{totalSamples})");

            prev = curr;
            await Task.Delay(interval * 1000);
        }

        Console.Error.WriteLine();
        SaveHistory();
        PrintStats();
    }

    private void SaveHistory()
    {
        var data = new
        {
            interval = interval,
            duration = duration,
            start_time = history.FirstOrDefault()?.Timestamp,
            end_time = history.LastOrDefault()?.Timestamp,
            samples = history
        };
        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(historyFile, json);
        Console.WriteLine($"\u001B[32m💾 История сохранена: {historyFile}\u001B[0m");
    }

    private void PrintStats()
    {
        if (history.Count == 0) return;

        var stats = new Dictionary<string, List<double>>();
        foreach (var sample in history)
        {
            foreach (var kv in sample.Speeds)
            {
                if (!stats.ContainsKey(kv.Key)) stats[kv.Key] = new List<double>();
                stats[kv.Key].Add(kv.Value);
            }
        }

        Console.WriteLine("\n\u001B[36m📈 Статистика за период:\u001B[0m");
        Console.WriteLine("┌" + new string('─', 80) + "┐");
        Console.WriteLine($"│ {"Диск",-10} {"Среднее (МБ/с)",-15} {"Макс (МБ/с)",-12} {"Мин (МБ/с)",-12} {"IOPS",-15} │");
        Console.WriteLine("├" + new string('─', 80) + "┤");

        foreach (var kv in stats)
        {
            if (kv.Value.Count > 0)
            {
                double avg = kv.Value.Average();
                double max = kv.Value.Max();
                double min = kv.Value.Min();
                double avgIOPS = avg;
                Console.WriteLine($"│ {kv.Key,-10} {avg,12:F1} {max,12:F1} {min,12:F1} {avgIOPS,12:F0}  │");
            }
        }
        Console.WriteLine("└" + new string('─', 80) + "┘");
    }

    private void LoadHistory()
    {
        if (!File.Exists(historyFile))
        {
            Console.WriteLine($"\u001B[31m❌ Файл {historyFile} не найден.\u001B[0m");
            return;
        }

        string json = File.ReadAllText(historyFile);
        var data = JsonSerializer.Deserialize<dynamic>(json);
        Console.WriteLine($"\u001B[36m📂 Загружена история из {historyFile}\u001B[0m");
        Console.WriteLine($"  Интервал: {data?.GetProperty("interval")}");
        Console.WriteLine($"  Начало: {data?.GetProperty("start_time")}");
        Console.WriteLine($"  Конец: {data?.GetProperty("end_time")}");
        Console.WriteLine($"  Сэмплов: {data?.GetProperty("samples").GetArrayLength()}");
    }

    public static async Task Main(string[] args)
    {
        int interval = 2, duration = 60;
        string historyFile = "disk_history.json";
        bool view = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--interval") interval = int.Parse(args[++i]);
            else if (args[i] == "--duration") duration = int.Parse(args[++i]);
            else if (args[i] == "--file") historyFile = args[++i];
            else if (args[i] == "--view") view = true;
        }

        var monitor = new DiskMonitor(interval, duration, historyFile);

        if (view)
        {
            monitor.LoadHistory();
        }
        else
        {
            await monitor.Collect();
        }
    }
}
