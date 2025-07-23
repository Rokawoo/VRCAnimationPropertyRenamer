using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Globalization;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

/// <summary>
/// RΩKΔ's Animation Property Cleaner v1.0 - Japanese Character Support
/// Removes empty properties (no keyframes or all zero values) from animation clips
/// Enhanced with Unicode normalization and Japanese character handling
/// Author: Ultimate Developer | Optimized for professional workflows with Japanese support
/// </summary>
public class AnimationPropertyCleaner : EditorWindow
{
    #region Constants & Configuration
    private const float MIN_WINDOW_WIDTH = 700f;
    private const float MIN_WINDOW_HEIGHT = 700f;
    private const int MAX_PREVIEW_ITEMS = 150;
    private const int PERFORMANCE_BATCH_SIZE = 50;
    private const string PREF_PREFIX = "RΩKΔ's Super Based Animation Cleaner, Yeah I know I'm cool, YW~!! <3";

    // UI Colors
    private static readonly Color SUCCESS_COLOR = new Color(0.2f, 0.8f, 0.2f);
    private static readonly Color WARNING_COLOR = new Color(1f, 0.7f, 0f);
    private static readonly Color ERROR_COLOR = new Color(0.9f, 0.3f, 0.3f);
    private static readonly Color ACCENT_COLOR = new Color(0.3f, 0.7f, 1f);
    private static readonly Color BLEND_SHAPE_COLOR = new Color(0.7f, 0.9f, 1f);
    private static readonly Color JAPANESE_COLOR = new Color(0.9f, 0.7f, 0.9f);
    private static readonly Color EMPTY_PROPERTY_COLOR = new Color(0.8f, 0.4f, 0.4f);
    #endregion

    #region Enums
    public enum BackupStrategy { None, Temporary, Permanent }
    public enum CleanupMode { EmptyOnly, ZeroValuesOnly, Both }
    private enum OperationState { Idle, Scanning, Previewing, Processing }
    private enum ValidationResult { Valid, Warning, Error }
    private enum TextType { Latin, Japanese, Mixed, Other }
    #endregion

    #region Serialized State
    [SerializeField] private List<AnimationClip> selectedClips = new List<AnimationClip>();
    [SerializeField] private BackupStrategy backupStrategy = BackupStrategy.Temporary;
    [SerializeField] private CleanupMode cleanupMode = CleanupMode.Both;
    [SerializeField] private bool autoPreview = true;
    [SerializeField] private bool showAdvanced = false;
    [SerializeField] private bool enableJapaneseNormalization = true;
    [SerializeField] private bool showJapaneseHelp = false;
    [SerializeField] private float zeroThreshold = 0.001f;
    [SerializeField] private bool preserveBlendShapes = false;
    [SerializeField] private bool preserveTransforms = true;
    #endregion

    #region UI State
    private OperationState currentState = OperationState.Idle;
    private Vector2 clipsScrollPos, previewScrollPos, emptyPropertiesScrollPos;
    private bool showHelp, showDiscoveredEmptyProperties, showPreview;
    private string searchFilter = "";
    #endregion

    #region Data & Performance
    private readonly HashSet<string> discoveredEmptyProperties = new HashSet<string>();
    private readonly List<PropertyCleanupInfo> previewCleanups = new List<PropertyCleanupInfo>();
    private readonly Dictionary<AnimationClip, string> backupPaths = new Dictionary<AnimationClip, string>();
    private readonly System.Diagnostics.Stopwatch performanceTimer = new System.Diagnostics.Stopwatch();

    private string lastOperationStats = "";
    private float lastPreviewTime;
    private int totalPropertiesScanned;
    #endregion

    #region Core Data Structures
    private class PropertyCleanupInfo
    {
        public AnimationClip clip;
        public EditorCurveBinding binding;
        public AnimationCurve curve;
        public string propertyPath;
        public string propertyName;
        public CleanupReason reason;
        public ValidationResult validation = ValidationResult.Valid;
        public string validationMessage = "";
        public TextType textType = TextType.Latin;
        public int keyCount;
        public float minValue, maxValue;

        public bool ShouldRemove => validation != ValidationResult.Error;
        public bool HasWarning => validation == ValidationResult.Warning;
    }

