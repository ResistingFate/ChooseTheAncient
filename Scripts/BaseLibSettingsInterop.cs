using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ChooseTheAncient.ChooseTheAncientCode;
using Godot;

namespace ChooseTheAncient.Scripts;

internal static class BaseLibSettingsInterop
{
    private const string ModId = "ChooseTheAncient";
    private const string PageBuilderMethodFieldName = "__chooseTheAncientPageBuilderMethod";
    private const string SettingChangedMethodFieldName = "__chooseTheAncientSettingChangedMethod";
    private static bool _registered;
    private static bool _waitingForDeferredRegister;
    private static int _deferredFramesRemaining;

    internal static void DeferredRegister()
    {
        if (_registered || _waitingForDeferredRegister)
            return;

        // BaseLib is usually loaded before dependent mods, so try immediately.
        // If the registry is not ready yet, fall back to a short frame delay.
        if (TryRegister())
            return;

        if (Engine.GetMainLoop() is SceneTree tree)
        {
            _waitingForDeferredRegister = true;
            _deferredFramesRemaining = 2;
            ModLog.Info("Scheduling deferred BaseLib settings registration for ChooseTheAncient.");
            tree.ProcessFrame += OnNextFrame;
        }
        else
        {
            ModLog.Warn("Could not schedule deferred BaseLib settings registration because no SceneTree was available.");
        }
    }

    private static void OnNextFrame()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        if (_deferredFramesRemaining > 0)
        {
            _deferredFramesRemaining--;
            return;
        }

