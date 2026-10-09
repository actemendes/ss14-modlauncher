# Security

Mods are native-trust local extensions: a mod DLL runs with the SS14 client process permissions. This is not a sandbox for untrusted downloads.

## Reporting

Report vulnerabilities privately through [GitHub Security Advisories](https://github.com/actemendes/ss14-modlauncher/security/advisories/new). Include the affected version, reproduction steps, impact and a minimal example. Do not post credentials, exploit payloads, private launcher files, or account data in a public issue.

Сообщайте об уязвимостях через [закрытый отчёт GitHub](https://github.com/actemendes/ss14-modlauncher/security/advisories/new). Укажите версию, шаги воспроизведения и последствия. Не публикуйте данные аккаунтов и файлы авторизации в Issues.

## Boundaries

- Manual update checks use `actemendes/ss14-modlauncher` by default. Changing the source trusts that repository's publishers; verify its owner first.
- A manifest hash detects a mismatched download; it does not independently authenticate the publisher.
- Normal SS14 authentication and engine verification are retained.
- Backup hashes protect against overwriting a changed installation. A failed consistency check should be investigated, not bypassed by deleting the state file.
- Files shipped with the original game, account tokens, and server content caches do not belong in release packages.

The first release is a compatibility-limited Windows x64 candidate. See the [release checklist](docs/releasing.md) for required verification before publication.
