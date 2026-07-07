# haste-sprite-swap

A Haste mod framework for replacing **player (Zoe/Courier) narrative sprites** — the layered illustrations shown during dialogue and story interactions.

This mod only affects the narrative UI. It does not change in-game 3D models, NPC portraits, or UI icons.

## How it works

1. Subscribe to or build this framework mod.
2. Ship a separate content mod (or use the same folder for local testing) with:
   - A `*.hastespriteswap.json` config at the **mod root**
   - PNG replacement images referenced by that config
3. When dialogue shows the player, the mod swaps sprites on the narrative character layers.

## Building the framework

```bash
dotnet build
```

Output:

```
bin/Debug/netstandard2.1/
├── haste-sprite-swap.dll
├── example.default.hastespriteswap.json   # sample config
└── img/sample_master_chief.png            # sample body swap
```

Point your Steam Workshop item's **local override** at `bin/Debug/netstandard2.1`, then launch Haste.

## Extending it: making your own sprite pack

You do **not** need to edit C# to add new sprites. Create a Workshop item (or local override folder) with a config + images.

### 1. Create your config file

Place it at the **root** of your mod folder (not in a subfolder).

**Filename format:**

```
MyPack.default.hastespriteswap.json
MyPack.4.hastespriteswap.json
```

| Part | Meaning |
|------|---------|
| `MyPack` | Any name you want |
| `default` | Applies to all body skins (fallback) |
| `4` | Applies only when the equipped **body** skin enum value is `4` (Shadow) |

> Use `default` unless you need different art per skin.

### 2. Fill in the JSON

```json
{
  "basePath": "",
  "overwriteAllSkins": true,
  "swaps": {
    "Body_Default": {
      "file": "sprites/body.png"
    },
    "Head_Default": {
      "file": "sprites/head.png"
    },
    "_0004_Expression_Happy": {
      "file": "sprites/happy.png"
    }
  }
}
```

| Field | Description |
|-------|-------------|
| `basePath` | Optional folder prefix for all `file` paths, relative to your mod root |
| `overwriteAllSkins` | If `true`, `Body_Default` also replaces `Body_Green`, `Body_Blue`, etc. Same for head/hair layers |
| `swaps` | Maps **game sprite names** to replacement PNG paths, or `{}` to hide a layer |
| `file` | Path to a `.png` inside your mod folder. Can also be a plain string: `"Body_Default": "sprites/body.png"` |

Omit a sprite key to leave the original game art unchanged.

Use `{}` to **hide** that sprite layer — useful for dropping shadow/white highlight layers when your replacement art already includes them:

```json
"Body_White_Default": {},
"Body_Shadow_Default": {}
```

### 3. Add your PNG files

Put images anywhere inside your mod folder and reference them relative to the mod root (or under `basePath`).

**Supported format:** `.png`

**Security note:** Paths must stay inside your mod folder. Absolute paths and `..` escapes are rejected.

### 4. Publish or test locally

- **Local testing:** set your Workshop item's local override to your mod folder
- **Publishing:** upload the folder as a Workshop item; players need this framework mod installed

You can ship the framework DLL + your config/images in one folder for local dev, or publish them as separate Workshop items.

## Sprite names (swap keys)

Keys must match the game's internal sprite names exactly.

### Body layers

| Key pattern | Example |
|-------------|---------|
| `Body_{Skin}` | `Body_Default`, `Body_Green`, `Body_Weeboh` |
| `Body_White_{Skin}` | `Body_White_Default` |
| `Body_Shadow_{Skin}` | `Body_Shadow_Default` |

### Head layers

| Key pattern | Example |
|-------------|---------|
| `Head_{Skin}` | `Head_Default`, `Head_Crispy` |
| `Head_White_{Skin}` | `Head_White_Default` |
| `Head_Shadow_{Skin}` | `Head_Shadow_Default` |

### Hair layers

| Key pattern | Example |
|-------------|---------|
| `Hair_{Skin}` | `Hair_Default`, `Hair_Crispy` |
| `Hair_White_{Skin}` | `Hair_White_Default` |
| `Hair_Shadow_{Skin}` | `Hair_Shadow_Default` |

### Overlays (some skins only)

- `Glasses_Crispy`, `Glasses_DarkClown`
- `Nose_Clown`, `Nose_DarkClown`

### Expressions

