# KODA.md — Контекст проекта express-desktop

## Обзор

`express-desktop` — нативное десктопное приложение для [express.ms](https://express.ms), написанное на **.NET MAUI**. Приложение должно предоставлять обёртку поверх веб-сервиса в виде нативного клиента, с использованием WinUI3 для Windows и Mac Catalyst для macOS.

**Автор:** Artem Krotov (2026)  
**Лицензия:** MIT

---

## Технологии и зависимости

| Технология | Версия / Детали |
|------------|----------------|
| **.NET SDK** | 9.0.300 |
| **.NET MAUI** | 9.0.120 (workload `maui`) |
| **UI-фреймворк** | XAML + .NET MAUI Controls |
| **Windows UI** | WinUI3 (MauiWinUIApplication) |
| **Язык** | C# 12+ (nullable enable, implicit usings) |
| **Шрифты** | OpenSans (Regular, Semibold) |
| **Цветовая схема** | Primary `#512BD4` (фиолетовый) |

### NuGet-пакеты

- `Microsoft.Maui.Controls` — ядро MAUI
- `Microsoft.Extensions.Logging.Debug` — отладочное логирование

---

## Целевые платформы

| Платформа | Target Framework | Мин. версия | Статус |
|-----------|-----------------|-------------|--------|
| **Windows** | `net9.0-windows10.0.19041.0` | 10.0.17763.0 | Основная |
| **macOS** | `net9.0-maccatalyst` | 15.0 | Планируется |
| **Android** | `net9.0-android` | API 21 | Подключён |
| **iOS** | `net9.0-ios` | 15.0 | Подключён |
| Tizen | `net9.0-tizen` | 6.5 | Закомментирован |

**Windows-сборка** настроена как `unpackaged` (`WindowsPackageType = None`) — распространяется как обычный `.exe` без упаковки в MSIX/Store.

---

## Структура проекта

```
express-desktop/
├── ExpressApp.csproj          # Файл проекта, SDK-стиль
├── MauiProgram.cs                # Точка инициализации MAUI-приложения
├── App.xaml / App.xaml.cs        # Приложение, подключение стилей
├── AppShell.xaml / .cs           # Shell-навигация (одна страница)
├── MainPage.xaml / .cs           # Главная страница (UI + обработчик кнопки)
├── Platforms/                    # Платформо-специфичные точки входа
│   ├── Windows/                  # WinUI3: App.xaml, app.manifest, Package.appxmanifest
│   ├── MacCatalyst/              # AppDelegate, Info.plist, Entitlements.plist
│   ├── iOS/                      # AppDelegate, Info.plist
│   ├── Android/                  # MainActivity, MainApplication, AndroidManifest.xml
│   └── Tizen/                    # Main.cs, tizen-manifest.xml
├── Properties/
│   └── launchSettings.json       # Профиль запуска: "Windows Machine"
├── Resources/
│   ├── AppIcon/                  # Иконка приложения (appicon.svg)
│   ├── Splash/                   # Splash-скрин (splash.svg)
│   ├── Images/                   # Изображения (dotnet_bot.png)
│   ├── Fonts/                    # Шрифты (OpenSans)
│   ├── Raw/                      # Сырые ассеты
│   └── Styles/
│       ├── Colors.xaml           # Палитра: Primary, Secondary, Tertiary, Gray
│       └── Styles.xaml           # Глобальные стили всех MAUI-контролов
├── .gitignore
├── README.md
└── LICENSE
```

---

## Сборка и запуск

### Предварительные требования

- .NET SDK 9.0+ (установлен SDK 9.0.300)
- Workload `maui` (установлен: 9.0.120/9.0.100)
- macOS — для Mac Catalyst и iOS (требования Apple)
- Android SDK/эмулятор — для Android (опционально)

### Команды

```bash
# Восстановление пакетов
dotnet restore

# Сборка (Windows)
dotnet build

# Запуск на Windows Machine (по умолчанию из launchSettings.json)
dotnet run

# Запуск на конкретной платформе
dotnet build -t:Run -f net9.0-windows10.0.19041.0
```

### Тестирование

> **TODO:** Тестовый фреймворк и пакет для тестирования не подключены. Рекомендуется добавить `Microsoft.NET.Test.Sdk` и `xunit` / `NUnit` (в текущем `.gitignore` уже есть заглушки для `MSTest` и `NUnit`).

---

## Архитектура и код

### Точка входа

1. `MauiProgram.cs` — создаёт `MauiApp`, регистрирует шрифты, включает `Logging.AddDebug()` в `DEBUG`-сборке.
2. `App.xaml.cs` — создаёт окно с `AppShell`.
3. `AppShell.xaml` — Shell-навигация, единственный `ShellContent` с маршрутом `MainPage`.
4. `MainPage.xaml` — шаблонная страница: изображение, два заголовка, кнопка-счётчик.

### Стилизация

Все стили вынесены в `Resources/Styles/`:

- **`Colors.xaml`** — определяет Primary (`#512BD4`), Secondary, Tertiary, Gray100–Gray950, а также соответствующие `SolidColorBrush`.
- **`Styles.xaml`** — глобальные стили для всех MAUI-контролов (`Button`, `Entry`, `Label`, `Shell`, `Page` и др.) с поддержкой **тёмной темы** через `AppThemeBinding`.

### Конвенции кодирования

- **Nullable reference types** включены (`<Nullable>enable</Nullable>`)
- **Implicit usings** включены
- **XAML-компиляция** включена (`<?xaml-comp compile="true" ?>`)
- Код C# — без `using`-директив (implicit usings), с `?` для nullable-аргументов
- `SemanticProperties` для доступности (ScreenReader) используется в XAML
- Платформо-специфичная логика вынесена в `Platforms/`

---

## Текущее состояние

Проект находится на **стартовой стадии** — код полностью идентичен дефолтному шаблону `dotnet new maui`. Бизнес-логика, интеграция с express.ms, навигация между страницами и какие-либо сервисы пока отсутствуют.

### Что предстоит сделать

| Приоритет | Задача |
|-----------|--------|
| 🔴 Высокий | Настроить `ApplicationId` и `ApplicationTitle` для express.ms |
| 🔴 Высокий | Определить архитектуру интеграции с express.ms (WebView, HTTP-клиент, API) |
| 🟡 Средний | Расширить `AppShell.xaml` — добавить навигацию, вкладки, боковое меню |
| 🟡 Средний | Добавить DI-сервисы в `MauiProgram.cs` |
| 🟡 Средний | Подключить тестовый фреймворк |
| 🟢 Низкий | Сузить целевые платформы до десктопа (Windows + Mac Catalyst) |
| 🟢 Низкий | Добавить кастомные иконки и splash-скрин |

---

## Git

| Параметр | Значение |
|----------|----------|
| **Репозиторий** | Git (локально) |
| **Удалённый origin** | `origin/main` |
| **Текущая ветка** | `main` |
| **Коммитов** | 2 (`Initial commit`) |
