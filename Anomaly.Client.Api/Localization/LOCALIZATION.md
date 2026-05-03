# Anomaly Localization (i18n)

Anomaly exposes a shared translation system that mods built on top of Anomaly can plug into. Using this system gives your mod:

- One locale preference shared across the whole Anomaly ecosystem (read from SCPSL's in-game language setting)
- A consistent translation file format, location, and contribution workflow
- Fallback-to-English for missing keys, with diagnostic logging
- No additional dependencies beyond Anomaly itself

---

## Quick start for mod authors

### 1. Pick a namespace

Your namespace is a lowercase, dotted identifier: `mymod`, `cool.mod`, `team_alpha`. It must match the regex `^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)*$` and cannot start with `anomaly.` or `launcher.` (reserved).

All your translation keys must start with this namespace followed by a dot: `mymod.button.ok`, `mymod.error.connection_lost`, etc.

### 2. Create your translation files

Put per-locale JSON files in your own directory under Anomaly's i18n tree. The recommended location is:

```
<UserData>/i18n/<your_namespace>/
    en.json      # canonical source — required
    fr.json
    de.json
    ...
```

Each JSON file is a flat key-value object. Keys are your full namespaced identifiers. Placeholders use `.NET` `string.Format` positional syntax (`{0}`, `{1}`).

`en.json`:
```json
{
  "mymod.greeting": "Hello, {0}!",
  "mymod.button.open_settings": "Open Settings",
  "mymod.error.connection_lost": "Lost connection to server ({0})"
}
```

`fr.json`:
```json
{
  "mymod.greeting": "Bonjour, {0} !",
  "mymod.button.open_settings": "Ouvrir les paramètres",
  "mymod.error.connection_lost": "Connexion perdue au serveur ({0})"
}
```

### 3. Register in your mod

In your `MelonMod.OnInitializeMelon()`:

```csharp
using Anomaly.Client.Api.Localization;

public override void OnInitializeMelon()
{
    // Extract your bundled translations to UserData/i18n/mymod first if needed.
    Tr.RegisterMod("mymod");
}
```

Use `Tr.RegisterMod("mymod", customDirectory)` only when your translations intentionally live outside the conventional `UserData/i18n/<namespace>/` path.

**Important:** Anomaly initializes its own localization in `Core.OnInitializeMelon()`, which means yours must run after it. Declare the dependency explicitly with `[assembly: MelonAdditionalDependencies("Anomaly")]` — CLR assembly references do not affect MelonLoader's `OnInitializeMelon` order. See [MODDING.md](../MODDING.md) for the full load-order contract.

### 4. Look up translations

```csharp
var hi = Tr.Get("mymod.greeting", "Player1");           // "Hello, Player1!"
var btn = Tr.Get("mymod.button.open_settings");         // "Open Settings"

// Cross-namespace read: you can reference Anomaly's keys too
var anomalyKey = Tr.Get("anomaly.overlay.authenticating");
```

Missing keys return the key itself and log a warning via `MelonLogger`. Don't panic — just add the key to your `en.json` and restart.

---

## Locale resolution

The active locale is determined at mod init, in priority order:

1. **SCPSL registry** (`%APPDATA%/SCP Secret Laboratory/registry.txt`, `translation_path` field) — authoritative
2. **Anomaly MelonPreferences** (`anomaly` category, `locale` entry) — override for testing/custom
3. **System culture** (`CultureInfo.CurrentCulture.TwoLetterISOLanguageName`)
4. **`"en"`** — final fallback

Locale changes require a mod restart in v1. Live switching may come in a later version via `Tr.LocaleChanged`.

---

## File lookup with region fallback

When the active locale has a region suffix (e.g., `pt_BR`, `zh_Hans`):

1. Try exact match (`pt_BR.json`)
2. Fall back to language-only (`pt.json`)
3. Fall back to `en.json`

This means most mods can ship just `en.json`, `fr.json`, `de.json`, etc. and still get acceptable results for regional variants.

---

## Rich text and TMP tags

Unity's TextMeshPro rich-text tags (`<color>`, `<b>`, `<size>`) can be included inside translated values:

```json
{
  "mymod.news.welcome": "<b><size=20>Welcome</size></b>\n<color=#ff3300>Build 42</color>"
}
```

Translators should preserve tags verbatim. If a tag is malformed, TMP will render it as literal text — which is ugly but not destructive.

---

## Namespace rules (summary)

- Lowercase, dots, underscores, alphanumerics only
- Must not start with `anomaly.` or `launcher.`
- One namespace per mod
- Registering a namespace twice logs a warning and ignores the second call
- Cross-namespace reads are allowed (any mod can read any registered key)
- Only the namespace owner writes files for that prefix

---

## File locations at runtime

- **Anomaly's own translations**: `UserData/i18n/anomaly/en.json`, `fr.json`, ...
- **Your mod's translations**: `UserData/i18n/<namespace>/en.json`, ...
- **User-edited locale override**: MelonPreferences category `anomaly`, entry `locale`
- **SCPSL language setting (source of truth)**: `%APPDATA%/SCP Secret Laboratory/registry.txt` → `translation_path`

---

## Troubleshooting

- **Missing-key warnings in the MelonLoader log**: add the missing key to your `en.json`. The system falls back to English if a translated file lacks a specific key, but a missing English source is an error.
- **Key shows the raw identifier instead of text**: your registration failed. Check the MelonLoader log for `[Anomaly i18n]` errors.
- **Wrong locale active**: confirm SCPSL's in-game language setting (`Settings → Language`). Override via `UserData/Anomaly/config.json` for testing.
- **Placeholders show as `{0}` literally**: mismatch between `Tr.Get(...)` argument count and the template. Fix the call or the translation.
