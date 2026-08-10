using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using ChooseTheAncient.ChooseTheAncientCode;
using Xunit;

// Test-only stand-in for BaseLib. CTA deliberately detects this type by FullName,
// so the tests do not need BaseLib as a test dependency.
namespace BaseLib.Abstracts
{
    public abstract class CustomAncientModel : Darv
    {
    }
}

// Test-only stand-in for RitsuLib's act-validity interface. CTA deliberately
// detects this interface by FullName, so the real RitsuLib assembly is unnecessary.
namespace STS2RitsuLib.Scaffolding.Content
{
    public interface IModAncientActValidity
    {
        bool IsValidForAct(ActModel act);
    }
}

namespace ChooseTheAncient.Tests
{
    public sealed class SharedAncientSubsetCompatibilityTests
    {
        private static readonly object ModelDbLock = new();
        private static readonly object LoggingPatchLock = new();
        private static bool _loggingPatchInstalled;

        static SharedAncientSubsetCompatibilityTests()
        {
            InstallTestLoggingBypass();
        }

        [Fact]
        public void Vanilla_shared_Darv_is_available_in_both_later_acts()
        {
            /*
             * Darv is the vanilla shared ancient and is valid for both Act 2 and Act 3.
             * CTA should keep Darv in the shared candidate pool for both later acts.
             */
            EnsureModelDbInitialized();

            AncientEventModel darv = ModelDb.AncientEvent<Darv>();
            ActModel act2 = ModelDb.Act<Underdocks>().ToMutable();
            ActModel act3 = ModelDb.Act<Glory>().ToMutable();
            RunState runState = CreateMinimalRunState();

            List<AncientEventModel> act2Candidates =
                InvokePrivate<List<AncientEventModel>>(
                    "GetSharedAncientsValidForTargetAct",
                    act2,
                    runState);

            List<AncientEventModel> act3Candidates =
                InvokePrivate<List<AncientEventModel>>(
                    "GetSharedAncientsValidForTargetAct",
                    act3,
                    runState);

            Assert.Contains(act2Candidates, ancient => ancient.Id == darv.Id);
            Assert.Contains(act3Candidates, ancient => ancient.Id == darv.Id);
        }

        [Fact]
        public void BaseLib_act2_only_ancient_does_not_leak_into_act3()
        {
            /*
             * BaseLib custom ancients can expose an IsValidForAct hook.
             * An Ancient that opts into Act 2 only must be rejected for Act 3.
             */
            EnsureModelDbInitialized();

            AncientEventModel custom = Canonical<BaseLibAct2OnlyAncient>();
            ActModel act2 = ModelDb.Act<Underdocks>().ToMutable();
            ActModel act3 = ModelDb.Act<Glory>().ToMutable();

            bool validForAct2 = InvokePrivate<bool>(
                "IsAncientValidForAct",
                custom,
                act2);

            bool validForAct3 = InvokePrivate<bool>(
                "IsAncientValidForAct",
                custom,
                act3);

            Assert.True(validForAct2);
            Assert.False(validForAct3);
        }

        [Fact]
        public void BaseLib_act3_only_ancient_does_not_leak_into_act2()
        {
            /*
             * The inverse BaseLib case: an Ancient that opts into Act 3 only must be
             * rejected for Act 2.
             */
            EnsureModelDbInitialized();

            AncientEventModel custom = Canonical<BaseLibAct3OnlyAncient>();
            ActModel act2 = ModelDb.Act<Underdocks>().ToMutable();
            ActModel act3 = ModelDb.Act<Glory>().ToMutable();

            bool validForAct2 = InvokePrivate<bool>(
                "IsAncientValidForAct",
                custom,
                act2);

            bool validForAct3 = InvokePrivate<bool>(
                "IsAncientValidForAct",
                custom,
                act3);

            Assert.False(validForAct2);
            Assert.True(validForAct3);
        }

        [Fact]
        public void BaseLib_ancient_without_validity_hook_defaults_to_both_later_acts()
        {
            /*
             * CTA's BaseLib compatibility intentionally treats a missing IsValidForAct hook
             * as valid. This matches custom ancients that do not restrict themselves to one
             * of the later acts.
             */
            EnsureModelDbInitialized();

            AncientEventModel custom = Canonical<BaseLibDefaultAncient>();
            ActModel act2 = ModelDb.Act<Underdocks>().ToMutable();
            ActModel act3 = ModelDb.Act<Glory>().ToMutable();

            Assert.True(InvokePrivate<bool>(
                "IsAncientValidForAct",
                custom,
                act2));

            Assert.True(InvokePrivate<bool>(
                "IsAncientValidForAct",
                custom,
                act3));
        }

