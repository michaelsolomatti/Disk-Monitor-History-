# disk_monitor.rb — Ruby версия

require 'json'
require 'time'
require 'open3'
require 'optparse'

class DiskMonitor
  def initialize(interval: 2, duration: 60, history_file: 'disk_history.json')
    @interval = interval
    @duration = duration
    @history_file = history_file
    @history = []
  end

  def get_disk_io
    result = {}
    if RUBY_PLATFORM =~ /linux/
      # Linux: читаем /proc/diskstats
      File.readlines('/proc/diskstats').each do |line|
        parts = line.strip.split
        next if parts.length < 14
        disk = parts[2]
        read_sectors = parts[5].to_i * 512
        write_sectors = parts[9].to_i * 512
        result[disk] = { read_bytes: read_sectors, write_bytes: write_sectors }
      end
    elsif RUBY_PLATFORM =~ /darwin/
      # macOS: используем iostat
      output, _ = Open3.capture2('iostat -d 1 2 2>/dev/null')
      output.each_line do |line|
        parts = line.strip.split
        next if parts.length < 4 || parts[0] == 'disk'
        disk = parts[0]
        result[disk] = { read_bytes: (parts[3].to_f * 1024).to_i, write_bytes: (parts[4].to_f * 1024).to_i }
      end
    else
      # Windows: заглушка
      ['C:', 'D:'].each do |disk|
        result[disk] = { read_bytes: 0, write_bytes: 0 }
      end
    end
    result
  end

  def calculate_speeds(prev, curr)
    speeds = {}
    curr.each do |disk, data|
      if prev[disk]
        read_mb = (data[:read_bytes] - prev[disk][:read_bytes]) / (1024.0 * 1024.0)
        speeds[disk] = read_mb / @interval
      end
    end
    speeds
  end

  def collect
    puts "\e[36m💾 Disk Monitor (History) (Ruby)\e[0m"
    puts "Интервал: #{@interval} сек, Длительность: #{@duration} сек"
    puts

    total_samples = @duration / @interval
    puts "📊 Мониторинг дисков..."

    prev = get_disk_io
    sleep @interval

    total_samples.times do |i|
      curr = get_disk_io
      speeds = calculate_speeds(prev, curr)

      entry = {
        timestamp: Time.now.iso8601,
        speeds: speeds
      }
      @history << entry

      # Прогресс
      percent = (i + 1).to_f / total_samples * 100
      bar_len = 30
      filled = (bar_len * (i + 1) / total_samples).to_i
      bar = '█' * filled + '░' * (bar_len - filled)
      STDERR.print "\r[#{bar}] #{'%.1f' % percent}% (#{i+1}/#{total_samples})"

      prev = curr
      sleep @interval
    end

    STDERR.puts
    save_history
    print_stats
  end

  def save_history
    data = {
      interval: @interval,
      duration: @duration,
      start_time: @history.first&.[](:timestamp),
      end_time: @history.last&.[](:timestamp),
      samples: @history
    }
    File.write(@history_file, JSON.pretty_generate(data))
    puts "\e[32m💾 История сохранена: #{@history_file}\e[0m"
  end

  def print_stats
    return if @history.empty?

    stats = Hash.new { |h, k| h[k] = [] }
    @history.each do |entry|
      entry[:speeds].each do |disk, speed|
        stats[disk] << speed
      end
    end

    puts "\n\e[36m📈 Статистика за период:\e[0m"
    puts "┌" + "─" * 80 + "┐"
    puts "│ #{'Диск'.ljust(10)} #{'Среднее (МБ/с)'.ljust(15)} #{'Макс (МБ/с)'.ljust(12)} #{'Мин (МБ/с)'.ljust(12)} #{'IOPS'.ljust(15)} │"
    puts "├" + "─" * 80 + "┤"

    stats.each do |disk, speeds|
      if speeds.any?
        avg = speeds.sum / speeds.size
        max = speeds.max
        min = speeds.min
        avg_iops = avg
        puts "│ #{disk.ljust(10)} #{'%12.1f' % avg} #{'%12.1f' % max} #{'%12.1f' % min} #{'%12.0f' % avg_iops}  │"
      end
    end

    puts "└" + "─" * 80 + "┘"
  end

  def load_history
    unless File.exist?(@history_file)
      puts "\e[31m❌ Файл #{@history_file} не найден.\e[0m"
      return
    end

    data = JSON.parse(File.read(@history_file), symbolize_names: true)
    puts "\e[36m📂 Загружена история из #{@history_file}\e[0m"
    puts "  Интервал: #{data[:interval]} сек"
    puts "  Начало: #{data[:start_time]}"
    puts "  Конец: #{data[:end_time]}"
    puts "  Сэмплов: #{data[:samples]&.size || 0}"
  end
end

def main
  options = { interval: 2, duration: 60, history_file: 'disk_history.json', view: false }
  OptionParser.new do |opts|
    opts.banner = "Usage: ruby disk_monitor.rb [options]"
    opts.on("--interval N", Integer, "Интервал сбора") { |v| options[:interval] = v }
    opts.on("--duration N", Integer, "Длительность сбора") { |v| options[:duration] = v }
    opts.on("--file FILE", "Файл истории") { |v| options[:history_file] = v }
    opts.on("--view", "Просмотреть историю") { options[:view] = true }
  end.parse!

  monitor = DiskMonitor.new(**options)

  if options[:view]
    monitor.load_history
  else
    monitor.collect
  end
end

main if __FILE__ == $0
