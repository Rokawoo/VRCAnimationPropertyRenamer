using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using System.Globalization;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

/// <summary>
/// Ultimate Animation Object & Property Renamer v2.2 - Japanese Character Support
/// Enhanced with Unicode normalization and Japanese character handling
/// Fixed parsing, discovery, and preview for "OBJECT : Component.SubComponent.PropertyName" format
/// Author: Ultimate Developer | Optimized for professional workflows with Japanese support
/// </summary>
public class AnimationObjectPropertyRenamer : EditorWindow
{
    #region Constants & Configuration
    private const float MIN_WINDOW_WIDTH = 700f;
    private const float MIN_WINDOW_HEIGHT = 800f;
    private const int MAX_PREVIEW_ITEMS = 150;
    private const int PERFORMANCE_BATCH_SIZE = 50;
    private const string PREF_PREFIX = "RΩKΔ's Super Based Animation Renamer, Yeah I know I'm cool, YW~!! <3";

    // UI Colors
    private static readonly Color SUCCESS_COLOR = new Color(0.2f, 0.8f, 0.2f);
    private static readonly Color WARNING_COLOR = new Color(1f, 0.7f, 0f);
    private static readonly Color ERROR_COLOR = new Color(0.9f, 0.3f, 0.3f);
    private static readonly Color ACCENT_COLOR = new Color(0.3f, 0.7f, 1f);
    private static readonly Color BLEND_SHAPE_COLOR = new Color(0.7f, 0.9f, 1f);
    private static readonly Color JAPANESE_COLOR = new Color(0.9f, 0.7f, 0.9f);
    #endregion

    #region Enums
    public enum RenameTarget { Object, PropertyName }
    public enum BackupStrategy { None, Temporary, Permanent }
    private enum OperationState { Idle, Scanning, Previewing, Processing }
    private enum ValidationResult { Valid, Warning, Error }
    private enum TextType { Latin, Japanese, Mixed, Other }
    #endregion

    #region Serialized State
    [SerializeField] private List<AnimationClip> selectedClips = new List<AnimationClip>();
    [SerializeField] private RenameTarget renameTarget = RenameTarget.PropertyName;
    [SerializeField] private BackupStrategy backupStrategy = BackupStrategy.Temporary;
    [SerializeField] private bool caseSensitive = false;
    [SerializeField] private bool autoPreview = true;
    [SerializeField] private bool showAdvanced = false;
    [SerializeField] private bool enableJapaneseNormalization = true;
    [SerializeField] private bool showJapaneseHelp = false;
    #endregion

    #region UI State
    private string fromValue = "";
    private string toValue = "";
    private OperationState currentState = OperationState.Idle;
    private Vector2 clipsScrollPos, previewScrollPos, objectsScrollPos, propertiesScrollPos;
    private bool showHelp, showDiscoveredObjects, showDiscoveredProperties, showPreview;
    private string searchFilter = "";
    private int selectedTabIndex = 1; // Start with Property mode
    #endregion

    #region Data & Performance
    private readonly HashSet<string> discoveredObjects = new HashSet<string>();
    private readonly HashSet<string> discoveredPropertyNames = new HashSet<string>();
    private readonly List<PropertyChangeInfo> previewChanges = new List<PropertyChangeInfo>();
    private readonly Dictionary<AnimationClip, string> backupPaths = new Dictionary<AnimationClip, string>();
    private readonly System.Diagnostics.Stopwatch performanceTimer = new System.Diagnostics.Stopwatch();

    private string lastOperationStats = "";
    private float lastPreviewTime;
    private int totalPropertiesScanned;
    #endregion

    #region Core Data Structures
    private class PropertyChangeInfo
    {
        public AnimationClip clip;
        public EditorCurveBinding originalBinding;
        public EditorCurveBinding newBinding;
        public AnimationCurve curve;
        public string oldPath, newPath, objectName, propertyName, middlePart;
        public ValidationResult validation = ValidationResult.Valid;
        public string validationMessage = "";
        public TextType textType = TextType.Latin;

        public bool WillChange => validation != ValidationResult.Error && oldPath != newPath;
        public bool HasWarning => validation == ValidationResult.Warning;
    }

    private class ParsedPath
    {
        public string objectName = "";
        public string middlePart = "";
        public string propertyName = "";
        public bool isValid = false;
        public bool hasObjectSeparator = false; // Tracks if " : " format is used
        public TextType textType = TextType.Latin;

        public string Reconstruct(string newObject = null, string newProperty = null)
        {
            var obj = newObject ?? objectName;
            var prop = newProperty ?? propertyName;

            if (hasObjectSeparator)
            {
                return $"{obj}{middlePart}{prop}";
            }
            else
            {
                // Simple path without object separator
                return prop;
            }
        }
    }

    private class OperationResult
    {
        public int successCount, errorCount, warningCount, totalProperties;
        public System.TimeSpan duration;
        public readonly List<string> errors = new List<string>();
        public readonly List<string> warnings = new List<string>();
    }
    #endregion

    #region Japanese Character Support Utilities
    /// <summary>
    /// Normalizes Unicode text for consistent comparison (especially important for Japanese)
    /// </summary>
    private string NormalizeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        if (enableJapaneseNormalization)
        {
            try
            {
                // Try to normalize to NFC (Canonical Decomposition followed by Canonical Composition)
                // This handles Japanese combining characters properly
                return text.Normalize(System.Text.NormalizationForm.FormC);
            }
            catch (System.Exception)
            {
                // Fallback if normalization is not available in this Unity version
                return text;
            }
        }

