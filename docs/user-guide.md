# User guide / Руководство

[English README](../README.md) · [Русский README](../README.ru.md)

## Mod library / Библиотека модов

The library lists all five bundled mods in compact rows with localized names, categories, versions and enable switches. Search by Russian or English name, ID, description or the current language's category. Filtering does not change the active profile. Click **Details** for full descriptions and usage instructions; Auto Chemistry includes an embedded, offline screenshot of the Russian AUTO tab. Enable changes are saved to the active profile and apply on the next client launch.

В списке пять модов: **Монитор экипажа**, **Переговоры**, **Зов о помощи**, **Автохимия**, **Тестовый мод**. Короткие строки показывают версию, категорию и переключатель. Поиск по названиям RU/EN, ID, описанию и категории не меняет профиль. **Подробнее** открывает описание и инструкцию; скриншот «Автохимии» доступен без сети. Выбор сохраняется в текущем профиле и применяется при следующем запуске клиента. Внутренние ID и имена DLL остались прежними.

## First launch / Первый запуск

Extract the entire release archive, then run `SS14ModLauncher.exe`. The application includes its .NET runtime. For a new installation, the first-run setup window looks for SS14 and shows the active profile and its selected mods. The initial default profile has **Crew Monitor / Монитор экипажа** enabled and **Test Mod / Тестовый мод** disabled. Existing profiles remain available and are not replaced by the recommendation.

Choose Russian or English, check the detected game folder, then press **Set up & play**. The action installs the selected mods and opens the original SS14 launcher for the normal server connection. A recognized Steam installation receives the mod patch and Steam launch integration together by default; an existing explicit opt-out is respected. Later, Steam Play opens ModLauncher. Standalone installations keep their original entry point.

If no game is found, use the folder chooser. Select the directory containing both `SS14.Launcher.exe` and `loader/SS14.Loader.dll`, or its game parent folder when those files are under `bin_x64`; the Steam library root is not sufficient. If the game or original launcher is running, close it and retry. ModLauncher itself can remain open; setup never forcibly closes game processes.

**Later** or closing the setup window installs nothing and remembers the dismissal for that installation. A dismissal before any game folder is found applies only while the folder is missing; choosing a new installation can show setup again. Already installed or previously restored installations skip the automatic popup, as do settings that cannot safely be written. Open **Installation → Quick setup** whenever you want to return to the wizard.

Close both the game and original launcher before subsequent installation, updates or restoration. If the chosen folder is protected by Windows, use a writable installation folder or grant the required file access; elevation is not a substitute for selecting the right directory. Profile and mod changes apply to the next client session; a running client retains its loaded DLLs.

Распакуйте весь архив и запустите `SS14ModLauncher.exe`. Для новой установки окно первого запуска найдёт SS14 и покажет текущий профиль с выбранными модами. Начальный стандартный профиль включает **Crew Monitor / Монитор экипажа**, а **Test Mod / Тестовый мод** оставляет выключенным. Существующие профили сохраняются и не заменяются рекомендуемым набором.

Выберите русский или английский язык, проверьте найденную папку и нажмите **Настроить и играть**. Приложение установит выбранные моды и откроет штатный лаунчер для обычного подключения к серверу. Для распознанной установки Steam патч и интеграция запуска устанавливаются вместе по умолчанию; прежнее явное отключение интеграции сохраняется. В дальнейшем «Играть» открывает ModLauncher. Установка вне Steam сохраняет прежнюю точку запуска.

Если игра не найдена, укажите папку с `SS14.Launcher.exe` и `loader/SS14.Loader.dll` или папку игры, внутри которой они лежат в `bin_x64`. Корень библиотеки Steam не подходит. Если игра или штатный лаунчер работают, закройте их и повторите попытку; ModLauncher можно оставить открытым. Настройка сама не завершает игровые процессы.

Кнопка **Позже** или закрытие окна ничего не устанавливает и сохраняет отказ от автоматического предложения для этой установки. Если папка игры ещё не найдена, отказ действует только до выбора установки. Для уже установленного или ранее восстановленного ModLauncher автоматическое окно не появляется; оно также пропускается, если настройки нельзя безопасно записать. Вернуться к мастеру можно через **Установка → Быстрая настройка**.

Перед последующей установкой, обновлением и восстановлением также закрывайте игру и штатный лаунчер. Изменения профиля и модов действуют для следующего запуска клиента.

## Help and hints / Справка и подсказки

