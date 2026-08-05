# API notes

Verified against the source in `third_party/` at the `net10-retarget` commits. Where this file disagrees with `project_brief.md`, this file wins.

## Corrections to the project brief

The brief states three things that the source contradicts.

1. **`BaseProfile` lives in `Endscript`, not in Nikki.** The namespace is `Endscript.Profiles`. The file is `Endscript/Profiles/BaseProfile.cs`. Nikki has no `Core/BaseProfile.cs`.
2. **The hash list statics are `MainHashList` and `CustomHashList`.** The brief calls the second one `UserHashList`. That name does not exist.
3. **Both statics hold file paths, not file contents.** They are `string` properties. You assign a path. The library reads the file.

## Types and where they live

| Type                  | Namespace              | Purpose                                         |
| --------------------- | ---------------------- | ----------------------------------------------- |
| `Launch`              | `Endscript.Core`       | The `VERSN1` manifest model                     |
| `BaseProfile`         | `Endscript.Profiles`   | A loaded set of game containers                 |
| `Underground2Profile` | `Endscript.Profiles`   | The per-game profile. One class per game.       |
| `EndScriptParser`     | `Endscript.Core`       | Reads a `VERSN2` script into commands           |
| `EndScriptManager`    | `Endscript.Core`       | Runs the commands against a profile             |
| `BaseCommand`         | `Endscript.Commands`   | The command base class                          |
| `ISelectable`         | `Endscript.Interfaces` | Implemented by `combobox`, `checkbox`, and `if` |
| `EndError`            | `Endscript.Helpers`    | One script error                                |
| `GameINT`             | `Nikki.Core`           | The game enum                                   |

## `Launch`

```csharp
public string ThisDir { get; set; }        // [JsonIgnore] — the manifest's own folder
public eUsage UsageID { get; }             // parsed from Usage
public GameINT GameID { get; }             // parsed from Game
public string Usage { get; set; }
public string Game { get; set; }
public string Directory { get; set; }      // the game install directory
public string Endscript { get; set; }
public List<string> Files { get; set; }
public List<SubLoader> Links { get; set; }

public static void Deserialize(string filename, out Launch launch);
public static void Serialize(string filename, Launch launch);
public void CheckEndscript();
public void CheckFiles();
public void LoadLinks();
```

