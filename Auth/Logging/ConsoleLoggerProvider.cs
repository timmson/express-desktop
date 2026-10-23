using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ExpressApp.Auth.Logging;

/// <summary>
/// Писатель логов в консоль.
/// В unpackaged Windows-сборке трассировка Debug-провайдера уходит только в
/// отладчик, поэтому для требования «все сетевые вызовы видны в консоли»
/// используется собственный провайдер поверх System.Console.
/// </summary>
public sealed class ConsoleLoggerProvider : ILoggerProvider
{
    private static readonly object ConsoleLock = new();

    static ConsoleLoggerProvider()
    {
        // Консоль Windows живёт в OEM-кодовой странице (866/437), а .NET пишет
        // строки как есть — кириллица в логах превращается в «кракозябры».
        // Приводим и кодовую страницу консоли, и Encoding потока вывода к UTF-8.
        TryUseUtf8Console();
    }

    private static void TryUseUtf8Console()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        try
        {
            // Меняем кодовую страницу вывода самой консоли, иначе она
            // интерпретирует UTF-8 байты как OEM-866.
            SetConsoleOutputCP(Utf8CodePage);

            // Без флага «без BOM» в начало вывода попадает U+FEFF.
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (IOException)
        {
            // Вывод перенаправлен либо консоли нет (запуск из IDE без терминала) —
            // логи всё равно уйдут в Debug-провайдер, коددировку не трогаем.
        }
    }

    private const uint Utf8CodePage = 65001;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleOutputCP(uint wCodePageID);

    public ILogger CreateLogger(string categoryName) => new ConsoleLogger(categoryName);

    public void Dispose()
    {
    }

    private sealed class ConsoleLogger(string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            var shortCategory = ShortenCategory(categoryName);
            var line = $"{DateTime.Now:HH:mm:ss.fff} [{LogLevelTag(logLevel)}] {shortCategory} — {message}";

            lock (ConsoleLock)
            {
                var previous = Console.ForegroundColor;
                Console.ForegroundColor = ColorFor(logLevel);
                Console.WriteLine(line);
                if (exception is not null)
                {
                    Console.WriteLine(exception);
                }

                Console.ForegroundColor = previous;
            }
        }

        private static string ShortenCategory(string categoryName)
        {
            var index = categoryName.LastIndexOf('.');
            return index >= 0 ? categoryName[(index + 1)..] : categoryName;
        }

        private static string LogLevelTag(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "FTL",
            _ => "???"
        };

        private static ConsoleColor ColorFor(LogLevel level) => level switch
        {
            LogLevel.Debug or LogLevel.Trace => ConsoleColor.DarkGray,
            LogLevel.Information => ConsoleColor.Cyan,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error or LogLevel.Critical => ConsoleColor.Red,
            _ => ConsoleColor.Gray
        };
    }
}
