# Changelog

## Known Bugs to fix

- None
 
## [v1.2.4] - Added compatability to the game's update v0.109.0 # Custom Ancient API improvements

### Features
- Custom Ancient API has now been improved. Just add the properties to your class.
- Readjusted Ancient Config positions for the vanilla ancients.
- Act 1 map, if you bring the map up before the act 1 ancient has been chosen, will temporarily show a unique Neow/Tanx Choose The Ancient Icon.
- Dialouge changed dynamic variables
- Added dialouge for the choosen ancient to repond to the ancient that reveals offers
- Added a way for mods to register new keys in the ancient.json that will happen based on code in your mods.
  - Say Ancient Affection tells you the ancient has a close bond, then the dialouge could change to say "why are you ignoring me?". Developers can come up with whatever they want and put it in the ancient.json.
- Now Choose the Ancient controls which ancients show for the selection ballet, so accidently in Ancient Config Plus where the host and client have different settings don't cause a state divergence.

### Fixes
- Seed is not ulong, not uint
- Posiition is better for AncientConfigs that custom Ancient's will use to align their ancient scene to the selection screen slot
- Updated EnterNextAct patch so doesn't show selection screen again in the Victor Room
- Changed EnterNextAct to fix small bugs in Endless Modes Mods but they still don't work on every level 
- Cards and button on selection screen are more cosistent when ancient count is high, or ancient names are way too long.
- Include BaseLib force-spawn ancients even when IsValidForAct returns false.
- Read RitsuLib act validity through IModAncientActValidity. This also supports ancients that implement the interface explicitly instead of exposing a public IsValidForAct method. 
- Add a defensive fallback in CreateSlot so one bad custom icon cannot break the whole CTA screen.
- Modlog system updated to use normal in-game logging as default. The advanced setting will allow ChooseTheAncient to use it's own logging system and log level.

### Techincal
- Compatability branch so main branch v0.107.1 with seed length of uint is supported for now
- all rng goes through a SeedCompatability.cs file and now supports ulong
- Updated EnterNextAct patch as 0.109.0 gave it 3 transitions changing the logic.
- The Custom Ancient Positioning API now starts at the center of the imahge.
- The ExtraOffset now takes a offset in coordinates instead of a percentage value
- Added a clean PostFix patch to Act1StartMapIcon to change the act 1 ancient icon before the room is entered.

## [v1.2.3]

### Fixes
- Left Hand Side relic reveals in selection screen now have their tooltip show to the right of the reveal option so it's no longer offscreen.
- Selection screen relics reveal animaiton more in line with vanilla's animaiton.

## [v1.2.2] - Added compatability to the game's update v0.108.0

### Fixes
- Mod now works on stable 0.107.1 and beta 0.108.0 branches

### Technical
- EventSynchronizer patch failed due to adding a new IRunState argument.
- I've used AccessTools to check if the beta version of the argument is there with the IRunState Argument. And if that works it will use that version of EventSynchornizer.
- If the IRunState argument is not there, it will fail and try to Access the EventSynchronizer assuming it does not have the IRunState argument.
- It will then check the stable version, same argument call with AccessTools but without the IRunState argument. And if that works it will use that version of EventSynchornizer.
- And if that fails, it throws an error so only the other parts of the mod works.
- An error will appear on the bottom right in main menu saying the dll didn't load. This is incorrect, and I'll fix it later.
- So, it will call the correct verison of the EventSynchronizer based on if you are on the public or private version of the game.

## [v1.2.1] - Added compatability to Ancient Config Plus and robustness for mods that add extra act, and a BaseLib menu.

### Fetaures
- This mod stores it's own settingns
- Baselib Menu
- Modconfig only shows the important settings
- RitsubLib and Baselib Menus have repackaged Moconfigs options into general, advanced, and redundant mod settings

### Fixes
- Modifier code now better at handling custom Modifiers
- First and Second Selection Round ties flicker to the winning Ancient now.
- Second round doesn't fix the first round winner ancient to the left.
- Now handles Selection Screen in infinite and looping act transitions, and also exits safely.

