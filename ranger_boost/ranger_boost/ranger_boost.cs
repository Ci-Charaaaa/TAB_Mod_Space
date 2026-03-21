using AX.ModLoader;
using AX.ModLoader.Config;
using DXVision;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using ZX;
using HarmonyLib;


namespace ranger_boost
{
    public class Ranger_Mod : IModEntry 
    {
        private Mod test_mod;
        private Test_Mod_config test_config;
        //public static int newLifeValue;
        //private static FieldInfo _lifeField;
        public static Ranger_Mod Instance;


        public override void OnLoad(Mod mod)
        {
            test_mod = mod;

            DXLog.Write($"[Test_Mod] 已加载！版本：{mod.Info.Version} gugugaga!");

            test_config = mod.RegisterConfig<Test_Mod_config>();

            Instance = this;

            var harmony = new Harmony("com.charaa.test1");
            harmony.UnpatchAll("com.charaa.test1");
            harmony.PatchAll(Assembly.GetExecutingAssembly()); // 告诉 Harmony 扫描这个项目里的 [HarmonyPatch]

            DXLog.Write($"[Test_Mod] Harmony 补丁已应用！正在等待游戏逻辑初始化...,随时准备进行属性加强模块注入");

            test_config.OnConfigChanged += OnConfigChanged;

        }

        // 钩住 ApplyMods
        [HarmonyPatch(typeof(ZXDefaultParams), "ApplyMods")]
        public class InjectionPatch
        {
            // 使用 Postfix，确保在游戏逻辑跑完后，执行强行覆盖
            public static void Postfix()
            {
                DXLog.Write("[StatModifierMod] 检测到关卡数据初始化，开始执行强行注入...");
                ApplyMemoryPatches();
            }
        }

