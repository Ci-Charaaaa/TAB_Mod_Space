using AX.ModLoader;
using AX.ModLoader.Config;
using DXVision;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using ZX;
using ZX.Entities;


namespace entity_boost
{
    public class Boost_Mod : IModEntry 
    {
        private Mod _test_mod;
        public Unit_Total_Config Config;
        public static Boost_Mod Instance;


        public override void OnLoad(Mod mod)
        {
            _test_mod = mod;
            Config = mod.RegisterConfig<Unit_Total_Config>();
            DXLog.Write($"[Test_Mod] 已加载！版本：{mod.Info.Version} gugugaga!");

            Instance = this;

            var harmony = new Harmony("com.charaa.test1");
            harmony.UnpatchAll("com.charaa.test1");
            harmony.PatchAll(Assembly.GetExecutingAssembly()); // 告诉 Harmony 扫描这个项目里的 [HarmonyPatch]

            DXLog.Write($"[Test_Mod] Harmony 补丁已应用！正在等待游戏逻辑初始化...,随时准备进行属性加强模块注入");

            Config.OnConfigChanged += (c) => 
            { _test_mod.SaveConfig(); 
                DXLog.Write("[Test_Mod] 配置已保存");
            };


        }

        // 钩住 ApplyMods
        [HarmonyPatch(typeof(ZXDefaultParams), "ApplyMods")]
        public class InjectionPatch
        {
            public static void Postfix()
            {
                if (Instance == null || Instance.Config == null) return;
                var c = Instance.Config;

                // 游侠
                ApplyUnitStats("Ranger", 60f, 4f, 6f, 8f, 10, 1000, 0, 0.05f,
                    c.Ranger_HealthyMult, c.Ranger_SpeedMult, c.Ranger_RangeMult,
                    c.Ranger_VisionMult, c.Ranger_ArmorAdd, c.Ranger_DamageMult, c.Ranger_DamageSpeedMult);

                // 士兵
                ApplyUnitStats("SoldierRegular", 120f, 2.4f, 5f, 6f, 16, 500, 0, 0.40f,
                    c.Soldier_HealthyMult, c.Soldier_SpeedMult, c.Soldier_RangeMult,
                    c.Soldier_VisionMult, c.Soldier_ArmorAdd, c.Soldier_DamageMult, c.Soldier_DamageSpeedMult);

                //狙击手
                ApplyUnitStats("Sniper", 150f, 1.8f, 8f, 9f, 100, 600, 1600, 0.05f,
                    c.Sniper_HealthyMult,c.Sniper_SpeedMult,c.Sniper_RangeMult,
                    c.Sniper_VisionMult,c.Sniper_ArmorAdd,c.Sniper_DamageMult,c.Sniper_DamageSpeedMult);
            }
        }

        // 钩住 Human 的 AddExperience 方法，来实现老兵晋升时的回血效果（这里懒得找具体的晋升通知方法了
        // ，通过检测增加经验值中需要查看是否晋升真值的这个方法，来间接去实现晋升时的生命值恢复）
        [HarmonyPatch(typeof(Human), "AddExperience")]
        public class Patch_VeteranHeal
        {
            private static bool _wasVeteranBefore;


            // 执行前：记录是否已经是老兵，防止重复执行
            public static void Prefix(Human __instance)
            {
                _wasVeteranBefore = __instance.IsVeteran;
            }

