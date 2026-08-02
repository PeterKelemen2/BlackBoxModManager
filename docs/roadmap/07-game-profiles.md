# Step 7 — Game profile support

Extend the application from Underground 2 to Underground 1, Most Wanted, Carbon, ProStreet, and Undercover.

**The container work is already done.** Nikki ships per-game support trees for Underground 1, Underground 2, Most Wanted, Carbon, ProStreet, and Undercover. That list is exactly our six targets. Endscript ships a profile class per game. This step is our own plumbing only.

## Work

1. Add a game descriptor type. It holds the `GameINT`, the display name, the executable name, the registry keys to probe, and the expected container paths.
2. Add one descriptor per target game.
3. Generalize the detection code from step 5 to iterate descriptors.
4. Generalize the hash list wiring. Map a `GameINT` to the right profile class and to the right `mainkeys` file name.
5. Make profiles game-scoped. A mod for one game must never appear in another game's profile.
6. Gather a wider manifest and script sample for the five new games.

## Pitfalls

**Each game has its own profile class with its own statics.** `Underground2Profile.MainHashList` and `CarbonProfile.MainHashList` are separate properties. A generic helper needs a switch on `GameINT`, or reflection. Prefer the switch.

**Set the statics for the target game immediately before every `Load`.** The properties are global and persist across operations. Stale values from a previous game produce wrong hash lookups rather than a clean error.

**Trust the manifest for the target game.** The `Game` field is reliable. Do not guess from the folder structure and do not ask the user. Reject an unknown value rather than installing hopefully.

**The `Links` boilerplate assumption is unverified for the new games.** All four inspected manifests are Underground 2 and share identical `Links`. Whether each of the other games has its own fixed set is an assumption. Gather samples before you rely on it. Compare against the expected per-game set and surface only deviations.

**Expect commands the Underground 2 mods never used.** The two example mods exercise 5 of 48 commands. Mods for the other five games will reach further. Step 8 handles that properly. Until then, an unknown verb must fail loudly.

**Enum membership is not support.** Every game in `GameINT` except `None` is a target, and that still does not make it supported. A game is supported when a descriptor for it exists, and a descriptor is valid only when a listing of a real install confirms it. Gate every code path on the descriptor list, never on `GameINT`.

**Registry layouts differ per title and per store.** A retail install, a Steam install, and an Origin install put paths in different places. Always allow a manual browse.

## Done when

All five new games detect, import mods, deploy, and revert. That makes six games with Underground 2. The per-game `Links` boilerplate assumption is either confirmed against real samples or replaced with what the samples show.

## Results

**Step 7 is part done.** The plumbing carries any number of games. The data does not exist for three of them.

The application now manages three games. It detects them, imports mods for them, deploys, and reverts. The window holds a game picker, and every mod in the store carries one game.

**Three targets wait for a listing of a real install.** This machine holds no Underground 1, no Carbon, and no Undercover. The rule of this project is that every value in a descriptor comes from a listing, so those three games have no descriptor. `GameCatalog.Absent` names them, and the window says so in its first log lines.

### The games

| Game            | Descriptor | Executable   | Confirmed from                       | Deploy verified            |
| --------------- | ---------- | ------------ | ------------------------------------ | -------------------------- |
| Underground 2   | Yes        | `SPEED2.EXE` | The vanilla install of step 0.        | Drop-in and container.      |
| Most Wanted     | Yes        | `speed.exe`  | A real install on this machine.       | Drop-in only.               |
| ProStreet       | Yes        | `nfs.exe`    | A real install on this machine.       | Drop-in only.               |
| Underground 1   | No         | —            | No install exists here.               | —                           |
| Carbon          | No         | —            | No install exists here.               | —                           |
| Undercover      | No         | —            | No install exists here.               | —                           |

**Drop-in means the link engine.** A test deploys an ASI mod to a Most Wanted tree and to a ProStreet tree, verifies it, and reverts it. The link engine and the staging code read a descriptor and nothing else, so that path is game-independent and now proven so.

**Container means the single pass of step 6.** Only Underground 2 has that proof. A Binary mod for another game needs a real mod sample, and we hold none. See "What is open".

