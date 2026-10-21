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
