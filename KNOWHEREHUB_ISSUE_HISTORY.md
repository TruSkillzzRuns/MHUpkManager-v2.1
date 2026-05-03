# KnowhereHub Crash/Recovery History

## Scope
- Project: `OmegaAssetStudio-main`
- Client target: `E:\SteamLibrary\steamapps\common\Marvel Heroes - Test`
- Server path referenced: `D:\Marvel Heroes Omega\MHServerEmu-1.0.0\MHServerEmu`
- Main log used: `C:\Users\TruSkillzzRuns\Documents\My Games\Marvel Heroes\MarvelGame\Logs\Launch.log`

## Initial Symptoms
- Repeated verify failures during startup/load:
  - `Verify failed: static_cast<size_t>(value) < lookup.m_enumValuePrototypeLookup.size()`
  - `DataDirectory.cpp Line:1652`
- Fatal crash dialogs with unresolved symbols (`DbgHelp error 487`).
- `SECURE CRT: Invalid parameter detected`.
- Client could login, but closed on load/warp to Knowhere.

## What Was Confirmed Not The Core Problem
- These warnings were treated as non-blocking:
  - `Failed to load 'SwfMovie ?INT?GFxUI.IME.MoviePath?'`
  - `Failed to load 'Texture MarvelHUD.xbox_stick_shine.png'`
  - `Failed to load 'Texture MarvelHUD.Tab_Slctd_Arrow.png'`
- Auth path/site config worked when healthy (`HTTP 200`, login handshake succeeded).

## Critical Findings From Logs
- Region handoff succeeded into Knowhere:
  - `RegionChange to Regions/HUBS/KnowhereHUB/KnowhereHUBRegion.prototype`
- Crash sequence then hit corruption warning:
  - `Detected data corruption [incorrect uncompressed size] calculated -1570986812 bytes, requested 1048576 bytes`
- This indicated bad/mismatched runtime asset data (not password/auth).

## Key Misconfiguration/Confusion Resolved
- `Account.db` and backup folder were not deleted/moved.
- They were viewed in a ZIP virtual path (`...MHServerEmu-1.0.0.zip\...`) instead of the live extracted server folder.

## Asset State Investigation (Knowhere/Thanos)
- Compared live client `CookedPCConsole` against baseline `Marvel Heroes 2016 1.34 Steam`.
- Found missing/mutated files tied to Knowhere/Thanos content:
  - Missing TFC:
    - `lighting_endgame_thanos_raid_thanos_raid_gauntlet_thanos_raid_gauntlet_c.tfc`
    - `lighting_endgame_thanos_raid_thanos_raid_gauntlet_thanos_raid_gauntlet_trans.tfc`
    - `lighting_knowhere.tfc`
    - `lighting_knowhere_raid_part2_a.tfc`
  - Modified UC gauntlet-related UPKs:
    - `UC__MarvelAgent_AIM_FireGauntlet_SF.upk`
    - `UC__PowerAIMFireGauntletFireSmash_SF.upk`
    - `UC__PowerAIMFireGauntletPunch_SF.upk`
    - `UC__PowerAIMFireGauntletSummonFire_SF.upk`
    - `UC__PowerBlade_HemoglycerinGauntletExplosion_SF.upk`
    - `UC__PowerMoonKnight_CestusGauntletPunch_SF.upk`
    - `UC__PowerMoonKnight_GauntletPunch_SF.upk`

## Recovery Actions That Got Us To Knowhere Load
1. Restored unstable test swaps when they caused startup regression (`Error Initializing Client App!`).
2. Re-established known-good SIP/UPK set from backup checkpoints.
3. Repaired Knowhere/Thanos support assets by copying from:
   - `C:\Users\TruSkillzzRuns\Desktop\Marvel Heroes 2016 1.34 Steam\UnrealEngine3\MarvelGame\CookedPCConsole`
   - Into:
   - `E:\SteamLibrary\steamapps\common\Marvel Heroes - Test\UnrealEngine3\MarvelGame\CookedPCConsole`
4. Created rollback backup before overwrite:
   - `E:\SteamLibrary\steamapps\common\Marvel Heroes - Test\UnrealEngine3\MarvelGame\CookedPCConsole\_codex_knowhere_fix_backup_20260502_160654`
5. Hash-verified restored files matched source baseline.
6. Verified `Knowhere_HUB_A.upk` hash matched baseline `Knowhere_RAIDHUB_A.upk`.

