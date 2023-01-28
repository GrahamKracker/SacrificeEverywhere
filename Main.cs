using System;
using System.Collections.Generic;
using System.Linq;
using BTD_Mod_Helper;
using BTD_Mod_Helper.Api;
using BTD_Mod_Helper.Api.Enums;
using BTD_Mod_Helper.Extensions;
using HarmonyLib;
using Il2CppAssets.Scripts.Models;
using Il2CppAssets.Scripts.Models.Towers;
using Il2CppAssets.Scripts.Models.Towers.Behaviors;
using Il2CppAssets.Scripts.Models.Towers.Behaviors.Abilities;
using Il2CppAssets.Scripts.Models.Towers.Behaviors.Attack;
using Il2CppAssets.Scripts.Models.Towers.Projectiles;
using Il2CppAssets.Scripts.Models.Towers.Projectiles.Behaviors;
using Il2CppAssets.Scripts.Simulation.Objects;
using Il2CppAssets.Scripts.Simulation.Towers;
using Il2CppAssets.Scripts.Simulation.Towers.Behaviors.Abilities;
using Il2CppAssets.Scripts.Unity;
using Il2CppAssets.Scripts.Unity.Bridge;
using Il2CppAssets.Scripts.Unity.Display;
using Il2CppAssets.Scripts.Unity.Display.Animation;
using Il2CppAssets.Scripts.Unity.Menu;
using Il2CppAssets.Scripts.Unity.UI_New.InGame;
using Il2CppAssets.Scripts.Unity.UI_New.InGame.RightMenu;
using Il2CppAssets.Scripts.Unity.UI_New.InGame.StoreMenu;
using Il2CppAssets.Scripts.Unity.UI_New.InGame.TowerSelectionMenu;
using Il2CppAssets.Scripts.Utils;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSystem.Linq;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using SacrificeEverywhere;
using Main = SacrificeEverywhere.Main;

[assembly: MelonInfo(typeof(Main), ModHelperData.Name, ModHelperData.Version, ModHelperData.RepoOwner)]
[assembly: MelonGame("Ninja Kiwi", "BloonsTD6")]

namespace SacrificeEverywhere;

[HarmonyPatch]
public class Main : BloonsTD6Mod
{
    [HarmonyPatch(typeof(Ability), nameof(Ability.Initialise))]
    [HarmonyPostfix]
    static void Ability_Initialize(Ability __instance)
    {
        if (__instance.abilityModel.name == "AbilityModel_Sacrifice")
        {
            __instance.hideAbilityInBottomRow = true;
        }
    }


    void addAbility(Tower tower)
    {
        var newtower = tower.towerModel.Duplicate();
        var sacrifice = new AbilityModel("Sacrifice", "Sacrifice", "", 1, 0,
            new SpriteReference { guidRef = VanillaSprites.BloodSacrificeAA }, 0f,
            new Il2CppReferenceArray<Model>(0), false, false,
            null, 0, 0, -1, false, false);
        sacrifice.canActivateBetweenRounds = true;
        sacrifice.restrictAbilityAfterMaxRoundTimer = false;

        newtower.AddBehavior(sacrifice);
        tower.UpdateRootModel(newtower);
    }

    public override void OnTowerCreated(Tower tower, Entity target, Model modelToUse)
    {
        addAbility(tower);
    }


    private static bool inCustomMode;

    private static Tower mainTower;


    public override void OnAbilityCast(Ability ability)
    {
        try
        {
            if (ability.abilityModel.name != "AbilityModel_Sacrifice")
                return;
            var inputManager = InGame.instance.inputManager;
            inputManager.inCustomMode = true;
            inputManager.CancelAllPlacementActions();
            inputManager.HidePlacementBlockingUI();
            inputManager.HideCoopPlacementArea();
            CancelPurchaseButton cancelPlacementBtn = RightMenu.instance.cancelPlacementBtn;
            cancelPlacementBtn.animator.SetInteger(cancelPlacementBtn.visibleStateLabel, 1);
            inputManager.OnHelperMessageChanged.Invoke("Select a Tower", -1);
            cancelPlacementBtn.gameObject.GetComponent<Button>().onClick.AddListener(() => { TryCancel(); });

            mainTower = ability.tower;

            foreach (var tower in InGame.instance.bridge.GetAllTowers().Where(x =>
                         !mainTower.Equals(x.tower) && !x.tower.isSelectionBlocked && !x.Def.isSubTower &&
                         !x.Def.isPowerTower && !x.Def.ignoreTowerForSelection))
            {
                CreateThing(tower);
            }


            inCustomMode = true;
        }
        catch (Exception e)
        {
            MelonLogger.Error(e);
        }
    }

    static List<(Tower, GameObject)> things = new();