        [Fact]
        public void RitsuLib_act2_only_ancient_does_not_leak_into_act3()
        {
            /*
             * RitsuLib can provide act validity through an explicit interface
             * implementation. CTA should honor that contract without requiring a public
             * IsValidForAct method on the concrete Ancient type.
             */
            EnsureModelDbInitialized();

            AncientEventModel custom = Canonical<RitsuAct2OnlyAncient>();
            ActModel act2 = ModelDb.Act<Underdocks>().ToMutable();
            ActModel act3 = ModelDb.Act<Glory>().ToMutable();

            Assert.True(InvokePrivate<bool>(
                "IsAncientValidForAct",
                custom,
                act2));

            Assert.False(InvokePrivate<bool>(
                "IsAncientValidForAct",
                custom,
                act3));
        }

        [Fact]
        public void RitsuLib_act3_only_ancient_does_not_leak_into_act2()
        {
            /*
             * The inverse RitsuLib case should also remain isolated to the requested act.
             */
            EnsureModelDbInitialized();

            AncientEventModel custom = Canonical<RitsuAct3OnlyAncient>();
            ActModel act2 = ModelDb.Act<Underdocks>().ToMutable();
            ActModel act3 = ModelDb.Act<Glory>().ToMutable();

            Assert.False(InvokePrivate<bool>(
                "IsAncientValidForAct",
                custom,
                act2));

            Assert.True(InvokePrivate<bool>(
                "IsAncientValidForAct",
                custom,
                act3));
        }

        [Fact]
        public void One_custom_ancient_per_act_has_expected_uniform_ballot_frequency()
        {
            /*
             * Minimal custom-content case:
             *
             * Act 2: Orobas, Pael, Tezcatara, Darv, one Act-2-only custom ancient.
             * Act 3: Nonupeipe, Tanx, Vakuu, Darv, one Act-3-only custom ancient.
             *
             * CTA displays 3 of 5 candidates. With uniform inclusion and no force-spawn
             * behavior, any one candidate should therefore appear on about 3/5 (60%) of
             * ballots. This checks CTA's real ballot limiter across many deterministic
             * run seeds rather than treating a high custom-Ancient appearance rate as
             * evidence of cross-act leakage.
             */
            EnsureModelDbInitialized();

            AncientEventModel darv = ModelDb.AncientEvent<Darv>();
            AncientEventModel act2Custom = Canonical<BaseLibAct2OnlyAncient>();
            AncientEventModel act3Custom = Canonical<BaseLibAct3OnlyAncient>();

            ActModel act1 = ModelDb.Act<Overgrowth>().ToMutable();
            ActModel act2 = ModelDb.Act<Underdocks>().ToMutable();
            ActModel act3 = ModelDb.Act<Glory>().ToMutable();

            List<AncientEventModel> act2Pool =
            [
                ModelDb.AncientEvent<Orobas>(),
                ModelDb.AncientEvent<Pael>(),
                ModelDb.AncientEvent<Tezcatara>(),
                darv,
                act2Custom
            ];

            List<AncientEventModel> act3Pool =
            [
                ModelDb.AncientEvent<Nonupeipe>(),
                ModelDb.AncientEvent<Tanx>(),
                ModelDb.AncientEvent<Vakuu>(),
                darv,
                act3Custom
            ];

            Assert.Equal(5, act2Pool.Select(a => a.Id).Distinct().Count());
            Assert.Equal(5, act3Pool.Select(a => a.Id).Distinct().Count());

            // Before testing frequency, verify the custom Ancients themselves obey the
            // expected act-validity contract.
            Assert.True(InvokePrivate<bool>(
                "IsAncientValidForAct",
                act2Custom,
                act2));
            Assert.False(InvokePrivate<bool>(
                "IsAncientValidForAct",
                act2Custom,
                act3));

            Assert.False(InvokePrivate<bool>(
                "IsAncientValidForAct",
                act3Custom,
                act2));
            Assert.True(InvokePrivate<bool>(
                "IsAncientValidForAct",
                act3Custom,
                act3));

            const int sampleCount = 2000;
            const int ballotSize = 3;

            int act2CustomAppearances = 0;
            int act3CustomAppearances = 0;

            string act2Signature = BuildTestCandidatePoolSignature(act2Pool);
            string act3Signature = BuildTestCandidatePoolSignature(act3Pool);

            for (int i = 0; i < sampleCount; i++)
            {
                RunState runState = CreateMinimalRunState(
                    $"one-custom-per-act-{i}",
                    [act1, act2, act3]);

                List<AncientEventModel> act2Ballot =
                    InvokePrivate<List<AncientEventModel>>(
                        "SelectAncientsForLimitedBallot",
                        runState,
                        1,
                        act2Pool,
                        ballotSize,
                        act2Signature);

                List<AncientEventModel> act3Ballot =
                    InvokePrivate<List<AncientEventModel>>(
                        "SelectAncientsForLimitedBallot",
                        runState,
                        2,
                        act3Pool,
                        ballotSize,
                        act3Signature);

                Assert.Equal(ballotSize, act2Ballot.Count);
                Assert.Equal(ballotSize, act3Ballot.Count);

                if (act2Ballot.Any(ancient => ancient.Id == act2Custom.Id))
                    act2CustomAppearances++;

                if (act3Ballot.Any(ancient => ancient.Id == act3Custom.Id))
                    act3CustomAppearances++;
            }

            double act2Rate = act2CustomAppearances / (double)sampleCount;
            double act3Rate = act3CustomAppearances / (double)sampleCount;

            /*
             * The exact theoretical inclusion rate is 3/5 = 60%. We deliberately allow
             * a five-percentage-point band because this is sampling CTA's deterministic
             * RNG over many different run seeds, not asserting one exact sequence.
             */
            Assert.InRange(act2Rate, 0.55, 0.65);
            Assert.InRange(act3Rate, 0.55, 0.65);
        }

