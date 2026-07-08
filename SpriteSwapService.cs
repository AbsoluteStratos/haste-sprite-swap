using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Landfall.Haste;
using Landfall.Modding;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Zorro.Core.CLI;

namespace haste_sprite_swap;

internal static class SpriteSwapService
{
    private const string ConfigSuffix = ".hastespriteswap.json";
    private const string LogPrefix = "[SpriteSwapMod]";

    private static readonly string[] PartPrefixes =
    [
        "Head_Shadow_",
        "Head_White_",
        "Head_",
        "Body_Shadow_",
        "Body_White_",
        "Body_",
        "Hair_Shadow_",
        "Hair_White_",
        "Hair_",
        "Glasses_",
        "Nose_",
    ];

    private static readonly string[] KnownPlayerSpriteKeys =
    [
        "Head_Default", "Head_White_Default", "Head_Shadow_Default",
        "Body_Default", "Body_White_Default", "Body_Shadow_Default",
        "Hair_Default", "Hair_White_Default", "Hair_Shadow_Default",
        "Head_Green", "Head_Blue", "Head_Crispy", "Head_Shadow", "Head_Wobbler",
        "Head_Clown", "Head_DarkClown", "Head_Weeboh",
        "Head_White_Clown", "Head_White_Weeboh", "Head_White_Wobbler",
        "Head_Shadow_Clown", "Head_Shadow_Weeboh", "Head_Shadow_Wobbler",
        "Body_Green", "Body_Blue", "Body_Crispy", "Body_Shadow", "Body_Wobbler_New",
        "Body_Clown", "Body_DarkClown", "Body_Weeboh",
        "Body_White_Clown", "Body_White_Crispy", "Body_White_Weeboh", "Body_White_Wobbler",
        "Body_Shadow_Clown", "Body_Shadow_Crispy", "Body_Shadow_Weeboh", "Body_Shadow_Wobbler",
        "Hair_Crispy", "Hair_DarkClown",
        "Glasses_Crispy", "Glasses_DarkClown",
        "Nose_Clown", "Nose_DarkClown",
        "_0004_Expression_Happy", "_0005_Expression_Confident", "_0006_Expression_Uncertain",
        "_0007_Expression_Shocked", "_0008_Expression_Smile",
        "Pixel_Expression_Happy", "Pixel_Expression_Confident", "Pixel_Expression_Uncertain",
        "Pixel_Expression_Shocked", "Pixel_Expression_Smile",
    ];

    private static readonly Regex SkinIndexPattern = new(
        @"\.(?<skin>(?:default|\d+))\.hastespriteswap\.json$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly struct ResolvedSwap
    {
        public readonly string File;
        public readonly string BasePath;
        public readonly string ModDirectory;
        public readonly bool OverwriteAllSkins;

        public bool IsEmpty => string.IsNullOrWhiteSpace(File);

        public ResolvedSwap(string file, string basePath, string modDirectory, bool overwriteAllSkins)
        {
            File = file;
            BasePath = basePath;
            ModDirectory = modDirectory;
            OverwriteAllSkins = overwriteAllSkins;
        }
    }

    private static readonly Dictionary<string, Sprite> SpriteCache = new(StringComparer.OrdinalIgnoreCase);
    private static Sprite? _emptySprite;
    private static readonly List<LoadedSpriteConfig> LoadedConfigs = [];
    private static Dictionary<string, ResolvedSwap> _activeSwaps = new(StringComparer.OrdinalIgnoreCase);
    private static string? _activeDisplayName;
    private static LocalizedString _originalCourierDisplayName = default!;
    private static bool _savedOriginalCourierDisplayName;

    private enum SwapApplyResult
    {
        Replaced,
        Cleared,
        Failed,
    }

    internal static void Initialize()
    {
        Debug.Log($"{LogPrefix} Initializing narrative player sprite swap framework.");

        Modloader.LoadPassComplete += () => Reload();
        Modloader.OnItemLoaded += _ => Reload();

        SceneManager.sceneLoaded += OnSceneLoaded;

        On.Landfall.Haste.InteractionSkinHandler.SetSkinIllustrations += (orig, self) =>
        {
            orig(self);
            ApplyToSkinHandler(self);
        };

        On.Landfall.Haste.InteractionCharacterUI.SetDisplaying += (orig, self, visible, immediate) =>
        {
            orig(self, visible, immediate);
            if (visible && self.skinHandler != null)
            {
                ApplyToSkinHandler(self.skinHandler, requireCourier: false);
            }
        };

        On.Landfall.Haste.InteractionCharacterFacialHolder.PlayReaction += (orig, self, reactionType) =>
        {
            orig(self, reactionType);
            ApplyToDisplayedFace(self);
        };

        Reload();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"{LogPrefix} Scene loaded: {scene.name}, refreshing swaps for current skin.");
        RefreshActiveSwaps();
        ScheduleReapply();
    }