**Getting started** in the left navigation explains setup, launching, updates and restoration. Hover over controls with hints to read their purpose; hovering over the bottom status also shows its full text. If a requested action fails, the main window shows an explanatory message as well as the status. Setup errors appear inside the wizard, with a way to open Diagnostics. Background update failures remain quiet on the Updates page.

Кнопка **Как начать?** слева объясняет настройку, запуск, обновления и восстановление. Наведите курсор на элементы с подсказками, чтобы узнать их назначение; наведение на нижнюю строку состояния показывает полный текст. Если действие не удалось, основное окно покажет пояснение и сообщение в строке состояния. Ошибки настройки выводятся внутри мастера, откуда можно открыть диагностику. Ошибки фоновой проверки обновлений остаются во вкладке «Обновления».

## Profiles

A profile is a saved set of enabled mod IDs. Use one for Crew Console, another for experiments, or a profile with every mod disabled. Applying an empty selection prevents mod DLL loading but does not restore the original loader; use restoration when you want the clean original files.

Профиль хранит набор включённых модов. Пустой профиль отключает загрузку модов, но не возвращает оригинальный файл загрузчика. Для этого используйте восстановление чистого SS14.

## Steam integration

Steam integration is **on by default** for an installation identified by a matching Steam app manifest and the expected library layout. Installing mods, the first launch that installs mods, and CLI `--install` use the same default. The patch and bridge are applied in one transaction; a failed installation is rolled back together.

The integration backs up the original launcher executable beside its DLL, installs a bridge at `SS14.Launcher.exe`, and stores ModLauncher in the installation's `SS14ModLauncher` folder. Steam Play then opens ModLauncher; the app starts the backed-up original launcher. Automatic integration requires the published single-file build from the release ZIP. A standalone installation does not acquire a Steam bridge automatically.

Use **Disable integration** in Installation to keep Steam launching the original launcher directly. This does not uninstall mods. The explicit off setting survives normal mod updates, repeated installs and restoration. **Enable integration** saves the on setting. Restoring clean SS14 removes the active bridge but retains the preference: a later reinstall enables it again if you left it on, and leaves it off if you explicitly disabled it.

No Steam account data, library metadata, or global Steam settings are edited. The integration affects this SS14 installation and also its existing shortcuts that target `SS14.Launcher.exe`. Starting ModLauncher directly works without integration.

Steam updates and Verify integrity can replace the bridge or loader. Refresh the installation state after a game update. If the app reports a hash conflict, keep the backups and stop modifying that installation until the changed files have been identified.

Для установки с соответствующим манифестом приложения и ожидаемой структурой библиотеки Steam интеграция **включена по умолчанию**. Обычная установка модов, первый запуск с установкой модов и CLI `--install` применяют одно правило. Патч и мост записываются одной транзакцией; при ошибке изменения откатываются вместе.

Оригинальный EXE сохраняется рядом со своей DLL, на место `SS14.Launcher.exe` устанавливается мост, а ModLauncher копируется в подпапку установки. Кнопка Steam открывает ModLauncher, затем он запускает оригинальный лаунчер. Для автоматической интеграции нужна опубликованная single-file сборка из ZIP релиза. Установка вне Steam не получает мост автоматически.

Кнопка **Отключить интеграцию** возвращает прямой запуск оригинального лаунчера из Steam, сохраняя установленные моды. Явное отключение сохраняется при обычных обновлениях модов, повторной установке и восстановлении. **Включить интеграцию** сохраняет включённое состояние. Восстановление чистого SS14 убирает действующий мост, но оставляет предпочтение: при следующей установке он вернётся, если вы оставили интеграцию включённой, и останется отключённым после явного отказа.

Настройки и аккаунт Steam не меняются. Обновление игры может заменить мост или загрузчик; после обновления проверьте состояние установки.

## Restore clean SS14

1. Close the client and original launcher.
2. Select the correct installation in ModLauncher.
3. Choose the restore-clean action and read the result.
4. Start SS14 normally to verify that the original launcher works.

The restore path checks backup and installed-file hashes. A file altered by another tool is not silently overwritten. It restores the original loader and removes the Steam entry-point replacement if installed. Retained mod files/settings are inactive after restoring the original loader; personal profiles do not have to be deleted. The Steam integration preference is retained for a later reinstall; restoration itself leaves the original launcher active.

Never remove `installation.json`, `SS14.Loader.original.dll`, `steam.json`, or `SS14.Launcher.clean.exe` to bypass an error. They are recovery evidence. If Steam has already restored original files, the app can recognize their recorded hashes. If backups are unavailable, preserve the installation for investigation before using Steam's own Verify integrity as an external recovery route.