`Deserialize` reads the file, requires the text to start with `[VERSN1]`, drops the first 8 characters, and then replaces every `\` with `\\`. This handles the non-standard JSON dialect for us. `Serialize` reverses the replacement.

`LoadLinks` resolves each link. `ePathType.Relative` resolves against `ThisDir`. `ePathType.Absolute` resolves against `Directory`. This is the reverse of what the names suggest.

**A link to a file that does not exist is normal.** Binary writes the same four links into every manifest of one game. A vanilla Underground 2 install holds only `LANGUAGES\Labels.bin` of them, and it holds no `GLOBAL\attributes.bin`, no `GLOBAL\fe_attrib.bin`, and no `LANGUAGES\Labels_Global.bin`. `Loader.LoadBinKeys`, `LoadVaultAttributes`, and `LoadVaultFEAttribs` all return at once for a missing file. Never treat a missing link file as an error.

**A rooted path in `File` ignores the base directory.** `Path.Combine` returns its second argument unchanged when that argument is rooted. One synthetic manifest holds one `ThisDir` and the variants hold several, so a relative link of one mod cannot resolve through it. Resolve every link to a full path and store that. `MergedLaunch` does this.

**Nothing in the library reads `UsageID`.** The property exists and no other file references it. Setting `Usage` to `Modder` declares what the run is, and it changes no behavior.

## `BaseProfile`

```csharp
public static BaseProfile NewProfile(GameINT game, string directory);
public static BaseProfile NewProfile(string game, string directory);
public string[] Load(Launch launch);   // returns non-fatal error strings
public string[] Save();                // returns non-fatal error strings
public abstract void LoadHashList();
public abstract void SaveHashList();
```

`Load` performs these steps in order:

1. Calls `this.LoadHashList()`.
2. Calls `launch.LoadLinks()`.
3. Returns an empty array at once if `launch.Files.Count == 0`.
4. Calls `launch.CheckFiles()`, which throws `FileNotFoundException` for any missing file.
5. Calls `this.AddNew(file)` for each entry in `launch.Files`.
6. Loads every container in parallel through `Task.Run`.
7. Removes the containers that failed.
8. Returns the error strings.

`Save` performs these steps in order:

1. Saves every container in parallel through `Task.Run`.
2. Calls `this.SaveHashList()`.
3. Returns the error strings.

## Per-game profile statics

```csharp
public static string MainHashList { get; set; }
public static string CustomHashList { get; set; }
```

One pair exists per game class: `Underground2Profile`, `MostWantedProfile`, `CarbonProfile`, `ProstreetProfile`, and the rest. The properties are `static`, so they are process-global. Set them before you call `Load`.

**All six classes hold the same code.** Each one is about 60 lines. Each one overrides `GameINT`, `GameSTR`, and `Directory`, declares the two statics, and implements `LoadHashList` and `SaveHashList` with the same body. No game needs different wiring, so one switch on `GameINT` covers every game. `ProfileHashLists` is that switch. `BaseProfile.NewProfile` holds the matching switch for the constructor.

`LoadHashList` calls `Map.ReloadBinKeys()` and then `Loader.LoadBinKeys(new[] { MainHashList, CustomHashList })`.

`SaveHashList` writes a file. It calls `System.IO.Directory.CreateDirectory(Path.GetDirectoryName(CustomHashList))` and then creates `CustomHashList` with `FileMode.Create`. See the pitfall in [02-binary-install.md](02-binary-install.md).

## `EndScriptParser`

```csharp
public EndScriptParser(string filename);
public BaseCommand[] Read();
public string CurrentFile { get; }
public string CurrentLine { get; }
public int CurrentIndex { get; }
public string Directory { get; }
```

Read `CurrentFile`, `CurrentLine`, and `CurrentIndex` inside a catch block. They identify the exact failure point.

## `CollectionMap`

```csharp
public CollectionMap(BaseProfile profile, string launcher);
public string Directory { get; }                                        // = Path.GetDirectoryName(launcher)
public Collectable GetCollection(string filename, string manager, string cname);
public bool ContainsCollection(string filename, string manager, string cname);
```

`EndScriptManager` builds one of these in its constructor. Two facts about it decide how the merged load must work.

**The map keys a collection by the container file name as plain text.** The key is `sdb.Filename + "|" + manager.Name + "|" + collection.CollectionName`. `GetCollection` does one dictionary lookup on that string and throws `LookupFailException` when it misses. Neither side normalizes the separator or the letter case.

**Consequence.** The `Files` entry of the merged manifest must keep the spelling that the scripts use. `GLOBAL\GLOBALB.LZC` and `GLOBAL/GlobalB.lzc` name one file on the disk and two different keys in this map. Both example mods write `GLOBAL\GLOBALB.LZC` in the manifest and in every command, so one spelling serves both. Two mods that disagree about the spelling cannot share one load.

**`Directory` is the directory of the launcher argument.** Every command that reads a file resolves against it. `ImportCommand`, `AddTextureCommand`, `CreateFileCommand`, and thirteen more read it.

## `EndScriptManager`

```csharp
public EndScriptManager(BaseProfile profile, BaseCommand[] commands, string launcher);
public void CommandChase();
public bool ProcessScript();
public IEnumerable<EndError> Errors { get; }
public int CurrentIndex { get; }
public BaseCommand CurrentCommand { get; }
```

**Pass the full path of the script as `launcher`.** The constructor uses it only for
`new CollectionMap(profile, launcher)`, which stores `Path.GetDirectoryName(launcher)`. A bare
file name gives an empty directory, and every command that reads a file then resolves against
the working directory of the process. The step 1 harness passed `launch.Endscript`, which is a
bare name. No command of either example mod reads a file, so nothing broke and nothing proved
the point either.

Call `CommandChase()` once before the first `ProcessScript()`. It resolves the jump targets for every selectable and logical command.

`ProcessScript()` returns `true` when the script finishes. It returns `false` when it needs an option answer. On `false`, cast `CurrentCommand` to `ISelectable`, set `Choice`, and call `ProcessScript()` again.

`ProcessScript()` throws on hard failures. It does not return an error code. Wrap it.

## `ISelectable`

```csharp
public int Choice { get; set; }
public int LastCommand { get; set; }
public string Description { get; }
public OptionState[] Options { get; }
public int ParseOption(string option);
public bool Contains(string option);
public OptionState this[string name] { get; }
```

Three command types implement `ISelectable`.

- `ComboboxCommand` — `Options` comes from the script. `Prepare` needs at least 4 tokens. It takes `splits[1 .. ^2]` as the options and `splits[^1]` as the description.
- `CheckboxCommand` — `Options` is always two fixed entries. Index 0 is named `disabled`. Index 1 is named `enabled`. `Prepare` needs exactly 2 tokens. The script block headers must use those two names.
- `IfStatementCommand` — this one does **not** pause. `ProcessScript` calls `Execute` on it and continues.

## Command argument shapes

`CommandCatalog` in `src/BlackboxModManager.Core/Mods/CommandCatalog.cs` holds the token number of every argument of all 48 verbs. Read the catalog before you read this section. This section holds the three rules that the catalog cannot state.

**The token count is fixed for every verb except four.** `Prepare` throws `InvalidArgsNumberException` for a wrong count. `update_collection` accepts 6 or 8. `update_incareer` accepts 8 or 10. `combobox` accepts 4 or more. `if` accepts 2 or more.

**A scalar write puts the value last and the name path in the middle.** The five verbs `update_collection`, `update_incareer`, `update_string`, `update_texture`, and `static` all follow it. Read the tokens between the container and the last token as the name path. Never read a fixed count for them.

**A `copy_` verb names the source before the target.** `copy_collection [file] [manager] [from] [to]`. The same order holds for `copy_texture` and `copy_incareer`. The thing that appears is the last token, not the first.

## `ePathType` and the two anchors

```csharp
public enum ePathType : int { Invalid = 0, Relative = 1, Absolute = 2 }
```

`EnumConverter.StringToPathType` reads the word `relative` or the word `absolute`. Every other word gives `Invalid`, and the command then throws in `Prepare`.

**`absolute` does not mean the root of the filesystem.** The two anchors are:

| Word       | Anchor                    | What it is in a deploy        |
| ---------- | ------------------------- | ----------------------------- |
| `relative` | `CollectionMap.Directory` | The mod directory.            |
| `absolute` | `map.Profile.Directory`   | The staging copy of the game. |

Nine commands take a path. `create_file`, `create_folder`, `erase_file`, `erase_folder`, and `move_file` read the anchor out of a token. `unlock_memory`, `speedreflect`, `pack_stream`, and `unpack_stream` always use the game directory.

**Every one of them calls `Path.Combine(anchor, path)` and nothing else.** So a path that names its own root reaches that place and not the anchor, and a `..` segment climbs out. See `PathSandbox`.

## `EndError`

```csharp
public string Filename { get; set; }
public string Line { get; set; }
public string Error { get; set; }
public int Index { get; set; }
```

## Call order for one mod

```csharp
Launch.Deserialize(manifestPath, out var launch);
launch.ThisDir = Path.GetDirectoryName(manifestPath);   // Deserialize does NOT set this
launch.Directory = stagingGameDir;
launch.Usage = nameof(eUsage.Modder);

Underground2Profile.MainHashList   = mainKeysPath;      // before Load
Underground2Profile.CustomHashList = ourWritablePath;   // before Load, never inside Binary

var profile = BaseProfile.NewProfile(launch.GameID, launch.Directory);
string[] loadErrors = profile.Load(launch);

var parser = new EndScriptParser(Path.Combine(launch.ThisDir, launch.Endscript));
BaseCommand[] commands = parser.Read();

// The third argument becomes Path.GetDirectoryName(launcher). Pass the full path.
var manager = new EndScriptManager(profile, commands, Path.Combine(launch.ThisDir, launch.Endscript));
manager.CommandChase();
while (!manager.ProcessScript())
{
    var selectable = (ISelectable)manager.CurrentCommand;
    selectable.Choice = ResolveChoice(selectable);       // validate the range first
}

if (manager.Errors.Any()) { /* failed deploy */ }
string[] saveErrors = profile.Save();
```
