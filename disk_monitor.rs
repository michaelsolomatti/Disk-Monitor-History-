// disk_monitor.rs — Rust версия

use sysinfo::{System, SystemExt, DiskExt};
use std::fs;
use std::time::{Duration, Instant};
use std::collections::HashMap;
use serde::{Deserialize, Serialize};
use colored::*;

#[derive(Debug, Serialize, Deserialize, Clone)]
struct DiskSample {
    timestamp: String,
    speeds: HashMap<String, f64>,
}

#[derive(Debug, Serialize, Deserialize)]
struct DiskHistory {
    interval: u64,
    duration: u64,
    start_time: String,
    end_time: String,
    samples: Vec<DiskSample>,
}

struct DiskMonitor {
    interval: u64,
    duration: u64,
    history_file: String,
    history: Vec<DiskSample>,
}

impl DiskMonitor {
    fn new(interval: u64, duration: u64, history_file: &str) -> Self {
        DiskMonitor {
            interval,
            duration,
            history_file: history_file.to_string(),
            history: Vec::new(),
        }
    }

    fn get_disk_io(&self) -> HashMap<String, (u64, u64)> {
        let mut sys = System::new_all();
        sys.refresh_all();
        let mut result = HashMap::new();
        for disk in sys.disks() {
            let name = disk.name().to_str().unwrap_or("unknown").to_string();
            result.insert(name, (disk.total_read_bytes(), disk.total_written_bytes()));
        }
        result
    }

    fn calculate_speeds(&self, prev: &HashMap<String, (u64, u64)>, curr: &HashMap<String, (u64, u64)>) -> HashMap<String, f64> {
        let mut speeds = HashMap::new();
        for (disk, &(curr_read, curr_write)) in curr {
            if let Some(&(prev_read, prev_write)) = prev.get(disk) {
                let read_mb = (curr_read - prev_read) as f64 / (1024.0 * 1024.0);
                speeds.insert(disk.clone(), read_mb / self.interval as f64);
            }
        }
        speeds
    }

    fn collect(&mut self) {
        println!("{}", "💾 Disk Monitor (History) (Rust)".cyan());
        println!("Интервал: {} сек, Длительность: {} сек", self.interval, self.duration);
        println!();

        let total_samples = self.duration / self.interval;
        println!("📊 Мониторинг дисков...");

        let mut prev = self.get_disk_io();
        std::thread::sleep(Duration::from_secs(self.interval));

        let start_time = chrono::Local::now().to_rfc3339();

        for i in 0..total_samples {
            let curr = self.get_disk_io();
            let speeds = self.calculate_speeds(&prev, &curr);

            let sample = DiskSample {
                timestamp: chrono::Local::now().to_rfc3339(),
                speeds,
            };
            self.history.push(sample);

            // Прогресс
            let percent = (i + 1) as f64 / total_samples as f64 * 100.0;
            let bar_len = 30;
            let filled = (bar_len as f64 * (i + 1) as f64 / total_samples as f64) as usize;
            let bar = "█".repeat(filled) + &"░".repeat(bar_len - filled);
            eprint!("\r[{}] {:.1}% ({}/{})", bar, percent, i + 1, total_samples);

            prev = curr;
            std::thread::sleep(Duration::from_secs(self.interval));
        }

        eprintln!();
        self.save_history();
        self.print_stats();
    }

    fn save_history(&self) {
        let data = DiskHistory {
            interval: self.interval,
            duration: self.duration,
            start_time: self.history.first().map(|s| s.timestamp.clone()).unwrap_or_default(),
            end_time: self.history.last().map(|s| s.timestamp.clone()).unwrap_or_default(),
            samples: self.history.clone(),
        };
        let json = serde_json::to_string_pretty(&data).unwrap();
        fs::write(&self.history_file, json).unwrap();
        println!("{}", format!("💾 История сохранена: {}", self.history_file).green());
    }

    fn print_stats(&self) {
        if self.history.is_empty() {
            return;
        }

        let mut stats: HashMap<String, Vec<f64>> = HashMap::new();
        for sample in &self.history {
            for (disk, speed) in &sample.speeds {
                stats.entry(disk.clone()).or_insert_with(Vec::new).push(*speed);
            }
        }

        println!("\n{}", "📈 Статистика за период:".cyan());
        println!("┌{}┐", "─".repeat(80));
        println!("│ {:<10} {:<15} {:<12} {:<12} {:<15} │", "Диск", "Среднее (МБ/с)", "Макс (МБ/с)", "Мин (МБ/с)", "IOPS");
        println!("├{}┤", "─".repeat(80));

        for (disk, speeds) in stats {
            if !speeds.is_empty() {
                let avg = speeds.iter().sum::<f64>() / speeds.len() as f64;
                let max = *speeds.iter().max_by(|a, b| a.partial_cmp(b).unwrap()).unwrap();
                let min = *speeds.iter().min_by(|a, b| a.partial_cmp(b).unwrap()).unwrap();
                let avg_iops = avg;
                println!("│ {:<10} {:>12.1} {:>12.1} {:>12.1} {:>12.0}  │", disk, avg, max, min, avg_iops);
            }
        }
        println!("└{}┘", "─".repeat(80));
    }

    fn load_history(&self) {
        let content = fs::read_to_string(&self.history_file);
        if let Err(_) = content {
            println!("{}", format!("❌ Файл {} не найден.", self.history_file).red());
            return;
        }

        let data: DiskHistory = serde_json::from_str(&content.unwrap()).unwrap();
        println!("{}", format!("📂 Загружена история из {}", self.history_file).cyan());
        println!("  Интервал: {} сек", data.interval);
        println!("  Начало: {}", data.start_time);
        println!("  Конец: {}", data.end_time);
        println!("  Сэмплов: {}", data.samples.len());
    }
}

fn main() {
    let args: Vec<String> = std::env::args().collect();
    let mut interval = 2;
    let mut duration = 60;
    let mut history_file = "disk_history.json".to_string();
    let mut view = false;

    let mut i = 1;
    while i < args.len() {
        match args[i].as_str() {
            "--interval" => { interval = args[i+1].parse().unwrap_or(2); i += 2; }
            "--duration" => { duration = args[i+1].parse().unwrap_or(60); i += 2; }
            "--file" => { history_file = args[i+1].clone(); i += 2; }
            "--view" => { view = true; i += 1; }
            _ => { i += 1; }
        }
    }

    let mut monitor = DiskMonitor::new(interval, duration, &history_file);

    if view {
        monitor.load_history();
    } else {
        monitor.collect();
    }
}