        [Fact]
        public void Act_specific_custom_ancient_from_GetUnlockedAncients_remains_in_default_pool()
        {
            /*
             * Guardrail for mods that add an ancient directly to an act rather than through
             * the shared-ancient system. The subset fix should only change shared discovery.
             */
            EnsureModelDbInitialized();

            AncientEventModel custom = Canonical<ActSpecificCustomAncient>();
            StubActModel targetAct = (StubActModel)Canonical<StubActModel>().ToMutable();
            targetAct.UnlockedAncients = [custom];
            targetAct.SetSharedAncientSubset([]);

            RunState runState = CreateMinimalRunState();

            List<AncientEventModel> pool =
                InvokePrivate<List<AncientEventModel>>(
                    "BuildDefaultCandidatePool",
                    targetAct,
                    runState,
                    2,
                    Array.Empty<AncientEventModel>(),
                    null);

            Assert.Contains(pool, ancient => ancient.Id == custom.Id);
        }

        private static void InstallTestLoggingBypass()
        {
            lock (LoggingPatchLock)
            {
                if (_loggingPatchInstalled)
                    return;

                /*
                 * Modlog accesses Gotod.OS, which causes errors for unit tests.
                 * This patches only ModLog.WriteAlways before ModLog is first initialized.
                 */
                Assembly modAssembly = typeof(ChooseTheAncientHelpers).Assembly;
                Type modLogType = modAssembly.GetType(
                    "ChooseTheAncient.ChooseTheAncientCode.ModLog",
                    throwOnError: true)!;

                MethodInfo writeAlways =
                    modLogType.GetMethod(
                        "WriteAlways",
                        BindingFlags.Static | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(
                        modLogType.FullName,
                        "WriteAlways");

                MethodInfo prefix =
                    typeof(SharedAncientSubsetCompatibilityTests).GetMethod(
                        nameof(SkipProductionLogWrite),
                        BindingFlags.Static | BindingFlags.NonPublic)
                    ?? throw new MissingMethodException(
                        typeof(SharedAncientSubsetCompatibilityTests).FullName,
                        nameof(SkipProductionLogWrite));

                Harmony harmony = new("ChooseTheAncient.Tests.DisableProductionLogging");
                harmony.Patch(writeAlways, prefix: new HarmonyMethod(prefix));

                /*
                 * Once that startup write is harmless, switch CTA's own logger into its
                 * quiet managed-only mode. In ModLog/Error mode, IsDebugEnabled is false,
                 * so LogPool exits before DescribeAncients() touches localized titles.
                 */
                MethodInfo configure =
                    modLogType.GetMethod(
                        "Configure",
                        BindingFlags.Static | BindingFlags.Public)
                    ?? throw new MissingMethodException(
                        modLogType.FullName,
                        "Configure");

                configure.Invoke(
                    null,
                    new object?[]
                    {
                        LogLevel.Error,
                        LogBackend.ModLog,
                        "xunit"
                    });

                _loggingPatchInstalled = true;
            }
        }

        private static bool SkipProductionLogWrite()
        {
            // Returning false from a Harmony prefix skips ModLog.WriteAlways's body.
            return false;
        }

        private static void EnsureModelDbInitialized()
        {
            lock (ModelDbLock)
            {
                if (ModelDb.Contains(typeof(Darv)))
                    return;

                /*
                 * The production game initializes ModelDb before a run exists. Plain xUnit does
                 * not have that bootstrap, so reproduce only that runtime prerequisite here.
                 */
                ModelDb.ResetForTest();

                ModelDb.Init(AbstractModelSubtypes.All.ToArray());
            }
        }

        private static T Canonical<T>()
            where T : AbstractModel
        {
            if (!ModelDb.Contains(typeof(T)))
                ModelDb.Inject(typeof(T));

            return ModelDb.GetById<T>(ModelDb.GetId<T>());
        }

        private static RunState CreateMinimalRunState()
        {
            return CreateMinimalRunState(
                "shared-ancient-compatibility-test",
                Array.Empty<ActModel>());
        }

        private static RunState CreateMinimalRunState(
            string seed,
            IReadOnlyList<ActModel> acts)
        {
            RunState runState =
                (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));

            SetAutoPropertyBackingField(
                runState,
                "UnlockState",
                UnlockState.all);

            SetAutoPropertyBackingField(
                runState,
                "Rng",
                new RunRngSet(seed));

            SetAutoPropertyBackingField(
                runState,
                "Acts",
                acts);

            return runState;
        }