        tree.ProcessFrame -= OnNextFrame;
        _waitingForDeferredRegister = false;
        TryRegister();
    }

    internal static bool TryRegister()
    {
        if (_registered)
            return true;

        try
        {
            var baseLibAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "BaseLib", StringComparison.Ordinal));

            if (baseLibAssembly == null)
            {
                ModLog.Debug("BaseLib was not detected; skipping BaseLib settings menu registration.");
                return false;
            }

            Type? simpleModConfigType = baseLibAssembly.GetType("BaseLib.Config.SimpleModConfig");
            Type? modConfigType = baseLibAssembly.GetType("BaseLib.Config.ModConfig");
            Type? registryType = baseLibAssembly.GetType("BaseLib.Config.ModConfigRegistry");

            if (simpleModConfigType == null || modConfigType == null || registryType == null)
            {
                ModLog.Warn("BaseLib was detected, but required config types were not found. Skipping BaseLib settings menu registration.");
                return false;
            }

            Type generatedType = BuildConfigHostType(simpleModConfigType);
            object instance = Activator.CreateInstance(generatedType)
                ?? throw new InvalidOperationException("Could not create generated BaseLib config host.");

            MethodInfo? registerMethod = registryType.GetMethod(
                "Register",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(string), modConfigType },
                modifiers: null);

            if (registerMethod == null)
            {
                ModLog.Warn("BaseLib ModConfigRegistry.Register(string, ModConfig) was not found. Skipping BaseLib settings menu registration.");
                return false;
            }

            registerMethod.Invoke(null, new[] { ModId, instance });
            _registered = true;
            ModLog.Trace("Registered ChooseTheAncient settings page with BaseLib.");
            return true;
        }
        catch (Exception e)
        {
            ModLog.Warn($"BaseLib settings menu registration failed; continuing without BaseLib settings. Error: {e}");
            return false;
        }
    }

    private static Type BuildConfigHostType(Type simpleModConfigType)
    {
        const string generatedAssemblyName = "ChooseTheAncient.BaseLibSettingsHost";
        var assemblyName = new AssemblyName(generatedAssemblyName);

        // BaseLib's settings registry requires an instance of its SimpleModConfig class.
        // ChooseTheAncient cannot inherit from that type at compile time without making
        // BaseLib a hard dependency, so we create a tiny runtime subclass only after
        // BaseLib has been detected.
        AssemblyBuilder assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule(generatedAssemblyName);

        TypeBuilder typeBuilder = moduleBuilder.DefineType(
            "ChooseTheAncient.GeneratedBaseLibSettingsHost",
            TypeAttributes.Public | TypeAttributes.Class,
            simpleModConfigType);

        BuildHostMarkerProperty(typeBuilder);

        Type gameModeEnumType = BuildGeneratedEnumType(
            moduleBuilder,
            "ChooseTheAncient.GeneratedBaseLibGameMode",
            ChooseTheAncientConfig.SelectionGameModeOptions.Select(ToSentenceCaseOption).ToArray());
        Type voteClickTargetEnumType = BuildGeneratedEnumType(
            moduleBuilder,
            "ChooseTheAncient.GeneratedBaseLibVoteClickTarget",
            ChooseTheAncientConfig.VoteClickTargetOptions.Select(ToSentenceCaseOption).ToArray());
        Type logBackendEnumType = BuildGeneratedEnumType(
            moduleBuilder,
            "ChooseTheAncient.GeneratedBaseLibLogBackend",
            ChooseTheAncientConfig.LogBackendOptions.Select(ToSentenceCaseOption).ToArray());
        Type logLevelEnumType = BuildGeneratedEnumType(
            moduleBuilder,
            "ChooseTheAncient.GeneratedBaseLibLogLevel",
            ChooseTheAncientConfig.LogLevelOptions.Select(ToSentenceCaseOption).ToArray());

        FieldBuilder settingChangedMethodField = typeBuilder.DefineField(
            SettingChangedMethodFieldName,
            typeof(MethodInfo),
            FieldAttributes.Public | FieldAttributes.Static);

        BuildGeneratedConfigProperties(
            typeBuilder,
            settingChangedMethodField,
            gameModeEnumType,
            voteClickTargetEnumType,
            logBackendEnumType,
            logLevelEnumType);

        FieldBuilder pageBuilderMethodField = typeBuilder.DefineField(
            PageBuilderMethodFieldName,
            typeof(MethodInfo),
            FieldAttributes.Public | FieldAttributes.Static);

        BuildConstructor(typeBuilder, simpleModConfigType);
        BuildSetupConfigUiOverride(typeBuilder, simpleModConfigType, pageBuilderMethodField);

        Type generatedType = typeBuilder.CreateType()
            ?? throw new InvalidOperationException("Failed to create generated BaseLib settings host type.");

        FieldInfo? field = generatedType.GetField(PageBuilderMethodFieldName, BindingFlags.Public | BindingFlags.Static);
        if (field == null)
            throw new MissingFieldException(generatedType.FullName, PageBuilderMethodFieldName);

        MethodInfo buildMethod = typeof(BaseLibSettingsPage).GetMethod(
            nameof(BaseLibSettingsPage.Build),
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(typeof(BaseLibSettingsPage).FullName, nameof(BaseLibSettingsPage.Build));

        field.SetValue(null, buildMethod);

        FieldInfo? settingChangedField = generatedType.GetField(SettingChangedMethodFieldName, BindingFlags.Public | BindingFlags.Static);
        if (settingChangedField == null)
            throw new MissingFieldException(generatedType.FullName, SettingChangedMethodFieldName);

        MethodInfo settingChangedMethod = typeof(BaseLibSettingsPage).GetMethod(
            nameof(BaseLibSettingsPage.OnGeneratedSettingChanged),
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(typeof(BaseLibSettingsPage).FullName, nameof(BaseLibSettingsPage.OnGeneratedSettingChanged));

        settingChangedField.SetValue(null, settingChangedMethod);
        return generatedType;
    }

    private static Type BuildGeneratedEnumType(ModuleBuilder moduleBuilder, string typeName, string[] displayNames)
    {
        EnumBuilder enumBuilder = moduleBuilder.DefineEnum(typeName, TypeAttributes.Public, typeof(int));

        for (int i = 0; i < displayNames.Length; i++)
        {
            // Metadata names are not C# identifiers here. Use the menu-facing display text
            // as the enum literal so BaseLib's dropdown shows sentence-case labels
            // when no localization entry exists.
            enumBuilder.DefineLiteral(displayNames[i], i);
        }

        return enumBuilder.CreateType()
            ?? throw new InvalidOperationException($"Failed to create generated enum type '{typeName}'.");
    }

    private static void BuildHostMarkerProperty(TypeBuilder typeBuilder)
    {
        FieldBuilder backingField = typeBuilder.DefineField(
            "_chooseTheAncientSettingsHost",
            typeof(bool),
            FieldAttributes.Private | FieldAttributes.Static);

        PropertyBuilder propertyBuilder = typeBuilder.DefineProperty(
            "ChooseTheAncientSettingsHost",
            PropertyAttributes.None,
            typeof(bool),
            Type.EmptyTypes);

        MethodBuilder getter = typeBuilder.DefineMethod(
            "get_ChooseTheAncientSettingsHost",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            typeof(bool),
            Type.EmptyTypes);

        ILGenerator getIl = getter.GetILGenerator();

        // Getter IL equivalent:
        //     return _chooseTheAncientSettingsHost;
        // The marker exists only so BaseLib treats this generated config as having
        // at least one setting and therefore shows the page.
        getIl.Emit(OpCodes.Ldsfld, backingField);
        getIl.Emit(OpCodes.Ret);

        MethodBuilder setter = typeBuilder.DefineMethod(
            "set_ChooseTheAncientSettingsHost",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            null,
            new[] { typeof(bool) });

        ILGenerator setIl = setter.GetILGenerator();

        // Setter IL equivalent:
        //     _chooseTheAncientSettingsHost = value;
        setIl.Emit(OpCodes.Ldarg_0);
        setIl.Emit(OpCodes.Stsfld, backingField);
        setIl.Emit(OpCodes.Ret);

        propertyBuilder.SetGetMethod(getter);
        propertyBuilder.SetSetMethod(setter);
    }


    private static void BuildGeneratedConfigProperties(
        TypeBuilder typeBuilder,
        FieldBuilder settingChangedMethodField,
        Type gameModeEnumType,
        Type voteClickTargetEnumType,
        Type logBackendEnumType,
        Type logLevelEnumType)
    {
        BuildGeneratedConfigProperty(typeBuilder, settingChangedMethodField, "AncientCount", typeof(int), sliderMin: 2, sliderMax: 8, sliderStep: 1);
        BuildGeneratedConfigProperty(typeBuilder, settingChangedMethodField, "GameMode", gameModeEnumType);
        BuildGeneratedConfigProperty(typeBuilder, settingChangedMethodField, "VoteClickTarget", voteClickTargetEnumType);
        BuildGeneratedConfigProperty(typeBuilder, settingChangedMethodField, "LogBackend", logBackendEnumType);
        BuildGeneratedConfigProperty(typeBuilder, settingChangedMethodField, "LogLevel", logLevelEnumType);
        BuildGeneratedConfigProperty(typeBuilder, settingChangedMethodField, "ShowAdvancedSettings", typeof(bool));
        BuildGeneratedConfigProperty(typeBuilder, settingChangedMethodField, "ShowRedundantSettings", typeof(bool));

        BuildGeneratedConfigProperty(typeBuilder, settingChangedMethodField, "ShowControllerHotkeys", typeof(bool));
        BuildGeneratedConfigProperty(typeBuilder, settingChangedMethodField, "ShowOnlyButtonOutline", typeof(bool));

        BuildGeneratedConfigProperty(typeBuilder, settingChangedMethodField, "Act1AncientsFromAct1", typeof(bool));
        BuildGeneratedConfigProperty(typeBuilder, settingChangedMethodField, "Act1AncientsFromAct2", typeof(bool));
        BuildGeneratedConfigProperty(typeBuilder, settingChangedMethodField, "Act1AncientsFromAct3", typeof(bool));
        BuildGeneratedConfigProperty(typeBuilder, settingChangedMethodField, "Act2AncientsFromAct1", typeof(bool));
        BuildGeneratedConfigProperty(typeBuilder, settingChangedMethodField, "Act2AncientsFromAct2", typeof(bool));
        BuildGeneratedConfigProperty(typeBuilder, settingChangedMethodField, "Act2AncientsFromAct3", typeof(bool));
        BuildGeneratedConfigProperty(typeBuilder, settingChangedMethodField, "Act3AncientsFromAct1", typeof(bool));
        BuildGeneratedConfigProperty(typeBuilder, settingChangedMethodField, "Act3AncientsFromAct2", typeof(bool));
        BuildGeneratedConfigProperty(typeBuilder, settingChangedMethodField, "Act3AncientsFromAct3", typeof(bool));

        BuildGeneratedConfigProperty(typeBuilder, settingChangedMethodField, "IncludeNeowInAct1Selection", typeof(bool));
        BuildGeneratedConfigProperty(typeBuilder, settingChangedMethodField, "IncludeNeowInAct2Selection", typeof(bool));
        BuildGeneratedConfigProperty(typeBuilder, settingChangedMethodField, "IncludeNeowInAct3Selection", typeof(bool));
        BuildGeneratedConfigProperty(typeBuilder, settingChangedMethodField, "IncludeDarvInAct1Selection", typeof(bool));
        BuildGeneratedConfigProperty(typeBuilder, settingChangedMethodField, "IncludeDarvInAct2Selection", typeof(bool));
        BuildGeneratedConfigProperty(typeBuilder, settingChangedMethodField, "IncludeDarvInAct3Selection", typeof(bool));
    }

    private static void BuildGeneratedConfigProperty(
        TypeBuilder typeBuilder,
        FieldBuilder settingChangedMethodField,
        string propertyName,
        Type propertyType,
        double? sliderMin = null,
        double? sliderMax = null,
        double? sliderStep = null)
    {
        FieldBuilder backingField = typeBuilder.DefineField(
            "_" + propertyName,
            propertyType,
            FieldAttributes.Private | FieldAttributes.Static);

        PropertyBuilder propertyBuilder = typeBuilder.DefineProperty(
            propertyName,
            PropertyAttributes.None,
            propertyType,
            Type.EmptyTypes);

        if (sliderMin.HasValue && sliderMax.HasValue && sliderStep.HasValue)
        {
            Type? sliderAttributeType = FindBaseLibType("BaseLib.Config.ConfigSliderAttribute");
            ConstructorInfo? sliderCtor = sliderAttributeType?.GetConstructor(new[] { typeof(double), typeof(double), typeof(double) });
            if (sliderCtor != null)
            {
                propertyBuilder.SetCustomAttribute(new CustomAttributeBuilder(
                    sliderCtor,
                    new object[] { sliderMin.Value, sliderMax.Value, sliderStep.Value }));
            }
        }

        MethodBuilder getter = typeBuilder.DefineMethod(
            "get_" + propertyName,
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            propertyType,
            Type.EmptyTypes);

        ILGenerator getIl = getter.GetILGenerator();

        // Getter IL equivalent:
        //     return _chooseTheAncientSettingsHost;
        // The marker exists only so BaseLib treats this generated config as having
        // at least one setting and therefore shows the page.
        getIl.Emit(OpCodes.Ldsfld, backingField);
        getIl.Emit(OpCodes.Ret);

        MethodBuilder setter = typeBuilder.DefineMethod(
            "set_" + propertyName,
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            null,
            new[] { propertyType });

        MethodInfo invokeMethod = typeof(MethodBase).GetMethod(
            nameof(MethodBase.Invoke),
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: new[] { typeof(object), typeof(object[]) },
            modifiers: null)
            ?? throw new MissingMethodException(typeof(MethodBase).FullName, nameof(MethodBase.Invoke));

        ILGenerator setIl = setter.GetILGenerator();
        LocalBuilder methodLocal = setIl.DeclareLocal(typeof(MethodInfo));
        System.Reflection.Emit.Label skipCallback = setIl.DefineLabel();

        // Generated property setter IL equivalent:
        //     _PropertyName = value;
        //     if (__chooseTheAncientSettingChangedMethod != null)
        //         __chooseTheAncientSettingChangedMethod.Invoke(null, new object[] { "PropertyName", value });
        //
        // The MethodInfo callback keeps generated IL from directly calling into CTA
        // methods, which avoids MethodAccessException on some Mono/Godot runtimes.
        setIl.Emit(OpCodes.Ldarg_0);
        setIl.Emit(OpCodes.Stsfld, backingField);

        setIl.Emit(OpCodes.Ldsfld, settingChangedMethodField);
        setIl.Emit(OpCodes.Stloc, methodLocal);
        setIl.Emit(OpCodes.Ldloc, methodLocal);
        setIl.Emit(OpCodes.Brfalse_S, skipCallback);

        setIl.Emit(OpCodes.Ldloc, methodLocal);
        setIl.Emit(OpCodes.Ldnull);
        setIl.Emit(OpCodes.Ldc_I4_2);
        setIl.Emit(OpCodes.Newarr, typeof(object));

        setIl.Emit(OpCodes.Dup);
        setIl.Emit(OpCodes.Ldc_I4_0);
        setIl.Emit(OpCodes.Ldstr, propertyName);
        setIl.Emit(OpCodes.Stelem_Ref);

        setIl.Emit(OpCodes.Dup);
        setIl.Emit(OpCodes.Ldc_I4_1);
        setIl.Emit(OpCodes.Ldarg_0);
        if (propertyType.IsValueType)
            setIl.Emit(OpCodes.Box, propertyType);
        setIl.Emit(OpCodes.Stelem_Ref);

        setIl.Emit(OpCodes.Callvirt, invokeMethod);
        setIl.Emit(OpCodes.Pop);

        setIl.MarkLabel(skipCallback);
        setIl.Emit(OpCodes.Ret);

        propertyBuilder.SetGetMethod(getter);
        propertyBuilder.SetSetMethod(setter);
    }

    private static void BuildConstructor(TypeBuilder typeBuilder, Type simpleModConfigType)
    {
        ConstructorInfo? baseStringCtor = simpleModConfigType.GetConstructor(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            types: new[] { typeof(string) },
            modifiers: null);

        ConstructorInfo? baseEmptyCtor = simpleModConfigType.GetConstructor(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        ConstructorBuilder ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes);

        ILGenerator il = ctor.GetILGenerator();

        // Constructor IL equivalent:
        //     base("ChooseTheAncient");
        // or, for older/newer BaseLib versions without that constructor:
        //     base();
        il.Emit(OpCodes.Ldarg_0);

        if (baseStringCtor != null)
        {
            il.Emit(OpCodes.Ldstr, ModId);
            il.Emit(OpCodes.Call, baseStringCtor);
        }
        else if (baseEmptyCtor != null)
        {
            il.Emit(OpCodes.Call, baseEmptyCtor);
        }
        else
        {
            throw new MissingMethodException(simpleModConfigType.FullName, ".ctor");
        }

        il.Emit(OpCodes.Ret);
    }

    private static void BuildSetupConfigUiOverride(
        TypeBuilder typeBuilder,
        Type simpleModConfigType,
        FieldBuilder pageBuilderMethodField)
    {
        MethodInfo? baseMethod = simpleModConfigType.GetMethod(
            "SetupConfigUI",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: new[] { typeof(Control) },
            modifiers: null);

        if (baseMethod == null || !baseMethod.IsVirtual)
            throw new MissingMethodException(simpleModConfigType.FullName, "SetupConfigUI(Control)");

        MethodInfo invokeMethod = typeof(MethodBase).GetMethod(
            nameof(MethodBase.Invoke),
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: new[] { typeof(object), typeof(object[]) },
            modifiers: null)
            ?? throw new MissingMethodException(typeof(MethodBase).FullName, nameof(MethodBase.Invoke));

        MethodBuilder overrideMethod = typeBuilder.DefineMethod(
            "SetupConfigUI",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            typeof(void),
            new[] { typeof(Control) });

        ILGenerator il = overrideMethod.GetILGenerator();

        // Override IL equivalent:
        //     __chooseTheAncientPageBuilderMethod.Invoke(null, new object[] { optionContainer, this });
        //
        // BaseLib calls this override when it opens the mod config page. The real UI
        // builder remains normal C# in BaseLibSettingsPage; only this small bridge
        // is generated because BaseLib requires a SimpleModConfig subclass.
        //
        // Call BaseLibSettingsPage.Build via MethodInfo.Invoke instead of emitting a direct
        // call/delegate call into the mod assembly. Mono/Godot can throw MethodAccessException
        // when a runtime-generated assembly directly targets mod-assembly methods.
        il.Emit(OpCodes.Ldsfld, pageBuilderMethodField);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, typeof(object));

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Callvirt, invokeMethod);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);

        typeBuilder.DefineMethodOverride(overrideMethod, baseMethod);
    }

