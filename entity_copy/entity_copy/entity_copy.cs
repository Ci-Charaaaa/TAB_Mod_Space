using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DXVision;
using ZX;
using AX.ModLoader;
using HarmonyLib;
using AX.ModLoader.Config;


namespace entity_copy.entity_copy
{
    public class Entity_copy : IModEntry
    {
        private Mod entity_copy_mod;
        public Entity_Copy_Config config;



        public override void OnLoad(Mod mod)
        {
            entity_copy_mod = mod;
            config = mod.RegisterConfig<Entity_Copy_Config>();
            DXLog.Write($"[Entity_copy_Mod] 已加载！版本：{mod.Info.Version}");





        }



        private void OnConfigChanged(ModConfig config)
        {
            //对于配置更改后的保存和更改
            entity_copy_mod.SaveConfig();
            DXLog.Write($"[Test_Mod] 配置已更改");

        }


    }

    



    public class Entity_Copy_Config : ModConfig
    {
        [ConfigOption("快捷键复制单位", ConfigOptionType.Checkbox,
            Description = "使用快捷键去复制单位",
            Category = "全局设置", Order = 1)]
        public bool IsCopy { get; set; } = true;
    }
}

