# RE:volution Client

Публичный репозиторий клиентской платформы RE:volution. Он содержит Windows launcher/updater, Auth API, client-only Forge 1.12.2 mod, manifest trust model, инструменты подписи и build/release automation.

**Последний tagged stable release: `v1.0.0`.** Текущий PROD launcher/distribution содержит post-`v1.0.0` изменения. Следующий Client release должен зафиксировать актуальный launcher и client-only mod как единый продукт RE:volution Client.

[Архитектура](docs/architecture.md) · [Launcher runtime](docs/launcher-runtime.md) · [Auth API](docs/auth-api.md)

Отдельные PROD/DEV EXE, установки и каналы обновления: [DEV channel](docs/dev-channel.md).

## Что входит в RE:volution Client

### Launcher

Windows-приложение, которое подготавливает и восстанавливает полный клиентский runtime RE:volution.

Launcher отвечает за:

- загрузку и проверку versioned manifest;
- проверку SHA-256 и размера каждого управляемого файла до активации;
- подготовку Minecraft / Forge / client files;
- выбор подходящего Java 8 runtime;
- загрузку и использование pinned managed Java 8, если системный Java отсутствует или не подходит;
- запуск Minecraft только после успешной проверки клиентского окружения;
- безопасное обновление файлов внутри configured installation root.

Launcher не содержит gameplay-логику и не получает прямой доступ к MongoDB или private Admin API.

### Client-only Forge mod

`mod/` содержит отдельный клиентский Forge 1.12.2 мод, который отвечает за RE:volution client UX:

- заменяет стандартный main-menu flow на клиентский экран RE:volution;
- предоставляет основной Play action;
- получает адрес игрового сервера из клиентской конфигурации;
- обрабатывает connection/error UX;
- не содержит серверную gameplay-логику и persistence.

Основной gameplay JAR поддерживается отдельно в [Theyyk/re-volution-mod](https://github.com/Theyyk/re-volution-mod).

### Auth API

`auth/` содержит Auth API клиентской платформы. Production launcher требует HTTPS Auth endpoint и не должен понижать production-конфигурацию до небезопасного local fallback.

Контракт API документирован в [docs/auth-api.md](docs/auth-api.md).

## Manifest trust model

Production manifest рассматривается как подписанные данные, а не как набор исполняемых команд.

Ключевые правила:

- managed paths должны оставаться внутри installation root;
- каждый управляемый файл имеет точный `sha256` и `size`;
- production manifest использует signed envelope;
- подпись проверяется ECDSA P-256 / SHA-256 по exact payload bytes;
- launcher доверяет только public keys из встроенного trust keyring;
- production manifest отклоняется при неизвестном `keyId`, неверном алгоритме, неправильной подписи или unsigned payload;
- unsigned remote manifest разрешён только при явном `developmentOnly=true`;
- private signing keys никогда не должны находиться в репозитории.

Production manifest также обязан управлять ровно одним `gameplay-mod` и одним `client-mod`.

Подробности: [docs/launcher-runtime.md](docs/launcher-runtime.md) и [docs/architecture.md](docs/architecture.md).

## Production distribution

Signed production manifest принадлежит этому репозиторию и хранится как `manifest/manifest.production.signed.json`.

Gameplay JAR обновляется независимо от launcher EXE. После опубликованного stable gameplay release репозиторий `re-volution-mod` отправляет `repository_dispatch` в этот Client-репозиторий. Client workflow скачивает точный stable gameplay JAR, проверяет фактические hash/size, обновляет только `gameplay-mod` entry, подписывает payload через `tools/ManifestSigner`, проверяет подпись и публикует обновлённый `manifest/manifest.production.signed.json` в `main`. Периодическая проверка остаётся fallback для пропущенного dispatch.

Это позволяет клиенту получать актуальный stable gameplay-мод без пересборки launcher для каждого gameplay release.

## Managed Java policy

RE:volution Client может использовать уже установленный совместимый 64-bit Java 8, но системная Java является только оптимизацией.

Если подходящий runtime не найден, launcher использует pinned managed Java:

- runtime скачивается из настроенного доверенного источника;
- проверяется до активации;
- хранится внутри installation root RE:volution Client;
- не изменяет системные `PATH`, `JAVA_HOME` или глобальную Java installation;
- частично скачанный или не прошедший проверку runtime не становится активным.

## Структура репозитория

- `launcher/` — Windows launcher/updater;
- `mod/` — client-only Forge 1.12.2 mod;
- `auth/` — Auth API;
- `manifest/` — manifest/trust configuration, включая trusted public keys;
- `tools/ManifestSigner/` — подпись и проверка production manifest;
- `scripts/` — development/build helpers;
- `docs/` — архитектура и runtime-контракты;
- `.github/workflows/` — CI для launcher, Auth API, client mod и manifest signing.

## Разработка и CI

Основная стабильная ветка репозитория — `main`.

GitHub Actions проверяет основные продуктовые компоненты независимо:

- client-only Forge mod build;
- Auth API build и smoke test;
- ManifestSigner build и signing smoke test;
- launcher build/publish и проверку publish payload.

Изменения в manifest trust model требуют особенно осторожной проверки: public trust keyring может находиться в репозитории, private signing material — никогда.

## Связанные репозитории

- [RE:volution Mod](https://github.com/Theyyk/re-volution-mod) — gameplay и dedicated-server logic;
- [RE:volution Resource Pack](https://github.com/Theyyk/re-volution-resource-pack) — rune-assets и Resource Pack releases; прежний production manifest там сохранён только как frozen legacy compatibility для старых launcher EXE;
- `re-volution-admin` — private local administration tool, не является частью player distribution.

## Документация

README показывает текущее состояние клиентской платформы. Детальные технические контракты находятся в `docs/`:

- [architecture.md](docs/architecture.md) — product boundary, responsibilities и trust model;
- [launcher-runtime.md](docs/launcher-runtime.md) — launcher/update runtime;
- [auth-api.md](docs/auth-api.md) — Auth API.

## Лицензия

Исходный код этого репозитория распространяется по [MIT License](LICENSE), если для конкретного стороннего компонента явно не указаны отдельные условия.

CI scheduling, cache, concurrency and artifact policy: [README-CI.md](README-CI.md).
