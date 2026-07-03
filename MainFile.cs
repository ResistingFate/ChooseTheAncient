using ChooseTheAncient.ChooseTheAncientCode;
using ChooseTheAncient.Scripts;
using ChooseTheAncient.ChooseTheAncientCode.Patches;
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
        ChooseTheAncientPlayerWarnings.Register(); // adds a warning to main menu in case EventSynchronizer patch fails
        ChooseTheAncientConfig.RefreshFromNativeSettings();
        ModConfigBridge.DeferredRegister();
        BaseLibSettingsInterop.DeferredRegister();
        Harmony harmony = new(ModId);
        harmony.PatchAll();
        NeowOptionIdentitySyncPatch.TryInstall(harmony); // Not included in PatchAll as SlayTheSpire 2 Updates have changed EventSynchronizer twice already.
    }
}