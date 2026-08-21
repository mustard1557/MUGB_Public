using RimWorld;
using Verse;
using Verse.AI;

namespace MUGB
{
    [DefOf]
    public static class MUGBDefOf
    {
        public static XenotypeDef MUGB_Goblin;
        public static XenotypeDef MUGB_Hobgoblin;

        public static FactionDef MUGB_GoblinTribe;
        public static FactionDef MUGB_GoblinCivilTribe;
        public static FactionDef MUGB_GoblinCivilMedieval;
        public static FactionDef MUGB_GoblinSavageMedieval;
        public static FactionDef MUGB_GoblinCultists;
        public static FactionDef MUGB_GoblinHunters;
        public static FactionDef MUGB_NeutralBeggarBand;
        public static IncidentDef MUGB_BeggarTravelerGroup;
        public static IncidentDef MUGB_GoblinCaravanAmbush;
        public static StorytellerDef MUGB_KimDeokPal;

        public static MemeDef MUGB_GoblinSupremacy;
        public static MemeDef MUGB_ChildrenOfBlinia;

        public static PawnKindDef MUGB_GoblinBareBrawler;
        public static PawnKindDef MUGB_HobgoblinBareBrawler;

        public static GeneDef MUGB_Gene_GoblinCore;
        public static GeneDef MUGB_Gene_HobgoblinFrame;
        public static GeneDef MUGB_Gene_CrossEyed;
        public static GeneDef MUGB_Gene_GoblinFleshCraving;
        public static GeneDef MUGB_Gene_GoblinWeakToxicPheromone;
        public static GeneDef MUGB_Gene_GoblinStrongToxicPheromone;
        public static GeneDef MUGB_Gene_GoblinSwarmPheromone;
        public static GeneDef MUGB_Gene_HobgoblinCommandPheromone;
        public static GeneDef MUGB_Gene_HalfGoblinAncestry;
        public static GeneDef MUGB_Gene_GoblinSlowLearner;
        public static GeneDef MUGB_Gene_GoblinFastLearner;
        public static GeneDef MUGB_Gene_GoblinFertile;
        public static GeneDef MUGB_Gene_GoblinStableCellMetabolism;
        public static GeneDef MUGB_Gene_GoblinUnstableCellMetabolism;

        public static NeedDef MUGB_FleshCraving;

        public static HeadTypeDef MUGB_GoblinHead;
        public static BodyTypeDef MUGB_GoblinThin;
        public static BodyTypeDef MUGB_HobgoblinMale;

        public static ThingDef MUGB_Bone;
        public static ThingDef MUGB_BoneDust;
        public static ThingDef MUGB_CrudeGoblinAlloy;
        public static ThingDef MUGB_GoblinAlloy;
        public static ThingDef MUGB_FineGoblinAlloy;
        public static ThingDef MUGB_Apparel_HumanHideMantle;
        public static ThingDef MUGB_Gskin;
        public static ThingDef MUGB_GoblinBoomstickWick;
        public static ThingDef MUGB_FlintlockMuzzleFlash;
        public static ThingDef MUGB_goblinbeacon;
        public static ThingDef MUGB_GoblinSculptureSmall;
        public static ThingDef MUGB_GoblinSculptureGrand;
        public static ThingDef MUGB_cookstation;
        public static ThingDef MUGB_smithbench;
        public static ThingDef MUGB_Basin;
        public static ThingDef MUGB_bigpot;
        public static ThingDef Meat_Goblin;
        public static ThingDef MUGB_Hchunk;
        public static ThingDef MUGB_Gchunk;
        public static ThingDef MUGB_Hgut;
        public static ThingDef MUGB_Ggut;
        public static ThingDef MUGB_heart;
        public static ThingDef MUGB_brain;
        public static ThingDef MUGB_gutstew;
        public static ThingDef MUGB_GoblinPepperCheese;
        public static ThingDef MUGB_GBroodSac;
        public static ThingDef MUGB_Gfetus;
        public static ThingDef MUGB_GloopJuice;
        public static ThingDef MUGB_SpermJuice;
        public static ThingDef MUGB_Smartyoil;
        public static ThingDef MUGB_GoblinStaffMobileShield;
        public static ThingDef MUGB_GoblinStinkbomb;
        public static ThingDef MUGB_StinkbombProjectile;
        public static ThingDef MUGB_StinkGasCloud;
        public static ThingDef MUGB_Apparel_GoblinPheromonePack;
        public static ThingDef MUGB_GoblinTunnelSpawnerA;
        public static ThingDef MUGB_GoblinTunnelSpawnerB;
        public static ThingDef MUGB_GoblinTunnelA;
        public static ThingDef MUGB_GoblinTunnelB;
        public static ThingDef MUGB_GoblinTunnelDiggingFX;
        public static ThingDef MUGB_GoblinMortarTunnelSpawner;
        public static ThingDef MUGB_GoblinMortarTunnel;
        public static ThingDef MUGB_GoblinMortarSupportTunnel;
        public static ThingDef MUGB_GoblinMortar;
        public static ThingDef MUGB_GoblinRepeaterBallista;
        public static ThingDef MUGB_GoblinHighExplosiveShell;
        public static ThingDef MUGB_GoblinStinkMortarShell;