        public static void ApplyMemoryPatches()
        {
            // 1. 检查单例和配置是否就绪
            if (Ranger_Mod.Instance == null || Ranger_Mod.Instance.test_config == null)
            {
                DXLog.Write("[Test_Mod] [ERROR] 注入终止：Ranger_Mod.Instance 或配置对象为空！");
                return;
            }

            var config = Ranger_Mod.Instance.test_config;
            var allParams = ZXDefaultParams.get_All();

            if (allParams == null)
            {
                DXLog.Write("[Test_Mod] [WARNING] ZXDefaultParams.get_All() 返回为空，可能尚未进入加载流程。");
                return;
            }

            DXLog.Write($"[Test_Mod] 开始扫描实体参数，总数: {allParams.Count}");

            foreach (var p in allParams)
            {
                if (p.ID == "Ranger" && p is ZXEntityDefaultParams ep)
                {
                    DXLog.Write($"[Test_Mod] >>> 找到目标实体: {p.ID}，开始注入属性...");


                    try
                    {
                        // 参数说明：目标对象, 字段名, 原始基础值, 放大倍率(0-5.0)
                        SetFieldSafe(ep, "_Life", 60f, config.HealthyMult / 100.0);
                        SetFieldSafe(ep, "_RunSpeed", 4f, config.SpeedMult / 100.0);
                        SetFieldSafe(ep, "_WatchRange", 8f, config.VisionMult / 100.0);

                        // 护甲通常是直接相加，所以倍率传1.0
                        SetFieldSafe(ep, "_Armor", (float)config.ArmorAdd, 0.01);

                        DXLog.Write($"[Test_Mod] <<< 实体 {p.ID} 属性注入尝试完成。");

                        try
                        {
                            FieldInfo shellField = typeof(ZXEntityDefaultParams).GetField("_AttackCommandBasic", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                            object shellObj = shellField?.GetValue(ep);

                            if (shellObj != null)
                            {
                                FieldInfo paramsField = shellObj.GetType().GetField("_Params", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                                object realParams = paramsField?.GetValue(shellObj);

                                if (realParams != null)
                                {
                                    
                                    UpdateIntField(realParams, "_Damage", 10, config.DamageMult / 100.0);
                                    UpdateIntField(realParams, "_TimeAction", 1, config.DamageMult / 1.0);
                                    UpdateFloatField(realParams, "_ActionRange", 6f, config.RangeMult / 100.0);

                                    DXLog.Write("[Test_Mod] [SUCCESS] 游侠战斗属性注入完成！");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            DXLog.Write($"[Test_Mod] [ERROR] 攻击层注入失败: {ex.Message}");
                        }

                    }
                    catch (Exception ex)
                    {
                        DXLog.Write($"[Test_Mod] [FATAL] 注入循环崩溃: {ex.Message}\n{ex.StackTrace}");
                    }
                    break; // 找到 Ranger 后跳出循环
                }
            }
        }

        private static void UpdateIntField(object target, string fieldName, int baseVal, double mult)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
            {
                int final = (int)(baseVal * mult);
                field.SetValue(target, final);
                DXLog.Write($"[Test_Mod] [OK] {fieldName} (Int32) -> {final}");
            }
        }

        private static void UpdateFloatField(object target, string fieldName, float baseVal, double mult)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
            {
                float final = (float)(baseVal * mult);
                field.SetValue(target, final);
                DXLog.Write($"[Test_Mod] [OK] {fieldName} (Single) -> {final}");
            }
        }

        // 核心：带日志的安全注入工具
        private static void SetFieldSafe(ZXEntityDefaultParams target, string fieldName, float baseValue, double multiplier)
        {
            try
            {
                // 1. 获取字段信息
                FieldInfo field = typeof(ZXEntityDefaultParams).GetField(fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                if (field == null)
                {
                    DXLog.Write($"[Test_Mod] [ERROR] 找不到字段: {fieldName}，请检查游戏版本是否变更了字段名。");
                    return;
                }

                // 2. 计算并转换
                double rawCalculated = baseValue * multiplier;

                // 根据字段的实际类型进行转换（游戏里有些是 int，有些是 float）
                object finalValue;
                if (field.FieldType == typeof(float))
                {
                    finalValue = (float)rawCalculated;
                }
                else
                {
                    finalValue = Convert.ToInt32(rawCalculated);
                }

                // 3. 执行注入
                field.SetValue(target, finalValue);

                // 4. 同步修改 DefaultValues (这是很多 Mod 失效的原因)
                if (target.DefaultValues != null)
                {
                    field.SetValue(target.DefaultValues, finalValue);
                }

                DXLog.Write($"[Test_Mod] [SUCCESS] 字段 {fieldName} 修改成功: {baseValue} -> {finalValue} (Type: {field.FieldType.Name})");
            }
            catch (Exception ex)
            {
                DXLog.Write($"[Test_Mod] [ERROR] 修改字段 {fieldName} 时发生异常: {ex.GetType().Name} - {ex.Message}");
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
            //对于配置更改后的加载
            test_mod.SaveConfig();

            DXLog.Write($"[Test_Mod] 配置已更改！启用功能 更强的游侠: {test_config.HealthyMult}");
            
        }


        private void Unload()
        {
            test_config.OnConfigChanged -= OnConfigChanged;
            test_mod.SaveConfig();
        }


    }
    public class Test_Mod_config : ModConfig
    {

        [ConfigOption("游侠生命", ConfigOptionType.Slider,
        Description = "调整游侠生命值,不能小于0，最大500%",
        Category = "游戏性",
        Order = 2)]
        [Range(1, 500, 1)]
        public double HealthyMult { get; set; } = 100;

        [ConfigOption("攻击倍率", ConfigOptionType.Slider, 
            Description = "调整游侠攻击", 
            Category = "游侠属性", 
            Order = 2)]
        [Range(1, 300, 1)]
        public double DamageMult { get; set; } = 100;

        [ConfigOption("攻击范围", ConfigOptionType.Slider,
            Description = "调整游侠攻击范围",
            Category = "游侠属性",
            Order = 2)]
        [Range(1, 200, 1)]
        public double RangeMult { get; set; } = 100;

        [ConfigOption("攻击速度", ConfigOptionType.Slider,
            Description = "调整游侠攻击速度",
            Category = "游侠属性",
            Order = 2)]
        [Range(1, 300, 1)]
        public double DamageSpeedMult { get; set; } = 100;

        [ConfigOption("移动速度", ConfigOptionType.Slider, 
            Description = "调整游侠移动速度", 
            Category = "游侠属性", 
            Order = 3)]
        [Range(1, 200, 1)]
        public double SpeedMult { get; set; } = 100;

        [ConfigOption("护甲值", ConfigOptionType.Slider, 
            Description = "调整游侠护甲", 
            Category = "游侠属性", 
            Order = 4)]
        [Range(0, 50, 1)]
        public double ArmorAdd { get; set; } = 0;

        [ConfigOption("视野倍率", ConfigOptionType.Slider, 
            Description = "调整游侠视野", 
            Category = "游侠属性", 
            Order = 5)]
        [Range(1, 200, 1)]
        public double VisionMult { get; set; } = 100;




    }

}