**Default style:**
- `_0004_Expression_Happy`
- `_0005_Expression_Confident`
- `_0006_Expression_Uncertain`
- `_0007_Expression_Shocked`
- `_0008_Expression_Smile`

**Pixel / Zoe64 style:**
- `Pixel_Expression_Happy`
- `Pixel_Expression_Confident`
- `Pixel_Expression_Uncertain`
- `Pixel_Expression_Shocked`
- `Pixel_Expression_Smile`

### Reference images

This repo includes original game sprites under `img/reference/` for sizing and layout reference. These are **not** included in the build output — copy or edit them into your own mod folder.

**Body shadow layers** (`img/reference/`):

| File | Swap key |
|------|----------|
| `Body_Shadow_Default.png` | `Body_Shadow_Default` |
| `Body_Shadow_Crispy.png` | `Body_Shadow_Crispy` |
| `Body_Shadow_Clown.png` | `Body_Shadow_Clown` |
| `Body_Shadow_Weeboh.png` | `Body_Shadow_Weeboh` |
| `Body_Shadow_Wobbler.png` | `Body_Shadow_Wobbler` |
| `Body_Shadow.png` | `Body_Shadow` (Shadow skin body) |

Most skins only have a shadow layer variant when the base skin has one in-game. Use the matching `Body_Shadow_{Skin}` reference alongside `Body_{Skin}` and `Body_White_{Skin}` when authoring a full body swap.

## Tips for good-looking packs

The narrative player is built from **layered sprites** (body, head, hair, shadow, white highlight, expression overlay). For best results:

1. Start from the matching reference PNG in `img/reference/`
2. Keep the same canvas size and alignment as the original
3. Swap all layers that are visible for your target skin, not just `Body_Default`
4. Include expression sprites if the character shows reactions during dialogue
5. Use `overwriteAllSkins: true` if you only want to author the `_Default` variants once

## In-game console commands (F1)

| Command | Description |
|---------|-------------|
| `SpriteSwapService.WriteExampleConfig` | Writes a template JSON with all known player sprite keys to the game directory |
| `SpriteSwapService.PrintCurrentSkinIndex` | Prints equipped body/head skin enum values |
| `SpriteSwapService.ReloadSpriteSwapConfigs` | Hot-reloads configs without restarting |
| `SpriteSwapService.PrintSwapStatus` | Prints discovered configs, active rules, and re-applies with a full swap summary |

## Troubleshooting

Check the game log for lines starting with `[SpriteSwapMod]`:

**Windows:** `%USERPROFILE%\AppData\LocalLow\Landfall\Haste\Player.log`

Use `SpriteSwapService.PrintSwapStatus` in the F1 console for a live dump of config + apply results.

Common messages:

| Log | Meaning |
|-----|---------|
| `Initializing narrative player sprite swap framework.` | Mod DLL loaded |
| `Loaded default config from ... (N replacement(s), M clear(s) ...)` | Config found and parsed |
| `Using config '...' for mod '...'` | That config was selected for the current body skin |
| `Active swap rules for body skin ...` | Lists every rule that will be used |
| `Apply summary (...): N replaced, M cleared, K failed` | What actually changed on the narrative UI |
| `Replaced Body_Default -> img/foo.png (rule 'Body_Default')` | A successful swap |
| `Cleared _0004_Expression_Happy (rule '...')` | A layer was hidden with `{}` |
| `Failed Body_Default -> ...: file not found` | PNG path problem |
| `No sprite swap config files were found` | No `*.hastespriteswap.json` at mod roots |
| `Could not reapply swaps: Courier narrative UI not found` | Normal on scenes without dialogue UI |
| `No narrative sprites matched any active swap rules` | Config loaded, but nothing matched visible sprites |

**Checklist if nothing changes:**

1. Is this framework mod enabled?
2. Is your `.hastespriteswap.json` at the **mod root**?
3. Is the filename format correct? (`name.default.hastespriteswap.json`)
4. Does the sprite key match exactly? (e.g. `Body_Default`, not `body_default`)
5. Trigger **dialogue** — swaps only apply in the narrative UI, not in gameplay

## Example included in this repo

`example.default.hastespriteswap.json` swaps only the player body to `img/sample_master_chief.png`, with `overwriteAllSkins: true` so it applies across all body skin variants.

## License

MIT — see [LICENSE](LICENSE).
