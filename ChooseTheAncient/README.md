# Choose The Ancient

No longer do you stumble upon the Ancients, now they come to you.
At the end of each act, vote the ancient you want for the start of the next.



## Features
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

## Optional  Requirements
- Modconfig for settings
- BaseLib and CustomAncients if you want more ancients

## Mods Compatability list
- BaseLib Custom Acients

## Technical
- I patch the EnterNextAct method in RunManager.cs so that this mod loads before the next act starts
- I loop through all ancients and check if they have a ValidForAct method (this is what baselib uses for Custom Ancients). The mod calls this itself to check if your ancient should be added to pool for Choose The Ancient in this act. 

## Any issuses
- Create an issue with your log after you've turned on the debug setting

## New features Roadmap:
- Add a debug settings for mod config
- Make sure Nert cursors in multiplayer hover above all elements in the scene
- Support for Ancients in Ritsulib
- Setting to set 1 round only
- Setting to enable previews for both options in round 2
- Setting to enable previews to all ancients in first round
- Clean up code with comments, Selection Screen segregatted by comment Headers for each section
- Compatability with Slay the Player
- Compatability with local multiplayer
- Ancient menu themes
- Language localizing for menu text
- Language localizing for mod config option text
- Add mod settings to BsaeLib's mod config version
- Look up split path mod in multiplayer, might need new mod for new path and more ancient nodes in the map
- Add ancient dialouges
- Add support for custom ancient dialouges
- Custom portal effect
- Load improvements
- Add testing
- Bugfixes when needed

Try out Publizer by adding the following to my .csproj file:
you add this to your dependencies `ItemGroup` (where the `PackageReference` to baselib is):
```xml
<PackageReference Include="Krafs.Publicizer" Version="2.3.0" PrivateAssets="All"/>
```
and then add this `ItemGroup` somewhere nearby:
```xml
<ItemGroup> <!--Allows access to originally private/protected members but may (unlikely to) cause errors.-->
    <Publicize Include="sts2" IncludeVirtualMembers="false" IncludeCompilerGeneratedMembers="false" />
</ItemGroup>
```