    private static void RefreshActiveSwaps()
    {
        LoadedConfigs.Clear();
        _activeSwaps = BuildActiveSwaps();
        _activeDisplayName = BuildActiveDisplayName();
        ApplyCourierDisplayName();
    }

    private static void ScheduleReapply()
    {
        if (MonoFunctions.instance != null)
        {
            MonoFunctions.DelayCall(ReapplyToCourier, 0.25f);
            return;
        }

        ReapplyToCourier();
    }

    private static void ReapplyToCourier()
    {
        var handler = FindCourierSkinHandler();
        if (handler == null)
        {
            Debug.LogWarning($"{LogPrefix} Could not reapply swaps: Courier narrative UI not found in the current scene.");
            return;
        }

        Debug.Log($"{LogPrefix} Reapplying sprite swaps after scene load.");
        ApplyCourierDisplayName();
        ApplyToSkinHandler(handler, requireCourier: false, context: "scene reapply");
    }

    [ConsoleCommand]
    public static void WriteExampleConfig()
    {
        var swaps = new Dictionary<string, SpriteSwapEntry>();
        foreach (var key in KnownPlayerSpriteKeys)
        {
            swaps[key] = new SpriteSwapEntry { File = string.Empty };
        }

        var example = new SpriteSwapConfig
        {
            BasePath = string.Empty,
            OverwriteAllSkins = false,
            Swaps = swaps,
        };

        var outputPath = Path.Combine(Directory.GetCurrentDirectory(), $"example.default{ConfigSuffix}");
        File.WriteAllText(outputPath, JsonConvert.SerializeObject(example, Formatting.Indented));
        Debug.Log($"{LogPrefix} Wrote example config to {outputPath}");
    }

    [ConsoleCommand]
    public static void PrintCurrentSkinIndex()
    {
        var body = SkinManager.GetBodySkinFromFacts();
        var head = SkinManager.GetHeadSkinFromFacts();
        Debug.Log($"{LogPrefix} Body skin: {body} ({(int)body}), Head skin: {head} ({(int)head})");
        Debug.Log($"{LogPrefix} Use .{(int)body}.hastespriteswap.json for body-specific swaps, or .default.hastespriteswap.json for all skins.");
    }

    [ConsoleCommand]
    public static void ReloadSpriteSwapConfigs() => Reload();

    [ConsoleCommand]
    public static void PrintSwapStatus()
    {
        var body = SkinManager.GetBodySkinFromFacts();
        var head = SkinManager.GetHeadSkinFromFacts();
        Debug.Log($"{LogPrefix} Status: body skin {body} ({(int)body}), head skin {head} ({(int)head})");
        Debug.Log($"{LogPrefix} Status: {LoadedConfigs.Count} config file(s) discovered, {_activeSwaps.Count} active swap rule(s).");

        foreach (var config in LoadedConfigs)
        {
            var nameInfo = string.IsNullOrWhiteSpace(config.Config.Name)
                ? string.Empty
                : $", name='{config.Config.Name}'";
            Debug.Log($"{LogPrefix} Status: config '{Path.GetFileName(config.ConfigPath)}' ({(config.SkinIndex == null ? "default" : $"skin {config.SkinIndex}")}, {config.Config.Swaps.Count} entries{nameInfo})");
        }

        Debug.Log(string.IsNullOrWhiteSpace(_activeDisplayName)
            ? $"{LogPrefix} Status: dialogue title unchanged (no active name override)."
            : $"{LogPrefix} Status: dialogue title override active -> '{_activeDisplayName}'");

        LogActiveSwapSummary();

        var handler = FindCourierSkinHandler();
        Debug.Log(handler == null
            ? $"{LogPrefix} Status: Courier narrative UI is not available right now."
            : $"{LogPrefix} Status: Courier narrative UI found, applying current swaps.");
        if (handler != null)
        {
            ApplyToSkinHandler(handler, requireCourier: false, context: "status check");
        }
    }

