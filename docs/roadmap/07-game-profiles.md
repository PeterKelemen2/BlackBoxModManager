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