        private static void SetAutoPropertyBackingField(
            object instance,
            string propertyName,
            object value)
        {
            FieldInfo field =
                instance.GetType().GetField(
                    $"<{propertyName}>k__BackingField",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    $"Could not locate {instance.GetType().Name}.{propertyName} backing field.");

            field.SetValue(instance, value);
        }

        private static string BuildTestCandidatePoolSignature(
            IEnumerable<AncientEventModel> ancients)
        {
            return string.Join(
                "|",
                ancients
                    .Select(ancient => ancient.Id.Entry)
                    .OrderBy(id => id, StringComparer.Ordinal));
        }

        private static T InvokePrivate<T>(string methodName, params object?[] args)
        {
            MethodInfo method =
                typeof(ChooseTheAncientHelpers).GetMethod(
                    methodName,
                    BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(
                    typeof(ChooseTheAncientHelpers).FullName,
                    methodName);

            object? result = method.Invoke(null, args);
            return (T)result!;
        }
    }

    internal sealed class ActSpecificCustomAncient : Darv
    {
    }

    internal sealed class BaseLibAct2OnlyAncient
        : BaseLib.Abstracts.CustomAncientModel
    {
        public bool IsValidForAct(ActModel act) => act is Underdocks;
    }

    internal sealed class BaseLibAct3OnlyAncient
        : BaseLib.Abstracts.CustomAncientModel
    {
        public bool IsValidForAct(ActModel act) => act is Glory;
    }

    internal sealed class BaseLibDefaultAncient
        : BaseLib.Abstracts.CustomAncientModel
    {
    }

    internal sealed class RitsuAct2OnlyAncient
        : Darv,
          STS2RitsuLib.Scaffolding.Content.IModAncientActValidity
    {
        bool STS2RitsuLib.Scaffolding.Content.IModAncientActValidity
            .IsValidForAct(ActModel act) => act is Underdocks;
    }

    internal sealed class RitsuAct3OnlyAncient
        : Darv,
          STS2RitsuLib.Scaffolding.Content.IModAncientActValidity
    {
        bool STS2RitsuLib.Scaffolding.Content.IModAncientActValidity
            .IsValidForAct(ActModel act) => act is Glory;
    }

    internal sealed class StubActModel : ActModel
    {
        public IReadOnlyList<AncientEventModel> UnlockedAncients { get; set; } =
            Array.Empty<AncientEventModel>();

        public override int Index => 2;
        public override bool IsDefault => false;
        public override Color MapTraveledColor => new(0f, 0f, 0f);
        public override Color MapUntraveledColor => new(0f, 0f, 0f);
        public override Color MapBgColor => new(0f, 0f, 0f);
        public override string[] BgMusicOptions => [];
        public override string[] MusicBankPaths => [];
        public override string AmbientSfx => string.Empty;
        protected override int BaseNumberOfRooms => 1;
        public override string ChestSpineSkinNameNormal => string.Empty;
        public override string ChestSpineSkinNameStroke => string.Empty;
        public override string ChestOpenSfx => string.Empty;
        public override IEnumerable<EncounterModel> BossDiscoveryOrder =>
            Array.Empty<EncounterModel>();
        public override IEnumerable<AncientEventModel> AllAncients =>
            UnlockedAncients;
        public override IEnumerable<EventModel> AllEvents =>
            Array.Empty<EventModel>();

        public override IEnumerable<EncounterModel> GenerateAllEncounters() =>
            Array.Empty<EncounterModel>();

        public override bool IsUnlocked(UnlockState unlockState) => true;

        public override IEnumerable<AncientEventModel> GetUnlockedAncients(
            UnlockState state) =>
            UnlockedAncients;

        protected override void ApplyActDiscoveryOrderModifications(
            UnlockState unlockState)
        {
        }

        public override MapPointTypeCounts GetMapPointTypes(Rng mapRng) =>
            new(0, 0);
    }
}