    private static void Reload()
    {
        LoadedConfigs.Clear();
        SpriteCache.Clear();
        RefreshActiveSwaps();

        if (LoadedConfigs.Count == 0)
        {
            Debug.LogWarning($"{LogPrefix} No sprite swap config files were found. Expected '*.(default|skinIndex).hastespriteswap.json' at mod roots.");
        }

        LogActiveSwapSummary();
        LogActiveDisplayNameSummary();
        ReapplyToCourier();
    }

    private static void LogActiveDisplayNameSummary()
    {
        if (string.IsNullOrWhiteSpace(_activeDisplayName))
        {
            return;
        }

        Debug.Log($"{LogPrefix} Will replace player dialogue title with '{_activeDisplayName}'.");
    }

    private static void LogActiveSwapSummary()
    {
        if (_activeSwaps.Count == 0)
        {
            Debug.LogWarning($"{LogPrefix} No active sprite swaps for the current equipped body skin.");
            return;
        }

        var body = SkinManager.GetBodySkinFromFacts();
        Debug.Log($"{LogPrefix} Active swap rules for body skin {body} ({(int)body}):");

        foreach (var swap in _activeSwaps.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var action = swap.Value.IsEmpty ? "clear" : $"replace -> {swap.Value.File}";
            var overwrite = swap.Value.OverwriteAllSkins ? ", overwriteAllSkins" : string.Empty;
            Debug.Log($"{LogPrefix}   {swap.Key}: {action}{overwrite}");
        }
    }

    private static Dictionary<string, ResolvedSwap> BuildActiveSwaps()
    {
        DiscoverConfigs();
        var currentSkin = (int)SkinManager.GetBodySkinFromFacts();
        var merged = new Dictionary<string, ResolvedSwap>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in LoadedConfigs.GroupBy(c => c.ModDirectory, StringComparer.OrdinalIgnoreCase))
        {
            var chosen = group.FirstOrDefault(c => c.SkinIndex == currentSkin)
                ?? group.FirstOrDefault(c => c.SkinIndex == null);

            if (chosen == null)
            {
                Debug.LogWarning($"{LogPrefix} No matching config for mod '{group.Key}' (body skin {currentSkin}). Expected '{currentSkin}' or 'default' in filename.");
                continue;
            }

            Debug.Log($"{LogPrefix} Using config '{Path.GetFileName(chosen.ConfigPath)}' for mod '{group.Key}'.");

            foreach (var (spriteKey, entry) in chosen.Config.Swaps)
            {
                merged[spriteKey] = new ResolvedSwap(
                    entry.File ?? string.Empty,
                    chosen.Config.BasePath ?? string.Empty,
                    chosen.ModDirectory,
                    chosen.Config.OverwriteAllSkins);
            }
        }

