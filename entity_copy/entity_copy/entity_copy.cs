using System;
using System.Collections.Generic;
using AX.ModLoader;
using AX.ModLoader.Config;
using DXVision;
using HarmonyLib;
using ZX;
using ZX.Components;
using ZX.Entities;
using ZX.Systems;

namespace entity_copy.entity_copy
{
    public class Entity_copy : IModEntry
    {
        private Mod entity_copy_mod;
        private Harmony harmony;
        public Entity_Copy_Config config;

        public override void OnLoad(Mod mod)
        {
            entity_copy_mod = mod;
            config = mod.RegisterConfig<Entity_Copy_Config>();
            config.OnConfigChanged += OnConfigChanged;

            EntityCopyRuntime.Initialize(config);
            harmony = new Harmony("entity_copy.runtime.patch");
            harmony.PatchAll();

            DXLog.Write($"[Entity_copy_Mod] 已加载！版本：{mod.Info.Version}");
        }

        private void OnConfigChanged(ModConfig changedConfig)
        {
            entity_copy_mod.SaveConfig();
            DXLog.Write($"[Entity_copy_Mod] 配置已更改，当前复制热键: {EntityCopyRuntime.GetConfiguredHotkeyName()}");
        }
    }

    [HarmonyPatch(typeof(ZXGame), "OnStartFrame")]
    internal static class Patch_ZXGame_OnStartFrame
    {
        // 游戏每帧结束时进入这里：用于刷新选中快照并处理复制请求。
        private static void Postfix()
        {
            EntityCopyRuntime.OnFrame();
        }
    }

    [HarmonyPatch(typeof(DXGame), "ProcessKeyUp")]
    internal static class Patch_DXGame_ProcessKeyUp
    {
        // 使用 KeyUp 触发，天然避免按住按键时一帧触发多次。
        private static void Postfix(DXGame __instance, DXKeys key)
        {
            EntityCopyRuntime.OnKeyUp(key);
        }
    }

    internal static class EntityCopyRuntime
    {
        private static Entity_Copy_Config _config;
        private static bool _pendingCopyRequest;
        private static int _lastSnapshotFrame = -1;
        private static readonly List<ZXEntity> _lastSelectedUnits = new List<ZXEntity>();
        // 配置滑条的索引 -> 实际按键映射表。
        private static readonly DXKeys[] _supportedHotkeys = new DXKeys[]
        {
            DXKeys.F1, DXKeys.F2, DXKeys.F3, DXKeys.F4, DXKeys.F5, DXKeys.F6, DXKeys.F7, DXKeys.F8, DXKeys.F9, DXKeys.F10,
            DXKeys.D1, DXKeys.D2, DXKeys.D3, DXKeys.D4, DXKeys.D5,
            DXKeys.C, DXKeys.V, DXKeys.B, DXKeys.X,
            DXKeys.Delete, DXKeys.Insert, DXKeys.Home, DXKeys.End
        };

        public static void Initialize(Entity_Copy_Config config)
        {
            _config = config;
            DXLog.Write($"[Entity_copy_Mod] 当前复制热键: {GetConfiguredHotkeyName()}");
        }

        // 后续快捷键检测到时，直接调用这个方法即可。
        public static void RequestCopySelected()
        {
            // 请求延迟到帧逻辑里执行，避免在输入回调里直接改游戏对象。
            _pendingCopyRequest = true;
        }

        public static void OnFrame()
        {
            if (_config == null || !_config.IsCopy)
            {
                return;
            }

            if (!DXGame.Current.InputEnabled)
            {
                return;
            }

            RefreshSelectionSnapshotIfNeeded();

            // 这里只消费“复制请求”，请求来源可以是快捷键、UI按钮或其他事件。
            if (!_pendingCopyRequest)
            {
                return;
            }

            _pendingCopyRequest = false;
            HandleCopyTriggered(_lastSelectedUnits);
        }

        public static void OnKeyUp(DXKeys key)
        {
            if (_config == null || !_config.IsCopy)
            {
                return;
            }

            if (key == GetConfiguredHotkey())
            {
                RequestCopySelected();
            }
        }

        public static string GetConfiguredHotkeyName()
        {
            return GetConfiguredHotkey().ToString();
        }

        private static DXKeys GetConfiguredHotkey()
        {
            if (_config == null)
            {
                return DXKeys.F6;
            }

            int index = _config.CopyHotkeyIndex;
            if (index < 0 || index >= _supportedHotkeys.Length)
            {
                // 配置越界时回退到默认热键，避免异常导致功能失效。
                index = 5; // F6
            }
            return _supportedHotkeys[index];
        }

