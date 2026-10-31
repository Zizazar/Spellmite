# Spellmite

Game jam project. Unity **6000.3.0f1**, **URP**, new **Input System**. Targets: **WebGL** (main, itch.io) and **Windows**.
Speed and a playable build matter more than architecture.

## Workflow rules

1. **Ask before writing code.** Before writing or changing any code, use the `AskUserQuestion` tool to confirm the approach.
   Also use it whenever an important decision has more than one reasonable solution. Put the recommended option first.
   Don't ask about things that have an obvious default or that you can check in the project yourself.
2. **No over-engineering.** Write the simplest thing that works for a jam: no interfaces with one implementation,
   no DI containers, no event buses, no abstract base classes "for later", no generic frameworks. Plain MonoBehaviours,
   ScriptableObjects for tunable data, direct references wired in the Inspector.
3. **No autonomous Play Mode or visual checks.** Never enter Play Mode, take screenshots or judge visuals yourself.
   After a change, check the console for compile errors only, then end with a **"Verify in Play Mode"** checklist:
   concrete steps for the user (what to press, what should happen, what to look at).
4. Don't commit unless asked.

## Unity Editor access (Unity MCP)

- The Editor is driven through the **Unity MCP** server (`.mcp.json` → `unity mcp`, backed by the `com.unity.pipeline` package).
  CLI fallback: `unity status`, `unity command`, `unity command <name> ...`.
- Use it for scenes, GameObjects, components, prefabs, materials, Animator controllers, build settings, and to read the console.
- **Never hand-edit `.unity`, `.prefab`, `.asset`, `.controller` YAML** while the Editor is connected.
  C# scripts, `.txt`, `.json`, `.md` are edited as files.
- After editing scripts: `recompile` → poll `recompile_status` → `console_status` / `console --level error`.
  Fix compile errors before anything else. New scripts can be attached only after recompiling (`attach_script`).
  If MCP/CLI can't connect, the Editor may be in Safe Mode (compile errors): check with `unity pipeline list`.
- If a scene is read-only, it's locked by a teammate (TeamTools scene locks) — tell the user, don't work around it.
  Prefer putting things into prefabs to reduce scene conflicts.

## Code layout

- Game code: `Assets/_Project/Scripts/<Feature>/` (e.g. `Player/`, `Spells/`, `UI/`). Default `Assembly-CSharp`, **no asmdef**.
- Namespace: `Spellmite`.
- Other content: `Assets/_Project/Prefabs`, `Art`, `Audio`, `Data` (ScriptableObjects). Create folders only when needed.
- Fields: `[SerializeField] private`. Public API only where another class actually uses it.
- Existing tools in `Assets/_Project/` — **ItchPublisher**, **TeamTools**, **UITools**, **Localization** — are template
  infrastructure. Use them, don't modify them unless asked.

## Libraries

### UniTask (`Cysharp.Threading.Tasks`)
- Use UniTask for all async/timed logic. **Avoid coroutines** (only if an API forces one).
- `async UniTask` / `async UniTaskVoid` + `.Forget()`. Never `async void` (except Unity event handlers, which then call a UniTask).
- **Always pass a `CancellationToken`** for anything tied to a GameObject: `destroyCancellationToken`
  (or `this.GetCancellationTokenOnDestroy()`), linked with a local `CancellationTokenSource` when the operation can be restarted.
  ```csharp
  await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: destroyCancellationToken);
  ```
- Never `.Result`, `.Wait()`, `Task.Run`, `UniTask.RunOnThreadPool`, `SwitchToThreadPool` (WebGL has no threads).

### PrimeTween
- Use for simple procedural animation: UI, pickups, hit flashes, camera shake, scale/punch, fades, moving platforms.
  `Tween.Scale(...)`, `Tween.PunchScale(...)`, `Tween.ShakeCamera(...)`, `Sequence.Create()...`.
- Tweens stop automatically when their target is destroyed. Keep the `Tween`/`Sequence` handle if it must be stopped or restarted
  (`tween.Stop()` before starting a new one on the same property).
- Awaiting: `await tween.ToUniTask().AttachExternalCancellation(destroyCancellationToken);`
- **Character animation is NOT done with tweens.** Characters use Animator + clips. Ask the user to add the required clips
  (list exact names, e.g. `Idle`, `Run`, `Cast`, `Hit`, `Death`, loop or not). Once the clips exist, the Animator controller
  (states, parameters, transitions) can be built through Unity MCP.

### Newtonsoft.Json (`com.unity.nuget.newtonsoft-json`)
- Use it whenever JSON is needed (`JsonConvert.SerializeObject` / `DeserializeObject<T>`). Not `JsonUtility`.
- IL2CPP strips unused code: mark DTO classes used only through JSON with `[UnityEngine.Scripting.Preserve]` (or add a `link.xml`).

### Others already in the project
- **Input System**: actions in `Assets/InputSystem_Actions.inputactions`. No legacy `Input.GetKey`.
- **Cinemachine 3** for cameras. **TextMeshPro** + uGUI for UI.
- **Localization** (`GameJam.Localization`): all player-facing text goes through `Loc.T("key")` or the `LocText` component.
  Add keys to `Assets/_Project/Localization/Resources/Languages/en.txt` and `ru.txt`. See its `README.md`.
- **Singletons are allowed** for managers (game state, audio, etc.). Keep it simple:
  ```csharp
  public static GameManager Instance { get; private set; }
  private void Awake() { Instance = this; }
  ```
  Add `DontDestroyOnLoad` / duplicate checks only if there are multiple scenes.

## WebGL limitations (code must always be WebGL-safe)

- **No threads**: no `Thread`, `Task.Run`, `Parallel`, thread-pool UniTask APIs. Everything runs on the main thread.
- **No blocking waits**: no `.Result`, `.Wait()`, sync `WWW`, busy loops.
- **Saving**: `PlayerPrefs` (call `PlayerPrefs.Save()` after important writes). No `System.IO` file saves.
- **Networking**: `UnityWebRequest` only, target must allow CORS. No raw sockets / `System.Net`.
- **Audio** starts only after the first user click/key press — show a "click to start" screen or start music on first input.
- **Cursor lock and fullscreen** work only from a user input event.
- No `Application.Quit()` (hide Quit button on WebGL: `#if !UNITY_WEBGL` or `Application.platform`).
- No compute shaders, geometry shaders or other unsupported GPU features; keep post-processing light.
- Memory is limited (max heap 2 GB): compressed textures, sensible sizes, no huge allocations per frame.
- Exceptions are "explicitly thrown only" in the build: don't rely on catching NullReference/IndexOutOfRange.
- Reflection-heavy code can be stripped by IL2CPP (see Newtonsoft note above).
- Use `#if UNITY_WEBGL` only where behavior must differ; the same code should run on Windows.

## Build & publish

`Tools → Itch.io → Publisher` builds and uploads WebGL and Windows builds (see `Assets/_Project/Editor/ItchPublisher/README.md`).
The user runs builds; don't start them unless asked.