        return text;
    }

    /// <summary>
    /// Determines the primary character type of the text
    /// </summary>
    private TextType DetermineTextType(string text)
    {
        if (string.IsNullOrEmpty(text)) return TextType.Latin;

        bool hasHiragana = false;
        bool hasKatakana = false;
        bool hasKanji = false;
        bool hasLatin = false;
        bool hasOther = false;

        foreach (char c in text)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);

            // Check for Japanese character ranges
            if (c >= 0x3040 && c <= 0x309F) // Hiragana
                hasHiragana = true;
            else if (c >= 0x30A0 && c <= 0x30FF) // Katakana
                hasKatakana = true;
            else if (c >= 0x4E00 && c <= 0x9FAF) // CJK Unified Ideographs (Kanji)
                hasKanji = true;
            else if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                hasLatin = true;
            else if (unicodeCategory != UnicodeCategory.ConnectorPunctuation &&
                     unicodeCategory != UnicodeCategory.DashPunctuation &&
                     unicodeCategory != UnicodeCategory.OtherPunctuation &&
                     c != '_' && c != '-' && c != '.' && c != ' ')
                hasOther = true;
        }

        // Determine primary type
        if (hasHiragana || hasKatakana || hasKanji)
        {
            if (hasLatin) return TextType.Mixed;
            return TextType.Japanese;
        }
        else if (hasLatin)
        {
            if (hasOther) return TextType.Mixed;
            return TextType.Latin;
        }
        else
        {
            return TextType.Other;
        }
    }

    /// <summary>
    /// Enhanced text comparison that handles Japanese characters properly
    /// </summary>
    private bool TextEquals(string text1, string text2, System.StringComparison comparison)
    {
        if (string.IsNullOrEmpty(text1) && string.IsNullOrEmpty(text2)) return true;
        if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2)) return false;

        // Normalize both texts for proper Japanese character comparison
        var normalized1 = NormalizeText(text1);
        var normalized2 = NormalizeText(text2);

        return string.Equals(normalized1, normalized2, comparison);
    }

    /// <summary>
    /// Enhanced text contains check for Japanese characters
    /// </summary>
    private bool TextContains(string source, string value, System.StringComparison comparison)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value)) return false;

        var normalizedSource = NormalizeText(source);
        var normalizedValue = NormalizeText(value);

        return normalizedSource.IndexOf(normalizedValue, comparison) >= 0;
    }

    /// <summary>
    /// Enhanced text replace that handles Japanese characters
    /// </summary>
    private string TextReplace(string source, string oldValue, string newValue, System.StringComparison comparison)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(oldValue)) return source;

        var normalizedSource = NormalizeText(source);
        var normalizedOldValue = NormalizeText(oldValue);
        var normalizedNewValue = NormalizeText(newValue ?? "");

        if (comparison == System.StringComparison.Ordinal)
        {
            return normalizedSource.Replace(normalizedOldValue, normalizedNewValue);
        }
        else
        {
            // Case-insensitive replace (more complex for Japanese)
            var index = normalizedSource.IndexOf(normalizedOldValue, comparison);
            if (index < 0) return source;

            var result = normalizedSource.Remove(index, normalizedOldValue.Length);
            return result.Insert(index, normalizedNewValue);
        }
    }

    /// <summary>
    /// Validates Japanese text for use in animation property names
    /// </summary>
    private ValidationResult ValidateJapaneseText(string text, out string message)
    {
        message = "";

        if (string.IsNullOrEmpty(text))
        {
            message = "Empty text";
            return ValidationResult.Error;
        }

        var textType = DetermineTextType(text);

        // Check for problematic characters in Unity animation system
        var problematicChars = new char[] { ':', '/', '\\', '<', '>', '|', '?', '*', '"', '\t', '\n', '\r' };
        if (text.IndexOfAny(problematicChars) >= 0)
        {
            message = "Contains invalid characters for Unity animation properties";
            return ValidationResult.Error;
        }

        // Warn about potential encoding issues
        if (textType == TextType.Japanese && !enableJapaneseNormalization)
        {
            message = "Japanese text detected - enable normalization for best results";
            return ValidationResult.Warning;
        }

        // Check for very long text that might cause issues
        if (text.Length > 100)
        {
            message = "Text is very long - may cause performance issues";
            return ValidationResult.Warning;
        }

        return ValidationResult.Valid;
    }
    #endregion

    #region Menu Integration
    [MenuItem("Tools/RΩKΔ's Animation Renamer", false, 0)]
    public static AnimationObjectPropertyRenamer ShowWindow()
    {
        var window = GetWindow<AnimationObjectPropertyRenamer>("RΩKΔ's Animation Renamer");
        window.minSize = new Vector2(MIN_WINDOW_WIDTH, MIN_WINDOW_HEIGHT);
        window.titleContent = new GUIContent("RΩKΔ's Animation Renamer", EditorGUIUtility.IconContent("AnimationClip Icon").image);
        window.Show();
        return window;
    }

    [MenuItem("Assets/Rename Animation Objects & Properties", true)]
    private static bool ValidateContextMenu() => Selection.objects.OfType<AnimationClip>().Any();

    [MenuItem("Assets/Rename Animation Objects & Properties", false, 25)]
    private static void ShowFromContextMenu()
    {
        var window = GetWindow<AnimationObjectPropertyRenamer>("RΩKΔ's Animation Renamer");
        window.AddSelectedClipsFromProject();
    }
    #endregion

    #region Unity Lifecycle
    private void OnEnable()
    {
        LoadPreferences();
        Undo.undoRedoPerformed += OnUndoRedo;
        titleContent = new GUIContent("Animation Renamer", EditorGUIUtility.IconContent("AnimationClip Icon").image);
    }

    private void OnDisable()
    {
        SavePreferences();
        Undo.undoRedoPerformed -= OnUndoRedo;
    }

    private void OnGUI()
    {
        DrawMainInterface();
        HandleKeyboardShortcuts();
        HandleAutoPreview();
    }

    private void OnUndoRedo()
    {
        ClearPreview();
        Repaint();
    }
    #endregion

    #region Core Operations - Enhanced Path Parsing with Japanese Support
    /// <summary>
    /// Enhanced parsing for "OBJECT : Component.SubComponent.PropertyName" format with Japanese character support
    /// Also handles root objects and simple paths like "rotation", "Armature/bone.rotation", etc.
    /// </summary>
    private ParsedPath ParseAnimationPath(string path)
    {
        var parsed = new ParsedPath();

        if (string.IsNullOrEmpty(path))
        {
            return parsed;
        }

        // Normalize the path for proper Japanese character handling
        path = NormalizeText(path);
        parsed.textType = DetermineTextType(path);

        // Check for the specific format: "OBJECT : Component.SubComponent.PropertyName"
        if (path.Contains(" : "))
        {
            parsed.hasObjectSeparator = true;
            var colonIndex = path.IndexOf(" : ");

            // Extract object name (everything before " : ")
            parsed.objectName = path.Substring(0, colonIndex);

            // Extract the rest (everything after " : ")
            var remainder = path.Substring(colonIndex + 3); // +3 for " : "

            // Find the property name (last segment after the last dot)
            var lastDotIndex = remainder.LastIndexOf('.');
            if (lastDotIndex >= 0 && lastDotIndex < remainder.Length - 1)
            {
                // We have a clear property at the end
                parsed.middlePart = " : " + remainder.Substring(0, lastDotIndex + 1);
                parsed.propertyName = remainder.Substring(lastDotIndex + 1);
                parsed.isValid = true;
            }
            else
            {
                // No clear property separation, treat remainder as property
                parsed.middlePart = " : ";
                parsed.propertyName = remainder;
                parsed.isValid = true;
            }
        }
        else
        {
            // Handle simple paths without object separator
            parsed.hasObjectSeparator = false;

            // Check if it's a hierarchy path (contains '/')
            if (path.Contains('/'))
            {
                var lastSlashIndex = path.LastIndexOf('/');
                var beforeSlash = path.Substring(0, lastSlashIndex);
                var afterSlash = path.Substring(lastSlashIndex + 1);

                // Check if there's a property after the slash
                var dotIndex = afterSlash.LastIndexOf('.');
                if (dotIndex >= 0 && dotIndex < afterSlash.Length - 1)
                {
                    parsed.objectName = beforeSlash + "/" + afterSlash.Substring(0, dotIndex);
                    parsed.middlePart = ".";
                    parsed.propertyName = afterSlash.Substring(dotIndex + 1);
                }
                else
                {
                    // No property, afterSlash might be object or property
                    if (IsLikelyPropertyName(afterSlash))
                    {
                        parsed.objectName = beforeSlash;
                        parsed.middlePart = "/";
                        parsed.propertyName = afterSlash;
                    }
                    else
                    {
                        parsed.objectName = path; // Whole path is the object
                    }
                }
                parsed.isValid = true;
            }
            else if (path.Contains('.'))
            {
                // Simple dotted path
                var lastDotIndex = path.LastIndexOf('.');
                var beforeDot = path.Substring(0, lastDotIndex);
                var afterDot = path.Substring(lastDotIndex + 1);

                if (IsLikelyPropertyName(afterDot))
                {
                    parsed.objectName = beforeDot;
                    parsed.middlePart = ".";
                    parsed.propertyName = afterDot;
                }
                else
                {
                    // Treat whole thing as object (like "MyObject.SubObject")
                    parsed.objectName = path;
                }
                parsed.isValid = true;
            }
            else
            {
                // Simple single word - could be root object or property
                // IMPORTANT: For root objects, we should treat them as objects, not properties
                // This handles cases like just "Head" or "Body" at the root level

                // Check if it looks like a common property name
                if (IsLikelyPropertyName(path) && path.Length <= 4)
                {
                    // Short names like "x", "y", "z" are likely properties
                    parsed.propertyName = path;
                }
                else
                {
                    // Longer names or names that don't match property patterns are likely objects
                    // This includes root-level objects like "Head", "Body", "Armature", etc.
                    parsed.objectName = path;
                }
                parsed.isValid = true;
            }
        }

        return parsed;
    }

    /// <summary>
    /// Enhanced property name detection with Japanese character support
    /// </summary>
    private bool IsLikelyPropertyName(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        var textType = DetermineTextType(text);
        var normalizedText = NormalizeText(text);

        // Common property patterns (English)
        var propertyPatterns = new[]
        {
            // Transform properties
            "x", "y", "z", "w",
            "rotation", "position", "scale",
            "localRotation", "localPosition", "localScale",
            "m_LocalRotation", "m_LocalPosition", "m_LocalScale",
            
            // Common animation properties
            "weight", "blend", "alpha", "color", "intensity",
            "enabled", "active", "visible",
            
            // Blend shape naming patterns
            "smile", "blink", "eye", "mouth", "jaw", "brow", "cheek",
            "look", "open", "close", "left", "right", "up", "down"
        };

        var lowerText = normalizedText.ToLower();

        // Check exact matches or starts with
        foreach (var pattern in propertyPatterns)
        {
            if (lowerText == pattern || lowerText.StartsWith(pattern))
                return true;
        }

        // Japanese-specific patterns
        if (textType == TextType.Japanese || textType == TextType.Mixed)
        {
            // Common Japanese blend shape patterns
            var japanesePropertyPatterns = new[]
            {
                // Facial expressions in Japanese
                "笑顔", "笑い", "ウィンク", "まばたき",
                "目", "口", "眉", "頬", "鼻",
                "あ", "い", "う", "え", "お", // Vowel shapes
                "開く", "閉じる", "上", "下", "左", "右",
                // Common suffixes
                "L", "R", "_L", "_R", "左", "右"
            };

            foreach (var pattern in japanesePropertyPatterns)
            {
                if (TextContains(normalizedText, pattern, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Check for common Japanese naming patterns
            // Names ending with directional indicators
            if (normalizedText.EndsWith("L") || normalizedText.EndsWith("R") ||
                normalizedText.EndsWith("_L") || normalizedText.EndsWith("_R") ||
                normalizedText.EndsWith("左") || normalizedText.EndsWith("右"))
                return true;

            // Names ending with numbers (common in Japanese blend shapes)
            if (Regex.IsMatch(normalizedText, @"[0-9]+$"))
                return true;
        }

        // Blend shape patterns (alphanumeric with numbers/L/R at end)
        if (Regex.IsMatch(normalizedText, @"^[a-zA-Z_\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FAF][a-zA-Z0-9_\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FAF]*[0-9LRlr]$", RegexOptions.IgnoreCase))
            return true;

        // Short property names (1-4 chars, all lowercase or mixed case)
        if (normalizedText.Length <= 4 && (normalizedText.All(char.IsLower) || normalizedText.Any(char.IsUpper)))
            return true;

        // Camel case properties (English)
        if (textType == TextType.Latin && Regex.IsMatch(normalizedText, @"^[a-z][a-zA-Z0-9]*$"))
            return true;

        // Japanese character sequences that look like properties
        if (textType == TextType.Japanese && normalizedText.Length <= 6)
            return true;

        return false;
    }

    /// <summary>
    /// Extract clean object name from parsed path with Japanese support
    /// </summary>
    private string GetCleanObjectName(ParsedPath parsed)
    {
        if (!parsed.isValid || string.IsNullOrEmpty(parsed.objectName))
            return "";

        var cleanName = NormalizeText(parsed.objectName.Trim());

        // For "OBJECT : ..." format, return the object directly
        if (parsed.hasObjectSeparator)
        {
            return cleanName;
        }

        // For hierarchy paths, get the deepest object
        if (cleanName.Contains('/'))
        {
            var parts = cleanName.Split('/');
            return NormalizeText(parts[parts.Length - 1]);
        }

        return cleanName;
    }

    /// <summary>
    /// Extract clean property name from parsed path with Japanese support
    /// </summary>
    private string GetCleanPropertyName(ParsedPath parsed)
    {
        if (!parsed.isValid || string.IsNullOrEmpty(parsed.propertyName))
            return "";

        return NormalizeText(parsed.propertyName.Trim());
    }

    /// <summary>
    /// Determines if a property is specifically a "Blend Shape." property
    /// </summary>
    private bool IsBlendShapeProperty(string propertyPath)
    {
        var normalizedPath = NormalizeText(propertyPath);
        return normalizedPath.Contains("blendShape.") || normalizedPath.Contains("Blend Shape.");
    }
    #endregion

    #region Discovery Operations - Mode-Specific with Blend Shape Filtering and Japanese Support
    private void DiscoverObjects()
    {
        if (selectedClips.Count == 0)
        {
            EditorUtility.DisplayDialog("No Clips", "Please add animation clips first.", "OK");
            return;
        }

        if (renameTarget != RenameTarget.Object)
        {
            EditorUtility.DisplayDialog("Wrong Mode", "Please switch to 'Object Name' mode to discover objects.", "OK");
            return;
        }

        discoveredObjects.Clear();

        PerformOperation(OperationState.Scanning, "Discovering Objects", (clip, progress) =>
        {
            EditorUtility.DisplayProgressBar("Discovering Objects", $"Scanning {clip.name}...", progress);

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var normalizedPath = NormalizeText(binding.path);

                // Method 1: Direct extraction from " : " format (most reliable for your case)
                if (normalizedPath.Contains(" : "))
                {
                    var colonIndex = normalizedPath.IndexOf(" : ");
                    var objectName = normalizedPath.Substring(0, colonIndex).Trim();
                    if (!string.IsNullOrEmpty(objectName))
                    {
                        discoveredObjects.Add(objectName);
                    }
                }
                // Method 2: Hierarchy paths like "Armature/Head"
                else if (normalizedPath.Contains("/"))
                {
                    var parts = normalizedPath.Split('/');
                    foreach (var part in parts)
                    {
                        var cleanPart = part.Split('.')[0]; // Remove property part if exists
                        cleanPart = NormalizeText(cleanPart);
                        if (!string.IsNullOrEmpty(cleanPart))
                        {
                            discoveredObjects.Add(cleanPart);
                        }
                    }
                }
                // Method 3: Simple root paths (fallback)
                else if (!normalizedPath.Contains("."))
                {
                    discoveredObjects.Add(normalizedPath);
                }
                // Method 4: Dotted paths - take first part as object
                else if (normalizedPath.Contains("."))
                {
                    var firstDotIndex = normalizedPath.IndexOf('.');
                    var objectPart = normalizedPath.Substring(0, firstDotIndex);
                    objectPart = NormalizeText(objectPart);
                    if (!string.IsNullOrEmpty(objectPart))
                    {
                        discoveredObjects.Add(objectPart);
                    }
                }
            }
        }, () =>
        {
            lastOperationStats = $"Discovered {discoveredObjects.Count} unique objects in {performanceTimer.ElapsedMilliseconds}ms";
            showDiscoveredObjects = true;

            // Debug.Log($"[Discovery] Found {discoveredObjects.Count} objects: {string.Join(", ", discoveredObjects.OrderBy(x => x))}");
        });
    }

    private void DiscoverProperties()
    {
        if (selectedClips.Count == 0)
        {
            EditorUtility.DisplayDialog("No Clips", "Please add animation clips first.", "OK");
            return;
        }

        if (renameTarget != RenameTarget.PropertyName)
        {
            EditorUtility.DisplayDialog("Wrong Mode", "Please switch to 'Property Name' mode to discover properties.", "OK");
            return;
        }

        discoveredPropertyNames.Clear();

        PerformOperation(OperationState.Scanning, "Discovering Properties", (clip, progress) =>
        {
            EditorUtility.DisplayProgressBar("Discovering Properties", $"Scanning {clip.name}...", progress);

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var normalizedPropertyName = NormalizeText(binding.propertyName);

                // Check if this is a blend shape property
                if (normalizedPropertyName.StartsWith("blendShape."))
                {
                    // Extract just the property name after "blendShape."
                    var cleanPropertyName = normalizedPropertyName.Substring("blendShape.".Length);
                    cleanPropertyName = NormalizeText(cleanPropertyName);
                    if (!string.IsNullOrEmpty(cleanPropertyName))
                    {
                        discoveredPropertyNames.Add(cleanPropertyName);
                    }
                }
                else if (normalizedPropertyName.StartsWith("Blend Shape."))
                {
                    // Extract just the property name after "Blend Shape."
                    var cleanPropertyName = normalizedPropertyName.Substring("Blend Shape.".Length);
                    cleanPropertyName = NormalizeText(cleanPropertyName);
                    if (!string.IsNullOrEmpty(cleanPropertyName))
                    {
                        discoveredPropertyNames.Add(cleanPropertyName);
                    }
                }
            }
        }, () =>
        {
            lastOperationStats = $"Discovered {discoveredPropertyNames.Count} unique blend shape properties in {performanceTimer.ElapsedMilliseconds}ms";
            showDiscoveredProperties = true;

            // Debug.Log($"[Discovery] Found {discoveredPropertyNames.Count} blend shape properties: {string.Join(", ", discoveredPropertyNames.OrderBy(x => x).Take(10))}");
        });
    }

    private void PerformOperation(OperationState state, string operation, System.Action<AnimationClip, float> clipAction, System.Action onComplete)
    {
        currentState = state;
        performanceTimer.Restart();

        try
        {
            for (int i = 0; i < selectedClips.Count; i++)
            {
                var clip = selectedClips[i];
                if (clip == null) continue;

                var progress = (float)i / selectedClips.Count;
                clipAction(clip, progress);
            }

            performanceTimer.Stop();
            onComplete?.Invoke();
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog($"{operation} Error", $"Error during {operation.ToLower()}:\n{e.Message}", "OK");
            Debug.LogError($"[{operation}] Error: {e}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            currentState = OperationState.Idle;
            Repaint();
        }
    }
    #endregion

    #region Preview Operations - Fixed Implementation with Japanese Support
    private void PreviewChanges()
    {
        if (!ValidateInput()) return;

        previewChanges.Clear();
        totalPropertiesScanned = 0;

        PerformOperation(OperationState.Previewing, "Previewing Changes", (clip, progress) =>
        {
            EditorUtility.DisplayProgressBar("Previewing Changes", $"Analyzing {clip.name}...", progress);

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                totalPropertiesScanned++;
                var changeInfo = CreatePropertyChangeInfo(clip, binding);
                if (changeInfo != null)
                {
                    previewChanges.Add(changeInfo);
                }
            }
        }, () =>
        {
            var validChanges = previewChanges.Count(p => p.WillChange);
            lastOperationStats = $"Preview: {validChanges} changes found from {totalPropertiesScanned} properties in {performanceTimer.ElapsedMilliseconds}ms";
            showPreview = true;

            Debug.Log($"[Preview] Results: {validChanges} valid changes from {previewChanges.Count} total matches");
            Debug.Log($"[Preview] Target: {renameTarget}, From: '{fromValue}' → To: '{toValue}'");

            if (validChanges > 0)
            {
                var clipGroups = previewChanges.Where(p => p.WillChange).GroupBy(p => p.clip.name);
                foreach (var group in clipGroups)
                {
                    Debug.Log($"[Preview] {group.Key}: {group.Count()} changes");
                }
            }
            else
            {
                Debug.LogWarning($"[Preview] No matches found for {renameTarget}: '{fromValue}'");
            }
        });
    }

    /// <summary>
    /// Enhanced change info creation with proper matching logic and Japanese support
    /// </summary>
    private PropertyChangeInfo CreatePropertyChangeInfo(AnimationClip clip, EditorCurveBinding binding)
    {
        var parsed = ParseAnimationPath(binding.path);

        var changeInfo = new PropertyChangeInfo
        {
            clip = clip,
            originalBinding = binding,
            curve = AnimationUtility.GetEditorCurve(clip, binding),
            oldPath = binding.path,
            objectName = parsed.isValid ? GetCleanObjectName(parsed) : "",
            propertyName = parsed.isValid ? GetCleanPropertyName(parsed) : "",
            middlePart = parsed.isValid ? parsed.middlePart : "",
            textType = parsed.textType
        };

        bool matches = false;
        string newPath = binding.path;
        string newPropertyName = binding.propertyName;

        var comparison = caseSensitive ? System.StringComparison.Ordinal : System.StringComparison.OrdinalIgnoreCase;

        switch (renameTarget)
        {
            case RenameTarget.Object:
                if (TextContains(binding.path, fromValue, comparison))
                {
                    // Replace in the path directly with Japanese support
                    newPath = TextReplace(binding.path, fromValue, toValue, comparison);

                    // Set display name for preview
                    changeInfo.objectName = fromValue;
                    matches = true;
                }
                break;

            case RenameTarget.PropertyName:
                var normalizedPropertyName = NormalizeText(binding.propertyName);

                // Check if this is a blend shape property and if it matches our search
                if (normalizedPropertyName.StartsWith("blendShape."))
                {
                    var cleanPropertyName = normalizedPropertyName.Substring("blendShape.".Length);

                    if (TextEquals(cleanPropertyName, fromValue, comparison))
                    {
                        newPropertyName = "blendShape." + NormalizeText(toValue);
                        changeInfo.propertyName = cleanPropertyName;

                        // Make oldPath and newPath different for display
                        changeInfo.oldPath = binding.propertyName;
                        newPath = newPropertyName;

                        matches = true;
                    }
                }
                else if (normalizedPropertyName.StartsWith("Blend Shape."))
                {
                    var cleanPropertyName = normalizedPropertyName.Substring("Blend Shape.".Length);

                    if (TextEquals(cleanPropertyName, fromValue, comparison))
                    {
                        newPropertyName = "Blend Shape." + NormalizeText(toValue);
                        changeInfo.propertyName = cleanPropertyName;

                        // Make oldPath and newPath different for display
                        changeInfo.oldPath = binding.propertyName;
                        newPath = newPropertyName;

                        matches = true;
                    }
                }
                break;
        }

        if (!matches) return null;

        changeInfo.newPath = newPath;
        changeInfo.newBinding = new EditorCurveBinding
        {
            path = newPath, // For objects, this is the new path. For properties, keep original path
            type = binding.type,
            propertyName = newPropertyName
        };

        // For property renames, we need to keep the original path
        if (renameTarget == RenameTarget.PropertyName)
        {
            changeInfo.newBinding = new EditorCurveBinding
            {
                path = binding.path, // Keep original path for property renames
                type = binding.type,
                propertyName = newPropertyName
            };
        }

        ValidatePropertyChange(changeInfo);
        return changeInfo;
    }

    private void ValidatePropertyChange(PropertyChangeInfo changeInfo)
    {
        changeInfo.validation = ValidationResult.Valid;
        changeInfo.validationMessage = "";

        if (string.IsNullOrEmpty(changeInfo.newPath))
        {
            changeInfo.validation = ValidationResult.Error;
            changeInfo.validationMessage = "Empty path";
            return;
        }

        if (string.IsNullOrEmpty(changeInfo.newBinding.propertyName))
        {
            changeInfo.validation = ValidationResult.Error;
            changeInfo.validationMessage = "Empty property name";
            return;
        }

        // Enhanced validation for property names with Japanese support
        var japaneseValidation = ValidateJapaneseText(changeInfo.newBinding.propertyName, out string japaneseMessage);
        if (japaneseValidation == ValidationResult.Error)
        {
            changeInfo.validation = ValidationResult.Error;
            changeInfo.validationMessage = japaneseMessage;
            return;
        }
        else if (japaneseValidation == ValidationResult.Warning)
        {
            changeInfo.validation = ValidationResult.Warning;
            changeInfo.validationMessage = japaneseMessage;
        }

        // Additional Unity-specific validation
        if (changeInfo.newBinding.propertyName.IndexOfAny(new[] { '\t', '\n' }) >= 0 ||
            changeInfo.newBinding.propertyName.StartsWith(".") ||
            changeInfo.newBinding.propertyName.EndsWith("."))
        {
            changeInfo.validation = ValidationResult.Error;
            changeInfo.validationMessage = "Invalid property name format";
            return;
        }

        if (BindingExists(changeInfo.clip, changeInfo.newBinding, changeInfo.originalBinding))
        {
            changeInfo.validation = ValidationResult.Warning;
            changeInfo.validationMessage = "Binding already exists";
        }
    }

    private bool BindingExists(AnimationClip clip, EditorCurveBinding newBinding, EditorCurveBinding excludeBinding)
    {
        return AnimationUtility.GetCurveBindings(clip).Any(b =>
            b.path == newBinding.path &&
            b.propertyName == newBinding.propertyName &&
            b.type == newBinding.type &&
            !BindingsAreEqual(b, excludeBinding));
    }

    private bool BindingsAreEqual(EditorCurveBinding a, EditorCurveBinding b) =>
        a.path == b.path && a.propertyName == b.propertyName && a.type == b.type;
    #endregion

    #region Main UI Framework with Japanese Support
    private void DrawMainInterface()
    {
        using (new EditorGUILayout.VerticalScope())
        {
            DrawHeader();
            DrawModeSelection();
            DrawConfigurationPanel();
            DrawClipManagement();
            DrawDiscoveryPanel();
            DrawActionBar();
            DrawPreviewPanel();
            DrawStatusFooter();
        }

        HandleGlobalDragAndDrop();
    }

    private void DrawHeader()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("RΩKΔ's Animation Renamer", EditorStyles.largeLabel);
                GUILayout.FlexibleSpace();

                if (DrawIconButton("_Help", "Show/Hide Help", 24))
                    showHelp = !showHelp;
            }

            var description = renameTarget == RenameTarget.Object
                ? "Rename OBJECT in 'OBJECT : Skinned Mesh Renderer.Blend Shape.PropertyName'"
                : "Rename PropertyName in 'OBJECT : Skinned Mesh Renderer.Blend Shape.PropertyName'";

            GUILayout.Label(description, EditorStyles.miniLabel);
            GUILayout.Label("✨ Enhanced with Japanese character support (日本語対応)", EditorStyles.miniLabel);

            if (showHelp) DrawHelpSection();
        }

        EditorGUILayout.Space(3);
    }

    private void DrawHelpSection()
    {
        using (new EditorGUILayout.VerticalScope("helpbox"))
        {
            EditorGUILayout.LabelField("🎯 Quick Start Guide", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("1. Add animation clips (drag & drop or buttons)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("2. Choose Object or Property mode", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("3. Discover available items", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("4. Click discovered items to fill 'From' field", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("5. Enter 'To' value and apply changes", EditorStyles.miniLabel);

            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("⌨️ Shortcuts: Ctrl+D (Add clips) | Ctrl+P (Preview) | Ctrl+Enter (Apply)", EditorStyles.miniLabel);

            EditorGUILayout.Space(3);
            showJapaneseHelp = EditorGUILayout.Foldout(showJapaneseHelp, "🇯🇵 Japanese Character Support", true);
            if (showJapaneseHelp)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("• Supports Hiragana (ひらがな), Katakana (カタカナ), Kanji (漢字)", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("• Unicode normalization for consistent matching", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("• Common Japanese blend shape patterns recognized", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("• Mixed Japanese/English text supported", EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }
        }
    }

    private void DrawModeSelection()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Rename Target", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            selectedTabIndex = GUILayout.Toolbar(selectedTabIndex, new[] { "Object Name", "Property Name" },
                GUILayout.Height(25));

            if (EditorGUI.EndChangeCheck())
            {
                renameTarget = (RenameTarget)selectedTabIndex;
                ClearPreview();
                ClearDiscoveredData();
            }
        }

        EditorGUILayout.Space(3);
    }

    #region Configuration Panel with Japanese Options
    private void DrawConfigurationPanel()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);

            DrawValueFields();
            DrawAdvancedOptions();
            DrawExampleSection();
        }

        EditorGUILayout.Space(3);
    }

    private void DrawValueFields()
    {
        EditorGUI.BeginChangeCheck();

        var fromLabel = renameTarget == RenameTarget.Object ? "From Object:" : "From Property:";
        var toLabel = renameTarget == RenameTarget.Object ? "To Object:" : "To Property:";

        // From field with enhanced UI and Japanese support
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(fromLabel, GUILayout.Width(100));

            var fromStyle = GetTextFieldStyle(fromValue);
            fromValue = EditorGUILayout.TextField(fromValue, fromStyle);

            // Show text type indicator
            if (!string.IsNullOrEmpty(fromValue))
            {
                var textType = DetermineTextType(fromValue);
                var typeColor = GetTextTypeColor(textType);
                var typeLabel = GetTextTypeLabel(textType);
                EditorGUILayout.LabelField(typeLabel, CreateLabelStyle(typeColor), GUILayout.Width(30));
            }

            if (DrawIconButton("Refresh", "Clear field", 24))
            {
                fromValue = "";
                GUI.FocusControl(null);
            }
        }

        // To field with Japanese support
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(toLabel, GUILayout.Width(100));
            toValue = EditorGUILayout.TextField(toValue);

            // Show text type indicator
            if (!string.IsNullOrEmpty(toValue))
            {
                var textType = DetermineTextType(toValue);
                var typeColor = GetTextTypeColor(textType);
                var typeLabel = GetTextTypeLabel(textType);
                EditorGUILayout.LabelField(typeLabel, CreateLabelStyle(typeColor), GUILayout.Width(30));
            }

            if (DrawIconButton("Refresh", "Clear field", 24))
            {
                toValue = "";
                GUI.FocusControl(null);
            }
        }

        if (EditorGUI.EndChangeCheck())
        {
            lastPreviewTime = Time.realtimeSinceStartup;
            if (!autoPreview) ClearPreview();
        }
    }

    private GUIStyle GetTextFieldStyle(string text)
    {
        if (string.IsNullOrEmpty(text))
            return EditorStyles.textField;

        bool isDiscovered = discoveredObjects.Contains(text) || discoveredPropertyNames.Contains(text);
        if (isDiscovered)
            return CreateTextFieldStyle(SUCCESS_COLOR);

        var textType = DetermineTextType(text);
        return textType == TextType.Japanese ? CreateTextFieldStyle(JAPANESE_COLOR) :
               CreateTextFieldStyle(WARNING_COLOR);
    }

    private Color GetTextTypeColor(TextType textType)
    {
        return textType switch
        {
            TextType.Japanese => JAPANESE_COLOR,
            TextType.Mixed => new Color(0.8f, 0.6f, 0.9f),
            TextType.Latin => Color.white,
            _ => Color.gray
        };
    }

    private string GetTextTypeLabel(TextType textType)
    {
        return textType switch
        {
            TextType.Japanese => "日",
            TextType.Mixed => "混",
            TextType.Latin => "En",
            _ => "?"
        };
    }

    private void DrawAdvancedOptions()
    {
        showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced Options", true);

        if (showAdvanced)
        {
            EditorGUI.indentLevel++;

            EditorGUI.BeginChangeCheck();

            caseSensitive = EditorGUILayout.Toggle("Case Sensitive:", caseSensitive);
            autoPreview = EditorGUILayout.Toggle("Auto Preview:", autoPreview);

            // Japanese-specific options
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("Japanese Support", EditorStyles.boldLabel);
            enableJapaneseNormalization = EditorGUILayout.Toggle("Unicode Normalization:", enableJapaneseNormalization);

            if (!enableJapaneseNormalization)
            {
                EditorGUILayout.HelpBox("Disable only if experiencing issues. Normalization ensures consistent Japanese character handling.", MessageType.Info);
            }

            if (EditorGUI.EndChangeCheck())
            {
                if (autoPreview) lastPreviewTime = Time.realtimeSinceStartup;
                else ClearPreview();
            }

            EditorGUILayout.Space(3);
            backupStrategy = (BackupStrategy)EditorGUILayout.EnumPopup("Backup Strategy:", backupStrategy);

            var backupMsg = backupStrategy switch
            {
                BackupStrategy.None => ("⚠️ No backups - changes cannot be undone!", MessageType.Warning),
                BackupStrategy.Temporary => ("Create backups, auto-delete on success", MessageType.Info),
                BackupStrategy.Permanent => ("Create backups, keep permanently", MessageType.Info),
                _ => ("", MessageType.None)
            };

            if (!string.IsNullOrEmpty(backupMsg.Item1))
                EditorGUILayout.HelpBox(backupMsg.Item1, backupMsg.Item2);

            EditorGUI.indentLevel--;
        }
    }

    private void DrawExampleSection()
    {
        if (string.IsNullOrEmpty(fromValue) || string.IsNullOrEmpty(toValue)) return;

        EditorGUILayout.Space(3);
        EditorGUILayout.LabelField("Preview Transformation:", EditorStyles.boldLabel);

        if (TextEquals(fromValue, toValue, System.StringComparison.OrdinalIgnoreCase))
        {
            EditorGUILayout.HelpBox("From and To values are identical - no changes will occur!", MessageType.Warning);
        }
        else
        {
            var normalizedFrom = NormalizeText(fromValue);
            var normalizedTo = NormalizeText(toValue);

            var example = renameTarget == RenameTarget.Object
                ? $"{normalizedFrom} : Skinned Mesh Renderer.Blend Shape.Example\n→\n{normalizedTo} : Skinned Mesh Renderer.Blend Shape.Example"
                : $"Example : Skinned Mesh Renderer.Blend Shape.{normalizedFrom}\n→\nExample : Skinned Mesh Renderer.Blend Shape.{normalizedTo}";

            EditorGUILayout.LabelField(example, EditorStyles.helpBox);

            // Show text type information
            var fromType = DetermineTextType(fromValue);
            var toType = DetermineTextType(toValue);
            if (fromType != TextType.Latin || toType != TextType.Latin)
            {
                var typeInfo = $"Text types: {GetTextTypeLabel(fromType)} → {GetTextTypeLabel(toType)}";
                EditorGUILayout.LabelField(typeInfo, EditorStyles.miniLabel);
            }
        }
    }
    #endregion

    #region Discovery Panel with Japanese Filtering
    private void DrawDiscoveryPanel()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Discovery", EditorStyles.boldLabel);

            // Discovery buttons - mode-specific
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = selectedClips.Count > 0 && currentState == OperationState.Idle && renameTarget == RenameTarget.Object;
                if (GUILayout.Button("🔍 Discover Objects", GUILayout.Height(28)))
                    DiscoverObjects();

                GUI.enabled = selectedClips.Count > 0 && currentState == OperationState.Idle && renameTarget == RenameTarget.PropertyName;
                if (GUILayout.Button("🔍 Discover Properties", GUILayout.Height(28)))
                    DiscoverProperties();

                GUI.enabled = true;
            }

            // Mode indicator
            var modeText = renameTarget == RenameTarget.Object ? "Object discovery mode" : "Property discovery mode";
            EditorGUILayout.LabelField(modeText, EditorStyles.miniLabel);

            // Search filter
            if (discoveredObjects.Count > 0 || discoveredPropertyNames.Count > 0)
            {
                EditorGUILayout.Space(3);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Search:", GUILayout.Width(45));
                    searchFilter = EditorGUILayout.TextField(searchFilter);

                    if (GUILayout.Button("Clear", GUILayout.Width(50)))
                        searchFilter = "";
                }
            }

            // Discovery results side by side
            using (new EditorGUILayout.HorizontalScope())
            {
                if (renameTarget == RenameTarget.Object)
                {
                    DrawDiscoveredObjects();
                    DrawEmptyPropertiesPanel();
                }
                else
                {
                    DrawEmptyObjectsPanel();
                    DrawDiscoveredProperties();
                }
            }
        }

        EditorGUILayout.Space(3);
    }

    private void DrawDiscoveredObjects()
    {
        using (new EditorGUILayout.VerticalScope("box", GUILayout.Width(position.width / 2 - 25)))
        {
            showDiscoveredObjects = EditorGUILayout.Foldout(showDiscoveredObjects,
                $"Objects ({discoveredObjects.Count})", true);

            if (showDiscoveredObjects && discoveredObjects.Count > 0)
            {
                using (var scrollView = new EditorGUILayout.ScrollViewScope(objectsScrollPos, GUILayout.Height(120)))
                {
                    objectsScrollPos = scrollView.scrollPosition;

                    var filtered = GetFilteredObjects();

                    foreach (var obj in filtered)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            var isSelected = obj == fromValue;
                            var textType = DetermineTextType(obj);
                            var style = isSelected ? CreateLabelStyle(SUCCESS_COLOR) :
                                       textType == TextType.Japanese ? CreateLabelStyle(JAPANESE_COLOR) :
                                       EditorStyles.miniLabel;

                            if (GUILayout.Button(obj, style, GUILayout.ExpandWidth(true)))
                            {
                                fromValue = obj;
                                ClearPreview();
                                GUI.FocusControl(null);
                            }

                            // Show text type indicator
                            if (textType != TextType.Latin)
                            {
                                var typeLabel = GetTextTypeLabel(textType);
                                var typeColor = GetTextTypeColor(textType);
                                EditorGUILayout.LabelField(typeLabel, CreateLabelStyle(typeColor), GUILayout.Width(20));
                            }
                        }
                    }
                }
            }
            else if (showDiscoveredObjects)
            {
                EditorGUILayout.LabelField("No objects discovered yet", EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.LabelField("Click 'Discover Objects' above", EditorStyles.centeredGreyMiniLabel);
            }
        }
    }

    private void DrawDiscoveredProperties()
    {
        using (new EditorGUILayout.VerticalScope("box", GUILayout.Width(position.width / 2 - 25)))
        {
            showDiscoveredProperties = EditorGUILayout.Foldout(showDiscoveredProperties,
                $"Properties ({discoveredPropertyNames.Count})", true);

            if (showDiscoveredProperties && discoveredPropertyNames.Count > 0)
            {
                using (var scrollView = new EditorGUILayout.ScrollViewScope(propertiesScrollPos, GUILayout.Height(120)))
                {
                    propertiesScrollPos = scrollView.scrollPosition;

                    var filtered = GetFilteredProperties();

                    foreach (var prop in filtered)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            var isSelected = prop == fromValue;
                            var textType = DetermineTextType(prop);
                            var style = isSelected ? CreateLabelStyle(SUCCESS_COLOR) :
                                       textType == TextType.Japanese ? CreateLabelStyle(JAPANESE_COLOR) :
                                       CreateLabelStyle(BLEND_SHAPE_COLOR);

                            if (GUILayout.Button(prop, style, GUILayout.ExpandWidth(true)))
                            {
                                fromValue = prop;
                                ClearPreview();
                                GUI.FocusControl(null);
                            }

                            // Show text type indicator
                            if (textType != TextType.Latin)
                            {
                                var typeLabel = GetTextTypeLabel(textType);
                                var typeColor = GetTextTypeColor(textType);
                                EditorGUILayout.LabelField(typeLabel, CreateLabelStyle(typeColor), GUILayout.Width(20));
                            }
                        }
                    }
                }
            }
            else if (showDiscoveredProperties)
            {
                EditorGUILayout.LabelField("No properties discovered yet", EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.LabelField("Click 'Discover Properties' above", EditorStyles.centeredGreyMiniLabel);
            }
        }
    }

    private void DrawEmptyObjectsPanel()
    {
        using (new EditorGUILayout.VerticalScope("box", GUILayout.Width(position.width / 2 - 25)))
        {
            EditorGUILayout.LabelField("Objects (0)", EditorStyles.foldout);
            EditorGUILayout.LabelField("Switch to Object mode to discover", EditorStyles.centeredGreyMiniLabel);
        }
    }

    private void DrawEmptyPropertiesPanel()
    {
        using (new EditorGUILayout.VerticalScope("box", GUILayout.Width(position.width / 2 - 25)))
        {
            EditorGUILayout.LabelField("Properties (0)", EditorStyles.foldout);
            EditorGUILayout.LabelField("Switch to Property mode to discover", EditorStyles.centeredGreyMiniLabel);
        }
    }

    private System.Collections.Generic.IEnumerable<string> GetFilteredObjects()
    {
        if (string.IsNullOrEmpty(searchFilter))
        {
            return discoveredObjects.OrderBy(x => x);
        }

        var normalizedFilter = NormalizeText(searchFilter);
        return discoveredObjects
            .Where(x => TextContains(x, normalizedFilter, System.StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x);
    }

    private System.Collections.Generic.IEnumerable<string> GetFilteredProperties()
    {
        if (string.IsNullOrEmpty(searchFilter))
        {
            return discoveredPropertyNames.OrderBy(x => x);
        }

        var normalizedFilter = NormalizeText(searchFilter);
        return discoveredPropertyNames
            .Where(x => TextContains(x, normalizedFilter, System.StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x);
    }
    #endregion

    #region Action Bar & Preview Panel
    private void DrawActionBar()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var canPreview = CanPreview();
                var canApply = CanApply();

                // Preview button
                GUI.enabled = canPreview;
                if (GUILayout.Button("👀 Preview Changes", GUILayout.Height(35)))
                    PreviewChanges();

                // Apply button with visual emphasis
                GUI.enabled = canApply;
                var applyStyle = canApply ? CreateButtonStyle(SUCCESS_COLOR) : GUI.skin.button;

                if (GUILayout.Button("✓ Apply Changes", applyStyle, GUILayout.Height(35)))
                    ApplyChanges();

                GUI.enabled = true;
            }

            // Operation status
            if (currentState != OperationState.Idle)
            {
                EditorGUILayout.Space(3);
                var statusText = currentState switch
                {
                    OperationState.Scanning => "🔍 Scanning properties...",
                    OperationState.Previewing => "👀 Generating preview...",
                    OperationState.Processing => "⚙️ Applying changes...",
                    _ => ""
                };

                EditorGUILayout.LabelField(statusText, EditorStyles.centeredGreyMiniLabel);

                // Progress bar simulation
                var rect = GUILayoutUtility.GetRect(0, 3, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(rect, ACCENT_COLOR * 0.5f);
            }
        }

        EditorGUILayout.Space(3);
    }

    private void DrawPreviewPanel()
    {
        if (!showPreview || previewChanges.Count == 0) return;

        using (new EditorGUILayout.VerticalScope("box"))
        {
            DrawPreviewHeader();
            DrawPreviewContent();
        }
    }

    private void DrawPreviewHeader()
    {
        EditorGUILayout.LabelField("📋 Preview Changes", EditorStyles.boldLabel);

        var validChanges = previewChanges.Count(p => p.WillChange);
        var warnings = previewChanges.Count(p => p.HasWarning);
        var errors = previewChanges.Count(p => p.validation == ValidationResult.Error);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"Total Found: {previewChanges.Count}", GUILayout.Width(100));

            if (validChanges > 0)
            {
                var style = CreateLabelStyle(SUCCESS_COLOR);
                EditorGUILayout.LabelField($"✓ Will Change: {validChanges}", style, GUILayout.Width(120));
            }

            if (warnings > 0)
            {
                var style = CreateLabelStyle(WARNING_COLOR);
                EditorGUILayout.LabelField($"⚠ Warnings: {warnings}", style, GUILayout.Width(100));
            }

            if (errors > 0)
            {
                var style = CreateLabelStyle(ERROR_COLOR);
                EditorGUILayout.LabelField($"✗ Errors: {errors}", style, GUILayout.Width(80));
            }
        }

        if (validChanges == 0)
        {
            var targetText = renameTarget == RenameTarget.Object ? "objects" : "properties";
            EditorGUILayout.HelpBox($"No {targetText} named '{fromValue}' found in selected clips.", MessageType.Warning);
        }
    }

    private void DrawPreviewContent()
    {
        var validChanges = previewChanges.Where(p => p.WillChange).ToList();
        if (validChanges.Count == 0) return;

        using (var scrollView = new EditorGUILayout.ScrollViewScope(previewScrollPos, GUILayout.Height(200)))
        {
            previewScrollPos = scrollView.scrollPosition;

            // Group changes by clip for better organization
            var groupedChanges = validChanges.Take(MAX_PREVIEW_ITEMS).GroupBy(p => p.clip);

            foreach (var clipGroup in groupedChanges)
                DrawClipPreview(clipGroup.Key, clipGroup.ToList());

            if (validChanges.Count > MAX_PREVIEW_ITEMS)
            {
                EditorGUILayout.HelpBox($"Showing first {MAX_PREVIEW_ITEMS} of {validChanges.Count} changes. All will be applied.", MessageType.Info);
            }
        }
    }

    private void DrawClipPreview(AnimationClip clip, List<PropertyChangeInfo> changes)
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            // Clip header with icon and stats
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(EditorGUIUtility.IconContent("AnimationClip Icon"), GUILayout.Width(18));
                EditorGUILayout.LabelField(clip.name, EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"({changes.Count} changes)", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"{clip.length:F1}s", EditorStyles.miniLabel, GUILayout.Width(40));
            }

            // Changes preview with better formatting
            EditorGUI.indentLevel++;

            // Show sample of changes (max 8 per clip to avoid UI overflow)
            var displayChanges = changes.Take(8).ToList();
            foreach (var change in displayChanges)
                DrawPropertyChangePreview(change);

            if (changes.Count > 8)
            {
                EditorGUILayout.LabelField($"... and {changes.Count - 8} more changes", EditorStyles.miniLabel);
            }

            EditorGUI.indentLevel--;
        }
    }

    private void DrawPropertyChangePreview(PropertyChangeInfo change)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            // Status icon
            var statusIcon = change.validation switch
            {
                ValidationResult.Valid => "✓",
                ValidationResult.Warning => "⚠",
                ValidationResult.Error => "✗",
                _ => "?"
            };

            var iconStyle = change.validation switch
            {
                ValidationResult.Valid => CreateLabelStyle(SUCCESS_COLOR),
                ValidationResult.Warning => CreateLabelStyle(WARNING_COLOR),
                ValidationResult.Error => CreateLabelStyle(ERROR_COLOR),
                _ => EditorStyles.label
            };

            EditorGUILayout.LabelField(statusIcon, iconStyle, GUILayout.Width(15));

            // Change description - show FROM → TO with Japanese support
            string changeText;
            if (renameTarget == RenameTarget.Object)
            {
                var normalizedFrom = NormalizeText(change.objectName);
                var normalizedTo = NormalizeText(toValue);
                changeText = $"{normalizedFrom} → {normalizedTo}";
            }
            else
            {
                var normalizedFrom = NormalizeText(change.propertyName);
                var normalizedTo = NormalizeText(toValue);
                changeText = $"{normalizedFrom} → {normalizedTo}";
            }

            // Show text type if Japanese
            var textType = DetermineTextType(changeText);
            var textStyle = textType == TextType.Japanese ? CreateLabelStyle(JAPANESE_COLOR) : EditorStyles.label;

            EditorGUILayout.LabelField(changeText, textStyle, GUILayout.Width(200));

            // Key count with more space
            EditorGUILayout.LabelField($"({change.curve.keys.Length} keys)", EditorStyles.miniLabel, GUILayout.Width(80));

            // Validation message
            if (change.validation != ValidationResult.Valid && !string.IsNullOrEmpty(change.validationMessage))
            {
                var msgStyle = CreateLabelStyle(change.validation == ValidationResult.Warning ? WARNING_COLOR : ERROR_COLOR);
                EditorGUILayout.LabelField(change.validationMessage, msgStyle);
            }
        }
    }
    #endregion

    #region Apply Changes - Complete Implementation with Japanese Support
    private void ApplyChanges()
    {
        if (!showPreview || !previewChanges.Any(p => p.WillChange))
        {
            EditorUtility.DisplayDialog("No Changes", "Please preview changes first.", "OK");
            return;
        }

        var validChanges = previewChanges.Where(p => p.WillChange && p.validation != ValidationResult.Error).ToList();
        var errors = previewChanges.Count(p => p.validation == ValidationResult.Error);

        if (errors > 0 && !EditorUtility.DisplayDialog("Validation Issues",
            $"Found {errors} invalid changes that will be skipped.\nProceed with {validChanges.Count} valid changes?",
            "Continue", "Cancel"))
            return;

        if (!ConfirmOperation(validChanges.Count)) return;

        var result = ExecuteChanges(validChanges);
        ShowOperationResults(result);

        if (result.errorCount == 0) ClearPreview();
    }

    private bool ConfirmOperation(int changeCount)
    {
        var clipsAffected = previewChanges.Where(p => p.WillChange && p.validation != ValidationResult.Error)
                                        .Select(p => p.clip).Distinct().Count();

        var targetText = renameTarget == RenameTarget.Object ? "object" : "property";
        var normalizedFrom = NormalizeText(fromValue);
        var normalizedTo = NormalizeText(toValue);

        var message = $"Apply {changeCount} {targetText} changes across {clipsAffected} clips?\n\n" +
                     $"From: '{normalizedFrom}' → To: '{normalizedTo}'\n\n";

        // Show Japanese text type information
        var fromType = DetermineTextType(fromValue);
        var toType = DetermineTextType(toValue);
        if (fromType != TextType.Latin || toType != TextType.Latin)
        {
            message += $"Text types: {GetTextTypeLabel(fromType)} → {GetTextTypeLabel(toType)}\n\n";
        }

        message += backupStrategy switch
        {
            BackupStrategy.None => "⚠️ NO BACKUPS will be created!",
            BackupStrategy.Temporary => "Temporary backups will be auto-deleted on success.",
            BackupStrategy.Permanent => "Permanent backups will be retained.",
            _ => ""
        };

        return EditorUtility.DisplayDialog("Confirm Changes", message, "Apply", "Cancel");
    }

    private OperationResult ExecuteChanges(List<PropertyChangeInfo> validChanges)
    {
        var result = new OperationResult { totalProperties = validChanges.Count };
        backupPaths.Clear();
        currentState = OperationState.Processing;

        try
        {
            performanceTimer.Restart();
            AssetDatabase.StartAssetEditing();

            var clipGroups = validChanges.GroupBy(c => c.clip).ToList();

            for (int groupIndex = 0; groupIndex < clipGroups.Count; groupIndex++)
            {
                var clipGroup = clipGroups[groupIndex];
                var clip = clipGroup.Key;

                var progress = (float)groupIndex / clipGroups.Count;
                EditorUtility.DisplayProgressBar("Applying Changes", $"Processing {clip.name}...", progress);

                try
                {
                    // Create backup
                    if (backupStrategy != BackupStrategy.None)
                    {
                        var backupPath = CreateBackup(clip);
                        if (!string.IsNullOrEmpty(backupPath))
                            backupPaths[clip] = backupPath;
                    }

                    // Record undo
                    Undo.RecordObject(clip, $"Rename Animation {(renameTarget == RenameTarget.Object ? "Objects" : "Properties")}");

                    // Apply changes
                    foreach (var change in clipGroup)
                    {
                        try
                        {
                            // Remove old binding
                            AnimationUtility.SetEditorCurve(clip, change.originalBinding, null);

                            // Add new binding
                            AnimationUtility.SetEditorCurve(clip, change.newBinding, change.curve);

                            if (change.validation == ValidationResult.Warning)
                            {
                                result.warningCount++;
                                result.warnings.Add($"{clip.name}: {change.validationMessage}");
                            }
                        }
                        catch (System.Exception e)
                        {
                            result.errors.Add($"{clip.name} - {change.oldPath}: {e.Message}");
                        }
                    }

                    EditorUtility.SetDirty(clip);
                    result.successCount++;
                }
                catch (System.Exception e)
                {
                    result.errorCount++;
                    result.errors.Add($"{clip.name}: {e.Message}");
                }
            }

            performanceTimer.Stop();
            result.duration = performanceTimer.Elapsed;
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.ClearProgressBar();
            currentState = OperationState.Idle;
        }

        // Cleanup temporary backups on success
        if (backupStrategy == BackupStrategy.Temporary && result.errorCount == 0)
            CleanupTemporaryBackups();

        return result;
    }

    private string CreateBackup(AnimationClip clip)
    {
        try
        {
            var assetPath = AssetDatabase.GetAssetPath(clip);
            var directory = Path.GetDirectoryName(assetPath);
            var fileName = Path.GetFileNameWithoutExtension(assetPath);
            var extension = Path.GetExtension(assetPath);

            var timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupPath = Path.Combine(directory, $"{fileName}_backup_{timestamp}{extension}");

            int counter = 1;
            var originalPath = backupPath;
            while (File.Exists(backupPath))
            {
                backupPath = originalPath.Replace($"_backup_{timestamp}", $"_backup_{timestamp}_{counter}");
                counter++;
            }

            return AssetDatabase.CopyAsset(assetPath, backupPath) ? backupPath : null;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Backup failed for {clip.name}: {e.Message}");
            return null;
        }
    }

    private void CleanupTemporaryBackups()
    {
        foreach (var kvp in backupPaths)
        {
            try
            {
                if (AssetDatabase.DeleteAsset(kvp.Value))
                    Debug.Log($"Deleted temporary backup: {kvp.Value}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Failed to delete backup {kvp.Value}: {e.Message}");
            }
        }
        backupPaths.Clear();
    }

    private void ShowOperationResults(OperationResult result)
    {
        var targetText = renameTarget == RenameTarget.Object ? "objects" : "properties";
        var normalizedFrom = NormalizeText(fromValue);
        var normalizedTo = NormalizeText(toValue);

        var message = $"Operation completed in {result.duration.TotalMilliseconds:F0}ms\n\n" +
                     $"✓ Clips processed: {result.successCount}\n" +
                     $"✓ {targetText.Substring(0, 1).ToUpper()}{targetText.Substring(1)} renamed: {result.totalProperties - result.errors.Count}\n" +
                     $"From: '{normalizedFrom}' → To: '{normalizedTo}'\n";

        if (result.warningCount > 0)
            message += $"⚠ Warnings: {result.warningCount}\n";

        if (result.errorCount > 0)
            message += $"✗ Errors: {result.errorCount}\n";

        if (backupPaths.Count > 0)
        {
            message += backupStrategy == BackupStrategy.Temporary ?
                $"\n🗑 Cleaned up {backupPaths.Count} temporary backups" :
                $"\n💾 Created {backupPaths.Count} backup files";
        }

        if (result.errors.Count > 0)
        {
            message += "\n\nErrors:\n" + string.Join("\n", result.errors.Take(3));
            if (result.errors.Count > 3)
                message += $"\n... and {result.errors.Count - 3} more (check console)";
        }

        lastOperationStats = $"Last: {result.successCount} clips, {result.totalProperties} {targetText} in {result.duration.TotalMilliseconds:F0}ms";
        EditorUtility.DisplayDialog("Operation Complete", message, "OK");
    }
    #endregion

    #region Clip Management - Complete Implementation
    private void DrawClipManagement()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            // Header with stats
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Animation Clips ({selectedClips.Count})", EditorStyles.boldLabel);

                if (selectedClips.Count > 0)
                {
                    var totalProps = selectedClips.Where(c => c != null).Sum(GetPropertyCount);
                    EditorGUILayout.LabelField($"({totalProps} total properties)", EditorStyles.miniLabel);
                }

                GUILayout.FlexibleSpace();

                // Action buttons
                if (GUILayout.Button("Add Selected", GUILayout.Width(90)))
                    AddSelectedClipsFromProject();

                if (GUILayout.Button("Add Folder", GUILayout.Width(80)))
                    AddClipsFromSelectedFolder();

                if (selectedClips.Count > 0 && GUILayout.Button("Clear All", GUILayout.Width(70)))
                    ClearAllClips();
            }

            DrawClipDropZone();
            DrawClipList();
        }

        EditorGUILayout.Space(3);
    }

    private void DrawClipDropZone()
    {
        var dropRect = GUILayoutUtility.GetRect(0, 45, GUILayout.ExpandWidth(true));
        var isDragValid = IsDragValid();

        var style = new GUIStyle("box") { alignment = TextAnchor.MiddleCenter, fontSize = 11 };

        if (isDragValid)
        {
            EditorGUI.DrawRect(dropRect, ACCENT_COLOR * 0.3f);
            GUI.Label(dropRect, "📁 Drop Animation Clips Here", style);
        }
        else
        {
            GUI.Box(dropRect, "📁 Drag Animation Clips or Folders Here\n(Or use buttons above)", style);
        }

        HandleDragAndDrop(dropRect);
    }

    private void DrawClipList()
    {
        if (selectedClips.Count == 0)
        {
            EditorGUILayout.HelpBox("No animation clips selected. Add clips using the buttons above or drag them here.", MessageType.Info);
            return;
        }

        using (var scrollView = new EditorGUILayout.ScrollViewScope(clipsScrollPos, GUILayout.Height(System.Math.Min(130, selectedClips.Count * 22 + 10))))
        {
            clipsScrollPos = scrollView.scrollPosition;

            for (int i = 0; i < selectedClips.Count; i++)
                DrawClipItem(i);
        }
    }

    private void DrawClipItem(int index)
    {
        var clip = selectedClips[index];

        using (new EditorGUILayout.HorizontalScope("box"))
        {
            if (clip != null)
            {
                EditorGUILayout.LabelField(EditorGUIUtility.IconContent("AnimationClip Icon"), GUILayout.Width(18));
                EditorGUILayout.LabelField(clip.name, GUILayout.ExpandWidth(true));
                EditorGUILayout.LabelField($"{clip.length:F1}s", GUILayout.Width(40));

                var propCount = GetPropertyCount(clip);
                if (propCount > 0)
                    EditorGUILayout.LabelField($"({propCount})", EditorStyles.miniLabel, GUILayout.Width(50));
            }
            else
            {
                EditorGUILayout.LabelField("⚠ Missing Clip", EditorStyles.miniLabel);
            }

            if (GUILayout.Button("×", GUILayout.Width(22)))
                RemoveClipAt(index);
        }
    }

    private void AddSelectedClipsFromProject()
    {
        var selected = Selection.objects.OfType<AnimationClip>().ToList();
        if (selected.Count == 0)
        {
            EditorUtility.DisplayDialog("No Clips Selected", "Please select animation clips in the Project view.", "OK");
            return;
        }

        int addedCount = selected.Count(AddClip);
        if (addedCount > 0)
        {
            ClearPreview();
            ClearDiscoveredData();
            Debug.Log($"Added {addedCount} animation clips");
        }
    }

    private void AddClipsFromSelectedFolder()
    {
        var selectedFolder = Selection.activeObject;
        if (selectedFolder == null || !AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(selectedFolder)))
        {
            EditorUtility.DisplayDialog("Invalid Selection", "Please select a folder in the Project view.", "OK");
            return;
        }

        var folderPath = AssetDatabase.GetAssetPath(selectedFolder);
        var clipGuids = AssetDatabase.FindAssets("t:AnimationClip", new[] { folderPath });
        int addedCount = 0;

        foreach (var guid in clipGuids)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
            if (clip != null && AddClip(clip)) addedCount++;
        }

        if (addedCount > 0)
        {
            ClearPreview();
            ClearDiscoveredData();
            Debug.Log($"Added {addedCount} clips from {folderPath}");
        }
        else
        {
            EditorUtility.DisplayDialog("No Clips Found", $"No animation clips found in {folderPath}", "OK");
        }
    }

    private bool AddClip(AnimationClip clip)
    {
        if (clip == null || selectedClips.Contains(clip)) return false;
        selectedClips.Add(clip);
        return true;
    }

    private void RemoveClipAt(int index)
    {
        if (index >= 0 && index < selectedClips.Count)
        {
            selectedClips.RemoveAt(index);
            ClearPreview();
            ClearDiscoveredData();
        }
    }

    private void ClearAllClips()
    {
        if (EditorUtility.DisplayDialog("Clear All Clips",
            $"Remove all {selectedClips.Count} clips?", "Clear", "Cancel"))
        {
            selectedClips.Clear();
            ClearPreview();
            ClearDiscoveredData();
        }
    }

    private int GetPropertyCount(AnimationClip clip) =>
        clip == null ? 0 : AnimationUtility.GetCurveBindings(clip).Length;
    #endregion

    #region Drag & Drop System
    private void HandleGlobalDragAndDrop()
    {
        var evt = Event.current;
        if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
            HandleDragAndDrop(new Rect(0, 0, position.width, position.height));
    }

    private void HandleDragAndDrop(Rect dropArea)
    {
        var evt = Event.current;
        if (!dropArea.Contains(evt.mousePosition)) return;

        switch (evt.type)
        {
            case EventType.DragUpdated:
                DragAndDrop.visualMode = IsDragValid() ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                break;

            case EventType.DragPerform:
                if (IsDragValid())
                {
                    DragAndDrop.AcceptDrag();
                    ProcessDraggedObjects();
                    ClearPreview();
                    ClearDiscoveredData();
                }
                break;
        }
    }

    private bool IsDragValid() => DragAndDrop.objectReferences.Any(obj =>
        obj is AnimationClip || (obj != null && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(obj))));

    private void ProcessDraggedObjects()
    {
        int addedCount = 0;
        foreach (var obj in DragAndDrop.objectReferences)
        {
            if (obj is AnimationClip clip)
            {
                if (AddClip(clip)) addedCount++;
            }
            else
            {
                var path = AssetDatabase.GetAssetPath(obj);
                if (AssetDatabase.IsValidFolder(path))
                    addedCount += AddClipsFromFolder(path);
            }
        }

        if (addedCount > 0)
            Debug.Log($"Added {addedCount} clips via drag & drop");
    }

    private int AddClipsFromFolder(string folderPath)
    {
        var clipGuids = AssetDatabase.FindAssets("t:AnimationClip", new[] { folderPath });
        return clipGuids.Sum(guid =>
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
            return clip != null && AddClip(clip) ? 1 : 0;
        });
    }
    #endregion

    #region Input & Validation with Japanese Support
    private bool ValidateInput()
    {
        if (string.IsNullOrEmpty(fromValue))
        {
            var target = renameTarget == RenameTarget.Object ? "object name" : "property name";
            EditorUtility.DisplayDialog("Invalid Input", $"Please enter the {target} to rename from.", "OK");
            return false;
        }

        if (string.IsNullOrEmpty(toValue))
        {
            var target = renameTarget == RenameTarget.Object ? "object name" : "property name";
            EditorUtility.DisplayDialog("Invalid Input", $"Please enter the new {target}.", "OK");
            return false;
        }

        if (TextEquals(fromValue, toValue, System.StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog("Invalid Input", "From and To values cannot be the same.", "OK");
            return false;
        }

        // Validate Japanese text
        var fromValidation = ValidateJapaneseText(fromValue, out string fromMessage);
        var toValidation = ValidateJapaneseText(toValue, out string toMessage);

        if (fromValidation == ValidationResult.Error)
        {
            EditorUtility.DisplayDialog("Invalid From Value", fromMessage, "OK");
            return false;
        }

        if (toValidation == ValidationResult.Error)
        {
            EditorUtility.DisplayDialog("Invalid To Value", toMessage, "OK");
            return false;
        }

        return true;
    }

    private bool CanPreview() => selectedClips.Count > 0 && !string.IsNullOrEmpty(fromValue) &&
                                !string.IsNullOrEmpty(toValue) &&
                                !TextEquals(fromValue, toValue, System.StringComparison.OrdinalIgnoreCase) &&
                                currentState == OperationState.Idle;

    private bool CanApply() => showPreview && previewChanges.Any(p => p.WillChange) &&
                              currentState == OperationState.Idle;

    private void HandleKeyboardShortcuts()
    {
        var evt = Event.current;
        if (evt.type != EventType.KeyDown) return;

        bool ctrl = evt.control || evt.command;

        if (ctrl && evt.keyCode == KeyCode.D)
        {
            if (evt.shift) ClearAllClips();
            else AddSelectedClipsFromProject();
            evt.Use();
        }
        else if (ctrl && evt.keyCode == KeyCode.P && CanPreview())
        {
            PreviewChanges();
            evt.Use();
        }
        else if (ctrl && evt.keyCode == KeyCode.Return && CanApply())
        {
            ApplyChanges();
            evt.Use();
        }
    }

    private void HandleAutoPreview()
    {
        if (autoPreview && Time.realtimeSinceStartup - lastPreviewTime > 1.0f && CanPreview())
        {
            lastPreviewTime = float.MaxValue; // Prevent retriggering
            PreviewChanges();
        }
    }

    private void ClearPreview()
    {
        previewChanges.Clear();
        showPreview = false;
    }

    private void ClearDiscoveredData()
    {
        discoveredObjects.Clear();
        discoveredPropertyNames.Clear();
        showDiscoveredObjects = false;
        showDiscoveredProperties = false;
    }
    #endregion

    #region Status Footer
    private void DrawStatusFooter()
    {
        if (string.IsNullOrEmpty(lastOperationStats)) return;

        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("📊 Last Operation", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(lastOperationStats, EditorStyles.miniLabel);
        }
    }
    #endregion

    #region UI Styling & Utilities
    private bool DrawIconButton(string icon, string tooltip, float width)
    {
        // Just use text instead of trying to load icons
        var text = icon switch
        {
            "Help" or "_Help" => "?",
            "Refresh" or "_Refresh" => "X",
            _ => "•"
        };

        var content = new GUIContent(text, tooltip);
        return GUILayout.Button(content, GUILayout.Width(width), GUILayout.Height(20));
    }

    private GUIStyle CreateButtonStyle(Color color)
    {
        var style = new GUIStyle(GUI.skin.button);
        style.normal.textColor = Color.white;
        style.normal.background = CreateTexture(color);
        return style;
    }

    private GUIStyle CreateLabelStyle(Color color)
    {
        var style = new GUIStyle(EditorStyles.label);
        style.normal.textColor = color;
        return style;
    }

    private GUIStyle CreateTextFieldStyle(Color color)
    {
        var style = new GUIStyle(EditorStyles.textField);
        style.normal.background = CreateTexture(color * 0.3f);
        return style;
    }

    private Texture2D CreateTexture(Color color)
    {
        var texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }
    #endregion

    #region Preferences with Japanese Support
    private void LoadPreferences()
    {
        renameTarget = (RenameTarget)EditorPrefs.GetInt($"{PREF_PREFIX}RenameTarget", 1);
        backupStrategy = (BackupStrategy)EditorPrefs.GetInt($"{PREF_PREFIX}BackupStrategy", 1);
        caseSensitive = EditorPrefs.GetBool($"{PREF_PREFIX}CaseSensitive", false);
        autoPreview = EditorPrefs.GetBool($"{PREF_PREFIX}AutoPreview", true);
        showAdvanced = EditorPrefs.GetBool($"{PREF_PREFIX}ShowAdvanced", false);
        selectedTabIndex = EditorPrefs.GetInt($"{PREF_PREFIX}SelectedTab", 1);
        enableJapaneseNormalization = EditorPrefs.GetBool($"{PREF_PREFIX}JapaneseNormalization", true);
        showJapaneseHelp = EditorPrefs.GetBool($"{PREF_PREFIX}ShowJapaneseHelp", false);
    }

    private void SavePreferences()
    {
        EditorPrefs.SetInt($"{PREF_PREFIX}RenameTarget", (int)renameTarget);
        EditorPrefs.SetInt($"{PREF_PREFIX}BackupStrategy", (int)backupStrategy);
        EditorPrefs.SetBool($"{PREF_PREFIX}CaseSensitive", caseSensitive);
        EditorPrefs.SetBool($"{PREF_PREFIX}AutoPreview", autoPreview);
        EditorPrefs.SetBool($"{PREF_PREFIX}ShowAdvanced", showAdvanced);
        EditorPrefs.SetInt($"{PREF_PREFIX}SelectedTab", selectedTabIndex);
        EditorPrefs.SetBool($"{PREF_PREFIX}JapaneseNormalization", enableJapaneseNormalization);
        EditorPrefs.SetBool($"{PREF_PREFIX}ShowJapaneseHelp", showJapaneseHelp);
    }
    #endregion
}
#endregion