        private static void RefreshSelectionSnapshotIfNeeded()
        {
            int frame = DXScene.Current.Render_Iteration;
            if (_lastSnapshotFrame == frame)
            {
                // 同一帧只刷新一次，避免重复加锁读取。
                return;
            }

            _lastSnapshotFrame = frame;
            _lastSelectedUnits.Clear();

            HashSet<CSelectable> selectedLocked = null;
            try
            {
                // AllSelected 是并发集合，使用官方锁定接口做快照读取更安全。
                selectedLocked = CSelectable.AllSelected.GetObjectLocked();
                foreach (CSelectable selectable in selectedLocked)
                {
                    if (selectable == null)
                    {
                        continue;
                    }

                    ZXEntity entity = selectable.Entity as ZXEntity;
                    if (entity is Unit)
                    {
                        _lastSelectedUnits.Add(entity);
                    }
                }
            }
            catch (Exception ex)
            {
                DXLog.Write($"[Entity_copy_Mod] 读取选中单位失败: {ex.Message}");
            }
            finally
            {
                if (selectedLocked != null)
                {
                    CSelectable.AllSelected.ReleaseObjectLocked();
                }
            }
        }

        private static void HandleCopyTriggered(List<ZXEntity> selectedUnitsSnapshot)
        {
            if (selectedUnitsSnapshot == null || selectedUnitsSnapshot.Count == 0)
            {
                DXLog.Write("[Entity_copy_Mod] 当前没有可复制的选中单位。");
                return;
            }

            int successCount = 0;
            int failCount = 0;

            foreach (ZXEntity source in new List<ZXEntity>(selectedUnitsSnapshot))
            {
                // 复制时遍历快照副本，避免源集合在处理中被其他系统改动。
                if (source == null)
                {
                    failCount++;
                    continue;
                }

                if (TryCloneUnitFromSource(source))
                {
                    successCount++;
                }
                else
                {
                    failCount++;
                }
            }

            DXLog.Write($"[Entity_copy_Mod] 复制完成，成功: {successCount}，失败: {failCount}");
        }

        private static bool TryCloneUnitFromSource(ZXEntity source)
        {
            try
            {
                Type sourceType = source.GetType();
                // 走模板实例化，尽量复用游戏原生组件初始化流程。
                DXEntityTemplate template = ZXGame.get_Current().GetTemplateFromEntityType(sourceType);
                if (template == null)
                {
                    return false;
                }

                ZXEntity cloned = template.CreateInstance(null) as ZXEntity;
                if (cloned == null)
                {
                    return false;
                }

                cloned.Relation = source.Relation;
                cloned.Team = source.Team;

                // 兼容实现：直接在源单位附近做一个小偏移，避免依赖不存在的网格寻位 API。
                cloned.Position = DXExtensions_Point.Add(source.Position, 0.8f, 0.8f);

                // 入场 + 注册关卡状态，两者缺一都会导致行为/状态异常。
                cloned.InvokeAddToScene(null);
                ZXLevelState.get_Current().OnEntityCreated(cloned);

                // 若源单位是老兵，复制体同步为老兵。
                if (source.IsVeteran)
                {
                    cloned.IsVeteran = true;
                }

                SysEventManager.get_Current().NotifyEntityBuilt(cloned);
                return true;
            }
            catch (Exception ex)
            {
                DXLog.Write($"[Entity_copy_Mod] 复制单位失败: {ex.Message}");
                return false;
            }
        }
    }

    public class Entity_Copy_Config : ModConfig
    {
        [ConfigOption("快捷键复制单位", ConfigOptionType.Checkbox,
            Description = "使用快捷键去复制单位",
            Category = "全局设置", Order = 1)]
        public bool IsCopy { get; set; } = true;

        [ConfigOption("复制热键", ConfigOptionType.Slider,
            Description = "按索引选择热键: 0-F1,1-F2,2-F3,3-F4,4-F5,5-F6,6-F7,7-F8,8-F9,9-F10,10-1,11-2,12-3,13-4,14-5,15-C,16-V,17-B,18-X,19-Delete,20-Insert,21-Home,22-End",
            Category = "全局设置", Order = 2)]
        [Range(0, 22, 1)]
        public int CopyHotkeyIndex { get; set; } = 5;
    }
}
