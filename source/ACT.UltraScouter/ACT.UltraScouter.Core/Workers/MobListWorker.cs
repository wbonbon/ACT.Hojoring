using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ACT.UltraScouter.Config;
using ACT.UltraScouter.Config.UI.ViewModels;
using ACT.UltraScouter.Models;
using ACT.UltraScouter.ViewModels;
using ACT.UltraScouter.Views;
using FFXIV.Framework.Bridge;
using FFXIV.Framework.Common;
using FFXIV.Framework.XIVHelper;
using FFXIV_ACT_Plugin.Common.Models;
using NPOI.SS.Formula.Functions;
using Sharlayan.Core.Enums;

namespace ACT.UltraScouter.Workers
{
    public class MobListWorker :
        TargetInfoWorker
    {
        #region Singleton

        private static MobListWorker instance;
        public static new MobListWorker Instance => instance;

        public static new void Initialize() => instance = new MobListWorker();

        public static new void Free() => instance = null;

        private MobListWorker()
        {
        }

        #endregion Singleton

        /// <summary>
        /// 任意ターゲット系のオーバーレイではない
        /// </summary>
        protected override bool IsTargetOverlay => false;

        /// <summary>
        /// サブオーバーレイである
        /// </summary>
        protected override bool IsSubOverlay => true;

        public override TargetInfoModel Model => MobListModel.Instance;
        private List<MobInfo> targetMobList;

        public override void End()
        {
            lock (MainWorker.Instance.ViewRefreshLocker)
            {
                this.mobListVM = null;
            }
        }

        private DateTime combatantsTimestamp = DateTime.MinValue;

        protected override void GetCombatant()
        {
            lock (this.TargetInfoLock)
            {
                var now = DateTime.UtcNow;
                if ((now - this.combatantsTimestamp).TotalMilliseconds
                    < Settings.Instance.MobList.RefreshRateMin)
                {
                    return;
                }

                this.combatantsTimestamp = now;
            }

            #region Test Mode

            // テストモード？
            if (Settings.Instance.MobList.TestMode)
            {
                var dummyTargets = new List<MobInfo>();

                dummyTargets.Add(new MobInfo()
                {
                    Name = "TEST:シルバーの吉田直樹",
                    Rank = "EX",
                    Combatant = new CombatantEx()
                    {
                        ID = 1,
                        Name = "TEST:シルバーの吉田直樹",
                        Type = (byte)Actor.Type.Monster,
                        MaxHP = 1,
                        PosX = 0,
                        PosY = 10,
                        PosZ = 10,
                    },
                });

                dummyTargets.Add(new MobInfo()
                {
                    Name = "TEST:イクシオン",
                    Rank = "EX",
                    Combatant = new CombatantEx()
                    {
                        ID = 2,
                        Name = "TEST:イクシオン",
                        Type = (byte)Actor.Type.Monster,
                        MaxHP = 1,
                        PosX = 100,
                        PosY = 100,
                        PosZ = -10,
                    },
                });

                dummyTargets.Add(new MobInfo()
                {
                    Name = "TEST:イクシオン",
                    Rank = "EX",
                    Combatant = new CombatantEx()
                    {
                        ID = 21,
                        Name = "TEST:イクシオン",
                        Type = (byte)Actor.Type.Monster,
                        MaxHP = 1,
                        PosX = 100,
                        PosY = 100,
                        PosZ = -10,
                    },
                });

                dummyTargets.Add(new MobInfo()
                {
                    Name = "TEST:ソルト・アンド・ライト",
                    Rank = "S",
                    Combatant = new CombatantEx()
                    {
                        ID = 3,
                        Name = "TEST:ソルト・アンド・ライト",
                        Type = (byte)Actor.Type.Monster,
                        MaxHP = 1,
                        PosX = 10,
                        PosY = 0,
                        PosZ = 0,
                    },
                });

                dummyTargets.Add(new MobInfo()
                {
                    Name = "TEST:オルクス",
                    Rank = "A",
                    Combatant = new CombatantEx()
                    {
                        ID = 4,
                        Name = "TEST:オルクス",
                        Type = (byte)Actor.Type.Monster,
                        MaxHP = 1,
                        PosX = 100,
                        PosY = -100,
                        PosZ = 0,
                    },
                });

                dummyTargets.Add(new MobInfo()
                {
                    Name = "TEST:宵闇のヤミニ",
                    Rank = "B",
                    Combatant = new CombatantEx()
                    {
                        ID = 5,
                        Name = "TEST:宵闇のヤミニ",
                        Type = (byte)Actor.Type.Monster,
                        MaxHP = 1,
                        PosX = 0,
                        PosY = -100,
                        PosZ = 0,
                    },
                });

                dummyTargets.Add(new MobInfo()
                {
                    Name = CombatantEx.NameToInitial("TEST:Hime Hana", ConfigBridge.Instance.PCNameStyle),
                    Rank = "DEAD",
                    Combatant = new CombatantEx()
                    {
                        ID = 7,
                        Name = CombatantEx.NameToInitial("TEST:Hime Hana", ConfigBridge.Instance.PCNameStyle),
                        Type = (byte)Actor.Type.Monster,
                        Job = (byte)JobIDs.BLM,
                        MaxHP = 43462,
                        PosX = -100,
                        PosY = -100,
                        PosZ = 0,
                    },
                });

                lock (this.TargetInfoLock)
                {
                    this.TargetInfo = dummyTargets.First().Combatant;
                    this.targetMobList = dummyTargets;
                }

                return;
            }

            #endregion Test Mode

            if ((CombatantsManager.Instance.CombatantsMainCount + CombatantsManager.Instance.CombatantsOtherCount) <= 0)
            {
                return;
            }

            var targets = default(IEnumerable<MobInfo>);
            var combatants = CombatantsManager.Instance.GetCombatants();

            // モブを検出する
            IEnumerable<MobInfo> GetTargetMobs()
            {
                var actorsInfo = SharlayanHelper.Instance.Actors;

                foreach (var x in combatants)
                {
                    if (string.IsNullOrEmpty(x?.Name))
                    {
                        continue;
                    }

                    if (x.ActorType == Actor.Type.PC ||
                        x.ActorType == Actor.Type.Monster)
                    {
                        if (x.MaxHP <= 0 ||
                            (x.MaxHP > 0 && x.CurrentHP <= 0))
                        {
                            continue;
                        }
                    }

                    var targetInfo = Settings.Instance.MobList.GetTargetMobInfo(x.Name);
                    if (string.IsNullOrEmpty(targetInfo.Name))
                    {
                        continue;
                    }

                    yield return new MobInfo()
                    {
                        Name = x.Name,
                        Combatant = x,
                        Rank = targetInfo.Rank,
                        MaxDistance = targetInfo.MaxDistance,
                        TTSEnabled = targetInfo.TTSEnabled,
                    };
                }

                // 風脈の泉を検出する
                if (!Settings.Instance.MobList.DisableAether) {
                    foreach (var actor in actorsInfo)
                    {
                        if (actor.Name == "風脈の泉")
                        {
                            var targetInfo = Settings.Instance.MobList.GetTargetMobInfo(actor.Name);
                            if (string.IsNullOrEmpty(targetInfo.Name))
                            {
                                break;
                            }
                            CombatantEx combatant = new CombatantEx();
                            CombatantEx.CopyToEx(actor, combatant);
                            yield return new MobInfo()
                            {
                                Name = actor.Name,
                                Combatant = combatant,
                                Rank = targetInfo.Rank,
                                MaxDistance = targetInfo.MaxDistance,
                                TTSEnabled = targetInfo.TTSEnabled,
                            };
                        }
                    }
                }
            }

            targets = GetTargetMobs();

            // 戦闘不能者を検出する？
            var deadmenInfo = Settings.Instance.MobList.GetDetectDeadmenInfo;
            if (!string.IsNullOrEmpty(deadmenInfo.Name))
            {
                var party = CombatantsManager.Instance.GetPartyList();
                var deadmen =
                    from x in party
                    where
                    x != null &&
                    !x.IsPlayer &&
                    x.ActorType == Actor.Type.PC &&
                    x.MaxHP > 0 && x.CurrentHP <= 0
                    select new MobInfo()
                    {
                        Name = x.NameForDisplay,
                        Combatant = x,
                        Rank = deadmenInfo.Rank,
                        MaxDistance = deadmenInfo.MaxDistance,
                        TTSEnabled = deadmenInfo.TTSEnabled,
                    };

                targets = targets.Concat(deadmen);
            }

            // クエリを実行する
            targets = targets.ToArray();

            lock (this.TargetInfoLock)
            {
                this.targetMobList = targets
                    .Where(x => x.Distance <= x.MaxDistance)
                    .ToList();

                this.TargetInfo = this.targetMobList.FirstOrDefault()?.Combatant;

                if (this.TargetInfo == null)
                {
                    var model = this.Model as MobListModel;
                    if (model != null &&
                        model.MobList.Any())
                    {
                        WPFHelper.BeginInvoke(model.ClearMobList);
                    }
                }
            }

            if (combatants != null)
            {
                CombatantsViewModel.RefreshCombatants(combatants.ToArray());
            }
        }

        protected override NameViewModel NameVM => null;

        protected override HPViewModel HpVM => null;

        protected override HPBarViewModel HpBarVM => null;

        protected override ActionViewModel ActionVM => null;

        protected override DistanceViewModel DistanceVM => null;

        protected override FFLogsViewModel FFLogsVM => null;

        protected override EnmityViewModel EnmityVM => null;

        #region MobList

        protected MobListView mobListView;

        public MobListView MobListView => this.mobListView;

        protected MobListViewModel mobListVM;

        protected MobListViewModel MobListVM =>
            this.mobListVM ?? (this.mobListVM = new MobListViewModel());

        #endregion MobList

        protected override bool IsAllViewOff =>
            !(Settings.Instance?.MobList?.Visible ?? false);

        protected override void CreateViews()
        {
            base.CreateViews();

            this.CreateView<MobListView>(ref this.mobListView, this.MobListVM);
            this.TryAddViewAndViewModel(this.MobListView, this.MobListView?.ViewModel);
        }

        protected override void RefreshModel(
            CombatantEx targetInfo)
        {
            base.RefreshModel(targetInfo);

            // MobListを更新する
            this.RefreshMobListView(targetInfo);
        }

        protected virtual void RefreshMobListView(
            CombatantEx targetInfo)
        {
            if (this.MobListView == null)
            {
                return;
            }

            if (!this.MobListView.ViewModel.OverlayVisible)
            {
                return;
            }

            var model = this.Model as MobListModel;
            if (model == null)
            {
                return;
            }

            // プレイヤー情報を更新する
            var player = CombatantsManager.Instance.Player;
            model.MeX = player?.PosX ?? 0;
            model.MeY = player?.PosY ?? 0;
            model.MeZ = player?.PosZ ?? 0;

            // モブリストを取り出す
            var moblist = default(IReadOnlyList<MobInfo>);
            lock (this.TargetInfoLock)
            {
                moblist = this.targetMobList;

                if (Settings.Instance.MobList.UngroupSameNameMobs)
                {
                    moblist = moblist.OrderBy(y => y.Distance).ToList();
                }
                else
                {
                    moblist = (
                        from x in moblist
                        group x by x.Name into g
                        select g.OrderBy(x => x.Distance).First().Clone((clone =>
                        {
                            clone.DuplicateCount = g.Count();
                        }))).ToList();
                }
            }

            // 表示件数によるフィルタをかけるメソッド
            void SortAndFilterMobList()
            {
                // ソートして表示を設定する
                var i = 0;
                var sorted =
                    from x in model.MobList
                    orderby
                    x.RankSortKey,
                    x.Distance
                    select new
                    {
                        Index = ++i,
                        MobInfo = x,
                    };

                foreach (var item in sorted)
                {
                    Thread.Yield();

                    item.MobInfo.Visible = item.Index <= Settings.Instance.MobList.DisplayCount;
                    item.MobInfo.Index = item.MobInfo.Visible ?
                        item.Index :
                        9999;
                }
            }

            lock (model.MobList)
            {
                try
                {
                    // テストモード？
                    if (Settings.Instance.MobList.TestMode)
                    {
                        if (!model.MobList.Any(x => x.Name.Contains("TEST")))
                        {
                            model.MobList.Clear();

                            foreach (var mob in moblist)
                            {
                                model.MobList.Add(mob);
                            }

                            // ソートと件数のフィルタを行う
                            SortAndFilterMobList();
                        }

                        return;
                    }

                    // この先は本番モード
                    var isChanged = false;
                    var targets = moblist.Where(x => !x.Name.Contains("TEST"));

                    foreach (var mob in targets)
                    {
                        Thread.Yield();

                        var item = model.MobList.FirstOrDefault(x =>
                            x.Combatant?.UniqueObjectID == mob.Combatant?.UniqueObjectID);

                        // 存在しないものは追加する
                        if (item == null)
                        {
                            model.MobList.Add(mob);

                            // TTSで通知する
                            mob.NotifyByTTS();

                            isChanged = true;
                            continue;
                        }

                        var distance = item.Distance;
                        var rankSortKey = item.RankSortKey;

                        // 更新する
                        item.Name = mob.Name;
                        item.Rank = mob.Rank;
                        item.Combatant = mob.Combatant;
                        item.DuplicateCount = mob.DuplicateCount;
                        item.RefreshDistance();

                        if (distance != item.Distance ||
                            rankSortKey != item.RankSortKey)
                        {
                            isChanged = true;
                        }
                    }

                    // 不要になったモブを抽出する
                    var itemsForRemove = model.MobList
                        .Where(x =>
                            !targets.Any(y =>
                                y.Combatant?.UniqueObjectID == x.Combatant?.UniqueObjectID))
                        .ToArray();

                    // 除去する
                    foreach (var item in itemsForRemove)
                    {
                        Thread.Yield();

                        model.MobList.Remove(item);
                        isChanged = true;
                    }

                    // ソートと件数のフィルタを行う
                    if (isChanged)
                    {
                        SortAndFilterMobList();
                    }
                }
                finally
                {
                    model.MobListCount = model.MobList.Count;
                }
            }
        }
    }
}