## Current State Reached
- You were able to execute:
  - `!region warp knowherehub unsafe`
- Client successfully loaded into KnowhereHub area (visual proof of in-map spawn achieved).
- This marks the first stable milestone from repeated pre-load crashes to actual Knowhere map entry.

## Remaining Files Still Not Baseline-Named
- Present in target but not in 1.34 baseline name set:
  - `Knowhere_HUB_A.upk`
  - `Thanos_Raid_Gauntlet_Entry_A_152.upk`
- `Knowhere_HUB_A.upk` content hash matched clean baseline payload at time of verification.

## Operational Guardrails Going Forward
- Do not treat login/password as root cause unless logs show auth failure.
- Keep a backup before any further content swap.
- Validate each new test with:
  1. Warp command result
  2. `Launch.log` tail
  3. Corruption/verify/crt markers

## Known-Good Restore Set (Added 2026-05-02)
- This exact file set is the checkpoint tied to successful `KnowhereHUBRegion` loads:
  - `Data\Game\mu_cdata.sip`
  - `Data\Game\Calligraphy.sip`
  - `UnrealEngine3\MarvelGame\CookedPCConsole\Knowhere_HUB_A.upk`
  - `UnrealEngine3\MarvelGame\CookedPCConsole\Thanos_Raid_Gauntlet_Entry_A_152.upk`
- Source bundle used:
  - `E:\SteamLibrary\steamapps\common\Marvel Heroes - Test\_codex_asset_reset_20260502_151435`

### Verified Hashes For Restore Set
- `mu_cdata.sip` = `694F8AF3F1950BAC8B154E4E1AC354516F70BD3D895D6E9472EABD2DF635D425`
- `Calligraphy.sip` = `992A502E3EB977730ED159B44A66FD09622F5AFC7E59DCB191439C6686995147`
- `Knowhere_HUB_A.upk` = `87222D648F30DA7BCC939200ED10E9E0889905B2F9CF541D26AA69F5D2EEA1AB`
- `Thanos_Raid_Gauntlet_Entry_A_152.upk` = `DE311634D3CCBB91802ABB7BC9A791099141E6F86FBA1F1C0540FC3091FEF177`

### Texture/Manifest Alignment Required At Same Time
- Keep these 1.52 files hash-matched (test/live):
  - `Textures.tfc` = `2CD18681187831A21632EB705E6090A75D8233ADE253858D7E7808BE5AAD0333`
  - `CharTextures.tfc` = `6342780D35E539DD289D9FCA5EB51B22FDBC91D0296234D4105945B08FA49366`
  - `TextureFileCacheManifest.bin` = `486ADE0832EEAB1F4FB9C2A49685F2F3B8007D02D39F3853A73A96F34777B02D`

### Logs That Confirmed Working Knowhere Region Handoff
- `Launch-backup-2026.05.02-16.19.52.log`
  - Contains: `RegionChange to Regions/HUBS/KnowhereHUB/KnowhereHUBRegion.prototype`
  - No corruption/missing-package markers in that run.
- `Launch-backup-2026.05.02-17.21.35.log`
  - Contains: `RegionChange to Regions/HUBS/KnowhereHUB/KnowhereHUBRegion.prototype`
  - No corruption/missing-package markers in that run.

## Isolation Pass (Added 2026-05-02 18:59)
- Objective: remove non-live Knowhere/Thanos UPKs from test to isolate KERNELBASE invalid-parameter crash during `KnowhereHUBRegion` load.
- Action completed:
  - Backed up and moved all non-live `Knowhere*.upk`/`Thanos*.upk` from:
    - `E:\SteamLibrary\steamapps\common\Marvel Heroes - Test\UnrealEngine3\MarvelGame\CookedPCConsole`
  - Backup folder:
    - `E:\SteamLibrary\steamapps\common\Marvel Heroes - Test\UnrealEngine3\MarvelGame\CookedPCConsole\_codex_cleanup_nonlive_knowhere_thanos_20260502_185957`
  - Files moved: `24`
- Remaining Knowhere/Thanos UPKs in test after cleanup:
  - `Knowhere_HUB_A.upk`
  - `Thanos_Raid_Gauntlet_Entry_A_152.upk`
- Purpose: enforce live-aligned package surface and remove duplicate/extra package collision risk before next warp repro.