private static string ToSentenceCaseOption(string value)
{
    if (string.IsNullOrWhiteSpace(value))
        return value;

    string lower = value.ToLowerInvariant();
    return char.ToUpperInvariant(lower[0]) + lower.Substring(1);
}

private static Type? FindBaseLibType(string fullName)
{
    return AppDomain.CurrentDomain.GetAssemblies()
        .FirstOrDefault(a => string.Equals(a.GetName().Name, "BaseLib", StringComparison.Ordinal))
        ?.GetType(fullName);
}

}

public static class BaseLibSettingsPage
{
    private const string ModPrefix = "ChooseTheAncient-";
    private static object? _activeBaseLibConfigInstance;
    private static bool _suppressGeneratedSettingCallbacks;
    private static int _currentAncientPoolTargetActIndex = -1;
    private static bool _showAdvancedSettings;
    private static bool _showRedundantSettings;
    private static Control? _advancedSettingsContainer;
    private static Control? _redundantSettingsContainer;

    public static void Build(Control optionContainer, object baseLibConfigInstance)
    {
        _activeBaseLibConfigInstance = baseLibConfigInstance;
        _advancedSettingsContainer = null;
        _redundantSettingsContainer = null;
        SyncGeneratedBaseLibProperties(baseLibConfigInstance);

        try
        {
            AddSlider(
                optionContainer,
                "Ancients in vote",
                ChooseTheAncientConfig.AncientCount,
                2,
                8,
                value =>
                {
                    ChooseTheAncientConfig.ApplyAncientCount(value);
                    ModConfigBridge.PushImportantSettingsToModConfig();
                });

            AddChoice(
                optionContainer,
                "Game mode",
                ChooseTheAncientConfig.SelectionGameModeOptions,
                ChooseTheAncientConfig.SelectionGameModeToOption(ChooseTheAncientConfig.GameMode),
                value =>
                {
                    ChooseTheAncientConfig.ApplySelectionGameMode(value);
                    ModConfigBridge.PushImportantSettingsToModConfig();
                });

            AddSectionBreak(optionContainer, "Advanced settings");

            AddToggle(
                optionContainer,
                "Show advanced settings",
                _showAdvancedSettings,
                value =>
                {
                    _showAdvancedSettings = value;
                    UpdateAdvancedSettingsVisibility();
                    UpdateRedundantSettingsVisibility();
                });

            var advancedContainer = new VBoxContainer
            {
                Visible = _showAdvancedSettings,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            _advancedSettingsContainer = advancedContainer;

            AddChoice(
                advancedContainer,
                "Logging mode",
                ChooseTheAncientConfig.LogBackendOptions,
                ChooseTheAncientConfig.LogBackendToOption(ChooseTheAncientConfig.CurrentLogBackend),
                value =>
                {
                    ChooseTheAncientConfig.ApplyLogBackend(value);
                    ModConfigBridge.PushImportantSettingsToModConfig();
                });

            AddChoice(
                advancedContainer,
                "ModLog level",
                ChooseTheAncientConfig.LogLevelOptions,
                ChooseTheAncientConfig.LogLevelToOption(ChooseTheAncientConfig.CurrentLogLevel),
                value =>
                {
                    ChooseTheAncientConfig.ApplyLogLevel(value);
                    ModConfigBridge.PushImportantSettingsToModConfig();
                });

            AddChoice(
                advancedContainer,
                "Vote selection",
                ChooseTheAncientConfig.VoteClickTargetOptions,
                ChooseTheAncientConfig.VoteClickTargetToOption(ChooseTheAncientConfig.VoteClickTarget),
                value =>
                {
                    ChooseTheAncientConfig.ApplyVoteClickTarget(value);
                    ModConfigBridge.PushImportantSettingsToModConfig();
                });

            AddToggle(
                advancedContainer,
                "Button outline",
                ChooseTheAncientConfig.ShowOnlyButtonOutline,
                value =>
                {
                    ChooseTheAncientConfig.ApplyShowOnlyButtonOutlineHotkeys(value);
                    ModConfigBridge.PushImportantSettingsToModConfig();
                });

            AddToggle(
                advancedContainer,
                "Controller hotkeys",
                ChooseTheAncientConfig.ShowControllerHotkeys,
                value =>
                {
                    ChooseTheAncientConfig.ApplyShowControllerHotkeys(value);
                    ModConfigBridge.PushImportantSettingsToModConfig();
                });

            AddButton(
                advancedContainer,
                "Reset all settings",
                "Reset",
                () =>
                {
                    ChooseTheAncientConfig.ResetAllSettingsToDefaults();
                    ModConfigBridge.PushImportantSettingsToModConfig();
                    SyncGeneratedBaseLibProperties(baseLibConfigInstance);
                    UpdateAdvancedSettingsVisibility();
                    UpdateRedundantSettingsVisibility();
                });

            optionContainer.AddChild(advancedContainer);

            AddSectionBreak(optionContainer, "Redundant settings");

            AddToggle(
                optionContainer,
                "Show redundant settings",
                _showRedundantSettings,
                value =>
                {
                    _showRedundantSettings = value;
                    UpdateRedundantSettingsVisibility();
                });

            var redundantContainer = new VBoxContainer
            {
                Visible = _showRedundantSettings,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            _redundantSettingsContainer = redundantContainer;

            redundantContainer.AddChild(CreateDescription(
                "Use AncientConfigsPlus for ancient filtering when it is installed. " +
                "(Plus these legacy options only affect vanilla ancients.)"));

            redundantContainer.AddChild(CreateSubSectionHeader("Special ancient overrides"));

            foreach (string ancientId in new[] { "NEOW", "DARV" })
            {
                string ancientDisplayName = ToAncientDisplayName(ancientId);
                redundantContainer.AddChild(CreateSubHeader($"{ancientDisplayName} overrides"));
                for (int targetActIndex = 0; targetActIndex < 3; targetActIndex++)
                {
                    int capturedTargetActIndex = targetActIndex;
                    AddToggle(
                        redundantContainer,
                        $"{ancientDisplayName} — {ToSentenceStart(ChooseTheAncientConfig.GetSpecialAncientOverrideToggleLabel(targetActIndex))}",
                        ChooseTheAncientConfig.IsSpecialAncientOverrideEnabled(ancientId, targetActIndex),
                        enabled =>
                        {
                            ChooseTheAncientConfig.ApplySpecialAncientOverrideToggle(ancientId, capturedTargetActIndex, enabled);
                            ModConfigBridge.PushImportantSettingsToModConfig();
                        });
                }
            }

            redundantContainer.AddChild(CreateSubSectionHeader("Ancient pool source acts"));

            for (int targetActIndex = 0; targetActIndex < 3; targetActIndex++)
            {
                int capturedTargetActIndex = targetActIndex;
                redundantContainer.AddChild(CreateSubHeader(ChooseTheAncientConfig.GetAncientPoolTargetActLabel(targetActIndex)));
                _currentAncientPoolTargetActIndex = targetActIndex;

                for (int sourceActIndex = 0; sourceActIndex < 3; sourceActIndex++)
                {
                    int capturedSourceActIndex = sourceActIndex;
                    bool enabled = ChooseTheAncientConfig
                        .GetEnabledAncientPoolSourceActs(targetActIndex)
                        .Contains(sourceActIndex);

                    AddToggle(
                        redundantContainer,
                        $"Allow {ChooseTheAncientConfig.GetAncientPoolSourceActLabel(sourceActIndex)} ancients",
                        enabled,
                        value =>
                        {
                            ChooseTheAncientConfig.ApplyAncientPoolSourceActToggle(
                                capturedTargetActIndex,
                                capturedSourceActIndex,
                                value);
                            ModConfigBridge.PushImportantSettingsToModConfig();
                        });
                }

                _currentAncientPoolTargetActIndex = -1;
            }

            optionContainer.AddChild(redundantContainer);

            TrySetupFocusNeighbors(baseLibConfigInstance, optionContainer);
        }
        catch (Exception e)
        {
            ModLog.Warn($"Failed to build BaseLib settings page: {e}");
            optionContainer.AddChild(CreateDescription("ChooseTheAncient failed to build its BaseLib settings page. Check the log for details."));
        }
        finally
        {
            _activeBaseLibConfigInstance = null;
        }
    }

    public static void OnGeneratedSettingChanged(string propertyName, object? value)
    {
        if (_suppressGeneratedSettingCallbacks)
            return;

        try
        {
            switch (propertyName)
            {
                case "AncientCount":
                    ChooseTheAncientConfig.ApplyAncientCount(Convert.ToInt32(value));
                    break;
                case "GameMode":
                    ChooseTheAncientConfig.ApplySelectionGameMode(ToCanonicalOption(value?.ToString() ?? string.Empty, ChooseTheAncientConfig.SelectionGameModeOptions));
                    break;
                case "VoteClickTarget":
                    ChooseTheAncientConfig.ApplyVoteClickTarget(ToCanonicalOption(value?.ToString() ?? string.Empty, ChooseTheAncientConfig.VoteClickTargetOptions));
                    break;
                case "LogBackend":
                    ChooseTheAncientConfig.ApplyLogBackend(ToCanonicalOption(value?.ToString() ?? string.Empty, ChooseTheAncientConfig.LogBackendOptions));
                    break;
                case "LogLevel":
                    ChooseTheAncientConfig.ApplyLogLevel(ToCanonicalOption(value?.ToString() ?? string.Empty, ChooseTheAncientConfig.LogLevelOptions));
                    break;

                case "ShowAdvancedSettings":
                    _showAdvancedSettings = Convert.ToBoolean(value);
                    UpdateAdvancedSettingsVisibility();
                    break;
                case "ShowRedundantSettings":
                    _showRedundantSettings = Convert.ToBoolean(value);
                    UpdateRedundantSettingsVisibility();
                    break;
                case "ShowControllerHotkeys":
                    ChooseTheAncientConfig.ApplyShowControllerHotkeys(Convert.ToBoolean(value));
                    break;
                case "ShowOnlyButtonOutline":
                    ChooseTheAncientConfig.ApplyShowOnlyButtonOutlineHotkeys(Convert.ToBoolean(value));
                    break;

                case "Act1AncientsFromAct1":
                    ChooseTheAncientConfig.ApplyAncientPoolSourceActToggle(0, 0, Convert.ToBoolean(value));
                    break;
                case "Act1AncientsFromAct2":
                    ChooseTheAncientConfig.ApplyAncientPoolSourceActToggle(0, 1, Convert.ToBoolean(value));
                    break;
                case "Act1AncientsFromAct3":
                    ChooseTheAncientConfig.ApplyAncientPoolSourceActToggle(0, 2, Convert.ToBoolean(value));
                    break;
                case "Act2AncientsFromAct1":
                    ChooseTheAncientConfig.ApplyAncientPoolSourceActToggle(1, 0, Convert.ToBoolean(value));
                    break;
                case "Act2AncientsFromAct2":
                    ChooseTheAncientConfig.ApplyAncientPoolSourceActToggle(1, 1, Convert.ToBoolean(value));
                    break;
                case "Act2AncientsFromAct3":
                    ChooseTheAncientConfig.ApplyAncientPoolSourceActToggle(1, 2, Convert.ToBoolean(value));
                    break;
                case "Act3AncientsFromAct1":
                    ChooseTheAncientConfig.ApplyAncientPoolSourceActToggle(2, 0, Convert.ToBoolean(value));
                    break;
                case "Act3AncientsFromAct2":
                    ChooseTheAncientConfig.ApplyAncientPoolSourceActToggle(2, 1, Convert.ToBoolean(value));
                    break;
                case "Act3AncientsFromAct3":
                    ChooseTheAncientConfig.ApplyAncientPoolSourceActToggle(2, 2, Convert.ToBoolean(value));
                    break;

                case "IncludeNeowInAct1Selection":
                    ChooseTheAncientConfig.ApplySpecialAncientOverrideToggle("NEOW", 0, Convert.ToBoolean(value));
                    break;
                case "IncludeNeowInAct2Selection":
                    ChooseTheAncientConfig.ApplySpecialAncientOverrideToggle("NEOW", 1, Convert.ToBoolean(value));
                    break;
                case "IncludeNeowInAct3Selection":
                    ChooseTheAncientConfig.ApplySpecialAncientOverrideToggle("NEOW", 2, Convert.ToBoolean(value));
                    break;
                case "IncludeDarvInAct1Selection":
                    ChooseTheAncientConfig.ApplySpecialAncientOverrideToggle("DARV", 0, Convert.ToBoolean(value));
                    break;
                case "IncludeDarvInAct2Selection":
                    ChooseTheAncientConfig.ApplySpecialAncientOverrideToggle("DARV", 1, Convert.ToBoolean(value));
                    break;
                case "IncludeDarvInAct3Selection":
                    ChooseTheAncientConfig.ApplySpecialAncientOverrideToggle("DARV", 2, Convert.ToBoolean(value));
                    break;

                default:
                    ModLog.Debug($"Ignoring unknown generated BaseLib setting '{propertyName}'.");
                    return;
            }

            ModConfigBridge.PushImportantSettingsToModConfig();
        }
        catch (Exception e)
        {
            ModLog.Warn($"Failed to apply generated BaseLib setting '{propertyName}' = '{value}': {e}");
        }
    }

    private static void SyncGeneratedBaseLibProperties(object baseLibConfigInstance)
    {
        _suppressGeneratedSettingCallbacks = true;

        try
        {
            Type type = baseLibConfigInstance.GetType();

            SetGeneratedProperty(type, "AncientCount", ChooseTheAncientConfig.AncientCount);
            SetGeneratedEnumOptionProperty(
                type,
                "GameMode",
                ChooseTheAncientConfig.SelectionGameModeOptions,
                ChooseTheAncientConfig.SelectionGameModeToOption(ChooseTheAncientConfig.GameMode));
            SetGeneratedEnumOptionProperty(
                type,
                "VoteClickTarget",
                ChooseTheAncientConfig.VoteClickTargetOptions,
                ChooseTheAncientConfig.VoteClickTargetToOption(ChooseTheAncientConfig.VoteClickTarget));
            SetGeneratedEnumOptionProperty(
                type,
                "LogBackend",
                ChooseTheAncientConfig.LogBackendOptions,
                ChooseTheAncientConfig.LogBackendToOption(ChooseTheAncientConfig.CurrentLogBackend));
            SetGeneratedEnumOptionProperty(
                type,
                "LogLevel",
                ChooseTheAncientConfig.LogLevelOptions,
                ChooseTheAncientConfig.LogLevelToOption(ChooseTheAncientConfig.CurrentLogLevel));
            SetGeneratedProperty(type, "ShowAdvancedSettings", _showAdvancedSettings);
            SetGeneratedProperty(type, "ShowRedundantSettings", _showRedundantSettings);
            SetGeneratedProperty(type, "ShowControllerHotkeys", ChooseTheAncientConfig.ShowControllerHotkeys);
            SetGeneratedProperty(type, "ShowOnlyButtonOutline", ChooseTheAncientConfig.ShowOnlyButtonOutline);

            for (int targetActIndex = 0; targetActIndex < 3; targetActIndex++)
            {
                var enabledSources = ChooseTheAncientConfig.GetEnabledAncientPoolSourceActs(targetActIndex);
                for (int sourceActIndex = 0; sourceActIndex < 3; sourceActIndex++)
                {
                    string propertyName = $"Act{targetActIndex + 1}AncientsFromAct{sourceActIndex + 1}";
                    SetGeneratedProperty(type, propertyName, enabledSources.Contains(sourceActIndex));
                }
            }

            SetGeneratedProperty(type, "IncludeNeowInAct1Selection", ChooseTheAncientConfig.IsSpecialAncientOverrideEnabled("NEOW", 0));
            SetGeneratedProperty(type, "IncludeNeowInAct2Selection", ChooseTheAncientConfig.IsSpecialAncientOverrideEnabled("NEOW", 1));
            SetGeneratedProperty(type, "IncludeNeowInAct3Selection", ChooseTheAncientConfig.IsSpecialAncientOverrideEnabled("NEOW", 2));
            SetGeneratedProperty(type, "IncludeDarvInAct1Selection", ChooseTheAncientConfig.IsSpecialAncientOverrideEnabled("DARV", 0));
            SetGeneratedProperty(type, "IncludeDarvInAct2Selection", ChooseTheAncientConfig.IsSpecialAncientOverrideEnabled("DARV", 1));
            SetGeneratedProperty(type, "IncludeDarvInAct3Selection", ChooseTheAncientConfig.IsSpecialAncientOverrideEnabled("DARV", 2));
        }
        finally
        {
            _suppressGeneratedSettingCallbacks = false;
        }
    }

    private static void SetGeneratedEnumOptionProperty(Type type, string propertyName, string[] options, string currentOption)
    {
        PropertyInfo? property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);
        if (property == null || !property.PropertyType.IsEnum)
            return;

        int selectedIndex = Array.FindIndex(options, option => string.Equals(option, currentOption, StringComparison.Ordinal));
        if (selectedIndex < 0)
            selectedIndex = 0;

        object enumValue = Enum.ToObject(property.PropertyType, selectedIndex);
        property.SetValue(null, enumValue);
    }

    private static void SetGeneratedProperty(Type type, string propertyName, object value)
    {
        type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static)?.SetValue(null, value);
    }

