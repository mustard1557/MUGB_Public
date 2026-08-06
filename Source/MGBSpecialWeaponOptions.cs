using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace MUGB
{
    public enum MUGBSpecialWeaponKind
    {
        None,
        Matchlock,
        Warbow,
        Crossbow,
        RepeatingCrossbow,
        Handcannon
    }

    public sealed class MUGBSpecialWeaponOption
    {
        public string id;
        public string labelKey;
        public string descriptionKey;
        public float weight = 1f;
        public HashSet<string> groups = new HashSet<string>();
        public HashSet<MUGBSpecialWeaponKind> weapons = new HashSet<MUGBSpecialWeaponKind>();
        public string visualPath;
        public Color? tint;
        public Dictionary<string, float> factors = new Dictionary<string, float>();
        public Dictionary<string, float> offsets = new Dictionary<string, float>();

        public bool Allows(MUGBSpecialWeaponKind kind) => weapons.Contains(kind);
        public string Label => labelKey.Translate();
        public string Description => descriptionKey.Translate();
    }

    public static class MUGBSpecialWeaponOptionDatabase
    {
        public const string BoneStock = "MUGB_BoneStock";
        public const string SkullBayonet = "MUGB_SkullBayonet";
        public const string LongBarrel = "MUGB_LongBarrel";
        public const string BladedLimbs = "MUGB_BladedLimbs";
        public const string TalkingHead = "MUGB_TalkingHead";
        public const string PreviousOwner = "MUGB_PreviousOwner";
        public const string BoneGrip = "MUGB_BoneGrip";
        public const string FingerCrank = "MUGB_FingerCrank";
        public const string OverchargedShot = "MUGB_OverchargedShot";
        public const string BoneSight = "MUGB_BoneSight";
        public const string ToxTip = "MUGB_ToxTip";
        public const string FireTip = "MUGB_FireTip";
        public const string PiercingTip = "MUGB_PiercingTip";
        public const string PrecisionTip = "MUGB_PrecisionTip";
        public const string OverloadPowder = "MUGB_OverloadPowder";
        public const string ScattershotTeeth = "MUGB_ScattershotTeeth";
        public const string TwinNock = "MUGB_TwinNock";
        public const string RifledBarrel = "MUGB_RifledBarrel";
        public const string GutString = "MUGB_GutString";
        public const string QuickReload = "MUGB_QuickReload";

        private const string UniqueRoot = "Things/Weapons/ranged/UniqueWeapons/";
        private static readonly Dictionary<string, MUGBSpecialWeaponOption> ById;
        public static readonly List<MUGBSpecialWeaponOption> All;

        static MUGBSpecialWeaponOptionDatabase()
        {
            MUGBSpecialWeaponKind[] all = { MUGBSpecialWeaponKind.Matchlock, MUGBSpecialWeaponKind.Warbow, MUGBSpecialWeaponKind.Crossbow, MUGBSpecialWeaponKind.RepeatingCrossbow, MUGBSpecialWeaponKind.Handcannon };
            MUGBSpecialWeaponKind[] bows = { MUGBSpecialWeaponKind.Warbow, MUGBSpecialWeaponKind.Crossbow, MUGBSpecialWeaponKind.RepeatingCrossbow };
            All = new List<MUGBSpecialWeaponOption>
            {
                O(BoneStock, "Visual", new[]{MUGBSpecialWeaponKind.Matchlock}, UniqueRoot+"MGB_UniquematchlockA", null, F("MeleeWeapon_DamageMultiplier",1.25f,"MeleeWeapon_CooldownMultiplier",0.9f)),
                O(SkullBayonet, "Visual", new[]{MUGBSpecialWeaponKind.Matchlock}, UniqueRoot+"MGB_UniquematchlockB", null, F("MeleeWeapon_DamageMultiplier",1.4f)),
                O(LongBarrel, "Visual,Barrel", new[]{MUGBSpecialWeaponKind.Matchlock}, UniqueRoot+"MGB_UniquematchlockD", null, F("RangedWeapon_RangeMultiplier",1.2f)),
                O(BladedLimbs, "Visual", new[]{MUGBSpecialWeaponKind.Warbow}, UniqueRoot+"MGB_UniquewarbowA", null, F("MeleeWeapon_DamageMultiplier",1.4f)),
                O(TalkingHead, "Visual", new[]{MUGBSpecialWeaponKind.Warbow}, UniqueRoot+"MGB_UniquewarbowB", null, F("MarketValue",1.4f), D("Beauty",8f)),
                O(PreviousOwner, "Visual", new[]{MUGBSpecialWeaponKind.Warbow}, UniqueRoot+"MGB_UniquewarbowC", null, AccuracyAll(1.15f), weight:0.5f),
                O(BoneGrip, "Visual", new[]{MUGBSpecialWeaponKind.RepeatingCrossbow}, UniqueRoot+"MGB_UniqueRcrossA", null, AccuracyNear(1.1f)),
                O(FingerCrank, "Visual", new[]{MUGBSpecialWeaponKind.RepeatingCrossbow}, UniqueRoot+"MGB_UniqueRcrossB", null, F("RangedWeapon_Cooldown",0.8f)),
                O(OverchargedShot, "Visual,AmmoType", new[]{MUGBSpecialWeaponKind.Handcannon}, UniqueRoot+"MGB_UniqueHcannonA", C(1f,0.42f,0.08f), F("RangedWeapon_RangeMultiplier",1.2f), AccuracyAll(0.85f)),
                O(BoneSight, "Sights", new[]{MUGBSpecialWeaponKind.Matchlock}, null, null, AccuracyAll(1.1f), F("MeleeWeapon_DamageMultiplier",1.15f)),
                O(BoneSight, "Visual,Sights", new[]{MUGBSpecialWeaponKind.Handcannon}, UniqueRoot+"MGB_UniqueHcannonB", null, AccuracyAll(1.1f), F("MeleeWeapon_DamageMultiplier",1.15f)),
                O(ToxTip, "AmmoType", all, null, C(0.2f,0.75f,0.25f)),
                O(FireTip, "AmmoType", all, null, C(0.9f,0.12f,0.08f)),
                O(PiercingTip, "AmmoType", all, null, C(0.22f,0.22f,0.22f), F("RangedWeapon_ArmorPenetrationMultiplier",1.2f)),
                O(PrecisionTip, "AmmoType", bows, null, C(0.9f,0.86f,0.72f), AccuracyFar(1.15f)),
                O(OverloadPowder, "AmmoType", new[]{MUGBSpecialWeaponKind.Matchlock}, null, C(0.95f,0.72f,0.08f), F("RangedWeapon_DamageMultiplier",1.3f,"RangedWeapon_ArmorPenetrationMultiplier",1.15f), AccuracyAll(0.9f)),
                O(ScattershotTeeth, "AmmoType", new[]{MUGBSpecialWeaponKind.Matchlock}, null, C(0.88f,0.8f,0.62f), F("RangedWeapon_DamageMultiplier",0.8f), AccuracyFar(0.8f)),
                O(TwinNock, "AmmoType", new[]{MUGBSpecialWeaponKind.Warbow}, null, C(0.88f,0.8f,0.62f), F("RangedWeapon_DamageMultiplier",0.8f), AccuracyAll(0.9f)),
                O(RifledBarrel, "Barrel", new[]{MUGBSpecialWeaponKind.Matchlock}, null, null, AccuracyFar(1.2f)),
                O(GutString, "String", bows, null, null, F("RangedWeapon_RangeMultiplier",1.15f)),
                O(QuickReload, "", new[]{MUGBSpecialWeaponKind.Matchlock,MUGBSpecialWeaponKind.Warbow,MUGBSpecialWeaponKind.Crossbow,MUGBSpecialWeaponKind.Handcannon}, null, null, F("RangedWeapon_Cooldown",0.8f), AccuracyAll(0.95f))
            };
            ById = All.GroupBy(x => x.id).ToDictionary(x => x.Key, x => x.First());
        }

        public static MUGBSpecialWeaponOption Get(string id, MUGBSpecialWeaponKind kind)
        {
            return All.FirstOrDefault(x => x.id == id && x.Allows(kind));
        }

        private static MUGBSpecialWeaponOption O(string id, string groups, IEnumerable<MUGBSpecialWeaponKind> weapons, string visual, Color? tint, params Dictionary<string,float>[] factors)
            => O(id, groups, weapons, visual, tint, factors, null, 1f);
        private static MUGBSpecialWeaponOption O(string id, string groups, IEnumerable<MUGBSpecialWeaponKind> weapons, string visual, Color? tint, Dictionary<string,float> factors, float weight)
            => O(id, groups, weapons, visual, tint, new[]{factors}, null, weight);
        private static MUGBSpecialWeaponOption O(string id, string groups, IEnumerable<MUGBSpecialWeaponKind> weapons, string visual, Color? tint, Dictionary<string,float> factors, Dictionary<string,float> offsets)
            => O(id, groups, weapons, visual, tint, new[]{factors}, offsets, 1f);
        private static MUGBSpecialWeaponOption O(string id, string groups, IEnumerable<MUGBSpecialWeaponKind> weapons, string visual, Color? tint, Dictionary<string,float>[] factors, Dictionary<string,float> offsets, float weight)
        {
            var option = new MUGBSpecialWeaponOption { id=id, labelKey=id+"Label", descriptionKey=id+"Desc", visualPath=visual, tint=tint, weight=weight };
            foreach (string group in groups.Split(new[]{','}, StringSplitOptions.RemoveEmptyEntries)) option.groups.Add(group.Trim());
            foreach (var weapon in weapons) option.weapons.Add(weapon);
            foreach (var dict in factors.Where(x=>x!=null)) foreach (var pair in dict) option.factors[pair.Key] = option.factors.TryGetValue(pair.Key,out float old) ? old*pair.Value : pair.Value;
            if (offsets != null) foreach (var pair in offsets) option.offsets[pair.Key]=pair.Value;
            return option;
        }

        private static Dictionary<string,float> F(params object[] values) { var d=new Dictionary<string,float>(); for(int i=0;i<values.Length;i+=2)d[(string)values[i]]=(float)values[i+1]; return d; }
        private static Dictionary<string,float> D(string stat,float value)=>new Dictionary<string,float>{{stat,value}};
        private static Dictionary<string,float> AccuracyAll(float value)=>F("AccuracyTouch",value,"AccuracyShort",value,"AccuracyMedium",value,"AccuracyLong",value);
        private static Dictionary<string,float> AccuracyNear(float value)=>F("AccuracyTouch",value,"AccuracyShort",value);
        private static Dictionary<string,float> AccuracyFar(float value)=>F("AccuracyMedium",value,"AccuracyLong",value);
        private static Color C(float r,float g,float b)=>new Color(r,g,b);
    }

    public class CompProperties_MUGBSpecialWeapon : CompProperties
    {
        public CompProperties_MUGBSpecialWeapon() { compClass=typeof(CompMUGBSpecialWeapon); }
    }

    public class CompMUGBSpecialWeapon : ThingComp
    {
        private bool active;
        private List<string> optionIds = new List<string>();
        private Graphic cachedGraphic;
        private string cachedPath;

        // 절차적 이름 조각. 형용사는 옵션에서 매번 결정하므로 저장하지 않고,
        // 무작위로 굴리는 별칭·랜덤형용사만 생성 시점에 정해 저장합니다.
        private string nameEpithetKey;
        private string nameFlavorKey;

        // 이름에 쓸 대표 형용사는 최우선 그룹 옵션에서 고릅니다(옵션 시스템의 텍스처 우선순위와 동일).
        private static readonly Dictionary<string, int> NameGroupPriority = new Dictionary<string, int>
        { { "Visual", 4 }, { "AmmoType", 3 }, { "Barrel", 2 }, { "String", 2 }, { "Sights", 1 } };
        private static int NamePriority(MUGBSpecialWeaponOption o) =>
            o.groups.Count == 0 ? 0 : o.groups.Max(g => NameGroupPriority.TryGetValue(g, out int p) ? p : 0);

        // 옵션 개수가 많을수록 더 화려한 별칭이 붙습니다. 죽음·식사·의식 이미지로 통일.
        private static readonly string[] EpithetTier2 = { "MUGB_Epithet_Breath", "MUGB_Epithet_Feast", "MUGB_Epithet_Shadow" };
        private static readonly string[] EpithetTier3 = { "MUGB_Epithet_LastWords", "MUGB_Epithet_Dirge", "MUGB_Epithet_Promise", "MUGB_Epithet_Night" };
        // 옵션과 무관하게 붙는 순수 플레이버 형용사.
        private static readonly string[] FlavorBank =
        {
            "MUGB_Flavor_Screaming", "MUGB_Flavor_Panting", "MUGB_Flavor_Spirited", "MUGB_Flavor_Moldy",
            "MUGB_Flavor_Musty", "MUGB_Flavor_NightBloom", "MUGB_Flavor_Sticky", "MUGB_Flavor_Alluring", "MUGB_Flavor_Throbbing"
        };

        public bool Active => active;
        public MUGBSpecialWeaponKind Kind => MUGBSpecialWeaponUtility.KindFor(parent?.def);
        public IReadOnlyList<string> OptionIds => optionIds;
        public IEnumerable<MUGBSpecialWeaponOption> Options => optionIds.Select(id=>MUGBSpecialWeaponOptionDatabase.Get(id,Kind)).Where(x=>x!=null);

        public void Activate(int minOptions=1,int maxOptions=3)
        {
            if (active) return;
            active=true;
            optionIds.Clear();
            int target=Rand.RangeInclusive(Mathf.Clamp(minOptions,1,3),Mathf.Clamp(maxOptions,minOptions,3));
            bool needsMark = Kind==MUGBSpecialWeaponKind.Warbow || Kind==MUGBSpecialWeaponKind.Handcannon;
            if (needsMark)
            {
                var marked=MUGBSpecialWeaponOptionDatabase.All.Where(x=>x.Allows(Kind)&&(x.groups.Contains("Visual")||x.groups.Contains("AmmoType"))).RandomElementByWeight(x=>x.weight);
                optionIds.Add(marked.id);
            }
            while(optionIds.Count<target)
            {
                HashSet<string> usedGroups=new HashSet<string>(Options.SelectMany(x=>x.groups));
                var candidates=MUGBSpecialWeaponOptionDatabase.All.Where(x=>x.Allows(Kind)&&!optionIds.Contains(x.id)&&!x.groups.Any(usedGroups.Contains)).ToList();
                if(candidates.Count==0) break;
                optionIds.Add(candidates.RandomElementByWeight(x=>x.weight).id);
            }
            cachedGraphic=null; cachedPath=null;
            RollGeneratedName();
        }

        // 별칭·랜덤형용사를 한 번만 굴려 고정합니다. 형용사는 여기서 정하지 않고 읽을 때 옵션에서 뽑습니다.
        private void RollGeneratedName()
        {
            nameEpithetKey=null; nameFlavorKey=null;
            int count=optionIds.Count;
            float epithetChance = count>=3 ? 0.6f : count==2 ? 0.4f : 0f;
            if(Rand.Value<epithetChance) nameEpithetKey=(count>=3?EpithetTier3:EpithetTier2).RandomElement();
            if(Rand.Value<0.25f) nameFlavorKey=FlavorBank.RandomElement();
        }

        // 이름 앞에 붙일 형용사. 최우선 그룹 옵션에서 고르며, 저장하지 않아 구세이브에도 바로 적용됩니다.
        private string LeadAdjective()
        {
            var opts=Options.ToList();
            if(opts.Count==0) return null;
            var lead=opts.OrderByDescending(NamePriority).ThenBy(o=>optionIds.IndexOf(o.id)).First();
            return (lead.id+"Adjective").Translate().ToString();
        }

        public bool Has(string id)=>active && optionIds.Contains(id);

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref active,"mugbSpecialWeaponActive",false);
            Scribe_Collections.Look(ref optionIds,"mugbSpecialWeaponOptions",LookMode.Value);
            Scribe_Values.Look(ref nameEpithetKey,"mugbSpecialWeaponEpithet");
            Scribe_Values.Look(ref nameFlavorKey,"mugbSpecialWeaponFlavor");
            if(Scribe.mode==LoadSaveMode.PostLoadInit) optionIds ??= new List<string>();
        }

        public override float GetStatFactor(StatDef stat)
        {
            if(!active||stat==null) return 1f;
            float factor=stat==StatDefOf.MarketValue ? 1.4f+0.2f*optionIds.Count : 1f;
            foreach(var option in Options) if(option.factors.TryGetValue(stat.defName,out float value)) factor*=value;
            return factor;
        }

        public override float GetStatOffset(StatDef stat)
        {
            if(!active||stat==null) return 0f;
            float offset=0f;
            foreach(var option in Options) if(option.offsets.TryGetValue(stat.defName,out float value)) offset+=value;
            return offset;
        }

        public override Color? ForceColor() => active ? Options.Select(x=>x.tint).FirstOrDefault(x=>x.HasValue) : null;

        public Graphic SpecialGraphic
        {
            get
            {
                if(!active) return null;
                string path=Options.FirstOrDefault(x=>!x.visualPath.NullOrEmpty())?.visualPath ?? MUGBSpecialWeaponUtility.DefaultSpecialPath(Kind,parent.def.graphicData.texPath);
                if(cachedGraphic==null||cachedPath!=path)
                {
                    Graphic baseGraphic=parent.def.graphicData.Graphic;
                    Color? forcedColor=ForceColor();
                    Color tint=forcedColor.HasValue ? forcedColor.Value : Color.white;
                    cachedGraphic=GraphicDatabase.Get<Graphic_Single>(path,ShaderDatabase.CutoutComplex,baseGraphic.drawSize,tint,Color.white,parent.def.graphicData);
                    cachedPath=path;
                }
                return cachedGraphic;
            }
        }

        public override string TransformLabel(string label)
        {
            if(!active || parent.StyleSourcePrecept!=null) return label;
            string adjective=LeadAdjective();
            if(adjective==null) return label; // 옵션이 없으면 원래 이름 유지(안전장치)
            string name=adjective+" "+label;
            if(!nameEpithetKey.NullOrEmpty())
                name="MUGB_WeaponNameEpithet".Translate(name, nameEpithetKey.Translate()).ToString();
            if(!nameFlavorKey.NullOrEmpty())
                name=nameFlavorKey.Translate().ToString()+" "+name;
            return name;
        }

        public override string CompInspectStringExtra()
        {
            if(!active) return null;
            return "MUGB_SpecialWeaponOptions".Translate(Options.Select(x=>x.Label).ToCommaList());
        }

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats()
        {
            if(!active) yield break;
            foreach(var option in Options)
                yield return new StatDrawEntry(StatCategoryDefOf.Weapon,option.Label,option.Description,option.Description,5500);
        }
    }

    public static class MUGBSpecialWeaponUtility
    {
        public static readonly string[] EligibleDefNames={"MUGB_GoblinMusket","MUGB_GoblinWarbow","MUGB_GoblinCrossbow","MUGB_GoblinRepeatingCrossbow","MUGB_GoblinHandcannon"};
        public static bool IsEligible(ThingDef def)=>def!=null&&EligibleDefNames.Contains(def.defName);
        public static MUGBSpecialWeaponKind KindFor(ThingDef def)
        {
            switch(def?.defName){case "MUGB_GoblinMusket":return MUGBSpecialWeaponKind.Matchlock;case "MUGB_GoblinWarbow":return MUGBSpecialWeaponKind.Warbow;case "MUGB_GoblinCrossbow":return MUGBSpecialWeaponKind.Crossbow;case "MUGB_GoblinRepeatingCrossbow":return MUGBSpecialWeaponKind.RepeatingCrossbow;case "MUGB_GoblinHandcannon":return MUGBSpecialWeaponKind.Handcannon;default:return MUGBSpecialWeaponKind.None;}
        }
        public static string DefaultSpecialPath(MUGBSpecialWeaponKind kind,string fallback)
        {
            const string root="Things/Weapons/ranged/UniqueWeapons/";
            switch(kind){case MUGBSpecialWeaponKind.Matchlock:return root+"MGB_UniquematchlockC";case MUGBSpecialWeaponKind.Crossbow:return root+"MGB_UniquecrossbowB";case MUGBSpecialWeaponKind.RepeatingCrossbow:return root+"MGB_UniqueRcrossC";default:return fallback;}
        }
        public static CompMUGBSpecialWeapon Activate(Thing thing,int min=1,int max=3){var comp=thing?.TryGetComp<CompMUGBSpecialWeapon>();comp?.Activate(min,max);return comp;}
    }

    [HarmonyPatch(typeof(Verb),nameof(Verb.BurstShotCount),MethodType.Getter)]
    public static class MUGB_SpecialWeaponBurstCountPatch
    {
        public static void Postfix(Verb __instance,ref int __result)
        {
            var comp=__instance?.EquipmentSource?.TryGetComp<CompMUGBSpecialWeapon>();
            if(comp?.Has(MUGBSpecialWeaponOptionDatabase.ScattershotTeeth)==true||comp?.Has(MUGBSpecialWeaponOptionDatabase.TwinNock)==true) __result=2;
        }
    }

    [HarmonyPatch(typeof(Verb),nameof(Verb.TicksBetweenBurstShots),MethodType.Getter)]
    public static class MUGB_SpecialWeaponBurstSpeedPatch
    {
        public static void Postfix(Verb __instance,ref int __result)
        {
            var comp=__instance?.EquipmentSource?.TryGetComp<CompMUGBSpecialWeapon>();
            if(comp?.Has(MUGBSpecialWeaponOptionDatabase.ScattershotTeeth)==true||comp?.Has(MUGBSpecialWeaponOptionDatabase.TwinNock)==true) __result=2;
        }
    }

    [HarmonyPatch(typeof(Projectile),"Impact")]
    public static class MUGB_SpecialWeaponProjectileImpactPatch
    {
        public struct State { public Map map; public IntVec3 cell; public Thing equipment; }
        public static void Prefix(Projectile __instance,Thing ___equipment,ref State __state){__state.map=__instance.Map;__state.cell=__instance.Position;__state.equipment=___equipment;}
        public static void Postfix(Thing hitThing,State __state)
        {
            var comp=__state.equipment?.TryGetComp<CompMUGBSpecialWeapon>();
            if(comp?.Active!=true) return;
            if(comp.Has(MUGBSpecialWeaponOptionDatabase.FireTip)&&__state.map!=null&&Rand.Chance(0.35f)) FireUtility.TryStartFireIn(__state.cell,__state.map,0.35f,null);
        }
    }

    [HarmonyPatch(typeof(Projectile_Explosive),"Impact")]
    public static class MUGB_SpecialWeaponExplosiveImpactPatch
    {
        public static void Prefix(Projectile_Explosive __instance,Thing ___equipment,ref MUGB_SpecialWeaponProjectileImpactPatch.State __state)
        {
            __state.map=__instance.Map; __state.cell=__instance.Position; __state.equipment=___equipment;
        }

        public static void Postfix(Thing hitThing,MUGB_SpecialWeaponProjectileImpactPatch.State __state)
        {
            CompMUGBSpecialWeapon comp=__state.equipment?.TryGetComp<CompMUGBSpecialWeapon>();
            if(comp?.Active!=true) return;
            if(comp.Has(MUGBSpecialWeaponOptionDatabase.FireTip)&&__state.map!=null&&Rand.Chance(0.35f)) FireUtility.TryStartFireIn(__state.cell,__state.map,0.35f,null);
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Launch), new[] { typeof(Thing), typeof(Vector3), typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(ProjectileHitFlags), typeof(bool), typeof(Thing), typeof(ThingDef) })]
    public static class MUGB_SpecialWeaponProjectileLaunchPatch
    {
        public static void Postfix(Projectile __instance, Thing equipment)
        {
            CompMUGBSpecialWeapon comp = equipment?.TryGetComp<CompMUGBSpecialWeapon>();
            if (comp?.Has(MUGBSpecialWeaponOptionDatabase.ToxTip) == true)
            {
                DamageDef toxic = DefDatabase<DamageDef>.GetNamedSilentFail("ScratchToxic");
                if (toxic != null)
                {
                    __instance.damageDefOverride = toxic;
                }
            }
        }
    }
}
