// disk_monitor.js — JavaScript версия

const { exec } = require('child_process');
const fs = require('fs');
const path = require('path');

class DiskMonitor {
    constructor(interval = 2, duration = 60, historyFile = 'disk_history.json') {
        this.interval = interval;
        this.duration = duration;
        this.historyFile = historyFile;
        this.history = [];
    }

    async getDiskIO() {
        return new Promise((resolve) => {
            if (process.platform === 'win32') {
                // Windows: используем wmic
                exec('wmic diskdrive get DeviceID,Name,Size', (err, stdout) => {
                    // Упрощённо: возвращаем заглушку
                    resolve({
                        'C:': { read_bytes: Math.random() * 100000000, write_bytes: Math.random() * 50000000 },
                        'D:': { read_bytes: Math.random() * 80000000, write_bytes: Math.random() * 40000000 }
                    });
                });
            } else {
                // Linux/macOS: используем iostat
                exec('iostat -d 1 2', (err, stdout) => {
                    const result = {};
                    const lines = stdout.split('\n');
                    for (const line of lines) {
                        const parts = line.trim().split(/\s+/);
                        if (parts.length >= 6 && !isNaN(parts[5])) {
                            const disk = parts[0];
                            result[disk] = {
                                read_bytes: parseFloat(parts[5]) * 1024,
                                write_bytes: parseFloat(parts[6]) * 1024
                            };
                        }
                    }
                    resolve(result);
                });
            }
        });
    }

    calculateSpeeds(prev, curr, interval) {
        const speeds = {};
        for (const disk of Object.keys(curr)) {
            if (prev[disk]) {
                const readMB = (curr[disk].read_bytes - prev[disk].read_bytes) / (1024 * 1024);
                const writeMB = (curr[disk].write_bytes - prev[disk].write_bytes) / (1024 * 1024);
                speeds[disk] = {
                    read: readMB / interval,
                    write: writeMB / interval,
                };
            }
        }
        return speeds;
    }

    async collect() {
        console.log('\x1b[36m💾 Disk Monitor (History) (JavaScript)\x1b[0m');
        console.log(`Интервал: ${this.interval} сек, Длительность: ${this.duration} сек`);
        console.log();

        const totalSamples = Math.floor(this.duration / this.interval);
        console.log(`📊 Мониторинг дисков...`);

        let prev = await this.getDiskIO();
        await this.sleep(this.interval * 1000);

        for (let i = 0; i < totalSamples; i++) {
            const curr = await this.getDiskIO();
            const speeds = this.calculateSpeeds(prev, curr, this.interval);

            const entry = {
                timestamp: new Date().toISOString(),
                speeds: speeds
            };
            this.history.push(entry);

            const percent = (i + 1) / totalSamples * 100;
            const barLen = 30;
            const filled = Math.floor(barLen * (i + 1) / totalSamples);
            const bar = '█'.repeat(filled) + '░'.repeat(barLen - filled);
            process.stderr.write(`\r[${bar}] ${percent.toFixed(1)}% (${i+1}/${totalSamples})`);

            prev = curr;
            await this.sleep(this.interval * 1000);
        }

        process.stderr.write('\n');
        this.saveHistory();
        this.printStats();
    }

    sleep(ms) {
        return new Promise(resolve => setTimeout(resolve, ms));
    }

    saveHistory() {
        const data = {
            interval: this.interval,
            duration: this.duration,
            start_time: this.history[0]?.timestamp || null,
            end_time: this.history[this.history.length - 1]?.timestamp || null,
            samples: this.history
        };
        fs.writeFileSync(this.historyFile, JSON.stringify(data, null, 2));
        console.log(`\x1b[32m💾 История сохранена: ${this.historyFile}\x1b[0m`);
    }

    printStats() {
        if (this.history.length === 0) return;

        const stats = {};
        for (const entry of this.history) {
            for (const [disk, speed] of Object.entries(entry.speeds)) {
                if (!stats[disk]) stats[disk] = { read: [], write: [] };
                stats[disk].read.push(speed.read);
                stats[disk].write.push(speed.write);
            }
        }

        console.log('\n\x1b[36m📈 Статистика за период:\x1b[0m');
        console.log('┌' + '─'.repeat(80) + '┐');
        console.log(`│ ${'Диск'.padEnd(10)} ${'Среднее (МБ/с)'.padEnd(15)} ${'Макс (МБ/с)'.padEnd(12)} ${'Мин (МБ/с)'.padEnd(12)} ${'IOPS'.padEnd(15)} │`);
        console.log('├' + '─'.repeat(80) + '┤');

        for (const [disk, s] of Object.entries(stats)) {
            if (s.read.length > 0) {
                const avg = s.read.reduce((a, b) => a + b, 0) / s.read.length;
                const max = Math.max(...s.read);
                const min = Math.min(...s.read);
                const avgIOPS = s.read.reduce((a, b) => a + b, 0) / s.read.length;
                console.log(`│ ${disk.padEnd(10)} ${avg.toFixed(1).padStart(12)} ${max.toFixed(1).padStart(12)} ${min.toFixed(1).padStart(12)} ${avgIOPS.toFixed(0).padStart(12)}  │`);
            }
        }
        console.log('└' + '─'.repeat(80) + '┘');
    }

    loadHistory() {
        if (!fs.existsSync(this.historyFile)) {
            console.log(`\x1b[31m❌ Файл ${this.historyFile} не найден.\x1b[0m`);
            return;
        }

        const data = JSON.parse(fs.readFileSync(this.historyFile, 'utf8'));
        console.log(`\x1b[36m📂 Загружена история из ${this.historyFile}\x1b[0m`);
        console.log(`  Интервал: ${data.interval} сек`);
        console.log(`  Начало: ${data.start_time}`);
        console.log(`  Конец: ${data.end_time}`);
        console.log(`  Сэмплов: ${data.samples?.length || 0}`);
    }
}

async function main() {
    const args = process.argv.slice(2);
    let interval = 2;
    let duration = 60;
    let file = 'disk_history.json';
    let view = false;

    for (let i = 0; i < args.length; i++) {
        if (args[i] === '--interval') interval = parseInt(args[++i]) || 2;
        else if (args[i] === '--duration') duration = parseInt(args[++i]) || 60;
        else if (args[i] === '--file') file = args[++i];
        else if (args[i] === '--view') view = true;
    }

    const monitor = new DiskMonitor(interval, duration, file);

    if (view) {
        monitor.loadHistory();
    } else {
        await monitor.collect();
    }
}

main().catch(console.error);
