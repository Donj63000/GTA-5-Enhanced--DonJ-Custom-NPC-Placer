using System;
using GTA;

public sealed partial class DonJEnemySpawner
{
    private const int RuntimeStageErrorLogCooldownMs = 10000;

    private readonly int[] _runtimeTickStageNextErrorLogAt =
        new int[(int)RuntimeTickStage.Count];

    private readonly bool[] _runtimeTickStageHasLoggedError =
        new bool[(int)RuntimeTickStage.Count];

    private enum RuntimeTickStage
    {
        Relationships,
        JusticeEarly,
        CartelEarly,
        Terminator,
        CustomModelRequest,
        SaveRequest,
        LoadRequest,
        Placement,
        MenuAnimation,
        Hud,
        Menu,
        PendingSpawn,
        PlayerHostility,
        JusticeLate,
        JusticeRecovery,
        Npcs,
        CartelLate,
        Vehicles,
        Objects,
        ObjectInteractions,
        Portals,
        Status,
        JusticeDamageFlush,
        Count
    }

    private enum RuntimeShutdownStage
    {
        Justice,
        Terminator,
        Placement,
        Menu,
        DangerAction,
        HighSecurityEscort,
        NpcBlips,
        VehicleBlips,
        Relationships
    }

    private void ReportRuntimeTickStageFailure(RuntimeTickStage stage, Exception exception)
    {
        try
        {
            int now;

            try
            {
                now = Game.GameTime;
            }
            catch
            {
                now = Environment.TickCount;
            }

            if (!ShouldLogRuntimeTickStageFailure(stage, now))
            {
                return;
            }

            string context = GetRuntimeTickStageLogContext(stage);
            LogException(context, exception);

            try
            {
                // Je rends l'erreur visible au joueur au même rythme borné que le log.
                ShowStatus(
                    "Erreur " + TrainerTitle + " (" + context + "): " +
                    (exception == null ? "exception inconnue" : exception.Message),
                    7000);
            }
            catch
            {
                // Je ne laisse jamais l'affichage d'une erreur interrompre les autres domaines du tick.
            }
        }
        catch
        {
            // Je garde le garde-fou lui-même sans effet sur le tick si l'horloge ou le log échoue.
        }
    }

    private bool ShouldLogRuntimeTickStageFailure(RuntimeTickStage stage, int now)
    {
        int index = (int)stage;

        if (index < 0 ||
            index >= (int)RuntimeTickStage.Count ||
            _runtimeTickStageNextErrorLogAt == null ||
            _runtimeTickStageHasLoggedError == null ||
            index >= _runtimeTickStageNextErrorLogAt.Length ||
            index >= _runtimeTickStageHasLoggedError.Length)
        {
            return true;
        }

        if (_runtimeTickStageHasLoggedError[index] &&
            !HasRuntimeStageCooldownElapsed(now, _runtimeTickStageNextErrorLogAt[index]))
        {
            return false;
        }

        _runtimeTickStageHasLoggedError[index] = true;
        _runtimeTickStageNextErrorLogAt[index] = unchecked(now + RuntimeStageErrorLogCooldownMs);
        return true;
    }

    private static bool HasRuntimeStageCooldownElapsed(int now, int deadline)
    {
        // Je compare les échéances par différence signée pour rester correct au rebouclage de GameTime.
        return unchecked(now - deadline) >= 0;
    }

    private static string GetRuntimeTickStageLogContext(RuntimeTickStage stage)
    {
        switch (stage)
        {
            case RuntimeTickStage.Relationships: return "Tick.Relationships";
            case RuntimeTickStage.JusticeEarly: return "Tick.JusticeEarly";
            case RuntimeTickStage.CartelEarly: return "Tick.CartelEarly";
            case RuntimeTickStage.Terminator: return "Tick.Terminator";
            case RuntimeTickStage.CustomModelRequest: return "Tick.CustomModelRequest";
            case RuntimeTickStage.SaveRequest: return "Tick.SaveRequest";
            case RuntimeTickStage.LoadRequest: return "Tick.LoadRequest";
            case RuntimeTickStage.Placement: return "Tick.Placement";
            case RuntimeTickStage.MenuAnimation: return "Tick.MenuAnimation";
            case RuntimeTickStage.Hud: return "Tick.Hud";
            case RuntimeTickStage.Menu: return "Tick.Menu";
            case RuntimeTickStage.PendingSpawn: return "Tick.PendingSpawn";
            case RuntimeTickStage.PlayerHostility: return "Tick.PlayerHostility";
            case RuntimeTickStage.JusticeLate: return "Tick.JusticeLate";
            case RuntimeTickStage.JusticeRecovery: return "Tick.JusticeRecovery";
            case RuntimeTickStage.Npcs: return "Tick.Npcs";
            case RuntimeTickStage.CartelLate: return "Tick.CartelLate";
            case RuntimeTickStage.Vehicles: return "Tick.Vehicles";
            case RuntimeTickStage.Objects: return "Tick.Objects";
            case RuntimeTickStage.ObjectInteractions: return "Tick.ObjectInteractions";
            case RuntimeTickStage.Portals: return "Tick.Portals";
            case RuntimeTickStage.Status: return "Tick.Status";
            case RuntimeTickStage.JusticeDamageFlush: return "Tick.JusticeDamageFlush";
            default: return "Tick.Unknown";
        }
    }