### Technical Details
- Made the Ancient's selection pool weighted but uniform
- Made an interop so that if Ancient Conif Plus is installed use that mods setting to adjust the weighted selection pool and remove ancients deselected by Ancient config Plus
- Remove hard-coded Act 2/3 for EnterNextAct Patch
- This mod has it's own ChooseTheAncientSettingStore instead of just using ModConfig.
- BaseLib is detected at runtime and used only as an optional menu host. The mod
does not reference BaseLib at compile time, and failures in the interop path are
logged without preventing Choose The Ancient from loading. The BaseLib page mirrors the native settings store, applies changes immediately,
and saves settings as they are changed. It includes grouped general, advanced,
and redundant settings, with toggles for hiding advanced and redundant options.

## [v1.2.0] - Added Act 1, Game Modes, Ancient Pool Options, fast mode and fixes. Requires sts2i v0.107.1 minimum

### Features
- Now works for Act 1
  - Defaults to skipping to Neow like Vanilla unless you add Act 1 Ancients or use the settings to add vanilla ancients to act 1 pool 
- GameModes
  - Monty hall (default)
  - Fair Fight
  - I Want To Know Everything
  - Simple Picker
- Ancient Pool
  - Whether Act 1 has Act 1, 2, or 3 Ancients
  - Whether Act 2 has Act 1, 2, or 3 Ancients
  - Whether Act 3 has Act 1, 2, or 3 Ancients
- Added fast mode support

### Fixes
- Each Base Game Ancient has their own accent color
- Cleaned up Controller tooltips
- Randomness of Second Place Ancient is based on the Most Voted Ancient
- Increased the font outline thickness of the card title and text
- Preview Options scale slightly better for more than 3 ancients
- I Want to Know Everything for greater than 3 ancients now uses vertical columns for Ancients.
- Fixed bug on v1.1 of the mod caused Custom Ancients to not always show.
- The second ancient selection should randomize propely, instead of always being the left or right option  you didn't pick.
- Ancients don't repeat same combination of relic rewards in they show up in a later act
- Other ancients in Act 1 don't heal more health than Neow on Weary Travelere
- Neow does not heal less health than other ancients on act 2 plus.
- Custom Act 1 Ancients do not need to set health to 0, it's done through the mod now.

### Technical
- GenerateMapPatch changes the Act 1 starting node before the map is shown.
- CreateRoomPatch stops vanilla from creating the normal room for that start node.
- It returns a custom ChooseTheAncientStartRoom shell instead.
- SetCurrentRoomPatch waits until that shell room is the current room, then launches the chooser.
- After the vote, it jumps straight into the chosen ancient room.
- Also Messages to fix Sealed Deck and other card stacking modifiers from desyncing in multiplayer
- A Neow patch so Neow doesn't give modifiers twice
- Also Neow Messages to fix their modifier desyncs in multiplayer.
- Added sharedBuffer field to Custom Messages due to 1.05 update.
- RewardRng prefix patch returning false was added so all ancient generate different reward seeds on different acts.
- NeowHpBaselineTranspilerPatch is used to replace the line to set health to 0 so it's controlled by the mod.

## [v1.1.0] - Specific Ancient Text andSmall fixes

### Features
- Ancient specific Secound Ronud Banner Text
- Multiple Ancient specific Secound Round Dialouge for each Ancient
- Dictionaries in ChooseTheAncientBaseAncientText.cs hold specific dialouge lines and banner headings for each Ancient
- Loc Tables to change UI to other languages, currently eng and machine translated stub of zhs
- Loc Tables to change Ancient Dialouge to other languages, currently eng and machine translated stub of zhs

### Fixes
- Pushes up the Remote Cursors from other players above the vote buttons
- Fixed the hover glow overlaying above the game menu toolback 
- Fixed act console commands during selection screen softlocking the game.

## [v1.0.0] - Initial Release
- After you proceed Act 1 or Act 2, a new screen asks you to choose the ancient
- Ancients' scenes clash together, and zoom in as you hover over them
- Votes are weighted, randomized picks in multiplayer
- After the first round a second round starts where the second most voted ancient clashes with the choosen ancient
- The Second Ancient sweatens the deal by revealing their reilc options in case they have what you need
- Procceds cleanly to the next act, building the same rewards for the second most voted acient if you picked them
- Ancient spawning is no longer deterimned by the base game
- Darv can appear in both Act 2 and Act 3 in the same run
- Works with Custom Ancients
- Works with controller
- Resizes for different resolutions
- Optional Settings
    - Can edit the number of ancients shown 2 to 8 currently
    - Can enable controller tooltips
    - Set the Vote button colour to invisible for a clean desgin
    - Change whether a vote happens when you click the vote button, and the card surrounding it, or the the whole ancient
