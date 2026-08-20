# Choose The Ancient

No longer do you stumble upon the Ancients, now they come to you.
At the end of each act, vote the ancient you want for the start of the next.
(For Main 1.070.1 & Beta 0.110.0)

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
- Cmds begin with _cta_
- Optional Settings 
  - Number of ancients
  - GameModes
    - Monty hall (default)
    - Fair Fight
    - I Want To Know Everything
    - Simple Picker

## Mod Compatibility

* [BasLlib](https://steamcommunity.com/sharedfiles/filedetails/?id=3737335127) Custom Ancients
* [Ritsulib](https://steamcommunity.com/sharedfiles/filedetails/?id=3747602295) Custom Ancients
* Use either for Full Settings
* Limited settings on [Mod Config](https://steamcommunity.com/sharedfiles/filedetails/?id=3749062616)
* [Ancient Configs Plus](https://steamcommunity.com/sharedfiles/filedetails/?id=3747493112) to filter ancients. (BaseLib only)
* Works in endless mode, like [New Game ++](https://steamcommunity.com/sharedfiles/filedetails/?id=3771500862). (asc 10 breaks, ACP filters only act 1 to 3 bosses).
* Act 4 mods don't have an ancient node at start, so no choice screen here.
* Usable with [Ancient Affection](https://steamcommunity.com/workshop/filedetails/?id=3750930021), however the preview shows the "beloved" rewards on the wrong reward options.
* [Slay the Streamer 2](https://steamcommunity.com/sharedfiles/filedetails/?id=3761888849) has even more votes.


# For Custom Ancient Modders
- [Guide Here!](https://github.com/ResistingFate/ChooseTheAncient/blob/master/workshop/Gude_To_Perfectly_Fitting_Your_Ancient_In_ChooseTheAncient.md)

# Translation Volunteers
I'm thankful for any volunteers offering to translate. I will only be accepting translations from people willing to keep these localizations updated. Send me the modified localization files, and I’ll consider adding them to the mod. Contributors will be credited here.
- [ancients.json](https://raw.githubusercontent.com/ResistingFate/ChooseTheAncient/refs/heads/master/ChooseTheAncient/localization/eng/ancients.json)
- [gameplay_ui.json](https://raw.githubusercontent.com/ResistingFate/ChooseTheAncient/refs/heads/master/ChooseTheAncient/localization/eng/gameplay_ui.json)
- [settings_ui.json](https://raw.githubusercontent.com/ResistingFate/ChooseTheAncient/refs/heads/master/ChooseTheAncient/localization/eng/settings_ui.json)

### Note: I am **NOT** responsible or liable for the translations provided by Volunteers.
This is an accessibility feature provided by volunteers, the creators make no promises of, 
will not be responsible to update/modify/verify the authenticity of any content in regards 
to translated content.

## Technical
- I patch `EnterAct`/`EnterRoomInternal` in `RunManager.cs` so that this mod can enter the Ancient selection room before the next act's map opens, while still letting vanilla handle the rest of the act transition.
- I loop through all ancients and check if they have a `ValidForAct` / `IsValidForAct` method (this is what BaseLib and other custom Ancient libraries use). If the ancient uses `ShouldForceSpawn`, this mod should pick it up even if normal act validity returns false. RitsuLib also works because those ancients expose act-validity support as well.

### Patch Table
* **ActConsoleCmdNavigation**: Postfix on `ActConsoleCmd.Process`. Clears vanilla's stale act-transition votes and duplicate-transition guard after a successful `act` command. This allows debug navigation back to an earlier act without blocking the next legitimate act transition.
* **AncientRewardRng**: Prefix. Applies act-offset reward RNG when an ancient appears earlier or later than its normal minimum act.
* **ChooseTheAncientConsoleBallotPreprocess**: Non-skipping Prefix on local `DevConsole.ProcessCommand(string)`. For valid `ctaact` and `ctastay` commands only, it cancels an older CTA ballot before vanilla queues the replacement command and hides the local console. It does not replace vanilla command processing or affect other command names.
* **ChooseTheAncientConsoleSelectionResolutionHandlerRegistration**: Postfix on `RunManager.InitializeShared`. Registers CTA's immediate skip/cancel message handler after the run receives its network service. Registration is idempotent.
* **CreateRoom**: Low Priority Postfix. Lets vanilla create its normal Ancient `EventRoom`, then replaces the result with the custom selection room for the unresolved starting Ancient node.
* **EnterActStartingAncient**: Non-skipping Prefix and Postfix on `RunManager.EnterAct` and a narrow Prefix on `EnterRoomInternal`. Marks unresolved Act 2+ transitions and replaces only vanilla's upcoming `MapRoom` entry with the starting Ancient room.
* **GenerateMap**: Low Priority Postfix. Changes the unresolved starting map point into an Ancient node.
* **NeowBlessingMode**: Prefixes and Finalizers. Temporarily hides run modifiers while Neow builds its description and options so a later-act Neow still offers blessings, then restores them.
* **NeowOptionIdentitySync**: Optional `EventSynchronizer` compatibility patches. False returning Prefixes. Syncs Neow choices by option identity instead of raw list index when other mods reorder the options. It safely disables itself if the required multiplayer APIs are unavailable.
  * Was needed for extreme edge cases in multiplayer syncing pre patch 1.05. `EventSynchronizer` has updated since then so it might not be needed anymore.
* **NMapScreenStartingAncient**: Prefixes on `NMapScreen.OnMapPointSelectedLocally` and `NMapScreen.TravelToMapCoord`. Guards the already-resolved starting Ancient node from being selected or traveled to a second time, including multiplayer and debug-travel cases.
* **SelectionScreenMainMenuCleanup**: Last Priority Postfix. Closes any leftover CTA selection screen when returning to the main menu after an interrupted or abandoned run.
* **UnresolvedActStartMapIcon**: Low Priority Postfix. Replaces the unresolved starting Ancient map point with the Random Ancient icon.


### Things to watch out for when implementing an Act 1 Ancient
- Custom Ancients in Act 1 accidently heal the player to full health in higher ascensions as it's Neow's job to set the players health at run start. Hopefully, Baselib has implemented this fix. If not, the answer lies in AncientHpBaseline. Look at Hades II mod by JonnyBazooka89.
- Any Ancient that has made a fix for this will always apply the health calculation, even if another mod makes them spawn in act 2 or 3. Neow has the same problem. So this change has to be guarded to only occur during act 1.

This mod no longer applies these fixes. This allows the mod to maintain compatability with other mods. Your health will behave slightly incorrectly when using the redundant settings that allow Neow to spawn in act 2 or 3, or vanilla ancients to spawn in act 1.

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
- Or Join the official Slay the Spire Discord go to `modding forum` and add message me in the `Choose The Ancient` thread.
- https://discord.gg/MFDv7NyDM
- Also go to your Slay The Spire 2 game in your steam library, right click, click properties, in the Launch Options add:
- `-log generic verdebug`
- Or go into Choose The Ancient's setting menu in game and change it's logging to `Modlog` and that it's level is `VeryDubg'. They are in the advanced settings.
- You can find your log locations on Windows:
- C:\Users\ReplaceWithUserName\AppData\Roaming\SlayTheSpire2\logs

## New features Roadmap
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
