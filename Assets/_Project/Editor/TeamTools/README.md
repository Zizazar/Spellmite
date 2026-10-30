# Team Tools: scene locks and notes

## Scene locks

Only one person can edit a scene at a time. Everyone else opens it read-only.
Locks are stored on the git server (Git LFS File Locking): they're atomic and don't depend on
who has committed what.

### How to use

1. Open a scene and start changing something: the scene is **automatically locked for you**
   (a green lock appears in the Hierarchy and on the Team panel).
2. If someone else already holds the scene:
   - a red banner appears at the top of the Scene view;
   - saving (Ctrl+S) **won't write** your changes; instead you're offered to save a copy.
3. When you're done: **Commit, Push, then "Unlock"** (on the Team panel or in `Tools → Team → Scene Locks`).
   You can't unlock a scene with uncommitted or unpushed changes: the system will warn you.
4. When you quit Unity, you're offered to unlock your scenes.
5. If someone left and forgot to unlock a scene: click "Force unlock" in the locks window.

### Protection works in several layers

| Layer | What it does |
|---|---|
| Server | `git lfs lock` is atomic: two locks on the same file are impossible |
| Editor | Saving someone else's scene is blocked; the status is re-checked on the server before blocking |
| File system | Scenes you haven't locked are read-only (`*.unity lockable` attribute in `.gitattributes`) |
| Unlocking | You can't unlock until your changes are committed and pushed |
| Locking | After locking, it checks whether the server has a newer version of the scene (Pull needed) |

### Setup (once)

1. Create a repository on **GitHub / GitLab / Gitea** (they support LFS locks) and clone it.
2. Run `git lfs install` on each computer.
3. `.gitattributes` with `*.unity lockable` is already in the project root; commit it.
4. Open `Tools → Team → Scene Locks → Setup & Diagnostics`: every item should be green.

Sign in to git beforehand (any `git pull` or `git push` in a terminal), otherwise lock requests will fail.

## Scene notes

- **Alt+N** in the Scene view: new note under the cursor. If there's an object under the cursor,
  the note is anchored to it and moves with it.
- Click a sticker to edit it. The selected note has a move handle; Esc deselects it.
- Types: Task / Bug / Idea / Info, an "Assigned to" field, and a "Resolve" button.
- List of all notes with filters: `Tools → Team → Scene Notes` (double-click shows the note in the scene).
- Show and hide: the "Notes" button on the Team panel.

Notes are stored in the `TeamNotes/` folder next to `Assets/`, one JSON file per note.
The scene itself **doesn't change**, so you can leave a note even in a scene someone else has locked,
and git almost never reports conflicts. Commit the `TeamNotes/` folder along with your other changes.

## Preferences

`Edit → Preferences → Team Tools`: auto-lock, refresh interval, note author name, and more.
