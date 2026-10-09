# Crew Console

[English README](../../README.md) · [Русский README](../../README.ru.md)

## Features / Возможности

Crew Console extends the normal in-game crew monitoring window. It uses the ordinary console subscription and adds no custom network messages. Open the console, choose a crewmember from the list or map, and drag the damage timeline to inspect an earlier observation.

- Search by name or job, compact rows with the game's health/job icons, and a needs-help filter.
- Health, numeric damage and threshold, with separate critical and dead states.
- Timestamped position and health history; routes on the native station map.
- Previous/next observations and return to live data.
- A damage chart with death markers, gaps for unknown data, and a draggable time cursor.
- Landscape and portrait layouts inspired by the Crew Monitor web panel.

Мод расширяет штатное окно мониторинга экипажа и использует его обычную подписку. Собственных сетевых сообщений не добавляет. Выберите персонажа в списке или на карте, затем нажмите или перетащите курсор на графике для просмотра прошлого наблюдения.

## History semantics / Правила истории

History is recorded **only while the console is open** and lasts until that window closes. Limits are **3,600 observations per person** and **512 people**. The graph uses the actual sensor timestamps, not sample indices. These are game sensor times, not your computer's local time or a guaranteed round-start clock.

The chart's height is damage as a percentage of the threshold; its colour represents state. A cross marks death, grey ticks mark unknown damage, and the white line marks the selected moment. Steps represent observations. Gaps are not connected.

While viewing the past, the route ends at the selected moment and live map markers are hidden. The crew list remains explicitly current; map geometry also remains current. The history view is not a complete world replay.

История живёт **только до закрытия окна консоли**: максимум **3600 наблюдений на персонажа** и **512 персонажей**. Шкала использует время игровых датчиков, а не индекс наблюдения или часы компьютера. Высота графика — доля урона от порога, цвет — состояние, крестик — смерть, серые штрихи — неизвестный урон, белая линия — выбранный момент.

В прошлом маршрут обрывается на выбранном наблюдении, текущие маркеры карты скрыты. Список экипажа и геометрия карты остаются текущими. Это история телеметрии, а не полный replay мира.

Missing telemetry or coordinates, a different sensor or grid, and pauses longer than 30 seconds break a route. **No telemetry does not mean dead.** If there are no coordinates on the displayed grid, there is no route point. Health uses the same rounding categories as the web panel and game.

Пропуски телеметрии и координат, смена датчика или сетки и пауза более 30 секунд разрывают маршрут. **Отсутствие телеметрии не означает смерть.** Координаты не угадываются; здоровье использует категории округления игры и веб-панели.

## Scope

Rooms/zones, message history, v1–v5 recording files, full replay, and all features of the separate web app are outside this release. See [the original port research](../crew-monitor-port.md) for historical context and planned work; its earlier stage descriptions are not a current feature checklist.

Crew Console labels follow the RU/EN language selected in ModLauncher for the next game session. Native game controls retain their game localization. The bundled Hello World title also follows this setting.
