# Choose The Ancient

No longer do you stumble upon the Ancients, now they come to you.
At the end of each act, vote the ancient you want for the start of the next.
(Updated for STS2 v0.108.0)

<img src="https://raw.githubusercontent.com/ResistingFate/ChooseTheAncient/refs/heads/master/workshop/Choose_The_Ancients_Custom_Short_Decision.gif" alt="Alt Text" width="480" />

As you can see, you have 3 ancients to vote for and after you select one, another ancient has the chance to change your mind.
The Custom Ancient is Arq's Ancients - Phoenix by Arquebus

## Features
- After you start Act 1 or proceed to Act 2 or Act 3, a new screen asks you to choose the ancient
- Ancients' scenes clash together, and zoom in as you hover over them
- Multiplayer Support. Votes are weighted, randomized picks in multiplayer
- After the first round a second round starts where the second most voted ancient clashes with the choosen ancient
- The Second Ancient sweatens the deal by revealing their reilc options in case they have what you need
- Procceds cleanly to the next act, building the same rewards for the second most voted acient if you picked them
- Ancient spawning is no longer deterimned by the base game
- Darv can appear in both Act 2 and Act 3 in the same run
- Works with Custom Ancients
- Controller Support
- Resizes for different resolutions
- Optional Settings 
  - Number of ancients
  - GameModes
    - Monty hall (default)
    - Fair Fight
    - I Want To Know Everything
    - Simple Picker

## Optional  Requirements
- Modconfig for settings
- BaseLib and CustomAncients if you want more ancients
- Ancient Config Plus to weight or disable which Ancients show

# For Custom Ancient Modders
- [Guide Here!](https://github.com/ResistingFate/ChooseTheAncient/blob/master/workshop/Gude_To_Perfectly_Fitting_Your_Ancient_In_ChooseTheAncient.md)

## Mods Compatability list
- BaseLib Custom Acients
- Ancient Config Plus
- Some Endless modes

## Technical
- I patch the `EnterNextAct` method in `RunManager.cs` so that this mod loads before the next act starts
- I loop through all ancients and check if they have a `ValidForAct` method (this is what Baselib uses for Custom Ancients). If the ancient uses`ShouldForceSpawn` this mod  should pick it up even if `ValidForAct` returns false. Ritsulib also works as those ancients also define `ValidForAct`.

### Patch Table
* **Act1StartMapIcon**: Low Priority Postfix. Replaces the unresolved Act 1 starting map point with the Random Ancient icon.
* **AncientHpBaselineTranspiler**: Transpiler. Makes the selected Act 1 ancient use the starting HP baseline and prevents Neow from resetting HP when selected in later acts.
* **AncientRewardRng**: Prefix. Applies act-offset reward RNG when an ancient appears earlier or later than its normal minimum act.
* **CreateRoom**: False returning Prefix. Replaces Neow’s room with the custom selection room in Act 1. Still needed when using the Subscriber method.
* **EnterNextAct**: False returning Prefix. Shows the selection screen between acts.
  * For the Simplest version of this mod, it's the only patch needed. _(selection screen for act 2 and 3 only, no fixes)_
* **GenerateMap:** Low Priority Postfix. Changes the Act 1 starting map point for the original room-based trigger. Not needed with the Subscriber method.
* **NeowBlessingMode**: Prefixes and finalizers. Temporarily hides run modifiers while Neow builds its description and options so a later-act Neow still offers blessings, then restores them.
* **NeowOptionIdentitySync**: Optional `EventSynchronizer` compatibility patches. False returning Prefixes. Syncs Neow choices by option identity instead of raw list index when other mods reorder the options. It safely disables itself if the required multiplayer APIs are unavailable.
  * ( Was needed for extreme edge cases in multiplayer syncing pre patch 1.05. `Event Synchronizer` has updated since then so it might not be needed anymore.)
* **SetCurrentRoom:** Postfix. Launches the Act 1 selection screen after entering the custom room. Not needed when the Subscriber method handles this instead.

## Credit & Appreciation
- Thanks to the Slay The Spire Discord for mod resources and community 
- Thanks for Alchyr. It was their guide and templates I used to set up my JellyBeans Rider environment :
  - https://github.com/Alchyr/ModTemplate-StS2/wiki
- To Arquebus for publishing that cool phoenix custom ancient I use in my showcase
  - https://www.nexusmods.com/slaythespire2/mods/279
- Thanks Megacritic for making modding on Slay The Spire 2 accessable.
- 
## Any issuses
- Create an issue in the github with your log after you've turned on the Trace debug setting in ModConfig
- https://github.com/ResistingFate/ChooseTheAncient
- Also go to your Slay The Spire 2 game in your steam library, right click, click properties, in the Launch Options add:
- `-log generic verdebug`
- Log locations on Windows:
- C:\Users\ReplaceWithUserName\AppData\Roaming\SlayTheSpire2\logs

## New features Roadmap
- Slay the Stremer Relic addition patches? (maybe as separate mod)
- Ancient Affection patches? (maybe as separate mod)
- Nicer transition between moments in the flow like changing rooms in the base game. Right now if the loading takes a bit longer we get an empty black screen with the game's toolbar still in view.
- Ask help for good zhs translation
- Language localizing for mod config option texts
- Update mod description for zhs too in ChooseTheAncient.json or wherever translations go.
- Add shadow to Ancient Icon on card
- Touch up Ancient Dialouge in English
- Compatability with Slay the Player
- Compatability with local multiplayer
- Ancient menu themes
- Look up split path mod in multiplayer, might need new mod for new path and more ancient nodes in the map
- Custom portal effect
- Load improvements
- Bugfixes and testing when needed