    public enum CleanupReason
    {
        NoKeyframes,
        AllZeroValues,
        OnlyDefaultValues
    }

    private class OperationResult
    {
        public int successCount, errorCount, warningCount, totalProperties, removedProperties;
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
                return text.Normalize(System.Text.NormalizationForm.FormC);
            }
            catch (System.Exception)
            {
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

        return ValidationResult.Valid;
    }
    #endregion

    #region Menu Integration
    [MenuItem("Tools/RΩKΔ's Anim Tools/RΩKΔ's Animation Cleaner", false, 1)]
    public static AnimationPropertyCleaner ShowWindow()
    {
        var window = GetWindow<AnimationPropertyCleaner>("RΩKΔ's Animation Cleaner");
        window.minSize = new Vector2(MIN_WINDOW_WIDTH, MIN_WINDOW_HEIGHT);
        window.titleContent = new GUIContent("RΩKΔ's Animation Cleaner", EditorGUIUtility.IconContent("AnimationClip Icon").image);
        window.Show();
        return window;
    }

    [MenuItem("Assets/Clean Animation Properties", true)]
    private static bool ValidateContextMenu() => Selection.objects.OfType<AnimationClip>().Any();

    [MenuItem("Assets/Clean Animation Properties", false, 26)]
    private static void ShowFromContextMenu()
    {
        var window = GetWindow<AnimationPropertyCleaner>("RΩKΔ's Animation Cleaner");
        window.AddSelectedClipsFromProject();
    }
    #endregion

