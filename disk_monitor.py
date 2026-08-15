

### 1. `disk_monitor.py` (Python)

```python
# disk_monitor.py — Python версия

import psutil
import time
import json
import csv
import os
import sys
from datetime import datetime, timedelta
from collections import defaultdict
from colorama import init, Fore, Style

init(autoreset=True)

class DiskMonitor:
    def __init__(self, interval=2, duration=60, history_file='disk_history.json'):
        self.interval = interval
        self.duration = duration
        self.history_file = history_file
        self.history = []
        self.disk_names = []

    def get_disk_io(self):
        """Получает текущую статистику дисков."""
        io = psutil.disk_io_counters(perdisk=True)
        result = {}
        for disk, counters in io.items():
            result[disk] = {
                'read_bytes': counters.read_bytes,
                'write_bytes': counters.write_bytes,
                'read_count': counters.read_count,
                'write_count': counters.write_count,
                'read_time': counters.read_time,
                'write_time': counters.write_time,
            }
        return result

    def calculate_speed(self, prev, curr, interval):
        """Вычисляет скорость в МБ/с и IOPS."""
        speeds = {}
        for disk in curr:
            if disk in prev:
                read_mb = (curr[disk]['read_bytes'] - prev[disk]['read_bytes']) / (1024 * 1024)
                write_mb = (curr[disk]['write_bytes'] - prev[disk]['write_bytes']) / (1024 * 1024)
                read_iops = (curr[disk]['read_count'] - prev[disk]['read_count']) / interval
                write_iops = (curr[disk]['write_count'] - prev[disk]['write_count']) / interval
                speeds[disk] = {
                    'read': read_mb / interval,
                    'write': write_mb / interval,
                    'read_iops': read_iops,
                    'write_iops': write_iops,
                }
        return speeds

    def get_disk_usage(self):
        """Получает использование дисков в процентах."""
        usage = {}
        for part in psutil.disk_partitions():
            try:
                usage[part.device] = psutil.disk_usage(part.mountpoint).percent
            except:
                pass
        return usage

    def collect(self):
        """Собирает данные в течение заданного периода."""
        print(f"{Fore.CYAN}💾 Disk Monitor (History) (Python)")
        print(f"Интервал: {self.interval} сек, Длительность: {self.duration} сек")
        print()

        total_samples = int(self.duration / self.interval)
        print(f"📊 Мониторинг дисков...")

        prev = self.get_disk_io()
        time.sleep(self.interval)

        for i in range(total_samples):
            curr = self.get_disk_io()
            speeds = self.calculate_speed(prev, curr, self.interval)
            usage = self.get_disk_usage()

            timestamp = datetime.now().isoformat()
            entry = {
                'timestamp': timestamp,
                'speeds': speeds,
                'usage': usage
            }
            self.history.append(entry)

            # Прогресс
            percent = (i + 1) / total_samples * 100
            bar_len = 30
            filled = int(bar_len * (i + 1) / total_samples)
            bar = '█' * filled + '░' * (bar_len - filled)
            sys.stderr.write(f"\r[{bar}] {percent:.1f}% ({i+1}/{total_samples})")
            sys.stderr.flush()

            prev = curr
            time.sleep(self.interval)

        sys.stderr.write("\n")
        self.save_history()
        self.print_stats()

    def save_history(self):
        """Сохраняет историю в JSON."""
        data = {
            'interval': self.interval,
            'duration': self.duration,
            'start_time': self.history[0]['timestamp'] if self.history else None,
            'end_time': self.history[-1]['timestamp'] if self.history else None,
            'samples': self.history
        }
        with open(self.history_file, 'w', encoding='utf-8') as f:
            json.dump(data, f, indent=2, ensure_ascii=False)
        print(f"\n{Fore.GREEN}💾 История сохранена: {self.history_file}")

    def print_stats(self):
        """Выводит статистику за период."""
        if not self.history:
            return

        # Собираем статистику по дискам
        disk_stats = defaultdict(lambda: {
            'read': [], 'write': [], 'read_iops': [], 'write_iops': []
        })

        for entry in self.history:
            for disk, speeds in entry['speeds'].items():
                disk_stats[disk]['read'].append(speeds['read'])
                disk_stats[disk]['write'].append(speeds['write'])
                disk_stats[disk]['read_iops'].append(speeds['read_iops'])
                disk_stats[disk]['write_iops'].append(speeds['write_iops'])

        print(f"\n{Fore.CYAN}📈 Статистика за период:{Style.RESET_ALL}")
        print("┌" + "─" * 80 + "┐")
        print(f"│ {'Диск':<10} {'Среднее (МБ/с)':<15} {'Макс (МБ/с)':<12} {'Мин (МБ/с)':<12} {'IOPS':<15} │")
        print("├" + "─" * 80 + "┤")

        for disk, stats in disk_stats.items():
            if stats['read']:
                avg_read = sum(stats['read']) / len(stats['read'])
                max_read = max(stats['read'])
                min_read = min(stats['read'])
                avg_iops = (sum(stats['read_iops']) + sum(stats['write_iops'])) / (len(stats['read_iops']) * 2)
                print(f"│ {disk:<10} {avg_read:>12.1f} {max_read:>12.1f} {min_read:>12.1f} {avg_iops:>12.0f}  │")

        print("└" + "─" * 80 + "┘")

    def load_history(self):
        """Загружает историю из файла для просмотра."""
        if not os.path.exists(self.history_file):
            print(Fore.RED + f"❌ Файл {self.history_file} не найден.")
            return

        with open(self.history_file, 'r', encoding='utf-8') as f:
            data = json.load(f)

        print(f"{Fore.CYAN}📂 Загружена история из {self.history_file}")
        print(f"  Интервал: {data.get('interval', 'N/A')} сек")
        print(f"  Начало: {data.get('start_time', 'N/A')}")
        print(f"  Конец: {data.get('end_time', 'N/A')}")
        print(f"  Сэмплов: {len(data.get('samples', []))}")

def main():
    import argparse
    parser = argparse.ArgumentParser(description='Disk Monitor (History)')
    parser.add_argument('--interval', type=int, default=2, help='Интервал сбора (сек)')
    parser.add_argument('--duration', type=int, default=60, help='Длительность сбора (сек)')
    parser.add_argument('--file', default='disk_history.json', help='Файл для истории')
    parser.add_argument('--view', action='store_true', help='Просмотреть сохранённую историю')
    args = parser.parse_args()

    monitor = DiskMonitor(args.interval, args.duration, args.file)

    if args.view:
        monitor.load_history()
    else:
        monitor.collect()

if __name__ == "__main__":
    main()