            // 执行后：如果状态从 false 变为 true，说明这一刻晋升了
            public static void Postfix(Human __instance)
            {

                if (Instance == null || Instance.Config == null) return;
                var c = Instance.Config;

                if (!c.IsFull_HP)
                {
                    return; // 如果配置里没有开启老兵满血，就直接返回，不执行后续代码
                }

                if (!_wasVeteranBefore && __instance.IsVeteran)
                {
                    try
                    {
                        // 获取最大生命值（从配置参数中获取）
                        // 注意：ep._Life 是数据表中修改后的那个上限
                        var ep = __instance.get_Params();
                        if (ep != null)
                        {
                            // 设置实时生命值为上限值
                            __instance._CLife._Life = ep._Life;

                            DXLog.Write($"[Test_Mod] 单位 {__instance.ID} 晋升老兵，瞬间回满血: {ep._Life}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // 确保不会因为意外崩溃影响主进程
                        DXLog.Write($"[Test_Mod] [ERROR] 老兵满血处理失败: {ex.Message}");
                    }
                }
            }
        }

        // 核心注入方法：根据实体ID扫描并修改属性
        public static void ApplyUnitStats(string entity_ID, float bLife, float bSpeed, float bRange, float bVision, int bDamage, int bTimeAction, int bTimePrep, float bArmor,
             double mLife, double mSpeed, double mRange, double mVision, double aArmor, double mDamage, double mAtkSpeed)
        {
            var allParams = ZXDefaultParams.get_All();
            if (allParams == null) return;

            foreach (var p in allParams)
            {
                if (p == null || p.ID != entity_ID) continue;
                if (p is ZXEntityDefaultParams ep)
                {
                    try
                    {
                        DXLog.Write($"\n[Test_Mod] >>> 注入单位: {entity_ID}");

                        // 1. 基础属性
                        _setField(ep, "_Life", (float)(bLife * mLife / 100.0));
                        _setField(ep, "_RunSpeed", (float)(bSpeed * mSpeed / 100.0));
                        _setField(ep, "_WatchRange", (float)(bVision * mVision / 100.0));

                        // 2. 护甲处理
                        float finalArmor = Math.Max(0f, Math.Min(0.99f, (float)(bArmor + aArmor)));
                        _setField(ep, "_Armor", finalArmor);

                        // 3. 攻击指令注入 (处理 _AttackCommandBasic 和 _AttackCommandVeteran)
                        string[] cmds = { "_AttackCommandBasic", "_AttackCommandVeteran" };
                        foreach (var cmdName in cmds)
                        {
                            FieldInfo cmdField = typeof(ZXEntityDefaultParams).GetField(cmdName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                            object cmdObj = cmdField?.GetValue(ep);
                            if (cmdObj != null)
                            {
                                _processCommandParams(cmdObj, $"{cmdName}", bDamage, mDamage, bRange, mRange, bTimeAction, bTimePrep, mAtkSpeed);

                                // 同步修改 DefaultValues 里的指令
                                if (ep.DefaultValues is ZXEntityDefaultParams dep)
                                {
                                    object dCmdObj = cmdField?.GetValue(dep);
                                    if (dCmdObj != null) _processCommandParams(dCmdObj, $"{cmdName}.Default", bDamage, mDamage, bRange, mRange, bTimeAction, bTimePrep, mAtkSpeed);
                                }
                            }
                        }
                        DXLog.Write($"[Test_Mod] <<< {entity_ID} 注入流程结束");
                    }
                    catch (Exception ex) { DXLog.Write($"[Test_Mod] [FATAL] {entity_ID} 崩溃: {ex.Message}"); }
                }
            }
        }

        //这是数值注入的核心方法：反射设置字段值，并处理类型转换，同时增加了对单位配置镜像 DefaultValues 的同步修改
        private static void _setField(object target, string fieldName, object value)
        {
            if (target == null) return;
            FieldInfo f = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null)
            {
                object finalVal = Convert.ChangeType(value, f.FieldType);
                f.SetValue(target, finalVal);

                // 如果是单位配置，同步修改其对应的 DefaultValues 镜像
                if (target is ZXEntityDefaultParams ep && ep.DefaultValues != null && ep.DefaultValues != ep)
                {
                    _setField(ep.DefaultValues, fieldName, value);
                }
            }
        }

        //这是专门给攻击参数注入的方法，因为攻击的参数比较特殊，不与生命，护甲等其他值放在一起
        //，而是放在一个单独的命令参数对象里，所以需要专门处理，增加了日志输出以便排查问题
        private static void _processCommandParams(object commandObj, string label, int bDmg, double mDmg, float bRng, double mRng, int bTime, int bPrep, double mSpd)
        {
            FieldInfo paramsField = commandObj.GetType().GetField("_Params", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            object realParams = paramsField?.GetValue(commandObj);

            if (realParams != null)
            {
                double speedFactor = Math.Max(0.1, mSpd / 100.0);

                // 修正字段对应关系：
                // _Damage: 伤害
                // _ActionRange: 射程
                // _TimeAction: 动画执行/间隔 (数表中的 600/1000)
                // _TimePreAction: 行动前摇 (数表中的 1600/0) -> 关键修正点！

                _setField(realParams, "_Damage", (int)(bDmg * mDmg / 100.0));
                _setField(realParams, "_ActionRange", (float)(bRng * mRng / 100.0));

                // 缩减动画时间
                int finalAction = (int)Math.Max(1, bTime / speedFactor);
                _setField(realParams, "_TimeAction", finalAction);

                // 缩减前摇时间 (狙击手的瞄准逻辑)
                int finalPreAction = (int)(bPrep / speedFactor);
                _setField(realParams, "_TimePreAction", finalPreAction);

                // 可选：数表中还有一个“加载时间” (Load)，通常对应 _TimeLoad
                // 如果你想让狙击手举枪拉栓也变快，可以加上：
                // _setField(realParams, "_TimeLoad", (int)(500 / speedFactor));

                DXLog.Write($"[Test_Mod] [{label}] 注入: Dmg:{(int)(bDmg * mDmg / 100.0)}, Action:{finalAction}, PreAtk:{finalPreAction}");
            }
        }
    

        /*private void CopyFiles(string file_name,string event_name)
        {
            try
            {
                
                // 寻找目标资源文件
                string sourceDir = Path.Combine(test_mod.ModPath, "ZX",file_name);
                //寻找目标拷贝目录
                string targetDir = Path.Combine(test_mod.ModPath,"ZXRules.dat");

                if (!File.Exists(sourceDir))
                {
                    DXLog.Write($"[Test_Mod] error:源文件夹不存在 {sourceDir}");
                    return;
                }
                else
                {
                    DXLog.Write($"[Test_Mod] 找到源文件 {sourceDir}");
                    File.Copy(sourceDir, targetDir, true);

                    if (!File.Exists(targetDir))
                    {
                        DXLog.Write($"[Test Mod] error:哦，牛批，这能没有的 {targetDir}");
                        return;
                    }

                    DXLog.Write($"[Test_Mod] 已复制文件到 {targetDir}");
                   
                }

                DXLog.Write("[Test_Mod] " + event_name + " 文件部署完成！");
            }
            catch (Exception ex)
            {
                DXLog.Write($"[Test_Mod] 文件拷贝失败: {ex.Message}");
            }
        }*/


        private void OnConfigChanged(ModConfig config)
        {
            //对于配置更改后的保存和更改
            _test_mod.SaveConfig();
            //老兵恢复满血方法不一定如何实现，暂时不做处理，预留一个位置

            DXLog.Write($"[Test_Mod] 配置已更改");
            
        }


        private void Unload()
        {
            Config.OnConfigChanged -= OnConfigChanged;
            
            _test_mod.SaveConfig();
        }


    }

    public class Unit_Total_Config : ModConfig
    {
        // ================= 全局设置 =================
        [ConfigOption("老兵恢复满血", ConfigOptionType.Checkbox,
            Description = "单位晋升老兵时，是否恢复满血",
            Category = "全局设置", Order = 1)]
        public bool IsFull_HP { get; set; } = true;

        // ================= 游侠属性 (Ranger) =================
        [ConfigOption("游侠-生命倍率", ConfigOptionType.Slider, Category = "游侠属性", Order = 1)]
        [Range(1, 500, 1)]
        public double Ranger_HealthyMult { get; set; } = 100;

        [ConfigOption("游侠-攻击倍率", ConfigOptionType.Slider, Category = "游侠属性", Order = 2)]
        [Range(1, 300, 1)]
        public double Ranger_DamageMult { get; set; } = 100;

        [ConfigOption("游侠-射程倍率", ConfigOptionType.Slider, Category = "游侠属性", Order = 3)]
        [Range(1, 200, 1)]
        public double Ranger_RangeMult { get; set; } = 100;

        [ConfigOption("游侠-攻速倍率", ConfigOptionType.Slider, Category = "游侠属性", Order = 4)]
        [Range(1, 300, 1)]
        public double Ranger_DamageSpeedMult { get; set; } = 100;

        [ConfigOption("游侠-移速倍率", ConfigOptionType.Slider, Category = "游侠属性", Order = 5)]
        [Range(1, 200, 1)]
        public double Ranger_SpeedMult { get; set; } = 100;

        [ConfigOption("游侠-增加额外护甲值", ConfigOptionType.Slider, Category = "游侠属性", Order = 6)]
        [Range(0, 0.90, 0.01)]
        public double Ranger_ArmorAdd { get; set; } = 0;

        [ConfigOption("游侠-视野倍率", ConfigOptionType.Slider, Category = "游侠属性", Order = 7)]
        [Range(1, 200, 1)]
        public double Ranger_VisionMult { get; set; } = 100;

        // ================= 士兵属性 (Soldier) =================
        [ConfigOption("士兵-生命倍率", ConfigOptionType.Slider, Category = "士兵属性", Order = 1)]
        [Range(1, 500, 1)]
        public double Soldier_HealthyMult { get; set; } = 100;

        [ConfigOption("士兵-攻击倍率", ConfigOptionType.Slider, Category = "士兵属性", Order = 2)]
        [Range(1, 300, 1)]
        public double Soldier_DamageMult { get; set; } = 100;

        [ConfigOption("士兵-射程倍率", ConfigOptionType.Slider, Category = "士兵属性", Order = 3)]
        [Range(1, 200, 1)]
        public double Soldier_RangeMult { get; set; } = 100;

        [ConfigOption("士兵-攻速倍率", ConfigOptionType.Slider, Category = "士兵属性", Order = 4)]
        [Range(1, 300, 1)]
        public double Soldier_DamageSpeedMult { get; set; } = 100;

        [ConfigOption("士兵-移速倍率", ConfigOptionType.Slider, Category = "士兵属性", Order = 5)]
        [Range(1, 200, 1)]
        public double Soldier_SpeedMult { get; set; } = 100;

        [ConfigOption("士兵-增加额外护甲值", ConfigOptionType.Slider, Category = "士兵属性", Order = 6)]
        [Range(0, 0.55, 0.01)]
        public double Soldier_ArmorAdd { get; set; } = 0;

        [ConfigOption("士兵-视野倍率", ConfigOptionType.Slider, Category = "士兵属性", Order = 7)]
        [Range(1, 200, 1)]
        public double Soldier_VisionMult { get; set; } = 100;

        // ================= 狙击手属性 (Sniper) =================
        [ConfigOption("狙击手-生命倍率", ConfigOptionType.Slider, Category = "狙击手属性", Order = 1)]
        [Range(1, 500, 1)]
        public double Sniper_HealthyMult { get; set; } = 100;

        [ConfigOption("狙击手-攻击倍率", ConfigOptionType.Slider, Category = "狙击手属性", Order = 2)]
        [Range(1, 500, 1)] // 狙击手攻击成长空间大，上限给到500
        public double Sniper_DamageMult { get; set; } = 100;

        [ConfigOption("狙击手-射程倍率", ConfigOptionType.Slider, Category = "狙击手属性", Order = 3)]
        [Range(1, 200, 1)]
        public double Sniper_RangeMult { get; set; } = 100;

        [ConfigOption("狙击手-攻速倍率", ConfigOptionType.Slider, Category = "狙击手属性", Order = 4)]
        [Range(1, 500, 1)] // 默认极慢，给高倍率提升手感
        public double Sniper_DamageSpeedMult { get; set; } = 100;

        [ConfigOption("狙击手-移速倍率", ConfigOptionType.Slider, Category = "狙击手属性", Order = 5)]
        [Range(1, 200, 1)]
        public double Sniper_SpeedMult { get; set; } = 100;

        [ConfigOption("狙击手-增加额外护甲值", ConfigOptionType.Slider, Category = "狙击手属性", Order = 6)]
        [Range(0, 0.90, 0.01)]
        public double Sniper_ArmorAdd { get; set; } = 0;

        [ConfigOption("狙击手-视野倍率", ConfigOptionType.Slider, Category = "狙击手属性", Order = 7)]
        [Range(1, 200, 1)]
        public double Sniper_VisionMult { get; set; } = 100;
    }

}