        public static PawnsArrivalModeDef MUGB_GoblinTunnelArrival;
        public static PawnsArrivalModeDef MUGB_GoblinTunnelArrivalCenter;
        public static PawnsArrivalModeDef MUGB_GoblinMortarTunnelArrival;
        public static RaidStrategyDef MUGB_GoblinMortarTunnelSiege;
        public static RaidStrategyDef MUGB_GoblinSapperRaid;
        public static RaidStrategyDef MUGB_GoblinSuicideSapperRaid;
        public static RaidStrategyDef MUGB_GoblinCompositeSapperRaid;
        public static PawnsArrivalModeDef MUGB_GoblinCompositeTwoDirections;

        public static JobDef MUGB_LoadStewpotIngredient;
        public static JobDef MUGB_DispenseStewpotMeal;
        public static JobDef MUGB_ThrowSpear;
        public static JobDef MUGB_SpearCharge;
        public static JobDef MUGB_ReloadGoblinPheromonePack;
        public static JobDef MUGB_ProclaimSlaveMarriage;
        public static JobDef MUGB_NotifySlaveDivorce;
        public static JobDef MUGB_StandAtSlaveMarriage;
        public static JobDef MUGB_ReleaseRestraint;
        public static JobDef MUGB_UseStaffAbility;

