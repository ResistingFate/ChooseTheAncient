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

## Mods Compatability list
- BaseLib Custom Acients
- Ancient Config Plus
- Some Endless modes

## Technical
- I patch the EnterNextAct method in RunManager.cs so that this mod loads before the next act starts
- I loop through all ancients and check if they have a ValidForAct method (this is what baselib uses for Custom Ancients). The mod calls this itself to check if your ancient should be added to pool for Choose The Ancient in this act. 

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
- ModLog more in line with common modding logging practice. Check Ritsulib
- NeowOptionIdentitySyncPatch extend to all Ancients if relevent bugs are found?
- Nicer transition between moments in the flow like changing rooms in the base game. Right now if the loading takes a bit longer we get an empty black screen with the game's toolbar still in view.
- Fix {suppressedAnicent} formating defaulting when it shouldn't
- Ask help for good zhs translation
- Language localizing for mod config option texts
- Update mod description for zhs too in ChooseTheAncient.json or wherever translations for that go.
- Add shadow to Ancient Icon on card
- Implement Log switcher when game in VeryDebug state
- Touch up Ancient Dialouge in English
- Compatability with Slay the Player
- Compatability with local multiplayer
- Ancient menu themes
- Look up split path mod in multiplayer, might need new mod for new path and more ancient nodes in the map
- Add support for custom ancient dialouges
- Custom portal effect
- Load improvements
- Bugfixes and testing when needed