Для возврата чистого SS14 закройте игру и оригинальный лаунчер, выберите установку и нажмите восстановление. Проверяются хеши файлов и резервных копий; неизвестные изменения не затираются. Оставшиеся моды и настройки неактивны после восстановления исходного загрузчика. Предпочтение интеграции Steam сохраняется для будущей переустановки; сразу после восстановления работает оригинальный лаунчер. Не удаляйте файлы состояния и резервные копии ради обхода ошибки.

### After an upstream Steam update / После обновления Steam

If Steam installs a genuinely new launcher or loader, its hashes no longer match the old backup. Use Steam's own **Verify integrity** first, close both game and original launcher, then use ModLauncher's explicit action to adopt the verified original files as the new baseline. Confirm this only after that verification. The app checks structural compatibility but cannot independently prove the provenance of arbitrary local files.

The previous loader/launcher backups and state are archived under `loader/SS14LocalMods/History/<id>/`. The new original files remain unchanged until you explicitly install mods again. An active patched loader or Steam bridge cannot be adopted as an original.

Если Steam установил новую версию файлов, сначала выполните **проверку целостности в Steam**, закройте игру и штатный лаунчер и только затем подтвердите принятие проверенных оригиналов в ModLauncher. Старые копии и состояние сохраняются в `loader/SS14LocalMods/History/<id>/`. Новые оригиналы не меняются до повторной установки модов. Проверка структуры файла не заменяет проверку его происхождения.

## Updates and notifications / Обновления и уведомления

The default source is `actemendes/ss14-modlauncher`. When ModLauncher opens, it checks that repository's latest release and `mods-manifest.json` once in the background. You can keep using the launcher while it checks. Turn off **Check for updates at startup** in Updates to disable this behaviour; the choice is saved. Existing settings without this option default to enabled.

There is no periodic timer or tray updater, and nothing checks while ModLauncher is closed. An offline startup shows a quiet status on the Updates page instead of interrupting launch. **Check for updates** remains available for a manual retry. A missing source disables checks; source settings saved by an earlier build remain unchanged.

The notification distinguishes a new **ModLauncher** version from individual **mod** updates. Use **Download & apply mods** only when you want to install the listed compatible updates, with the game and original launcher closed. A mod requiring a newer launcher is listed separately with its required version; compatible mods can still be updated. The launcher itself is downloaded from its own ZIP link and upgraded manually. Nothing is downloaded or applied by the startup check.

Launcher **0.1.4** includes the previously published Crew Console **0.1.2** and Hello World **0.1.2** DLLs unchanged. A newer launcher does not imply new mod versions. A future release containing only mod updates can continue to point to an earlier, compatible launcher ZIP.

Источник по умолчанию — `actemendes/ss14-modlauncher`. При открытии ModLauncher один раз в фоне проверяет последний релиз и `mods-manifest.json`; пользоваться лаунчером можно сразу. Отключите **Проверять обновления при запуске** во вкладке «Обновления», если эта проверка не нужна. Выбор сохраняется. В старых настройках без такого поля проверка включена по умолчанию.

Периодического таймера и обновляющего процесса в трее нет; при закрытом ModLauncher проверок нет. Без сети запуск продолжается, а во вкладке «Обновления» появляется спокойное сообщение о недоступности проверки. Для повторной попытки есть **Проверить обновления**. Пустое поле источника отключает проверки; сохранённый ранее источник сам не заменяется.

Уведомление отдельно показывает новую версию **ModLauncher** и обновления **модов**. Кнопка **Загрузить и применить моды** устанавливает только совместимые обновления по вашей команде; перед этим закройте игру и оригинальный лаунчер. Мод, которому нужен новый лаунчер, показан отдельно с минимальной версией и не мешает обновить остальные совместимые моды. Сам лаунчер скачивается отдельным ZIP и обновляется вручную. Проверка при запуске ничего не скачивает и не устанавливает.

Лаунчер **0.1.4** включает прежние опубликованные DLL Crew Console **0.1.2** и Hello World **0.1.2** без изменения байтов. Новая версия лаунчера не означает новые версии модов. Релиз только с обновлениями модов может ссылаться на прежний совместимый ZIP лаунчера.

Use another `owner/repository` only if you trust that publisher. Invalid manifests, unsafe URLs and wrong hashes are rejected. Routine mod updates preserve an explicit Steam integration opt-out.

Другой `owner/repository` указывайте только при доверии к автору. Неверные манифесты, небезопасные URL и несовпадающие хеши отклоняются. Обновления модов сохраняют явное отключение интеграции Steam.

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
