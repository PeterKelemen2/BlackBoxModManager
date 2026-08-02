# Step 9 — Texmod support

Add `.tpf` texture packages.

**This step is explicitly last and explicitly optional.** No earlier step may depend on it. Do not write `.tpf` code until everything above works.

## Why it is different

Texmod packages never touch the disk. A hooking tool injects textures into the running game at launch time. There is no file to deploy, no container to edit, and no vanilla state to back up.

That makes this step almost entirely unlike the rest of the project. It shares the mod store, the profile model, and the load order UI. It shares nothing else.

## Work

1. Research the `.tpf` format and the current injection tools. None of that research exists yet.
2. Add a `ModPackage` implementation for the type.
3. Track which packages a profile injects, and in what order.
4. Wrap the game launch. Start the injector with the enabled packages, then start the game.
5. Add the packages to the load order UI, marked as a distinct type.

## Pitfalls

**There is no deployment step, so the existing engine does not apply.** Do not force this type through the link deployer. The `ModPackage` abstraction exists so that it does not have to fit.

**There is no backup or revert, because nothing changes on disk.** Disabling a package is enough. Do not build a revert path that has nothing to revert.

**Conflict detection does not transfer.** Two packages that replace the same texture conflict, but the key is a texture identity, not `(targetFile, keyPath)`. Whether the format exposes that identity without unpacking is unknown.

**The launcher wrapper is the hard part.** The application must start the injector and the game in the right order, under Wine as well as on Windows. Confirm the Wine behavior before you design the UI.

**Texture commands in `.end` scripts are a separate mechanism.** `update_texture`, `replace_texture`, and `bind_textures` edit containers on disk through Endscript. They are step 8 work, not this step. Do not merge the two paths.

## Done when

The user can enable `.tpf` packages in a profile, launch through the application, and see the textures in the running game. Nothing earlier in the roadmap depends on any of it.
