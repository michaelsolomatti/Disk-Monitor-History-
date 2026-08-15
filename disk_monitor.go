// disk_monitor.go — Go версия

package main

import (
	"encoding/json"
	"flag"
	"fmt"
	"os"
	"time"

	"github.com/shirou/gopsutil/v3/disk"
)

type DiskSample struct {
	Timestamp string              `json:"timestamp"`
	Speeds    map[string]Speed    `json:"speeds"`
	Usage     map[string]float64  `json:"usage"`
}

type Speed struct {
	Read      float64 `json:"read"`
	Write     float64 `json:"write"`
	ReadIOPS  float64 `json:"read_iops"`
	WriteIOPS float64 `json:"write_iops"`
}

type DiskHistory struct {
	Interval  int          `json:"interval"`
	Duration  int          `json:"duration"`
	StartTime string       `json:"start_time"`
	EndTime   string       `json:"end_time"`
	Samples   []DiskSample `json:"samples"`
}

func main() {
	interval := flag.Int("interval", 2, "Интервал сбора (сек)")
	duration := flag.Int("duration", 60, "Длительность сбора (сек)")
	file := flag.String("file", "disk_history.json", "Файл для истории")
	view := flag.Bool("view", false, "Просмотреть сохранённую историю")
	flag.Parse()

	fmt.Println("\x1b[36m💾 Disk Monitor (History) (Go)\x1b[0m")

	if *view {
		loadHistory(*file)
		return
	}

	collect(*interval, *duration, *file)
}

func collect(interval, duration int, filename string) {
	fmt.Printf("Интервал: %d сек, Длительность: %d сек\n\n", interval, duration)

	totalSamples := duration / interval
	fmt.Printf("📊 Мониторинг дисков...\n")

	prev, _ := disk.IOCounters()
	time.Sleep(time.Duration(interval) * time.Second)

	var history []DiskSample

	for i := 0; i < totalSamples; i++ {
		curr, _ := disk.IOCounters()
		speeds := calculateSpeeds(prev, curr, interval)
		usage := getDiskUsage()

		sample := DiskSample{
			Timestamp: time.Now().Format(time.RFC3339),
			Speeds:    speeds,
			Usage:     usage,
		}
		history = append(history, sample)

		// Прогресс
		percent := float64(i+1) / float64(totalSamples) * 100
		fmt.Printf("\r⏳ Прогресс: %.1f%% (%d/%d)", percent, i+1, totalSamples)

		prev = curr
		time.Sleep(time.Duration(interval) * time.Second)
	}
	fmt.Println()

	data := DiskHistory{
		Interval:  interval,
		Duration:  duration,
		StartTime: history[0].Timestamp,
		EndTime:   history[len(history)-1].Timestamp,
		Samples:   history,
	}

	jsonData, _ := json.MarshalIndent(data, "", "  ")
	os.WriteFile(filename, jsonData, 0644)
	fmt.Printf("\x1b[32m💾 История сохранена: %s\x1b[0m\n", filename)

	printStats(history)
}

func calculateSpeeds(prev, curr map[string]disk.IOCountersStat, interval int) map[string]Speed {
	speeds := make(map[string]Speed)
	for diskName, currStats := range curr {
		if prevStats, ok := prev[diskName]; ok {
			readMB := float64(currStats.ReadBytes-prevStats.ReadBytes) / (1024 * 1024)
			writeMB := float64(currStats.WriteBytes-prevStats.WriteBytes) / (1024 * 1024)
			readIOPS := float64(currStats.ReadCount-prevStats.ReadCount) / float64(interval)
			writeIOPS := float64(currStats.WriteCount-prevStats.WriteCount) / float64(interval)

			speeds[diskName] = Speed{
				Read:      readMB / float64(interval),
				Write:     writeMB / float64(interval),
				ReadIOPS:  readIOPS,
				WriteIOPS: writeIOPS,
			}
		}
	}
	return speeds
}

func getDiskUsage() map[string]float64 {
	usage := make(map[string]float64)
	parts, _ := disk.Partitions(false)
	for _, p := range parts {
		stat, _ := disk.Usage(p.Mountpoint)
		if stat != nil {
			usage[p.Device] = stat.UsedPercent
		}
	}
	return usage
}

func printStats(history []DiskSample) {
	if len(history) == 0 {
		return
	}

	stats := make(map[string]struct {
		read  []float64
		write []float64
	})

	for _, sample := range history {
		for disk, speed := range sample.Speeds {
			if _, ok := stats[disk]; !ok {
				stats[disk] = struct{ read []float64; write []float64 }{[]float64{}, []float64{}}
			}
			s := stats[disk]
			s.read = append(s.read, speed.Read)
			s.write = append(s.write, speed.Write)
			stats[disk] = s
		}
	}

	fmt.Println("\n\x1b[36m📈 Статистика за период:\x1b[0m")
	fmt.Println("┌" + "─"*80 + "┐")
	fmt.Printf("│ %-10s %-15s %-12s %-12s %-15s │\n", "Диск", "Среднее (МБ/с)", "Макс (МБ/с)", "Мин (МБ/с)", "IOPS")
	fmt.Println("├" + "─"*80 + "┤")

	for disk, s := range stats {
		if len(s.read) > 0 {
			var avgRead, avgWrite float64
			maxRead, minRead := s.read[0], s.read[0]
			for _, v := range s.read {
				avgRead += v
				if v > maxRead {
					maxRead = v
				}
				if v < minRead {
					minRead = v
				}
			}
			avgRead /= float64(len(s.read))
			// IOPS (упрощённо)
			var avgIOPS float64
			for _, v := range s.read {
				avgIOPS += v
			}
			avgIOPS /= float64(len(s.read))

			fmt.Printf("│ %-10s %12.1f %12.1f %12.1f %12.0f  │\n",
				disk, avgRead, maxRead, minRead, avgIOPS)
		}
	}
	fmt.Println("└" + "─"*80 + "┘")
}

func loadHistory(filename string) {
	data, err := os.ReadFile(filename)
	if err != nil {
		fmt.Printf("\x1b[31m❌ Файл %s не найден.\x1b[0m\n", filename)
		return
	}

	var history DiskHistory
	json.Unmarshal(data, &history)

	fmt.Printf("\x1b[36m📂 Загружена история из %s\x1b[0m\n", filename)
	fmt.Printf("  Интервал: %d сек\n", history.Interval)
	fmt.Printf("  Начало: %s\n", history.StartTime)
	fmt.Printf("  Конец: %s\n", history.EndTime)
	fmt.Printf("  Сэмплов: %d\n", len(history.Samples))

	printStats(history.Samples)
}
