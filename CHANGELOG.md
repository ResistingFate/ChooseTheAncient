# Changelog

## Unrelesaed

## [v1.2.0] - Added Act 1, Game Modes, Ancient Pool Options, fast mode and fixes. Requires sts2 v1.07.1 minimum

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