    static void CreateThing(TowerToSimulation towerToSimulation)
    {
        var rot = Quaternion.Euler(45, 0, 0);
        var holder = new GameObject("BloodSacReticleHolder")
        {
            transform =
            {
                parent = Game.instance.GetDisplayFactory().DisplayRoot,
            }
        };

        var bloodSacReticleGo = new GameObject("BloodSacReticle")
        {
            transform =
            {
                parent = holder.transform,
                position = new Vector3(towerToSimulation.tower.Position.X, 100,
                    -towerToSimulation.tower.Position.Y - 55f),
                rotation = rot,
            }
        };

        var offsetTowardsCamera = holder.AddComponent<OffsetTowardsCamera>();
        offsetTowardsCamera.offset = 0.2f;
        offsetTowardsCamera.offsetRotation = new Vector3(0, 0.2f, 0);

        var sr = bloodSacReticleGo.AddComponent<SpriteRenderer>();
        sr.sprite = ModContent.GetSprite<Main>("BloodSacReticle");
        sr.sortingLayerName = "Bloons";
        sr.sortingOrder = 32767;
        things.Add((towerToSimulation.tower, bloodSacReticleGo));
    }


    static bool TryCancel()
    {
        try
        {
            if (inCustomMode)
            {
                InGame.instance.inputManager.ExitCustomMode();
                InGame.instance.inputManager.CancelAllPlacementActions();
                inCustomMode = false;
                foreach (var thing in things)
                {
                    if (thing.Item2 != null)
                        thing.Item2.Destroy();
                }

                return false;
            }
        }
        catch (Exception e)
        {
            MelonLogger.Error(e);
        }

        return true;
    }

    static List<(Tower, float)> buffedTowers = new();

    public override void OnTowerUpgraded(Tower tower, string upgradeName, TowerModel newBaseTowerModel)
    {
        addAbility(tower);
    }

    [HarmonyPatch(typeof(TowerSelectionMenu), nameof(TowerSelectionMenu.SelectTower))]
    [HarmonyPostfix]
    static void Tower_Selected(TowerSelectionMenu __instance)
    {
        var tower = __instance.selectedTower.tower;
        if (!inCustomMode || !things.Exists(x => x.Item1.Equals(tower) || mainTower.Equals(tower)))
        {
            TryCancel();
            return;
        }

        var (_, thing) = things.Find(x => x.Item1.Equals(tower));

        if (thing is null)
        {
            TryCancel();
            return;
        }

        var towerModel = mainTower.towerModel.Duplicate();
        mainTower.worth += tower.worth/4;
        var level = (float)Math.Floor(tower.worth / 1000);
        if (level == 0)
            level = 1;

        buffedTowers.Add((mainTower, level));

        mainTower.display.node.graphic.transform.GetComponentInChildren<MonkeyAnimationController>().gameObject
            .transform.localScale += new Vector3(level / 100f, level / 100f, level / 100f);


        towerModel.range += level / 2f;
        foreach (var item in towerModel.GetDescendants<AttackModel>().ToArray())
        {
            item.range += level / 8f;
        }

        var weaponRate = 1 - (level / 100);

        foreach (var item in towerModel.GetDescendants<ProjectileModel>().ToArray())
        {
            item.pierce += level / 8f;
            if (item.HasBehavior<DamageModel>(out var damageModel))
            {
                damageModel.damage += level / 8f;
            }
        }

        thing.Destroy();

        foreach (var weapon in towerModel.GetWeapons())
        {
            weapon.rate *= weaponRate;
        }

        foreach (var attackModel in __instance.selectedTower.tower.towerModel.GetAttackModels()
                     .Where(x => x.GetDescendant<CashModel>() is not null))
        {
            towerModel.AddBehavior(attackModel.Duplicate());
        }

        if (__instance.selectedTower.tower.towerModel.HasBehavior<PerRoundCashBonusTowerModel>(
                out var perRoundCashBonusTowerModel))
        {
            towerModel.AddBehavior(perRoundCashBonusTowerModel.Duplicate());
        }

        mainTower.UpdateRootModel(towerModel);

        InGame.instance.GetTowerManager().TowerSacrificed(tower, mainTower);
        InGame.instance.GetTowerManager().DestroyTower(tower, tower.owner);

        TryCancel();
    }

    [HarmonyPatch(typeof(MenuManager), nameof(MenuManager.ProcessEscape))]
    [HarmonyPrefix]
    static bool MenuManager_ProcessEscape()
    {
        return TryCancel();
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.IsCursorInWorld))]
    [HarmonyPostfix]
    static void InputManager_IsCursorInWorld(bool __result)
    {
        if (!__result)
            TryCancel();
    }
}