        return merged;
    }

    private static string? BuildActiveDisplayName()
    {
        var currentSkin = (int)SkinManager.GetBodySkinFromFacts();
        string? displayName = null;

        foreach (var group in LoadedConfigs.GroupBy(c => c.ModDirectory, StringComparer.OrdinalIgnoreCase))
        {
            var chosen = group.FirstOrDefault(c => c.SkinIndex == currentSkin)
                ?? group.FirstOrDefault(c => c.SkinIndex == null);

            if (chosen == null || string.IsNullOrWhiteSpace(chosen.Config.Name))
            {
                continue;
            }

            displayName = chosen.Config.Name;
        }

        return displayName;
    }

    private static void ApplyCourierDisplayName()
    {
        if (InteractionUI.Instance == null || InteractionUI.Instance.Courier == null)
        {
            return;
        }

        var courier = InteractionUI.Instance.Courier;
        if (!_savedOriginalCourierDisplayName)
        {
            _originalCourierDisplayName = courier.DisplayName;
            _savedOriginalCourierDisplayName = true;
        }

        if (string.IsNullOrWhiteSpace(_activeDisplayName))
        {
            if (_savedOriginalCourierDisplayName)
            {
                courier.DisplayName = _originalCourierDisplayName;
            }

            return;
        }

        courier.DisplayName = new UnlocalizedString(_activeDisplayName);
    }

    private static void DiscoverConfigs()
    {
        var foundAnyModDirectory = false;
        var foundAnyConfigFile = false;

        foreach (var item in Modloader.LoadedItemDirectories)
        {
            var modDirectory = item.Value.Item1;
            if (string.IsNullOrWhiteSpace(modDirectory) || !Directory.Exists(modDirectory))
            {
                continue;
            }

            foundAnyModDirectory = true;
            var configPaths = Directory.GetFiles(modDirectory, $"*{ConfigSuffix}", SearchOption.TopDirectoryOnly);
            var hasNonExampleConfig = configPaths.Any(path => !IsExampleConfig(Path.GetFileName(path)));

            foreach (var configPath in configPaths)
            {
                var fileName = Path.GetFileName(configPath);
                if (hasNonExampleConfig && IsExampleConfig(fileName))
                {
                    Debug.Log($"{LogPrefix} Skipping example config '{fileName}' because another config exists in '{modDirectory}'.");
                    continue;
                }

                foundAnyConfigFile = true;
                TryLoadConfig(configPath, modDirectory);
            }
        }

        if (!foundAnyModDirectory)
        {
            Debug.LogWarning($"{LogPrefix} No loaded mod directories were available while searching for config files.");
        }
        else if (!foundAnyConfigFile)
        {
            Debug.LogWarning($"{LogPrefix} Searched loaded mod directories but found no '*{ConfigSuffix}' files.");
        }
    }

    private static bool IsExampleConfig(string fileName) =>
        fileName.StartsWith("example.", StringComparison.OrdinalIgnoreCase)
        && fileName.EndsWith(ConfigSuffix, StringComparison.OrdinalIgnoreCase);

    private static void TryLoadConfig(string configPath, string modDirectory)
    {
        try
        {
            var fileName = Path.GetFileName(configPath);
            var match = SkinIndexPattern.Match(fileName);
            if (!match.Success)
            {
                Debug.LogWarning($"{LogPrefix} Skipping {fileName}: expected format 'name.(skinIndex|default).hastespriteswap.json'");
                return;
            }

            int? skinIndex = match.Groups["skin"].Value.Equals("default", StringComparison.OrdinalIgnoreCase)
                ? null
                : int.Parse(match.Groups["skin"].Value);

            var json = File.ReadAllText(configPath);
            var root = JObject.Parse(json);
            var config = new SpriteSwapConfig
            {
                BasePath = root.Value<string>("basePath") ?? string.Empty,
                OverwriteAllSkins = root.Value<bool?>("overwriteAllSkins") ?? false,
                Name = root.Value<string>("name"),
                Swaps = ParseSwaps(root["swaps"]),
            };

            LoadedConfigs.Add(new LoadedSpriteConfig(modDirectory, configPath, skinIndex, config));
            var clearCount = config.Swaps.Count(pair => string.IsNullOrWhiteSpace(pair.Value.File));
            var replaceCount = config.Swaps.Count - clearCount;
            var nameInfo = string.IsNullOrWhiteSpace(config.Name) ? string.Empty : $", name='{config.Name}'";
            Debug.Log($"{LogPrefix} Loaded {(skinIndex == null ? "default" : $"skin {skinIndex}")} config from {configPath} ({replaceCount} replacement(s), {clearCount} clear(s), overwriteAllSkins={config.OverwriteAllSkins}{nameInfo}).");
            if (!string.IsNullOrWhiteSpace(config.Name))
            {
                Debug.Log($"{LogPrefix} Config '{fileName}' has a 'name' entry — will replace player dialogue title with '{config.Name}'.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"{LogPrefix} Could not parse sprite swap configuration {configPath}: {ex}");
        }
    }

    private static Dictionary<string, SpriteSwapEntry> ParseSwaps(JToken? swapsToken)
    {
        var swaps = new Dictionary<string, SpriteSwapEntry>(StringComparer.OrdinalIgnoreCase);
        if (swapsToken is not JObject swapsObject)
        {
            return swaps;
        }

        foreach (var property in swapsObject.Properties())
        {
            swaps[property.Name] = ParseSwapEntry(property.Value);
        }

        return swaps;
    }

    private static SpriteSwapEntry ParseSwapEntry(JToken token)
    {
        return token.Type switch
        {
            JTokenType.String => new SpriteSwapEntry { File = token.Value<string>() ?? string.Empty },
            // {} clears the sprite layer (shorthand for no replacement image)
            JTokenType.Object when token is JObject { Count: 0 } => new SpriteSwapEntry(),
            JTokenType.Object => token.ToObject<SpriteSwapEntry>() ?? new SpriteSwapEntry(),
            _ => new SpriteSwapEntry(),
        };
    }

    private static InteractionSkinHandler? FindCourierSkinHandler()
    {
        if (InteractionUI.Instance == null)
        {
            return null;
        }

        var courierUi = InteractionUI.Instance.GetComponentsInChildren<InteractionCharacterUI>(true)
            .FirstOrDefault(ui => ui.skinHandler != null);

        return courierUi?.skinHandler;
    }

    private static void ApplyToSkinHandler(InteractionSkinHandler handler, bool requireCourier = true, string context = "apply")
    {
        if (_activeSwaps.Count == 0)
        {
            Debug.LogWarning($"{LogPrefix} Skipping {context}: no active sprite swaps.");
            return;
        }

        if (requireCourier)
        {
            var courierHandler = FindCourierSkinHandler();
            if (courierHandler == null || handler != courierHandler)
            {
                Debug.LogWarning($"{LogPrefix} Skipping {context}: handler is not the Courier narrative UI.");
                return;
            }
        }

        handler.Initalize();
        var skins = handler.GetComponentsInChildren<InteractionSkin>(true);

        var replaced = new List<string>();
        var cleared = new List<string>();
        var failed = new List<string>();

        foreach (var skin in skins)
        {
            ApplyToImages(skin.GetComponentsInChildren<Image>(true), replaced, cleared, failed);
            ReplaceFacialArray(skin.FacialExpressionOverrides, replaced, cleared, failed);
        }

        var facialHolder = FindFacialHolderForHandler(handler);
        if (facialHolder != null)
        {
            ApplyToFacialHolders([facialHolder], replaced, cleared, failed);
        }
        else
        {
            Debug.LogWarning($"{LogPrefix} No InteractionCharacterFacialHolder found above handler '{handler.name}'. Expression swaps were skipped.");
        }

        LogApplySummary(context, replaced, cleared, failed);
    }

    private static InteractionCharacterFacialHolder? FindFacialHolderForHandler(InteractionSkinHandler handler)
    {
        return handler.GetComponentInParent<InteractionCharacterFacialHolder>(true);
    }

    private static void ApplyToDisplayedFace(InteractionCharacterFacialHolder holder)
    {
        if (holder.m_face == null || holder.m_face.sprite == null || _activeSwaps.Count == 0)
        {
            return;
        }

        var courierHandler = FindCourierSkinHandler();
        if (courierHandler == null || FindFacialHolderForHandler(courierHandler) != holder)
        {
            return;
        }

        if (!TryResolveSwap(holder.m_face.sprite.name, out var swap, out var matchedKey))
        {
            return;
        }

        if (swap.IsEmpty)
        {
            holder.m_face.sprite = GetEmptySprite();
            holder.m_face.gameObject.SetActive(false);
            return;
        }

        var result = TryLoadReplacementSprite(holder.m_face.sprite, swap, out var replacement, out _);
        if (result == SwapApplyResult.Replaced)
        {
            holder.m_face.sprite = replacement!;
            holder.m_face.gameObject.SetActive(true);
        }
    }

    private static void LogApplySummary(string context, List<string> replaced, List<string> cleared, List<string> failed)
    {
        Debug.Log($"{LogPrefix} Apply summary ({context}): {replaced.Count} replaced, {cleared.Count} cleared, {failed.Count} failed.");

        foreach (var line in replaced)
        {
            Debug.Log($"{LogPrefix}   Replaced {line}");
        }

        foreach (var line in cleared)
        {
            Debug.Log($"{LogPrefix}   Cleared {line}");
        }

        foreach (var line in failed)
        {
            Debug.LogWarning($"{LogPrefix}   Failed {line}");
        }

        if (replaced.Count == 0 && cleared.Count == 0 && failed.Count == 0)
        {
            Debug.LogWarning($"{LogPrefix} No narrative sprites matched any active swap rules during {context}.");
        }
    }

    private static void ApplyToFacialHolders(
        IEnumerable<InteractionCharacterFacialHolder> holders,
        List<string> replaced,
        List<string> cleared,
        List<string> failed)
    {
        foreach (var holder in holders)
        {
            ReplaceFacialArray(holder.FacialHolders, replaced, cleared, failed);
            ReplaceFacialArray(holder.FacialExpressionOverrides, replaced, cleared, failed);
        }
    }

    private static void ReplaceFacialArray(
        InteractionCharacterFacialHolder.FacialHolder[]? holders,
        List<string> replaced,
        List<string> cleared,
        List<string> failed)
    {
        if (holders == null)
        {
            return;
        }

        foreach (var holder in holders)
        {
            if (holder.expression == null)
            {
                continue;
            }

            if (TryResolveSwap(holder.expression.name, out var swap, out var matchedKey))
            {
                ApplySwap(holder.expression, matchedKey, swap, sprite => holder.expression = sprite, replaced, cleared, failed);
            }
        }
    }

    private static void ApplyToImages(
        IEnumerable<Image> images,
        List<string> replaced,
        List<string> cleared,
        List<string> failed)
    {
        foreach (var image in images)
        {
            if (image.sprite == null)
            {
                continue;
            }

            if (TryResolveSwap(image.sprite.name, out var swap, out var matchedKey))
            {
                ApplySwap(image.sprite, matchedKey, swap, sprite => image.sprite = sprite, replaced, cleared, failed);
            }
        }
    }

    private static void ApplySwap(
        Sprite original,
        string matchedKey,
        ResolvedSwap swap,
        Action<Sprite> assign,
        List<string> replaced,
        List<string> cleared,
        List<string> failed)
    {
        var spriteName = original.name;
        var viaRule = matchedKey.Equals(spriteName, StringComparison.OrdinalIgnoreCase)
            ? $"rule '{matchedKey}'"
            : $"rule '{matchedKey}' via overwriteAllSkins";

        if (swap.IsEmpty)
        {
            assign(GetEmptySprite());
            cleared.Add($"{spriteName} ({viaRule})");
            return;
        }

        var result = TryLoadReplacementSprite(original, swap, out var replacement, out var error);
        if (result == SwapApplyResult.Replaced)
        {
            assign(replacement!);
            replaced.Add($"{spriteName} -> {swap.File} ({viaRule})");
            return;
        }

        failed.Add($"{spriteName} -> {swap.File} ({viaRule}): {error}");
    }

    private static bool TryResolveSwap(string spriteName, out ResolvedSwap swap, out string matchedKey)
    {
        if (_activeSwaps.TryGetValue(spriteName, out swap))
        {
            matchedKey = spriteName;
            return true;
        }

        foreach (var candidate in _activeSwaps)
        {
            if (!candidate.Value.OverwriteAllSkins)
            {
                continue;
            }

            var defaultKey = GetDefaultKeyForSprite(spriteName);
            if (defaultKey != null && defaultKey.Equals(candidate.Key, StringComparison.OrdinalIgnoreCase))
            {
                swap = candidate.Value;
                matchedKey = candidate.Key;
                return true;
            }
        }

        swap = default;
        matchedKey = string.Empty;
        return false;
    }

    private static string? GetDefaultKeyForSprite(string spriteName)
    {
        foreach (var prefix in PartPrefixes)
        {
            if (!spriteName.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var suffix = spriteName[prefix.Length..];
            if (suffix.Equals("Default", StringComparison.Ordinal))
            {
                return null;
            }

            return prefix + "Default";
        }

        return null;
    }

    private static SwapApplyResult TryLoadReplacementSprite(
        Sprite original,
        ResolvedSwap swap,
        out Sprite? replacement,
        out string error)
    {
        replacement = null;
        error = string.Empty;

        if (swap.IsEmpty)
        {
            replacement = GetEmptySprite();
            return SwapApplyResult.Cleared;
        }

        var cacheKey = $"{swap.ModDirectory}|{swap.BasePath}|{swap.File}";
        if (SpriteCache.TryGetValue(cacheKey, out var cached))
        {
            replacement = cached;
            return SwapApplyResult.Replaced;
        }

        var resolvedPath = ResolveSafePath(swap.ModDirectory, swap.BasePath, swap.File);
        if (resolvedPath == null)
        {
            error = $"path '{swap.File}' escapes the mod folder";
            return SwapApplyResult.Failed;
        }

        if (!File.Exists(resolvedPath))
        {
            error = $"file not found at {resolvedPath}";
            return SwapApplyResult.Failed;
        }

        try
        {
            var bytes = File.ReadAllBytes(resolvedPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(texture);
                error = $"could not decode image data from {resolvedPath}";
                return SwapApplyResult.Failed;
            }

            texture.filterMode = FilterMode.Point;
            var pivot = new Vector2(
                original.pivot.x / original.rect.width,
                original.pivot.y / original.rect.height);

            replacement = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                pivot,
                original.pixelsPerUnit,
                0,
                SpriteMeshType.FullRect);

            SpriteCache[cacheKey] = replacement;
            return SwapApplyResult.Replaced;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return SwapApplyResult.Failed;
        }
    }

    private static string? ResolveSafePath(string modDirectory, string basePath, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            return null;
        }

        var combined = Path.GetFullPath(Path.Combine(modDirectory, basePath ?? string.Empty, relativePath));
        var normalizedModDirectory = Path.GetFullPath(modDirectory);
        if (!combined.StartsWith(normalizedModDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return combined;
    }

    private static Sprite GetEmptySprite()
    {
        if (_emptySprite != null)
        {
            return _emptySprite;
        }

        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, Color.clear);
        texture.Apply();
        texture.filterMode = FilterMode.Point;
        _emptySprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 100f);
        return _emptySprite;
    }

    private sealed class LoadedSpriteConfig(
        string modDirectory,
        string configPath,
        int? skinIndex,
        SpriteSwapConfig config)
    {
        public string ModDirectory { get; } = modDirectory;
        public string ConfigPath { get; } = configPath;
        public int? SkinIndex { get; } = skinIndex;
        public SpriteSwapConfig Config { get; } = config;
    }
}

internal sealed class SpriteSwapConfig
{
    [JsonProperty("basePath")]
    public string BasePath { get; set; } = string.Empty;

    [JsonProperty("overwriteAllSkins")]
    public bool OverwriteAllSkins { get; set; }

    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("swaps")]
    public Dictionary<string, SpriteSwapEntry> Swaps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class SpriteSwapEntry
{
    [JsonProperty("file")]
    public string File { get; set; } = string.Empty;
}
