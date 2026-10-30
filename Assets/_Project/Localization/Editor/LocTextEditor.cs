using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GameJam.Localization
{
    /// <summary>LocText inspector: key field with suggestions and a searchable picker, every language's translation
    /// (editable in place), and buttons to turn the text already on the object into a key.</summary>
    [CustomEditor(typeof(LocText))]
    public class LocTextEditor : Editor
    {
        SerializedProperty keyProp;
        static bool showTranslations = true;
        static readonly Color MissingTint = new Color(1f, 0.25f, 0.2f, 0.18f);

        void OnEnable()
        {
            keyProp = serializedObject.FindProperty("key");
            LocDatabase.Changed += Repaint;
        }

        void OnDisable() => LocDatabase.Changed -= Repaint;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var loc = (LocText)target;
            var textComponent = LocText.FindTextComponent(loc.gameObject);
            var key = keyProp.stringValue ?? "";

            if (textComponent == null)
                EditorGUILayout.HelpBox("Add a TextMeshPro, Text or TextMesh to this object; LocText writes the translation into it.", MessageType.Error);

            Rect fieldRect;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var typed = EditorGUILayout.TextField(new GUIContent("Key", "Localization key, e.g. menu.play. Plural keys (coins.one/coins.few) are used as \"coins\"."), key);
                fieldRect = GUILayoutUtility.GetLastRect();
                if (EditorGUI.EndChangeCheck())
                {
                    key = typed.Trim();
                    SetKey(loc, key, LocDatabase.IsDefined(key));
                }
                var buttonRect = GUILayoutUtility.GetRect(22, EditorGUIUtility.singleLineHeight, GUILayout.Width(22));
                if (EditorGUI.DropdownButton(buttonRect, new GUIContent("", "Browse keys"), FocusType.Passive, EditorStyles.popup))
                {
                    var dropdownRect = new Rect(fieldRect.x + EditorGUIUtility.labelWidth, fieldRect.y, fieldRect.width - EditorGUIUtility.labelWidth + 22, fieldRect.height);
                    new LocKeyDropdown(picked =>
                    {
                        serializedObject.Update();
                        SetKey(loc, picked, true);
                        Repaint();
                    }).Show(dropdownRect);
                }
            }

            bool defined = LocDatabase.IsDefined(key);
            if (key.Length > 0 && !defined)
            {
                EditorGUILayout.HelpBox($"'{key}' is not in {Loc.DefaultLanguage}.txt.", MessageType.Warning);
                DrawSuggestions(loc, key);
            }

            var currentText = textComponent != null ? LocText.GetText(textComponent) : null;
            if (!defined && !string.IsNullOrWhiteSpace(currentText)) DrawCreateFromText(loc, key, currentText);
            else if (key.Length == 0) EditorGUILayout.HelpBox("Pick a key with the ▾ button or type one.", MessageType.Info);

            if (defined) DrawTranslations(key);

            if (Application.isPlaying && defined)
                EditorGUILayout.LabelField("Showing now", Loc.T(key), EditorStyles.wordWrappedLabel);

            serializedObject.ApplyModifiedProperties();
        }

        void DrawSuggestions(LocText loc, string key)
        {
            var matches = LocDatabase.SelectableKeys
                .Select(k => (k, score: k.StartsWith(key, StringComparison.OrdinalIgnoreCase) ? 0
                    : k.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0 ? 1
                    : (LocDatabase.SourceText(LocDatabase.Base.Has(k) ? k : k + ".other") ?? "").IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 9))
                .Where(m => m.score < 9)
                .OrderBy(m => m.score).ThenBy(m => m.k.Length)
                .Take(6).ToList();
            if (matches.Count == 0) return;

            EditorGUILayout.LabelField("Did you mean", EditorStyles.miniBoldLabel);
            var style = new GUIStyle(EditorStyles.miniButton) { alignment = TextAnchor.MiddleLeft };
            foreach (var (k, _) in matches)
            {
                var english = LocDatabase.SourceText(LocDatabase.Base.Has(k) ? k : k + ".other");
                if (GUILayout.Button(new GUIContent($"{k}   — {LocAudit.Short(english, 40)}", english), style))
                {
                    SetKey(loc, k, true);
                    GUI.FocusControl(null);
                }
            }
        }

        void DrawCreateFromText(LocText loc, string key, string text)
        {
            EditorGUILayout.Space(2);
            var existing = LocDatabase.FindKeyByValue(text);
            if (existing != null && GUILayout.Button(new GUIContent($"Use existing '{existing}'", $"{Loc.DefaultLanguage}.txt already has this text")))
                SetKey(loc, existing, false);

            var newKey = key.Length > 0 && LocFormat.IsValidKey(key) && !LocFormat.IsMeta(key) ? key : LocDatabase.UniqueKey(LocAudit.SuggestKey(loc.gameObject, ContextName(loc.gameObject), text));
            if (GUILayout.Button(new GUIContent($"Create '{newKey}' from text \"{LocAudit.Short(text, 24)}\"", $"Adds {newKey}={LocFormat.Escape(text)} to {Loc.DefaultLanguage}.txt")))
            {
                var added = LocDatabase.AddKey(newKey, text);
                SetKey(loc, added, false);
                GUI.FocusControl(null);
            }
        }

        void DrawTranslations(string key)
        {
            showTranslations = EditorGUILayout.Foldout(showTranslations, "Translations", true);
            if (!showTranslations) return;

            bool plural = LocDatabase.Base == null || !LocDatabase.Base.Has(key);
            using (new EditorGUI.IndentLevelScope())
                foreach (var lang in LocDatabase.Languages)
                {
                    var doc = LocDatabase.Get(lang);
                    if (!plural)
                    {
                        Cell($"{doc.DisplayName} ({lang})", lang, key);
                        continue;
                    }
                    EditorGUILayout.LabelField($"{doc.DisplayName} ({lang})", EditorStyles.miniBoldLabel);
                    var forms = PluralRules.FormsFor(lang).Concat(PluralRules.Categories.Where(c => doc.Has(key + "." + c))).Distinct();
                    using (new EditorGUI.IndentLevelScope())
                        foreach (var form in PluralRules.Categories.Where(forms.Contains))
                            Cell(form, lang, key + "." + form);
                }
        }

        static void Cell(string label, string lang, string key)
        {
            var value = LocDatabase.Value(lang, key);
            var escaped = LocFormat.Escape(value);
            var rect = EditorGUILayout.GetControlRect();
            var edited = EditorGUI.DelayedTextField(rect, new GUIContent(label, key), escaped);
            if (string.IsNullOrEmpty(value))
                EditorGUI.DrawRect(new Rect(rect.x + EditorGUIUtility.labelWidth, rect.y, rect.width - EditorGUIUtility.labelWidth, rect.height), MissingTint);
            if (edited != escaped) LocDatabase.Set(lang, key, LocFormat.Unescape(edited));
        }

        void SetKey(LocText loc, string key, bool applyText)
        {
            keyProp.stringValue = key;
            serializedObject.ApplyModifiedProperties();
            if (Application.isPlaying)
            {
                loc.Refresh();
                return;
            }
            // Keep the text in the scene/prefab in English so it reads right before Play Mode.
            var text = LocText.FindTextComponent(loc.gameObject);
            if (!applyText || text == null || !LocDatabase.IsDefined(key)) return;
            var english = LocDatabase.SourceText(LocDatabase.Base.Has(key) ? key : key + ".other");
            if (english == null || LocText.GetText(text) == english) return;
            Undo.RecordObject(text, "Apply translation");
            LocText.SetText(text, english);
            EditorUtility.SetDirty(text);
        }

        static string ContextName(GameObject go)
        {
            var stage = PrefabStageUtility.GetPrefabStage(go);
            if (stage != null) return System.IO.Path.GetFileNameWithoutExtension(stage.assetPath);
            if (EditorUtility.IsPersistent(go)) return System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(go));
            return go.scene.name;
        }
    }
}
