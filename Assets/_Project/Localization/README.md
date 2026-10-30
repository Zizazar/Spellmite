# Localization

Plain-text translations, one file per language, in `Resources/Languages/`:

```
# en.txt
@name=English
menu.play=Play
hud.score=Score: {0}
dialog.intro=Hello, traveler!\nWhere are you going?
coins.one={0} coin
coins.other={0} coins
```

- File name is the language code (`en`, `ru`, `de`, `pt-BR`). `en.txt` is the source and the fallback for every other language.
- `@name=` is the name players see in a language picker.
- `#` starts a comment. Keys: letters, digits, `_ . -`. Keys and values are trimmed.
- Escapes in values: `\n` newline, `\t` tab, `\\` backslash.
- `{0}`, `{1}` are filled from code (`string.Format` rules, invariant culture).
- A key missing or empty in a language falls back to English, then to the key itself.

## Plurals

Write the forms as `key.<category>`; code uses the bare key and the first argument picks the form.

| Language | Forms |
|---|---|
| en, de, es, it, nl, sv, tr… | `one`, `other` |
| fr, pt | `one` (0 and 1), `other` |
| ru, uk, be, pl | `one`, `few`, `many` |
| cs, sk, sr, hr, ro | `one`, `few`, `other` |
| ja, zh, ko, th, vi | `other` |

An optional `key.zero` is used for 0 in any language ("No coins").

## Code

```csharp
using GameJam.Localization;

label.text = Loc.T("menu.play");
label.text = Loc.T("hud.score", score);
label.text = Loc.T("coins", count);        // plural, count is {0}
label.text = Loc.P("coins", count, name);  // explicit plural, count is {0}, name is {1}
Loc.Language = "ru";                       // saved to PlayerPrefs; raises Loc.Changed
```

- `LocText` component: puts a key's translation into the TMP / UI Text / TextMesh on its object and updates on language change. Dynamic values: `GetComponent<LocText>().SetArgs(score)`.
- `LocLanguageDropdown`: fills a TMP_Dropdown with the languages.
- `LocIgnore`: marks text the audit should not report (player names, debug labels). In code, a `// noloc` comment on the line does the same.

Works in every build including WebGL (files load as TextAssets from Resources). In the editor the files are read from disk and reloaded within half a second of any change, also during Play Mode.

## Editor

**Tools > Localization > Localization**
- **Keys**: every key with every language side by side; search keys and text; edit cells in place; right-click a key to copy or delete it.
- **Missing**: completion per language, the missing keys with the English text, add a language, copy the missing lines for a translator / paste them back.
- **Audit**: hardcoded text in scenes, prefabs and `.text = "..."` code; keys used but not defined; keys never used; duplicate, malformed and placeholder-mismatched lines. Each has a fix.

## For AI agents

- Edit the `.txt` files directly; a running editor picks the change up immediately (Play Mode included).
- To translate: for every key in `en.txt`, add the same key to `<code>.txt` with the translation, keeping `{0}` placeholders and `\n`. Use that language's plural forms from the table above.
- To check the project: `unity command eval --code 'return GameJam.Localization.LocAudit.Report();'`.
- Missing keys for one language: `unity command eval --code 'return string.Join("\n", GameJam.Localization.LocDatabase.MissingKeys("ru"));'`.