### The types

| Type                       | Holds                                                                        |
| -------------------------- | ---------------------------------------------------------------------------- |
| `GameDefinition`           | One game on the disk. Now also container files, directory hints, and links.   |
| `ManifestLink`             | One entry of the `Links` list. Endscript names the same shape `SubLoader`.    |
| `GameCatalog.Absent`       | The targets with no descriptor. The window names them.                        |
| `ManifestLinkAudit`        | Compares the `Links` of a package against the expected set of the game.       |
| `GameInstallLocator.FindAll` | One scan for every game. `Identify` says which game a directory is.         |
| `GameInstall.MissingContainers` | The containers of the descriptor that an install does not hold.          |
| `ModStore.List(game)`      | The mods of one game. `Assign` gives a drop-in mod a game.                     |
| `MainViewModel.SelectedGame` | The game picker. A switch reloads the install, the profiles, and the mods.   |

`tests/BlackboxModManager.Tests` holds 203 tests. The new ones build a tree from a descriptor, so they cover every game of the catalog with no game and no Wine.

### Facts that carry forward

1. **A game is supported when a descriptor exists, and never because `GameINT` holds it.** `GameCatalog.All` is the only answer to "which games does this application manage". `GameCatalog.Absent` is the only answer to "which target is missing". A test proves that every target sits in exactly one of the two lists.

2. **The three descriptors name three different executables.** `SPEED2.EXE`, `speed.exe`, and `nfs.exe` differ, so `Identify` returns one game per directory. A test guards that. A fourth descriptor that repeats a name breaks the browse message, not the deploy.

3. **The manifest decides the game of a Binary mod, and the window decides the game of a drop-in mod.** The import trusts the manifest. A manifest that names Most Wanted produces a Most Wanted mod. It does so even when the window manages Underground 2. The import then writes a note that says where the mod went.

4. **A Binary mod that names no game still enters the store.** An import stores a file. It installs nothing. `VariantReader` refuses such a mod at deploy time with a message that names the variant. A refused import would leave the user with a file and no way to look at it.

5. **A mod with no game belongs to every game.** The store wrote a game for Binary mods only before metadata version 2. Hiding those mods would read as a store that lost them. The `Set game` button and `ModStore.Assign` end that state, and `Assign` refuses a Binary mod.

6. **A missing container blocks nothing.** Only a Binary mod needs a container. An install that holds the executable and the markers takes drop-in mods, so `MissingContainers` reports and the validator still passes.

7. **The `Links` boilerplate is confirmed for Underground 2 alone.** `ManifestLinkAudit` compares a package against the expected set of its game and reports only the differences. **A game with an empty `ExpectedLinks` list produces no report.** Silence there means "not checked" and never "clean". Read `HasExpectation` before you show a result.

8. **The step 6 pass still gives the same bytes.** A full run after this work applied both example mods to one load and grew `GlobalB.lzc` from 5,145,778 to 8,263,472 bytes. That matches step 1 and step 6 to the byte.

### One deviation from the work list

**The descriptors name no registry key.** Work item 1 asks the descriptor to hold "the registry keys to probe". The locator reads six publisher and uninstall keys, takes every value whose name carries `Dir`, `Path`, or `Location`, and then tests the directory itself. That needs no per-game name.

A per-game key name is a value like every other value in a descriptor. A real Windows install of that game has to confirm it, and this machine runs Wine with no such install. A guessed key name would look like data and would be a guess. The value scan already finds what the key scan would find, so the descriptors stay honest and hold nothing about the registry.

### What is open

1. **Three descriptors.** Underground 1, Carbon, and Undercover need a listing of a real install each. The listing has to give the executable name, two marker files, three marker directories, and the container file names.
2. **Work item 6, the wider sample.** We hold five manifests and five scripts, and all ten belong to Underground 2. Until a sample of another game arrives, `ExpectedLinks` stays empty for Most Wanted and ProStreet, and step 8 has no new command to classify.
3. **A container deploy for a game other than Underground 2.** This needs a Binary mod sample for that game. Run it against a scratch copy, and start the game afterward. No automated check can confirm that a race runs or that a car handles.
