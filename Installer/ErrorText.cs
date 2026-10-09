using SS14ModLauncher.Core;
namespace SS14ModLauncher;
internal static class ErrorText
{
    public static string Message(Exception exception, string language)
    {
        if (language != "ru") return exception.Message;
        if (exception is InstallationException e) return e.Code switch
        {
            "backup-invalid" => "Резервная копия не прошла проверку SHA-256. Восстановление остановлено.",
            "file-changed" or "loader-changed" => "Файлы игры изменились. Проверьте состояние на вкладке «Диагностика»; исходные файлы не перезаписаны.",
            "game-running" => "Закройте игровой клиент и обычный SS14 Launcher, затем повторите действие.",
            "installation-busy" => "Другая операция изменяет эту установку. Дождитесь её завершения.",
            "interrupted-transaction" => "Обнаружена прерванная операция. Повторите установку или восстановление.",
            "loader-unsupported" => "Эта версия загрузчика SS14 не поддерживается. Исходные файлы не изменены.",
            "mod-missing" => "Файл выбранного мода отсутствует. Повторите установку.",
            "not-installed" => "Сначала установите моды на вкладке «Установка».",
            "orphan-backup" or "untracked-patch" => "Найдена резервная копия или модификация без корректной записи установки. Автоматическая перезапись остановлена.",
            "path-invalid" or "root-invalid" => "Выберите папку bin_x64 с SS14.Launcher.exe и loader/SS14.Loader.dll.",
            "path-link" => "Путь установки содержит символическую ссылку. Выберите фактическую папку игры.",
            "payload-conflict" => "Файл мода изменён вне ModLauncher. Перезапись остановлена.",
            "payload-invalid" => "Пакет модов повреждён или неполон. Повторно распакуйте релиз.",
            "process-check-failed" => "Не удалось проверить запущенные процессы. Закройте SS14 и повторите действие.",
            "selection-invalid" or "state-invalid" or "language-invalid" => "Файл настроек установки повреждён. Откройте диагностику.",
            "steam-backup-conflict" => "Резервная копия запускатора Steam уже существует или изменена.",
            "steam-changed" => "Steam изменил запускатор. Проверьте файлы игры и восстановите интеграцию.",
            "steam-unsupported" => "Для интеграции нужен оригинальный Windows-запускатор SS14 и релизная сборка ModLauncher.",
            "upgrade-required" => "Обнаружен старый установщик. Нажмите «Установить моды» для обновления.",
            "verify-game-first" => "Сначала проверьте целостность файлов игры в Steam. Текущие файлы ещё содержат модификацию или не распознаны как оригиналы.",
            _ => "Операция остановлена: " + e.Code
        };
        if (exception is HttpRequestException) return "Не удалось проверить GitHub. Проверьте репозиторий, подключение и наличие опубликованного релиза. " + exception.Message;
        if (exception is TaskCanceledException) return "Истекло время ожидания сети. Повторите проверку позже.";
        if (exception is InvalidDataException) return "Данные не прошли проверку целостности или формата. Изменения не применены; проверьте источник релиза.";
        if (exception is ArgumentException) return "Проверьте введённое значение. Источник обновлений: owner/repository; имя профиля: 1–40 символов.";
        if (exception is UnauthorizedAccessException) return "Нет доступа к папке. Выберите доступную установку или запустите с необходимыми правами.";
        return exception.Message;
    }
}