    private static void ReportRuntimeShutdownFailure(RuntimeShutdownStage stage, Exception exception)
    {
        LogException(GetRuntimeShutdownLogContext(stage), exception);
    }

    private static string GetRuntimeShutdownLogContext(RuntimeShutdownStage stage)
    {
        switch (stage)
        {
            case RuntimeShutdownStage.Justice: return "Shutdown.Justice";
            case RuntimeShutdownStage.Terminator: return "Shutdown.Terminator";
            case RuntimeShutdownStage.Placement: return "Shutdown.Placement";
            case RuntimeShutdownStage.Menu: return "Shutdown.Menu";
            case RuntimeShutdownStage.DangerAction: return "Shutdown.DangerAction";
            case RuntimeShutdownStage.HighSecurityEscort: return "Shutdown.HighSecurityEscort";
            case RuntimeShutdownStage.NpcBlips: return "Shutdown.NpcBlips";
            case RuntimeShutdownStage.VehicleBlips: return "Shutdown.VehicleBlips";
            case RuntimeShutdownStage.Relationships: return "Shutdown.Relationships";
            default: return "Shutdown.Unknown";
        }
    }

    private void RemoveAllNpcBlipsForShutdown()
    {
        if (_spawnedNpcs == null)
        {
            return;
        }

        Exception firstFailure = null;
        int failureCount = 0;

        for (int i = 0; i < _spawnedNpcs.Count; i++)
        {
            try
            {
                RemoveNpcBlip(_spawnedNpcs[i]);
            }
            catch (Exception ex)
            {
                failureCount++;
                if (firstFailure == null)
                {
                    firstFailure = ex;
                }
            }
        }

        LogShutdownCollectionFailures("Shutdown.NpcBlips", failureCount, firstFailure);
    }

    private void RemoveAllVehicleBlipsForShutdown()
    {
        if (_placedVehicles == null)
        {
            return;
        }

        Exception firstFailure = null;
        int failureCount = 0;

        for (int i = 0; i < _placedVehicles.Count; i++)
        {
            try
            {
                RemovePlacedVehicleBlip(_placedVehicles[i]);
            }
            catch (Exception ex)
            {
                failureCount++;
                if (firstFailure == null)
                {
                    firstFailure = ex;
                }
            }
        }

        LogShutdownCollectionFailures("Shutdown.VehicleBlips", failureCount, firstFailure);
    }

    private static void LogShutdownCollectionFailures(
        string context,
        int failureCount,
        Exception firstFailure)
    {
        if (failureCount <= 0 || firstFailure == null)
        {
            return;
        }

        LogException(context, firstFailure);
        LogWarning(
            context,
            failureCount.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            " élément(s) n'ont pas pu être nettoyés; les suivants ont quand même été traités.");
    }

    private void RemoveRuntimeRelationshipGroupsForShutdown()
    {
        RemoveRuntimeRelationshipGroupForShutdown(
            ref _hostileGroupHash,
            "Shutdown.Relationships.Hostile");
        RemoveRuntimeRelationshipGroupForShutdown(
            ref _neutralGroupHash,
            "Shutdown.Relationships.Neutral");
        RemoveRuntimeRelationshipGroupForShutdown(
            ref _allyGroupHash,
            "Shutdown.Relationships.Ally");
    }

    private static void RemoveRuntimeRelationshipGroupForShutdown(
        ref int ownedGroupHash,
        string context)
    {
        int groupHash = ownedGroupHash;

        try
        {
            if (groupHash != 0)
            {
                World.RemoveRelationshipGroup(groupHash);
            }
        }
        catch (Exception ex)
        {
            LogException(context, ex);
        }
        finally
        {
            // Je retire toujours la propriété locale pour empêcher toute réutilisation après l'arrêt.
            ownedGroupHash = 0;
        }
    }
}
