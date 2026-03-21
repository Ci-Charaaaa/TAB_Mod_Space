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
        public static int newLifeValue;
        private static FieldInfo _lifeField;


        public override void OnLoad(Mod mod)
        {
            test_mod = mod;

            DXLog.Write($"[Test_Mod] 已加载！版本：{mod.Info.Version} gugugaga!");

            //清理旧指针和缓存，确保反射字段在第一次使用时正确获取
            _lifeField = null;

            test_config = mod.RegisterConfig<Test_Mod_config>();


            newLifeValue = (int)((test_config.DifficultyMultiplier/100) * 60 );
            DXLog.Write($"[Test_Mod] 游侠生命值修改 已启用！");


            var harmony = new Harmony("com.charaa.test1");
            harmony.UnpatchAll("com.charaa.test1");
            harmony.PatchAll(Assembly.GetExecutingAssembly()); // 告诉 Harmony 扫描这个项目里的 [HarmonyPatch]
            
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
            
            var allParams = ZXDefaultParams.get_All();
            if (allParams == null) return;

            foreach (var p in allParams)
            {
                // 确保 ID 匹配
                if (p.ID == "Ranger")
                {
                    if (p is ZXEntityDefaultParams entityParams)
                    {
                        try
                        {

                            FieldInfo lifeField;

                            // 获取生命值字段
                            if (_lifeField == null)
                            {
                                _lifeField = entityParams.GetType().GetField("_Life", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                                lifeField = _lifeField;
                                DXLog.Write("[StatModifierMod] 生命值字段已缓存，正在使用反射修改...");
                            }
                            else
                            {
                                DXLog.Write("[StatModifierMod] 已缓存生命值字段，直接使用缓存...");
                                lifeField = _lifeField;
                            }
                                

                            if (lifeField != null)
                            {
                              
                                lifeField.SetValue(entityParams, newLifeValue);

                                if (entityParams.DefaultValues != null)
                                {
                                    // 同样修改 DefaultValues 中的生命值，确保一致性
                                    FieldInfo defLifeField = entityParams.DefaultValues.GetType().GetField("_Life", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                                    if (defLifeField != null)
                                    {
                                        defLifeField.SetValue(entityParams.DefaultValues, newLifeValue);
                                    }
                                }

                                DXLog.Write($"[StatModifierMod] 成功！Ranger 生命已强行锁定为: {newLifeValue}");
                            }
                        }
                        catch (Exception ex)
                        {
                            DXLog.Write($"[StatModifierMod] 注入失败: {ex.Message}");
                        }
                    }
                    break;
                }
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
            
            newLifeValue = (int)((test_config.DifficultyMultiplier / 100) * 60);
            test_mod.SaveConfig();

            DXLog.Write($"[Test_Mod] 配置已更改！启用功能 更强的游侠: {test_config.DifficultyMultiplier}");
            
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
        public double DifficultyMultiplier { get; set; } = 100;




    }

}
