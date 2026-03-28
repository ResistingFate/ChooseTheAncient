using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace ChooseTheAncient;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "ChooseTheAncient"; //Used for resource filepath

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        Harmony harmony = new(ModId);

        harmony.PatchAll();
        GD.Print($"[{ModId}] Patches applied.");
    }
}