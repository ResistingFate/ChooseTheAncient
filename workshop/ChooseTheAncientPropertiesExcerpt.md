For Baselib, put the following properties into your main class that looks like::
```
public sealed class CustomAncient : CustomAncientModel
```
For Ritsulib, put the following properties into your main class that looks like:
```
[RegisterSharedAncient]
   public sealed class CustomAncient : ModAncientEventTemplate ```
```
```
    //////////////////////////////////////////////////////////////////////////////////////
    ///////////////////// CHOOSE THE ANCIENT PRESENTATION PROPERTIES /////////////////////
    //////////////////////////////////////////////////////////////////////////////////////

    // These are convention properties. They do not create a compile-time dependency
    // on ChooseTheAncient.

    //////////////////////////////////////////////////////////////////////////////////////
    //////////////////////////////////// POSITION ////////////////////////////////////////
    //////////////////////////////////////////////////////////////////////////////////////

    private Vector2 baseSize =
        new (1920f, 1080);

    public Vector2 ChooseTheAncientPortalBaseSize =>
        baseSize;
   
    public Vector2 ChooseTheAncientPortalSourceAnchor =>
        new( 0 / baseSize.X, 0f / baseSize.Y );
    
    public float ChooseTheAncientPortalScale =>
        1f;


    public Vector2 ChooseTheAncientPortalExtraOffset =>
        new( 0f,0f );

    //////////////////////////////////////////////////////////////////////////////////////
    ///////////////////////////////////// COLOURS /////////////////////////////////////////
    //////////////////////////////////////////////////////////////////////////////////////

    // Accent colour used for ChooseTheAncient presentation elements.
    //
    // Use either ChooseTheAncientAccentColor or ChooseTheAncientAccentHex,
    // not both.

    public string ChooseTheAncientAccentHex => "#78DBFA";


    // Dialogue bubble colour.
    //
    // ChooseTheAncient already falls back to the Ancient model's DialogueColor,
    // but this explicitly supplies the same colour through the convention API.
    //
    // Use either ChooseTheAncientDialogueColor or
    // ChooseTheAncientDialogueColorHex, not both.

    public string ChooseTheAncientDialogueColorHex => "#27213F";


    //////////////////////////////////////////////////////////////////////////////////////
    //////////////////////////////////// DIALOGUE /////////////////////////////////////////
    //////////////////////////////////////////////////////////////////////////////////////

    // These values must exactly match keys added to this mod's ancients.json.
    //
    // ChooseTheAncient searches more-specific character and act suffixes first,
    // then falls back to the base prefix.

    private const string ChooseTheAncientLocRoot =
        "CustomAncient.choose_the_ancient";

    public string ChooseTheAncientSecondRoundDialoguePrefix =>
        $"{ChooseTheAncientLocRoot}.second_round.{LocalEntry}.";

    public string ChooseTheAncientFinalRevealBannerKey =>
        $"{ChooseTheAncientLocRoot}.final_reveal.{LocalEntry}";
```