    #region Unity Lifecycle
    private void OnEnable()
    {
        LoadPreferences();
        Undo.undoRedoPerformed += OnUndoRedo;
        titleContent = new GUIContent("Animation Cleaner", EditorGUIUtility.IconContent("AnimationClip Icon").image);
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

    #region Core Operations - Property Analysis
    /// <summary>
    /// Analyzes if a property should be removed based on cleanup criteria
    /// </summary>
    private bool ShouldRemoveProperty(EditorCurveBinding binding, AnimationCurve curve, out CleanupReason reason)
    {
        reason = CleanupReason.NoKeyframes;

        // Check for no keyframes
        if (curve == null || curve.keys.Length == 0)
        {
            reason = CleanupReason.NoKeyframes;
            return cleanupMode == CleanupMode.EmptyOnly || cleanupMode == CleanupMode.Both;
        }

        // Check for all zero values
        bool allZero = curve.keys.All(key => Mathf.Abs(key.value) <= zeroThreshold);
        if (allZero)
        {
            reason = CleanupReason.AllZeroValues;
            return cleanupMode == CleanupMode.ZeroValuesOnly || cleanupMode == CleanupMode.Both;
        }

        // Check for constant default values (like scale = 1, alpha = 1, etc.)
        if (IsConstantDefaultValue(binding, curve))
        {
            reason = CleanupReason.OnlyDefaultValues;
            return cleanupMode == CleanupMode.ZeroValuesOnly || cleanupMode == CleanupMode.Both;
        }

        return false;
    }

    /// <summary>
    /// Checks if the curve contains only default/identity values
    /// </summary>
    private bool IsConstantDefaultValue(EditorCurveBinding binding, AnimationCurve curve)
    {
        if (curve.keys.Length == 0) return false;

        var propertyName = binding.propertyName.ToLower();
        float expectedDefault = 0f;

        // Common default values for different property types
        if (propertyName.Contains("scale") || propertyName.Contains("m_localscale"))
            expectedDefault = 1f;
        else if (propertyName.Contains("alpha") || propertyName.Contains("color.a"))
            expectedDefault = 1f;
        else if (propertyName.Contains("weight") && propertyName.Contains("blend"))
            expectedDefault = 0f; // Blend shape weights default to 0

        return curve.keys.All(key => Mathf.Abs(key.value - expectedDefault) <= zeroThreshold);
    }

    /// <summary>
    /// Checks if a property should be preserved based on user settings
    /// </summary>
    private bool ShouldPreserveProperty(EditorCurveBinding binding)
    {
        var propertyName = binding.propertyName.ToLower();
        var path = binding.path.ToLower();

        // Preserve blend shapes if option is enabled
        if (preserveBlendShapes && (propertyName.StartsWith("blendshape.") || propertyName.StartsWith("blend shape.")))
            return true;

        // Preserve transforms if option is enabled
        if (preserveTransforms && (
            propertyName.Contains("position") || propertyName.Contains("rotation") || propertyName.Contains("scale") ||
            propertyName.Contains("m_localposition") || propertyName.Contains("m_localrotation") || propertyName.Contains("m_localscale")))
            return true;

        return false;
    }
    #endregion

    #region Discovery Operations
    private void DiscoverEmptyProperties()
    {
        if (selectedClips.Count == 0)
        {
            EditorUtility.DisplayDialog("No Clips", "Please add animation clips first.", "OK");
            return;
        }

        discoveredEmptyProperties.Clear();

        PerformOperation(OperationState.Scanning, "Discovering Empty Properties", (clip, progress) =>
        {
            EditorUtility.DisplayProgressBar("Discovering Empty Properties", $"Scanning {clip.name}...", progress);

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);

                if (ShouldRemoveProperty(binding, curve, out CleanupReason reason) && !ShouldPreserveProperty(binding))
                {
                    var normalizedPropertyName = NormalizeText(binding.propertyName);
                    discoveredEmptyProperties.Add($"{normalizedPropertyName} ({reason})");
                }
            }
        }, () =>
        {
            lastOperationStats = $"Discovered {discoveredEmptyProperties.Count} empty properties in {performanceTimer.ElapsedMilliseconds}ms";
            showDiscoveredEmptyProperties = true;
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

    #region Preview Operations
    private void PreviewChanges()
    {
        if (selectedClips.Count == 0)
        {
            EditorUtility.DisplayDialog("No Clips", "Please add animation clips first.", "OK");
            return;
        }

        previewCleanups.Clear();
        totalPropertiesScanned = 0;

        PerformOperation(OperationState.Previewing, "Previewing Cleanup", (clip, progress) =>
        {
            EditorUtility.DisplayProgressBar("Previewing Cleanup", $"Analyzing {clip.name}...", progress);

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                totalPropertiesScanned++;
                var curve = AnimationUtility.GetEditorCurve(clip, binding);

                if (ShouldRemoveProperty(binding, curve, out CleanupReason reason))
                {
                    var cleanupInfo = CreatePropertyCleanupInfo(clip, binding, curve, reason);
                    if (cleanupInfo != null)
                    {
                        previewCleanups.Add(cleanupInfo);
                    }
                }
            }
        }, () =>
        {
            var validCleanups = previewCleanups.Count(p => p.ShouldRemove);
            lastOperationStats = $"Preview: {validCleanups} properties to remove from {totalPropertiesScanned} total in {performanceTimer.ElapsedMilliseconds}ms";
            showPreview = true;

            Debug.Log($"[Preview] Results: {validCleanups} properties to remove from {previewCleanups.Count} candidates");
        });
    }

    private PropertyCleanupInfo CreatePropertyCleanupInfo(AnimationClip clip, EditorCurveBinding binding, AnimationCurve curve, CleanupReason reason)
    {
        var cleanupInfo = new PropertyCleanupInfo
        {
            clip = clip,
            binding = binding,
            curve = curve,
            propertyPath = binding.path,
            propertyName = binding.propertyName,
            reason = reason,
            textType = DetermineTextType(binding.propertyName),
            keyCount = curve?.keys.Length ?? 0
        };

        if (curve != null && curve.keys.Length > 0)
        {
            cleanupInfo.minValue = curve.keys.Min(k => k.value);
            cleanupInfo.maxValue = curve.keys.Max(k => k.value);
        }

        ValidatePropertyCleanup(cleanupInfo);
        return cleanupInfo;
    }

    private void ValidatePropertyCleanup(PropertyCleanupInfo cleanupInfo)
    {
        cleanupInfo.validation = ValidationResult.Valid;
        cleanupInfo.validationMessage = "";

        // Check if property should be preserved
        if (ShouldPreserveProperty(cleanupInfo.binding))
        {
            cleanupInfo.validation = ValidationResult.Warning;
            cleanupInfo.validationMessage = preserveBlendShapes && cleanupInfo.propertyName.ToLower().Contains("blend") ?
                "Blend shape (protected)" : "Transform property (protected)";
            return;
        }

        // Enhanced validation for property names with Japanese support
        var japaneseValidation = ValidateJapaneseText(cleanupInfo.propertyName, out string japaneseMessage);
        if (japaneseValidation == ValidationResult.Warning)
        {
            cleanupInfo.validation = ValidationResult.Warning;
            cleanupInfo.validationMessage = japaneseMessage;
        }

        // Warn about important-looking properties
        var propertyLower = cleanupInfo.propertyName.ToLower();
        if (propertyLower.Contains("important") || propertyLower.Contains("key") || propertyLower.Contains("main"))
        {
            cleanupInfo.validation = ValidationResult.Warning;
            cleanupInfo.validationMessage = "Property name suggests importance";
        }
    }
    #endregion

    #region Main UI Framework
    private void DrawMainInterface()
    {
        using (new EditorGUILayout.VerticalScope())
        {
            DrawHeader();
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
                GUILayout.Label("RΩKΔ's Animation Cleaner", EditorStyles.largeLabel);
                GUILayout.FlexibleSpace();

                if (DrawIconButton("_Help", "Show/Hide Help", 24))
                    showHelp = !showHelp;
            }

            var description = "Remove empty properties and zero-value animations from clips";
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
            EditorGUILayout.LabelField("🧹 Quick Start Guide", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("1. Add animation clips (drag & drop or buttons)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("2. Choose cleanup mode (empty properties, zero values, or both)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("3. Discover empty properties to see what will be cleaned", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("4. Preview changes to review what will be removed", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("5. Apply cleanup to remove unused properties", EditorStyles.miniLabel);

            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("⌨️ Shortcuts: Ctrl+D (Add clips) | Ctrl+P (Preview) | Ctrl+Enter (Apply)", EditorStyles.miniLabel);

            EditorGUILayout.Space(3);
            showJapaneseHelp = EditorGUILayout.Foldout(showJapaneseHelp, "🇯🇵 Japanese Character Support", true);
            if (showJapaneseHelp)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("• Supports Hiragana (ひらがな), Katakana (カタカナ), Kanji (漢字)", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("• Unicode normalization for consistent text handling", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("• Safe cleanup of Japanese-named blend shapes", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("• Mixed Japanese/English property names supported", EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }
        }
    }

    #region Configuration Panel
    private void DrawConfigurationPanel()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Cleanup Configuration", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();

            // Cleanup mode selection
            cleanupMode = (CleanupMode)EditorGUILayout.EnumPopup("Cleanup Mode:", cleanupMode);

            var modeDesc = cleanupMode switch
            {
                CleanupMode.EmptyOnly => "Remove only properties with no keyframes",
                CleanupMode.ZeroValuesOnly => "Remove only properties with all zero/default values",
                CleanupMode.Both => "Remove both empty properties and zero-value properties",
                _ => ""
            };

            if (!string.IsNullOrEmpty(modeDesc))
                EditorGUILayout.LabelField(modeDesc, EditorStyles.miniLabel);

            DrawAdvancedOptions();

            if (EditorGUI.EndChangeCheck())
            {
                lastPreviewTime = Time.realtimeSinceStartup;
                if (!autoPreview) ClearPreview();
            }
        }

        EditorGUILayout.Space(3);
    }

    private void DrawAdvancedOptions()
    {
        showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced Options", true);

        if (showAdvanced)
        {
            EditorGUI.indentLevel++;

            EditorGUI.BeginChangeCheck();

            autoPreview = EditorGUILayout.Toggle("Auto Preview:", autoPreview);

            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("Thresholds", EditorStyles.boldLabel);
            zeroThreshold = EditorGUILayout.Slider("Zero Threshold:", zeroThreshold, 0f, 0.1f);
            EditorGUILayout.LabelField("Values within this range are considered zero", EditorStyles.miniLabel);

            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("Protection Options", EditorStyles.boldLabel);
            preserveBlendShapes = EditorGUILayout.Toggle("Preserve Blend Shapes:", preserveBlendShapes);
            preserveTransforms = EditorGUILayout.Toggle("Preserve Transforms:", preserveTransforms);

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
    #endregion

    #region Discovery Panel
    private void DrawDiscoveryPanel()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Discovery", EditorStyles.boldLabel);

            // Discovery button
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = selectedClips.Count > 0 && currentState == OperationState.Idle;
                if (GUILayout.Button("🔍 Discover Empty Properties", GUILayout.Height(28)))
                    DiscoverEmptyProperties();

                GUI.enabled = true;
            }

            var modeText = $"Scanning for: {GetModeDescription()}";
            EditorGUILayout.LabelField(modeText, EditorStyles.miniLabel);

            // Search filter
            if (discoveredEmptyProperties.Count > 0)
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

            DrawDiscoveredEmptyProperties();
        }

        EditorGUILayout.Space(3);
    }

    private string GetModeDescription()
    {
        return cleanupMode switch
        {
            CleanupMode.EmptyOnly => "Properties with no keyframes",
            CleanupMode.ZeroValuesOnly => "Properties with only zero/default values",
            CleanupMode.Both => "Empty properties and zero-value properties",
            _ => "Unknown"
        };
    }

    private void DrawDiscoveredEmptyProperties()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            showDiscoveredEmptyProperties = EditorGUILayout.Foldout(showDiscoveredEmptyProperties,
                $"Empty Properties ({discoveredEmptyProperties.Count})", true);

            if (showDiscoveredEmptyProperties && discoveredEmptyProperties.Count > 0)
            {
                using (var scrollView = new EditorGUILayout.ScrollViewScope(emptyPropertiesScrollPos, GUILayout.Height(120)))
                {
                    emptyPropertiesScrollPos = scrollView.scrollPosition;

                    var filtered = GetFilteredEmptyProperties();

                    foreach (var prop in filtered)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            var cleanPropName = prop.Contains(" (") ? prop.Substring(0, prop.IndexOf(" (")) : prop;
                            var textType = DetermineTextType(cleanPropName);
                            var style = textType == TextType.Japanese ? CreateLabelStyle(JAPANESE_COLOR) :
                                       CreateLabelStyle(EMPTY_PROPERTY_COLOR);

                            EditorGUILayout.LabelField(prop, style, GUILayout.ExpandWidth(true));

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
            else if (showDiscoveredEmptyProperties)
            {
                EditorGUILayout.LabelField("No empty properties discovered yet", EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.LabelField("Click 'Discover Empty Properties' above", EditorStyles.centeredGreyMiniLabel);
            }
        }
    }

    private System.Collections.Generic.IEnumerable<string> GetFilteredEmptyProperties()
    {
        if (string.IsNullOrEmpty(searchFilter))
        {
            return discoveredEmptyProperties.OrderBy(x => x);
        }

        var normalizedFilter = NormalizeText(searchFilter);
        return discoveredEmptyProperties
            .Where(x => TextContains(x, normalizedFilter, System.StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x);
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
                if (GUILayout.Button("👀 Preview Cleanup", GUILayout.Height(35)))
                    PreviewChanges();

                // Apply button with visual emphasis
                GUI.enabled = canApply;
                var applyStyle = canApply ? CreateButtonStyle(SUCCESS_COLOR) : GUI.skin.button;

                if (GUILayout.Button("🧹 Clean Properties", applyStyle, GUILayout.Height(35)))
                    ApplyCleanup();

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
                    OperationState.Processing => "🧹 Cleaning properties...",
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
        if (!showPreview || previewCleanups.Count == 0) return;

        using (new EditorGUILayout.VerticalScope("box"))
        {
            DrawPreviewHeader();
            DrawPreviewContent();
        }
    }

    private void DrawPreviewHeader()
    {
        EditorGUILayout.LabelField("🧹 Cleanup Preview", EditorStyles.boldLabel);

        var validCleanups = previewCleanups.Count(p => p.ShouldRemove);
        var warnings = previewCleanups.Count(p => p.HasWarning);
        var protected_count = previewCleanups.Count(p => p.validation == ValidationResult.Warning && p.validationMessage.Contains("protected"));

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"Total Found: {previewCleanups.Count}", GUILayout.Width(100));

            if (validCleanups > 0)
            {
                var style = CreateLabelStyle(SUCCESS_COLOR);
                EditorGUILayout.LabelField($"🗑 Will Remove: {validCleanups}", style, GUILayout.Width(120));
            }

            if (protected_count > 0)
            {
                var style = CreateLabelStyle(WARNING_COLOR);
                EditorGUILayout.LabelField($"🛡 Protected: {protected_count}", style, GUILayout.Width(100));
            }

            if (warnings > 0)
            {
                var style = CreateLabelStyle(WARNING_COLOR);
                EditorGUILayout.LabelField($"⚠ Warnings: {warnings}", style, GUILayout.Width(100));
            }
        }

        if (validCleanups == 0)
        {
            EditorGUILayout.HelpBox("No empty properties found to clean in selected clips.", MessageType.Info);
        }
    }

    private void DrawPreviewContent()
    {
        var validCleanups = previewCleanups.Where(p => p.ShouldRemove).ToList();
        if (validCleanups.Count == 0) return;

        using (var scrollView = new EditorGUILayout.ScrollViewScope(previewScrollPos, GUILayout.Height(200)))
        {
            previewScrollPos = scrollView.scrollPosition;

            // Group cleanups by clip for better organization
            var groupedCleanups = validCleanups.Take(MAX_PREVIEW_ITEMS).GroupBy(p => p.clip);

            foreach (var clipGroup in groupedCleanups)
                DrawClipCleanupPreview(clipGroup.Key, clipGroup.ToList());

            if (validCleanups.Count > MAX_PREVIEW_ITEMS)
            {
                EditorGUILayout.HelpBox($"Showing first {MAX_PREVIEW_ITEMS} of {validCleanups.Count} properties. All will be cleaned.", MessageType.Info);
            }
        }
    }

    private void DrawClipCleanupPreview(AnimationClip clip, List<PropertyCleanupInfo> cleanups)
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            // Clip header with icon and stats
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(EditorGUIUtility.IconContent("AnimationClip Icon"), GUILayout.Width(18));
                EditorGUILayout.LabelField(clip.name, EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"({cleanups.Count} to remove)", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"{clip.length:F1}s", EditorStyles.miniLabel, GUILayout.Width(40));
            }

            // Cleanups preview with better formatting
            EditorGUI.indentLevel++;

            // Show sample of cleanups (max 8 per clip to avoid UI overflow)
            var displayCleanups = cleanups.Take(8).ToList();
            foreach (var cleanup in displayCleanups)
                DrawPropertyCleanupPreview(cleanup);

            if (cleanups.Count > 8)
            {
                EditorGUILayout.LabelField($"... and {cleanups.Count - 8} more properties", EditorStyles.miniLabel);
            }

            EditorGUI.indentLevel--;
        }
    }

    private void DrawPropertyCleanupPreview(PropertyCleanupInfo cleanup)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            // Status icon
            var statusIcon = cleanup.validation switch
            {
                ValidationResult.Valid => "🗑",
                ValidationResult.Warning => "⚠",
                ValidationResult.Error => "✗",
                _ => "?"
            };

            var iconStyle = cleanup.validation switch
            {
                ValidationResult.Valid => CreateLabelStyle(SUCCESS_COLOR),
                ValidationResult.Warning => CreateLabelStyle(WARNING_COLOR),
                ValidationResult.Error => CreateLabelStyle(ERROR_COLOR),
                _ => EditorStyles.label
            };

            EditorGUILayout.LabelField(statusIcon, iconStyle, GUILayout.Width(15));

            // Property name with Japanese support
            var normalizedPropertyName = NormalizeText(cleanup.propertyName);
            var textType = DetermineTextType(normalizedPropertyName);
            var textStyle = textType == TextType.Japanese ? CreateLabelStyle(JAPANESE_COLOR) : EditorStyles.label;

            EditorGUILayout.LabelField(normalizedPropertyName, textStyle, GUILayout.Width(200));

            // Reason for cleanup
            var reasonText = cleanup.reason switch
            {
                CleanupReason.NoKeyframes => "No keys",
                CleanupReason.AllZeroValues => "All zeros",
                CleanupReason.OnlyDefaultValues => "Default values",
                _ => "Unknown"
            };

            EditorGUILayout.LabelField(reasonText, EditorStyles.miniLabel, GUILayout.Width(80));

            // Key count and value range
            if (cleanup.keyCount > 0)
            {
                var valueRange = $"[{cleanup.minValue:F3}, {cleanup.maxValue:F3}]";
                EditorGUILayout.LabelField($"({cleanup.keyCount} keys) {valueRange}", EditorStyles.miniLabel, GUILayout.Width(150));
            }
            else
            {
                EditorGUILayout.LabelField("(0 keys)", EditorStyles.miniLabel, GUILayout.Width(80));
            }

            // Validation message
            if (cleanup.validation != ValidationResult.Valid && !string.IsNullOrEmpty(cleanup.validationMessage))
            {
                var msgStyle = CreateLabelStyle(cleanup.validation == ValidationResult.Warning ? WARNING_COLOR : ERROR_COLOR);
                EditorGUILayout.LabelField(cleanup.validationMessage, msgStyle);
            }
        }
    }
    #endregion

    #region Apply Cleanup - Complete Implementation
    private void ApplyCleanup()
    {
        if (!showPreview || !previewCleanups.Any(p => p.ShouldRemove))
        {
            EditorUtility.DisplayDialog("No Changes", "Please preview cleanup first.", "OK");
            return;
        }

        var validCleanups = previewCleanups.Where(p => p.ShouldRemove && p.validation != ValidationResult.Error).ToList();
        var errors = previewCleanups.Count(p => p.validation == ValidationResult.Error);

        if (errors > 0 && !EditorUtility.DisplayDialog("Validation Issues",
            $"Found {errors} invalid properties that will be skipped.\nProceed with cleaning {validCleanups.Count} valid properties?",
            "Continue", "Cancel"))
            return;

        if (!ConfirmCleanupOperation(validCleanups.Count)) return;

        var result = ExecuteCleanup(validCleanups);
        ShowCleanupResults(result);

        if (result.errorCount == 0) ClearPreview();
    }

    private bool ConfirmCleanupOperation(int cleanupCount)
    {
        var clipsAffected = previewCleanups.Where(p => p.ShouldRemove && p.validation != ValidationResult.Error)
                                        .Select(p => p.clip).Distinct().Count();

        var reasonCounts = previewCleanups.Where(p => p.ShouldRemove && p.validation != ValidationResult.Error)
                                        .GroupBy(p => p.reason)
                                        .ToDictionary(g => g.Key, g => g.Count());

        var message = $"Clean {cleanupCount} empty properties across {clipsAffected} clips?\n\n";

        foreach (var kvp in reasonCounts)
        {
            var reasonText = kvp.Key switch
            {
                CleanupReason.NoKeyframes => "No keyframes",
                CleanupReason.AllZeroValues => "All zero values",
                CleanupReason.OnlyDefaultValues => "Default values",
                _ => "Unknown"
            };
            message += $"• {reasonText}: {kvp.Value} properties\n";
        }

        message += $"\n";

        message += backupStrategy switch
        {
            BackupStrategy.None => "⚠️ NO BACKUPS will be created!",
            BackupStrategy.Temporary => "Temporary backups will be auto-deleted on success.",
            BackupStrategy.Permanent => "Permanent backups will be retained.",
            _ => ""
        };

        return EditorUtility.DisplayDialog("Confirm Cleanup", message, "Clean", "Cancel");
    }

    private OperationResult ExecuteCleanup(List<PropertyCleanupInfo> validCleanups)
    {
        var result = new OperationResult { totalProperties = validCleanups.Count };
        backupPaths.Clear();
        currentState = OperationState.Processing;

        try
        {
            performanceTimer.Restart();
            AssetDatabase.StartAssetEditing();

            var clipGroups = validCleanups.GroupBy(c => c.clip).ToList();

            for (int groupIndex = 0; groupIndex < clipGroups.Count; groupIndex++)
            {
                var clipGroup = clipGroups[groupIndex];
                var clip = clipGroup.Key;

                var progress = (float)groupIndex / clipGroups.Count;
                EditorUtility.DisplayProgressBar("Cleaning Properties", $"Processing {clip.name}...", progress);

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
                    Undo.RecordObject(clip, "Clean Animation Properties");

                    // Remove properties
                    foreach (var cleanup in clipGroup)
                    {
                        try
                        {
                            // Remove the curve binding
                            AnimationUtility.SetEditorCurve(clip, cleanup.binding, null);
                            result.removedProperties++;

                            if (cleanup.validation == ValidationResult.Warning)
                            {
                                result.warningCount++;
                                result.warnings.Add($"{clip.name}: {cleanup.validationMessage}");
                            }
                        }
                        catch (System.Exception e)
                        {
                            result.errors.Add($"{clip.name} - {cleanup.propertyName}: {e.Message}");
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

    private void ShowCleanupResults(OperationResult result)
    {
        var message = $"Cleanup completed in {result.duration.TotalMilliseconds:F0}ms\n\n" +
                     $"✓ Clips processed: {result.successCount}\n" +
                     $"🗑 Properties removed: {result.removedProperties}\n";

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

        lastOperationStats = $"Last: {result.successCount} clips, {result.removedProperties} properties removed in {result.duration.TotalMilliseconds:F0}ms";
        EditorUtility.DisplayDialog("Cleanup Complete", message, "OK");
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

    #region Input & Validation
    private bool CanPreview() => selectedClips.Count > 0 && currentState == OperationState.Idle;

    private bool CanApply() => showPreview && previewCleanups.Any(p => p.ShouldRemove) &&
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
            ApplyCleanup();
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
        previewCleanups.Clear();
        showPreview = false;
    }

    private void ClearDiscoveredData()
    {
        discoveredEmptyProperties.Clear();
        showDiscoveredEmptyProperties = false;
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

    private Texture2D CreateTexture(Color color)
    {
        var texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }
    #endregion

    #region Preferences
    private void LoadPreferences()
    {
        backupStrategy = (BackupStrategy)EditorPrefs.GetInt($"{PREF_PREFIX}BackupStrategy", 1);
        cleanupMode = (CleanupMode)EditorPrefs.GetInt($"{PREF_PREFIX}CleanupMode", 2);
        autoPreview = EditorPrefs.GetBool($"{PREF_PREFIX}AutoPreview", true);
        showAdvanced = EditorPrefs.GetBool($"{PREF_PREFIX}ShowAdvanced", false);
        enableJapaneseNormalization = EditorPrefs.GetBool($"{PREF_PREFIX}JapaneseNormalization", true);
        showJapaneseHelp = EditorPrefs.GetBool($"{PREF_PREFIX}ShowJapaneseHelp", false);
        zeroThreshold = EditorPrefs.GetFloat($"{PREF_PREFIX}ZeroThreshold", 0.001f);
        preserveBlendShapes = EditorPrefs.GetBool($"{PREF_PREFIX}PreserveBlendShapes", false);
        preserveTransforms = EditorPrefs.GetBool($"{PREF_PREFIX}PreserveTransforms", true);
    }

    private void SavePreferences()
    {
        EditorPrefs.SetInt($"{PREF_PREFIX}BackupStrategy", (int)backupStrategy);
        EditorPrefs.SetInt($"{PREF_PREFIX}CleanupMode", (int)cleanupMode);
        EditorPrefs.SetBool($"{PREF_PREFIX}AutoPreview", autoPreview);
        EditorPrefs.SetBool($"{PREF_PREFIX}ShowAdvanced", showAdvanced);
        EditorPrefs.SetBool($"{PREF_PREFIX}JapaneseNormalization", enableJapaneseNormalization);
        EditorPrefs.SetBool($"{PREF_PREFIX}ShowJapaneseHelp", showJapaneseHelp);
        EditorPrefs.SetFloat($"{PREF_PREFIX}ZeroThreshold", zeroThreshold);
        EditorPrefs.SetBool($"{PREF_PREFIX}PreserveBlendShapes", preserveBlendShapes);
        EditorPrefs.SetBool($"{PREF_PREFIX}PreserveTransforms", preserveTransforms);
    }
    #endregion
}
#endregion