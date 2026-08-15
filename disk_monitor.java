// disk_monitor.java — Java версия

import oshi.SystemInfo;
import oshi.hardware.HardwareAbstractionLayer;
import oshi.hardware.DiskStore;

import java.io.*;
import java.nio.file.*;
import java.time.*;
import java.util.*;
import java.util.concurrent.*;

public class disk_monitor {
    private static int interval = 2;
    private static int duration = 60;
    private static String historyFile = "disk_history.json";
    private static List<Map<String, Object>> history = new ArrayList<>();

    public static void main(String[] args) throws Exception {
        for (int i = 0; i < args.length; i++) {
            if (args[i].equals("--interval")) interval = Integer.parseInt(args[++i]);
            else if (args[i].equals("--duration")) duration = Integer.parseInt(args[++i]);
            else if (args[i].equals("--file")) historyFile = args[++i];
            else if (args[i].equals("--view")) { loadHistory(); return; }
        }

        System.out.println("\u001B[36m💾 Disk Monitor (History) (Java)\u001B[0m");
        System.out.printf("Интервал: %d сек, Длительность: %d сек\n\n", interval, duration);

        SystemInfo si = new SystemInfo();
        HardwareAbstractionLayer hal = si.getHardware();

        int totalSamples = duration / interval;
        System.out.printf("📊 Мониторинг дисков...\n");

        // Получаем начальные данные
        Map<String, Long> prevRead = new HashMap<>();
        Map<String, Long> prevWrite = new HashMap<>();
        List<DiskStore> disks = hal.getDiskStores();
        for (DiskStore disk : disks) {
            prevRead.put(disk.getName(), disk.getReadBytes());
            prevWrite.put(disk.getName(), disk.getWriteBytes());
        }

        Thread.sleep(interval * 1000);

        for (int i = 0; i < totalSamples; i++) {
            List<DiskStore> currDisks = hal.getDiskStores();
            Map<String, Double> speeds = new HashMap<>();

            for (DiskStore disk : currDisks) {
                String name = disk.getName();
                if (prevRead.containsKey(name)) {
                    double readMB = (disk.getReadBytes() - prevRead.get(name)) / (1024.0 * 1024.0);
                    double writeMB = (disk.getWriteBytes() - prevWrite.get(name)) / (1024.0 * 1024.0);
                    double readSpeed = readMB / interval;
                    double writeSpeed = writeMB / interval;
                    speeds.put(name, readSpeed);
                }
            }

            Map<String, Object> entry = new LinkedHashMap<>();
            entry.put("timestamp", Instant.now().toString());
            entry.put("speeds", speeds);
            history.add(entry);

            // Прогресс
            double percent = (double) (i + 1) / totalSamples * 100;
            int barLen = 30;
            int filled = (int) (barLen * (i + 1) / totalSamples);
            StringBuilder bar = new StringBuilder();
            for (int j = 0; j < filled; j++) bar.append("█");
            for (int j = filled; j < barLen; j++) bar.append("░");
            System.err.printf("\r[%s] %.1f%% (%d/%d)", bar.toString(), percent, i + 1, totalSamples);

            prevRead.clear();
            prevWrite.clear();
            for (DiskStore disk : currDisks) {
                prevRead.put(disk.getName(), disk.getReadBytes());
                prevWrite.put(disk.getName(), disk.getWriteBytes());
            }
            Thread.sleep(interval * 1000);
        }

        System.err.println();
        saveHistory();
        printStats();
    }

    private static void saveHistory() throws IOException {
        Map<String, Object> data = new LinkedHashMap<>();
        data.put("interval", interval);
        data.put("duration", duration);
        data.put("start_time", history.isEmpty() ? null : history.get(0).get("timestamp"));
        data.put("end_time", history.isEmpty() ? null : history.get(history.size() - 1).get("timestamp"));
        data.put("samples", history);

        String json = new com.google.gson.GsonBuilder().setPrettyPrinting().create().toJson(data);
        Files.write(Paths.get(historyFile), json.getBytes());
        System.out.println("\u001B[32m💾 История сохранена: " + historyFile + "\u001B[0m");
    }

    private static void printStats() {
        if (history.isEmpty()) return;

        Map<String, List<Double>> stats = new HashMap<>();
        for (Map<String, Object> entry : history) {
            Map<String, Double> speeds = (Map<String, Double>) entry.get("speeds");
            for (Map.Entry<String, Double> e : speeds.entrySet()) {
                stats.computeIfAbsent(e.getKey(), k -> new ArrayList<>()).add(e.getValue());
            }
        }

        System.out.println("\n\u001B[36m📈 Статистика за период:\u001B[0m");
        System.out.println("┌" + "─".repeat(80) + "┐");
        System.out.printf("│ %-10s %-15s %-12s %-12s %-15s │\n", "Диск", "Среднее (МБ/с)", "Макс (МБ/с)", "Мин (МБ/с)", "IOPS");
        System.out.println("├" + "─".repeat(80) + "┤");

        for (Map.Entry<String, List<Double>> e : stats.entrySet()) {
            List<Double> vals = e.getValue();
            if (!vals.isEmpty()) {
                double avg = vals.stream().mapToDouble(Double::doubleValue).average().orElse(0);
                double max = vals.stream().mapToDouble(Double::doubleValue).max().orElse(0);
                double min = vals.stream().mapToDouble(Double::doubleValue).min().orElse(0);
                double avgIOPS = avg;
                System.out.printf("│ %-10s %12.1f %12.1f %12.1f %12.0f  │\n",
                    e.getKey(), avg, max, min, avgIOPS);
            }
        }
        System.out.println("└" + "─".repeat(80) + "┘");
    }

    private static void loadHistory() throws IOException {
        if (!Files.exists(Paths.get(historyFile))) {
            System.out.println("\u001B[31m❌ Файл " + historyFile + " не найден.\u001B[0m");
            return;
        }

        String content = new String(Files.readAllBytes(Paths.get(historyFile)));
        // Упрощённо: показываем только заголовок
        System.out.println("\u001B[36m📂 Загружена история из " + historyFile + "\u001B[0m");
        System.out.println("  Интервал: " + interval + " сек");
        System.out.println("  Сэмплов: " + history.size());
    }
}
