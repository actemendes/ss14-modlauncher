# User guide / Руководство

[English README](../README.md) · [Русский README](../README.ru.md)

## First launch

Extract the entire release archive, then run `SS14ModLauncher.exe`. The application includes its .NET runtime. Select the directory containing both `SS14.Launcher.exe` and `loader/SS14.Loader.dll`; selecting the Steam library root is not sufficient.

Close both the game and original SS14 launcher before installation, updates, Steam integration, or restoration. The app can remain open. If the chosen folder is protected by Windows, use a writable installation folder or grant the required file access; elevation is not a substitute for selecting the right directory.

Choose Russian or English, select a profile and mods, and apply the selection. Then start SS14 from ModLauncher and connect normally through the original launcher. Changing a checkbox affects the next client session after applying it; already running client processes retain their loaded DLLs.

Распакуйте весь архив и запустите `SS14ModLauncher.exe`. Выберите папку с `SS14.Launcher.exe` и `loader/SS14.Loader.dll`, а не корень библиотеки Steam. Перед установкой, обновлением и восстановлением закройте игру и штатный лаунчер. Выберите язык, профиль, набор модов и примените его. Изменения действуют для следующего запуска клиента.

## Profiles

A profile is a saved set of enabled mod IDs. Use one for Crew Console, another for experiments, or a profile with every mod disabled. Applying an empty selection prevents mod DLL loading but does not restore the original loader; use restoration when you want the clean original files.

Профиль хранит набор включённых модов. Пустой профиль отключает загрузку модов, но не возвращает оригинальный файл загрузчика. Для этого используйте восстановление чистого SS14.

## Steam integration

Integration is optional. It backs up the original launcher executable beside its DLL, installs a bridge at `SS14.Launcher.exe`, and stores ModLauncher in the installation's `SS14ModLauncher` folder. Steam's normal Play action then opens ModLauncher; the app starts the backed-up original launcher.

No Steam account data, library metadata, or global Steam settings are edited. The integration affects this SS14 installation and also its existing shortcuts that target `SS14.Launcher.exe`. Starting ModLauncher directly works without integration.

Steam updates and Verify integrity can replace the bridge or loader. Refresh the installation state after a game update. If the app reports a hash conflict, keep the backups and stop modifying that installation until the changed files have been identified.

Интеграция Steam необязательна. Оригинальный EXE сохраняется рядом со своей DLL, на место `SS14.Launcher.exe` устанавливается мост, а ModLauncher копируется в подпапку установки. Обычная кнопка Steam открывает ModLauncher, затем он запускает оригинальный лаунчер. Настройки и аккаунт Steam не меняются. Обновление игры может заменить мост или загрузчик; после обновления проверьте состояние установки.

## Restore clean SS14

1. Close the client and original launcher.
2. Select the correct installation in ModLauncher.
3. Choose the restore-clean action and read the result.
4. Start SS14 normally to verify that the original launcher works.

The restore path checks backup and installed-file hashes. A file altered by another tool is not silently overwritten. It restores the original loader and removes the optional Steam entry-point replacement. Retained mod files/settings are inactive after restoring the original loader; personal profiles do not have to be deleted.

Never remove `installation.json`, `SS14.Loader.original.dll`, `steam.json`, or `SS14.Launcher.clean.exe` to bypass an error. They are recovery evidence. If Steam has already restored original files, the app can recognize their recorded hashes. If backups are unavailable, preserve the installation for investigation before using Steam's own Verify integrity as an external recovery route.

Для возврата чистого SS14 закройте игру и оригинальный лаунчер, выберите установку и нажмите восстановление. Проверяются хеши файлов и резервных копий; неизвестные изменения не затираются. Оставшиеся моды и настройки неактивны после восстановления исходного загрузчика. Не удаляйте файлы состояния и резервные копии ради обхода ошибки.

### After an upstream Steam update / После обновления Steam

If Steam installs a genuinely new launcher or loader, its hashes no longer match the old backup. Use Steam's own **Verify integrity** first, close both game and original launcher, then use ModLauncher's explicit action to adopt the verified original files as the new baseline. Confirm this only after that verification. The app checks structural compatibility but cannot independently prove the provenance of arbitrary local files.

The previous loader/launcher backups and state are archived under `loader/SS14LocalMods/History/<id>/`. The new original files remain unchanged until you explicitly install mods again. An active patched loader or Steam bridge cannot be adopted as an original.

Если Steam установил новую версию файлов, сначала выполните **проверку целостности в Steam**, закройте игру и штатный лаунчер и только затем подтвердите принятие проверенных оригиналов в ModLauncher. Старые копии и состояние сохраняются в `loader/SS14LocalMods/History/<id>/`. Новые оригиналы не меняются до повторной установки модов. Проверка структуры файла не заменяет проверку его происхождения.

## Mod updates

The default source is `actemendes/ss14-modlauncher`. Updates are checked manually against that repository's latest GitHub Release and its `mods-manifest.json` asset. Review offered updates before applying them with the game closed. You may enter another trusted publisher's `owner/repository` in update settings or leave the field empty to disable checks.

Settings saved by an earlier local build keep their existing source; enter `actemendes/ss14-modlauncher` if that field is empty. The update feature updates supported mod DLLs, not the launcher executable. Download a new launcher package from [Releases](https://github.com/actemendes/ss14-modlauncher/releases/latest) when needed. A release with no compatible manifest, an invalid hash, an unsupported version, or an unexpected URL is rejected.

Источник по умолчанию — `actemendes/ss14-modlauncher`. Если сохранились настройки ранней локальной сборки с пустым полем, укажите этот адрес вручную. Проверка запускается по кнопке и читает последний GitHub Release с файлом `mods-manifest.json`. Перед применением закройте игру. Обновляется код поддерживаемых модов; новый EXE лаунчера скачивается отдельным пакетом из Releases. Другой источник можно указать в формате `owner/repository`, только если вы доверяете его автору; пустое поле отключает проверку.

## One launch without mods / Один запуск без модов

Use **Launch once without mods** to start the original launcher with the mod bootstrap disabled for the new client session. Your saved profile remains available. Close an already running original launcher first so a new process receives the clean-session setting.

Кнопка **Без модов на один запуск** отключает bootstrap для новой сессии, сохраняя ваш профиль. Сначала закройте уже работающий штатный лаунчер: настройку должна получить новая цепочка процессов.

## Troubleshooting

| Symptom | Next step |
| --- | --- |
| Installation folder rejected | Select the folder with the launcher EXE and `loader/SS14.Loader.dll` |
| Game is still running | Close the client and original launcher, then retry |
| Hash mismatch / changed installation | Preserve backups, check whether Steam or another tool updated the files |
| Crew Console does not appear | Apply the profile, restart the client, and open a normal crew monitoring console |
| No historical route | Keep the console open to collect observations; missing telemetry is not reconstructed |
| Hello World keys do nothing | Enable/apply that mod and leave text-field focus |
| GitHub update check fails | Confirm repository spelling, latest release/manifest assets, network access, and GitHub rate limits |

The **Diagnostics** page can save a local report and open the runtime log folder. The report contains app/OS/installation state and selected mod IDs, without account tokens or game messages.

Settings: `%LOCALAPPDATA%/SS14ModLauncher/settings.json`. Mod runtime log: `%LOCALAPPDATA%/SS14LocalMods/mods.log`. Review and redact personal paths before sharing logs.
