<?php
// disk_monitor.php — PHP версия

class DiskMonitor {
    private $interval;
    private $duration;
    private $historyFile;
    private $history = [];

    public function __construct($interval = 2, $duration = 60, $historyFile = 'disk_history.json') {
        $this->interval = $interval;
        $this->duration = $duration;
        $this->historyFile = $historyFile;
    }

    private function getDiskIO() {
        $result = [];
        if (strtoupper(substr(PHP_OS, 0, 3)) === 'WIN') {
            // Windows
            $result['C:'] = ['read_bytes' => 0, 'write_bytes' => 0];
        } else {
            // Linux/macOS: используем iostat
            $output = shell_exec('iostat -d 1 2 2>/dev/null');
            $lines = explode("\n", $output);
            foreach ($lines as $line) {
                $parts = preg_split('/\s+/', trim($line));
                if (count($parts) >= 7 && is_numeric($parts[5])) {
                    $disk = $parts[0];
                    $result[$disk] = [
                        'read_bytes' => (int)($parts[5] * 1024),
                        'write_bytes' => (int)($parts[6] * 1024)
                    ];
                }
            }
        }
        return $result;
    }

    private function calculateSpeeds($prev, $curr) {
        $speeds = [];
        foreach ($curr as $disk => $data) {
            if (isset($prev[$disk])) {
                $readMB = ($data['read_bytes'] - $prev[$disk]['read_bytes']) / (1024 * 1024);
                $speeds[$disk] = $readMB / $this->interval;
            }
        }
        return $speeds;
    }

    public function collect() {
        echo "\033[36m💾 Disk Monitor (History) (PHP)\033[0m\n";
        echo "Интервал: {$this->interval} сек, Длительность: {$this->duration} сек\n\n";

        $totalSamples = (int)($this->duration / $this->interval);
        echo "📊 Мониторинг дисков...\n";

        $prev = $this->getDiskIO();
        sleep($this->interval);

        for ($i = 0; $i < $totalSamples; $i++) {
            $curr = $this->getDiskIO();
            $speeds = $this->calculateSpeeds($prev, $curr);

            $entry = [
                'timestamp' => date('c'),
                'speeds' => $speeds
            ];
            $this->history[] = $entry;

            // Прогресс
            $percent = ($i + 1) / $totalSamples * 100;
            $barLen = 30;
            $filled = (int)($barLen * ($i + 1) / $totalSamples);
            $bar = str_repeat('█', $filled) . str_repeat('░', $barLen - $filled);
            fprintf(STDERR, "\r[%s] %.1f%% (%d/%d)", $bar, $percent, $i + 1, $totalSamples);

            $prev = $curr;
            sleep($this->interval);
        }

        fprintf(STDERR, "\n");
        $this->saveHistory();
        $this->printStats();
    }

    private function saveHistory() {
        $data = [
            'interval' => $this->interval,
            'duration' => $this->duration,
            'start_time' => $this->history[0]['timestamp'] ?? null,
            'end_time' => $this->history[count($this->history)-1]['timestamp'] ?? null,
            'samples' => $this->history
        ];
        file_put_contents($this->historyFile, json_encode($data, JSON_PRETTY_PRINT));
        echo "\033[32m💾 История сохранена: {$this->historyFile}\033[0m\n";
    }

    private function printStats() {
        if (empty($this->history)) return;

        $stats = [];
        foreach ($this->history as $entry) {
            foreach ($entry['speeds'] as $disk => $speed) {
                if (!isset($stats[$disk])) $stats[$disk] = [];
                $stats[$disk][] = $speed;
            }
        }

        echo "\n\033[36m📈 Статистика за период:\033[0m\n";
        echo "┌" . str_repeat("─", 80) . "┐\n";
        echo "│ " . str_pad("Диск", 10) . " " . str_pad("Среднее (МБ/с)", 15) . " " . str_pad("Макс (МБ/с)", 12) . " " . str_pad("Мин (МБ/с)", 12) . " " . str_pad("IOPS", 15) . " │\n";
        echo "├" . str_repeat("─", 80) . "┤\n";

        foreach ($stats as $disk => $speeds) {
            if (!empty($speeds)) {
                $avg = array_sum($speeds) / count($speeds);
                $max = max($speeds);
                $min = min($speeds);
                $avgIOPS = $avg;
                echo "│ " . str_pad($disk, 10) . " " . str_pad(number_format($avg, 1), 15, " ", STR_PAD_LEFT) . " " . str_pad(number_format($max, 1), 12, " ", STR_PAD_LEFT) . " " . str_pad(number_format($min, 1), 12, " ", STR_PAD_LEFT) . " " . str_pad(number_format($avgIOPS, 0), 15, " ", STR_PAD_LEFT) . " │\n";
            }
        }

        echo "└" . str_repeat("─", 80) . "┘\n";
    }

    private function loadHistory() {
        if (!file_exists($this->historyFile)) {
            echo "\033[31m❌ Файл {$this->historyFile} не найден.\033[0m\n";
            return;
        }

        $data = json_decode(file_get_contents($this->historyFile), true);
        echo "\033[36m📂 Загружена история из {$this->historyFile}\033[0m\n";
        echo "  Интервал: {$data['interval']} сек\n";
        echo "  Начало: {$data['start_time']}\n";
        echo "  Конец: {$data['end_time']}\n";
        echo "  Сэмплов: " . count($data['samples']) . "\n";
    }
}

function main($argv) {
    $interval = 2;
    $duration = 60;
    $historyFile = 'disk_history.json';
    $view = false;

    for ($i = 1; $i < count($argv); $i++) {
        if ($argv[$i] == '--interval') $interval = (int)$argv[++$i];
        else if ($argv[$i] == '--duration') $duration = (int)$argv[++$i];
        else if ($argv[$i] == '--file') $historyFile = $argv[++$i];
        else if ($argv[$i] == '--view') $view = true;
    }

    $monitor = new DiskMonitor($interval, $duration, $historyFile);

    if ($view) {
        $monitor->loadHistory();
    } else {
        $monitor->collect();
    }
}

$argc = $_SERVER['argc'] ?? 0;
$argv = $_SERVER['argv'] ?? [];
main($argv);
?>