    private static void AddSlider(Control parent, string label, int value, int min, int max, Action<int> onChanged)
    {
        Control? baseLibSlider = TryCreateBaseLibPropertyControl("CreateRawSliderControl", "AncientCount");
        if (baseLibSlider != null)
        {
            parent.AddChild(CreateRow(label, baseLibSlider));
            return;
        }

        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = 1,
            Value = value,
            CustomMinimumSize = new Vector2(252, 64),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.Fill,
            FocusMode = Control.FocusModeEnum.All
        };

        var valueLabel = CreateSmallValueLabel(value.ToString());
        var settingContainer = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(324, 64),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd,
            SizeFlagsVertical = Control.SizeFlags.Fill
        };

        settingContainer.AddChild(slider);
        settingContainer.AddChild(valueLabel);

        slider.ValueChanged += raw =>
        {
            int nextValue = Mathf.RoundToInt((float)raw);
            valueLabel.Text = nextValue.ToString();
            onChanged(nextValue);
        };

        parent.AddChild(CreateRow(label, settingContainer));
    }

    private static void AddToggle(Control parent, string label, bool value, Action<bool> onChanged)
    {
        // Prefer BaseLib's real NConfigTickbox. It owns the game tickbox scene,
        // connects the correct NSettingsTickbox input handlers in _Ready(), and
        // writes back through the generated bool property. The previous direct
        // settings_tickbox scene looked right but did not receive clicks reliably.
        string? propertyName = TryGetGeneratedBoolPropertyName(label);
        Control? tickbox = propertyName == null
            ? null
            : TryCreateBaseLibPropertyControl("CreateRawTickboxControl", propertyName);

        if (tickbox != null)
        {
            parent.AddChild(CreateInlineToggleRow(label, tickbox));
            return;
        }

        // Last-resort fallback for older/unexpected BaseLib versions.
        var checkbox = new CheckBox
        {
            ButtonPressed = value,
            CustomMinimumSize = new Vector2(76, 76),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.Fill,
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Pass
        };

        checkbox.Toggled += pressed => onChanged(pressed);
        parent.AddChild(CreateInlineToggleRow(label, checkbox));
    }

    private static Control CreateInlineToggleRow(string labelText, Control toggleControl)
    {
        toggleControl.CustomMinimumSize = new Vector2(76, 76);
        toggleControl.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        toggleControl.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

        Control label = CreateBaseLibLabel(labelText, 32) ?? new Godot.Label
        {
            Text = labelText,
            CustomMinimumSize = new Vector2(0, 76),
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };

        if (label is Godot.Label godotLabel)
            godotLabel.AddThemeFontSizeOverride("font_size", 32);

        label.CustomMinimumSize = new Vector2(0, 76);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

        var rightSlot = new CenterContainer
        {
            CustomMinimumSize = new Vector2(324, 76),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Pass
        };
        rightSlot.AddChild(toggleControl);

        var row = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(0, 76),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Pass
        };
        row.AddThemeConstantOverride("separation", 16);
        row.AddChild(label);
        row.AddChild(rightSlot);

        var margin = new MarginContainer
        {
            CustomMinimumSize = new Vector2(0, 76),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Pass
        };
        margin.AddThemeConstantOverride("margin_left", 0);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 0);
        margin.AddThemeConstantOverride("margin_bottom", 0);
        margin.AddChild(row);
        return margin;
    }

    private static void UpdateAdvancedSettingsVisibility()
    {
        if (_advancedSettingsContainer == null)
            return;

        if (!GodotObject.IsInstanceValid(_advancedSettingsContainer))
        {
            _advancedSettingsContainer = null;
            return;
        }

        _advancedSettingsContainer.Visible = _showAdvancedSettings;
    }

    private static void UpdateRedundantSettingsVisibility()
    {
        if (_redundantSettingsContainer == null)
            return;

        if (!GodotObject.IsInstanceValid(_redundantSettingsContainer))
        {
            _redundantSettingsContainer = null;
            return;
        }

        _redundantSettingsContainer.Visible = _showRedundantSettings;
    }


    private static string ToAncientDisplayName(string ancientId)
    {
        return ancientId.ToUpperInvariant() switch
        {
            "NEOW" => "Neow",
            "DARV" => "Darv",
            _ => ToSentenceCaseOption(ancientId)
        };
    }

    private static string ToSentenceStart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        return char.ToLowerInvariant(value[0]) + value.Substring(1);
    }

    private static string ToSentenceCaseOption(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        string lower = value.ToLowerInvariant();
        return char.ToUpperInvariant(lower[0]) + lower.Substring(1);
    }

    private static string ToCanonicalOption(string displayValue, string[] canonicalOptions)
    {
        foreach (string option in canonicalOptions)
        {
            if (string.Equals(option, displayValue, StringComparison.Ordinal) ||
                string.Equals(ToSentenceCaseOption(option), displayValue, StringComparison.Ordinal))
            {
                return option;
            }
        }

        return displayValue;
    }

    private static string? TryGetGeneratedChoicePropertyName(string label)
    {
        return label switch
        {
            "Game mode" => "GameMode",
            "Vote selection" => "VoteClickTarget",
            "Vote click area" => "VoteClickTarget",
            "Log output" => "LogBackend",
            "Log level" => "LogLevel",
            _ => null
        };
    }

    private static void AddChoice(Control parent, string label, string[] options, string currentValue, Action<string> onChanged)
    {
        string? propertyName = TryGetGeneratedChoicePropertyName(label);
        Control? baseLibDropdown = propertyName == null
            ? null
            : TryCreateBaseLibPropertyControl("CreateRawDropdownControl", propertyName);

        if (baseLibDropdown != null)
        {
            parent.AddChild(CreateRow(label, baseLibDropdown));
            return;
        }

        var optionButton = new OptionButton
        {
            CustomMinimumSize = new Vector2(360, 70),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd,
            SizeFlagsVertical = Control.SizeFlags.Fill,
            FocusMode = Control.FocusModeEnum.All
        };

        int selectedIndex = 0;
        for (int i = 0; i < options.Length; i++)
        {
            optionButton.AddItem(ToSentenceCaseOption(options[i]), i);
            if (string.Equals(options[i], currentValue, StringComparison.Ordinal))
                selectedIndex = i;
        }

        ApplyFallbackDropdownTheme(optionButton);
        optionButton.Select(selectedIndex);
        optionButton.ItemSelected += index =>
        {
            int i = (int)index;
            if (i >= 0 && i < options.Length)
                onChanged(options[i]);
        };

        parent.AddChild(CreateRow(label, optionButton));
    }

    private static void ApplyFallbackDropdownTheme(OptionButton optionButton)
    {
        optionButton.AddThemeFontSizeOverride("font_size", 32);
        optionButton.AddThemeColorOverride("font_color", Color.FromHtml("#fff2d5"));
        optionButton.AddThemeColorOverride("font_hover_color", Color.FromHtml("#fff8df"));
        optionButton.AddThemeColorOverride("font_focus_color", Color.FromHtml("#fff8df"));
        optionButton.AddThemeColorOverride("font_pressed_color", Color.FromHtml("#fff8df"));
        optionButton.AddThemeStyleboxOverride("normal", CreateFallbackDropdownStyle("#213d45", "#a8d4df", 2));
        optionButton.AddThemeStyleboxOverride("hover", CreateFallbackDropdownStyle("#294c56", "#d5f6ff", 2));
        optionButton.AddThemeStyleboxOverride("pressed", CreateFallbackDropdownStyle("#162d34", "#f0d36a", 2));
        optionButton.AddThemeStyleboxOverride("focus", CreateFallbackDropdownStyle("#213d45", "#f0d36a", 3));

        PopupMenu popup = optionButton.GetPopup();
        popup.AddThemeFontSizeOverride("font_size", 30);
        popup.AddThemeColorOverride("font_color", Color.FromHtml("#fff2d5"));
        popup.AddThemeColorOverride("font_hover_color", Color.FromHtml("#fff8df"));
    }

    private static StyleBoxFlat CreateFallbackDropdownStyle(string backgroundColor, string borderColor, int borderWidth)
    {
        return new StyleBoxFlat
        {
            BgColor = Color.FromHtml(backgroundColor),
            BorderColor = Color.FromHtml(borderColor),
            BorderWidthLeft = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthBottom = borderWidth,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomRight = 5,
            CornerRadiusBottomLeft = 5,
            ContentMarginLeft = 18,
            ContentMarginRight = 18,
            ContentMarginTop = 8,
            ContentMarginBottom = 8
        };
    }

    private static void AddButton(Control parent, string label, string buttonText, Action onPressed)
    {
        Control button = TryCreateBaseLibButton(buttonText, onPressed) ?? new Button
        {
            Text = buttonText,
            CustomMinimumSize = new Vector2(324, 64),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd,
            SizeFlagsVertical = Control.SizeFlags.Fill,
            FocusMode = Control.FocusModeEnum.All
        };

        parent.AddChild(CreateRow(label, button));
    }

    private static Control CreateRow(string labelText, Control settingControl)
    {
        Control label = CreateBaseLibLabel(labelText, 30) ?? new Godot.Label
        {
            Text = labelText,
            CustomMinimumSize = new Vector2(0, 64),
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };

        if (label is Godot.Label rowLabel)
            rowLabel.AddThemeFontSizeOverride("font_size", 30);

        Control? baseLibRow = TryCreateBaseLibOptionRow(labelText, label, settingControl);
        if (baseLibRow != null)
            return baseLibRow;

        var row = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(0, 64),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        row.AddThemeConstantOverride("separation", 24);
        row.AddChild(label);
        row.AddChild(settingControl);

        var margin = new MarginContainer
        {
            CustomMinimumSize = new Vector2(0, 64),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Pass
        };
        margin.AddThemeConstantOverride("margin_left", 36);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddChild(row);
        return margin;
    }

    private static Control CreateSectionHeader(string text, bool alignToTop, bool centered)
    {
        Control? header = TryCreateBaseLibSectionHeader(text, alignToTop, centered);
        if (header != null)
            return header;

        return new Godot.Label
        {
            Text = text,
            CustomMinimumSize = new Vector2(0, 76),
            HorizontalAlignment = centered ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = alignToTop ? VerticalAlignment.Top : VerticalAlignment.Center,
            SizeFlagsHorizontal = centered ? Control.SizeFlags.ShrinkCenter : Control.SizeFlags.ShrinkBegin
        };
    }

    private static Control CreateSubSectionHeader(string text)
    {
        var margin = new MarginContainer
        {
            CustomMinimumSize = new Vector2(0, 72),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        margin.AddThemeConstantOverride("margin_left", 18);
        margin.AddThemeConstantOverride("margin_right", 18);
        margin.AddThemeConstantOverride("margin_top", 18);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        margin.AddChild(CreateBaseLibLabel(text, 38) ?? new Godot.Label
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        });
        return margin;
    }

    private static Control CreateSubHeader(string text)
    {
        var margin = new MarginContainer
        {
            CustomMinimumSize = new Vector2(0, 60),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        margin.AddThemeConstantOverride("margin_left", 54);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 6);
        margin.AddChild(CreateBaseLibLabel(text, 34) ?? new Godot.Label
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        });
        return margin;
    }

    private static Control CreateDescription(string text)
    {
        var margin = new MarginContainer
        {
            CustomMinimumSize = new Vector2(0, 72),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        margin.AddChild(CreateBaseLibLabel(text, 24) ?? new Godot.Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        });
        return margin;
    }

    private static Godot.Label CreateSmallValueLabel(string text)
    {
        return new Godot.Label
        {
            Text = text,
            CustomMinimumSize = new Vector2(56, 64),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd
        };
    }

    private static void AddSectionBreak(Control parent, string label)
    {
        parent.AddChild(CreateDivider());
        parent.AddChild(CreateSectionHeader(label, alignToTop: false, centered: true));
    }

    private static Control CreateDivider()
    {
        var separator = new HSeparator
        {
            CustomMinimumSize = new Vector2(0, 28),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };

        var margin = new MarginContainer
        {
            CustomMinimumSize = new Vector2(0, 28),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        margin.AddChild(separator);
        return margin;
    }

    private static string? TryGetGeneratedBoolPropertyName(string label)
    {
        return label switch
        {
            "Show advanced settings" => "ShowAdvancedSettings",
            "Show redundant settings" => "ShowRedundantSettings",
            "Controller hotkeys" => "ShowControllerHotkeys",
            "Show controller hotkeys" => "ShowControllerHotkeys",
            "Button outline" => "ShowOnlyButtonOutline",
            "Alternative vote button design" => "ShowOnlyButtonOutline",

            "Allow From Act 1 ancients" when _currentAncientPoolTargetActIndex == 0 => "Act1AncientsFromAct1",
            "Allow From Act 2 ancients" when _currentAncientPoolTargetActIndex == 0 => "Act1AncientsFromAct2",
            "Allow From Act 3 ancients" when _currentAncientPoolTargetActIndex == 0 => "Act1AncientsFromAct3",
            "Allow From Act 1 ancients" when _currentAncientPoolTargetActIndex == 1 => "Act2AncientsFromAct1",
            "Allow From Act 2 ancients" when _currentAncientPoolTargetActIndex == 1 => "Act2AncientsFromAct2",
            "Allow From Act 3 ancients" when _currentAncientPoolTargetActIndex == 1 => "Act2AncientsFromAct3",
            "Allow From Act 1 ancients" when _currentAncientPoolTargetActIndex == 2 => "Act3AncientsFromAct1",
            "Allow From Act 2 ancients" when _currentAncientPoolTargetActIndex == 2 => "Act3AncientsFromAct2",
            "Allow From Act 3 ancients" when _currentAncientPoolTargetActIndex == 2 => "Act3AncientsFromAct3",

            "Neow — include in Act 1 selection" => "IncludeNeowInAct1Selection",
            "Neow — include in Act 2 selection" => "IncludeNeowInAct2Selection",
            "Neow — include in Act 3 selection" => "IncludeNeowInAct3Selection",
            "Darv — include in Act 1 selection" => "IncludeDarvInAct1Selection",
            "Darv — include in Act 2 selection" => "IncludeDarvInAct2Selection",
            "Darv — include in Act 3 selection" => "IncludeDarvInAct3Selection",
            _ => null
        };
    }

    private static Control? TryCreateBaseLibPropertyControl(string methodName, string propertyName)
    {
        try
        {
            object? instance = _activeBaseLibConfigInstance;
            if (instance == null)
                return null;

            PropertyInfo? property = instance.GetType().GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Static);

            if (property == null)
            {
                ModLog.Debug($"BaseLib generated property '{propertyName}' was not found; using fallback control.");
                return null;
            }

            MethodInfo? method = FindInstanceMethod(instance.GetType(), methodName, typeof(PropertyInfo));
            if (method == null)
            {
                ModLog.Debug($"BaseLib method '{methodName}(PropertyInfo)' was not found; using fallback control.");
                return null;
            }

            return method.Invoke(method.IsStatic ? null : instance, new object[] { property }) as Control;
        }
        catch (Exception e)
        {
            ModLog.Debug($"Could not create BaseLib native control '{methodName}' for '{propertyName}'; using fallback control. Error: {e.Message}");
            return null;
        }
    }

    private static Control? TryCreateBaseLibOptionRow(string labelText, Control label, Control settingControl)
    {
        try
        {
            Type? rowType = FindBaseLibType("BaseLib.Config.UI.NConfigOptionRow");
            ConstructorInfo? ctor = rowType?.GetConstructor(new[] { typeof(string), typeof(string), typeof(Control), typeof(Control) });
            if (ctor == null)
                return null;

            string rowName = MakeSafeNodeName(labelText);
            return ctor.Invoke(new object[] { ModPrefix, rowName, label, settingControl }) as Control;
        }
        catch (Exception e)
        {
            ModLog.Debug($"Could not create BaseLib option row; using fallback row. Error: {e.Message}");
            return null;
        }
    }

    private static Control? TryCreateBaseLibSectionHeader(string label, bool alignToTop, bool centered)
    {
        try
        {
            object? instance = _activeBaseLibConfigInstance;
            MethodInfo? method = instance == null
                ? null
                : FindInstanceMethod(instance.GetType(), "CreateSectionHeader", typeof(string), typeof(bool), typeof(bool));

            return method?.Invoke(method.IsStatic ? null : instance, new object[] { label, alignToTop, centered }) as Control;
        }
        catch (Exception e)
        {
            ModLog.Debug($"Could not create BaseLib section header; using fallback header. Error: {e.Message}");
            return null;
        }
    }

    private static Control? CreateBaseLibLabel(string text, int fontSize)
    {
        try
        {
            object? instance = _activeBaseLibConfigInstance;
            MethodInfo? method = instance == null
                ? null
                : FindInstanceMethod(instance.GetType(), "CreateRawLabelControl", typeof(string), typeof(int));

            if (method == null)
                return null;

            Control? label = method.Invoke(method.IsStatic ? null : instance, new object[] { text, fontSize }) as Control;
            if (label != null)
            {
                label.CustomMinimumSize = new Vector2(0, 64);
                label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            }

            return label;
        }
        catch (Exception e)
        {
            ModLog.Debug($"Could not create BaseLib label; using fallback label. Error: {e.Message}");
            return null;
        }
    }

    private static Control? TryCreateBaseLibButton(string text, Action onPressed)
    {
        try
        {
            object? instance = _activeBaseLibConfigInstance;
            MethodInfo? method = instance == null
                ? null
                : FindInstanceMethod(instance.GetType(), "CreateRawButtonControl", typeof(string), typeof(Action));

            if (method == null)
                return null;

            Control? button = method.Invoke(method.IsStatic ? null : instance, new object[] { text, onPressed }) as Control;
            if (button != null)
            {
                button.CustomMinimumSize = new Vector2(324, 64);
                button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
                button.SizeFlagsVertical = Control.SizeFlags.Fill;
                button.FocusMode = Control.FocusModeEnum.All;
            }

            return button;
        }
        catch (Exception e)
        {
            ModLog.Debug($"Could not create BaseLib button; using fallback button. Error: {e.Message}");
            return null;
        }
    }

    private static MethodInfo? FindInstanceMethod(Type type, string name, params Type[] parameterTypes)
    {
        for (Type? current = type; current != null; current = current.BaseType)
        {
            MethodInfo? method = current.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: parameterTypes,
                modifiers: null);

            if (method != null)
                return method;
        }

        return null;
    }

    private static Type? FindBaseLibType(string fullName)
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, "BaseLib", StringComparison.Ordinal))
            ?.GetType(fullName);
    }

    private static string MakeSafeNodeName(string value)
    {
        var chars = value
            .Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-')
            .ToArray();

        return chars.Length == 0 ? "Setting" : new string(chars);
    }

    private static void TrySetupFocusNeighbors(object baseLibConfigInstance, Control optionContainer)
    {
        try
        {
            MethodInfo? setupFocusNeighbors = baseLibConfigInstance
                .GetType()
                .BaseType?
                .GetMethod("SetupFocusNeighbors", BindingFlags.Public | BindingFlags.Static);

            setupFocusNeighbors?.Invoke(null, new object[] { optionContainer });
        }
        catch (Exception e)
        {
            ModLog.Debug($"BaseLib SetupFocusNeighbors failed; Godot default focus behavior will be used. Error: {e.Message}");
        }
    }
}