        public static HediffDef MUGB_GoblinDartPoison;
        public static HediffDef MUGB_ChainSnare;
        public static HediffDef MUGB_StaffAwakened;
        public static HediffDef MUGB_StaffRousedFervor;
        public static HediffDef MUGB_StaffShieldCohesion;
        public static HediffDef MUGB_FleshCravingWeakness;
        public static HediffDef MUGB_HeartRush;
        public static HediffDef MUGB_BrainFocus;
        public static HediffDef MUGB_BrainStewInsight;
        public static HediffDef MUGB_LegBBQStride;
        public static HediffDef MUGB_ArmBBQGrip;
        public static HediffDef MUGB_LavishBBQFeast;
        public static HediffDef MUGB_GoblinBirthStrain;
        public static HediffDef MUGB_GoblinRapidPostpartum;
        public static HediffDef MUGB_GoblinPregnancyDrain;
        public static HediffDef MUGB_GoblinReproductiveSacDepleted;
        public static HediffDef MUGB_LiveButcheryAftermath;
        public static HediffDef MUGB_NosePickedLobotomy;
        public static HediffDef MUGB_ToxicPheromoneExposure;
        public static HediffDef MUGB_ToxicPheromoneCollapse;
        public static HediffDef MUGB_ToxicPheromoneImmunity;
        public static HediffDef MUGB_GoblinSwarmPheromoneBuff;
        public static HediffDef MUGB_HobgoblinCommandPheromoneBuff;
        public static HediffDef MUGB_PainShockLogCause;
        public static HediffDef MUGB_GoblinPheromoneConditioning;
        public static HediffDef MUGB_GoblinPheromonePreference;
        public static HediffDef MUGB_GoblinPepperCheeseFertility;
        public static HediffDef MUGB_GloopJuiceHigh;
        public static HediffDef MUGB_GloopJuiceCrash;
        public static HediffDef MUGB_SpermJuiceHigh;
        public static HediffDef MUGB_SpermJuiceCrash;
        public static HediffDef MUGB_SmartyoilSynapticReconnection;
        public static HediffDef MUGB_StinkGasExposure;
        public static HediffDef MUGB_StinkGasClouded;
        public static HediffDef MUGB_CorrosiveGoblinGiblets;
        public static InteractionDef MUGB_SlaveMarriageProclamation;
        public static ThoughtDef MUGB_AteVegetableIngredient;
        public static ThoughtDef MUGB_AteProperFlesh;
        public static ThoughtDef MUGB_AteHeartGoblin;
        public static ThoughtDef MUGB_AteBrainGoblin;
        public static ThoughtDef MUGB_AteGoblinMeatDirect;
        public static ThoughtDef MUGB_AteGoblinMeatDirectCannibal;
        public static ThoughtDef MUGB_AteGoblinMeatAsIngredient;
        public static ThoughtDef MUGB_AteGoblinMeatAsIngredientCannibal;
        public static ThoughtDef MUGB_AteGutSausageNonGoblin;
        public static ThoughtDef MUGB_AteGutStewNonGoblin;
        public static ThoughtDef MUGB_AteGoblinFoodPreferred;
        public static ThoughtDef MUGB_AteGoblinPepperCheese;
        public static ThoughtDef MUGB_AteGoblinPepperCheeseNonGoblin;
        public static ThoughtDef MUGB_FleshCravingLow;
        public static ThoughtDef MUGB_GoblinPregnancyBurden;
        public static ThoughtDef MUGB_GaveBirthToGoblinLitter;
        public static ThoughtDef MUGB_GoblinBabyBorn;
        public static ThoughtDef MUGB_GoblinChildDiedRelief;
        public static ThoughtDef MUGB_GoblinSiblingDied;
        public static ThoughtDef MUGB_GoblinColonistDied;
        public static ThoughtDef MUGB_GoblinFriendDied;
        public static ThoughtDef MUGB_GoblinBrawlBond;
        public static ThoughtDef MUGB_GoblinBrawlRelief;
        public static ThoughtDef MUGB_GoblinInsulted;
        public static ThoughtDef MUGB_GoblinSlighted;
        public static ThoughtDef MUGB_GoblinInsultedMood;
        public static ThoughtDef MUGB_ButcheredAlive;
        public static ThoughtDef MUGB_ButcheredAliveSocial;
        public static ThoughtDef MUGB_PerformedLiveButchery;
        public static ThoughtDef MUGB_GoblinSacNausea;
        public static ThoughtDef MUGB_RejectedByMaster;
        public static ThoughtDef MUGB_SpecialSlave;
        public static ThoughtDef MUGB_ViewedAsSpecialSlave;
        public static ThoughtDef MUGB_OpinionOfGoblin;
        public static ThoughtDef MUGB_GoblinHusband;
        public static ThoughtDef MUGB_LovinWithGoblinSlaveSpouse;
        public static ThoughtDef MUGB_SlaveSpouseDied;
        public static ThoughtDef MUGB_BrokenToySlaveSpouseDied;
        public static ThoughtDef MUGB_AttendedSlaveMarriageCeremony;
        public static ThoughtDef MUGB_FreeMarriageUnderSlaveMarriageIdeo;
        public static PreceptDef MUGB_SlaveMarriageCeremony;
        public static RecipeDef MUGB_ExtractHeart;
        public static DamageDef MUGB_StinkGasDamage;

        static MUGBDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(MUGBDefOf));
        }
    }
}
