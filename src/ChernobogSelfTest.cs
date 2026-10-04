using ArknightsChernobog.Acts;
using ArknightsChernobog.Encounters;
using ArknightsChernobog.Monsters;
using ArknightsChernobog.Powers;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Unlocks;

namespace ArknightsChernobog;

/// <summary>
/// headless 自检：设置环境变量 CHERNOBOG_SELFTEST=1 启动游戏时，在 ModelDb.Init 之后走一遍
/// 选幕、生成房间、战斗背景、休息处和地图底图的加载链路，以及整合运动敌人的模型、Spine 场景与测试遭遇战，
/// 结果以 [ArknightsChernobog][SelfTest] 前缀写日志。
/// 这些路径平时要到开局后才会触发，headless 加载验证覆盖不到。
/// </summary>
internal static class ChernobogSelfTest
{
	private const string Prefix = $"[{ModEntry.ModId}][SelfTest]";
	private static int _failures;

	public static void InstallIfRequested(Harmony harmony)
	{
		if (System.Environment.GetEnvironmentVariable("CHERNOBOG_SELFTEST") != "1")
		{
			return;
		}

		harmony.Patch(
			AccessTools.Method(typeof(ModelDb), nameof(ModelDb.Init)),
			postfix: new HarmonyMethod(typeof(ChernobogSelfTest), nameof(Run)));
		Log.Info($"{Prefix} Armed; will run after ModelDb.Init.");
	}

	private static void Run()
	{
		_failures = 0;
		ChernobogAct canonical = ModelDb.Act<ChernobogAct>();

		Check("ritsulib registration", () =>
		{
			// 幕、战斗池、事件、怪物、能力都经 RitsuLib 注册，id 形如 ARKNIGHTS_CHERNOBOG_<类别>_<类名>；
			// 测试与事件战斗用的遭遇不注册，保持原版按类名得出的 id。[IdMap] 行是旧 id → 新 id，改本地化键时用。
			const string prefix = "ARKNIGHTS_CHERNOBOG_";
			List<(Type Type, string Entry)> registered =
			[
				(typeof(ChernobogAct), canonical.Id.Entry),
				.. ModEntry.ActEncounterTypes.Select(t => (t, ModelDb.GetId(t).Entry)),
				.. ModEntry.ModelTypes<ReunionMonster>().Select(t => (t, ModelDb.GetId(t).Entry)),
				.. ModEntry.ModelTypes<PowerModel>().Select(t => (t, ModelDb.GetId(t).Entry)),
			];
			foreach ((Type type, string entry) in registered)
			{
				Require(entry.StartsWith(prefix), $"{type.Name} not registered through RitsuLib: {entry}");
				Log.Info($"[{ModEntry.ModId}][IdMap] {StringHelper.Slugify(type.Name)} {entry}");
			}

			string testEntry = ModelDb.GetId(typeof(ReunionPatriotTest)).Entry;
			Require(testEntry == "REUNION_PATRIOT_TEST", $"test encounter id {testEntry}");
			return $"{registered.Count} models; act {canonical.Id.Entry}";
		});

		Check("act index group", () =>
		{
			// RitsuLib 按 AllowInRandomActList 过滤这一组：没装 ActLikeIt2 时本幕与蜂巢同组，装了时退出（只经选幕界面出现）。
			IReadOnlyList<ActModel> group = ModelDb.ActsByIndex[ChernobogAct.ActIndex];
			string names = string.Join(", ", group.Select(a => $"{a.Id.Entry}(default={a.IsDefault})"));
			bool expectInGroup = !Integration.ActLikeIt2Bridge.IsLoaded;
			Require(group.Contains(canonical) == expectInGroup && group[0].IsDefault, $"expect in group={expectInGroup}: {names}");
			return $"ActLikeIt2={!expectInGroup}: {names}";
		});

		Check("default list unchanged", () =>
		{
			string names = string.Join(", ", ActModel.GetDefaultList().Select(a => a.Id.Entry));
			Require(!names.Contains(canonical.Id.Entry), names);
			return names;
		});

		// ActLikeIt2 是可选前置（见 ChernobogAct）：装了它，本幕只在第二幕选幕界面出现、不进随机列表；没装时随机列表里有时抽到本幕。
		Check("random list rolls act", () =>
		{
			int hits = 0;
			const int rolls = 200;
			for (ulong seed = 1; seed <= rolls; seed++)
			{
				List<ActModel> acts = ActModel.GetRandomList(new Rng(seed), UnlockState.all, isMultiplayer: true).ToList();
				Require(acts.Count == 3, $"seed {seed} gave {acts.Count} acts");
				if (acts[ChernobogAct.ActIndex] == canonical)
				{
					hits++;
				}
			}

			bool viaFork = Integration.ActLikeIt2Bridge.IsLoaded;
			Require(viaFork ? hits == 0 : hits > 0 && hits < rolls, $"ActLikeIt2={viaFork} hits={hits}");
			return $"ActLikeIt2={viaFork}: act 2 = {canonical.Id.Entry} in {hits}/{rolls} multiplayer rolls";
		});

		Check("ActLikeIt2 fork registration", () =>
		{
			if (!Integration.ActLikeIt2Bridge.IsLoaded)
			{
				return "ActLikeIt2 not loaded (optional); skipped";
			}

			string? description = Integration.ActLikeIt2Bridge.RegisteredOptionDescription();
			Require(description != null, $"{canonical.Id.Entry} not registered for act {Integration.ActLikeIt2Bridge.ActNumber}");
			Require(description!.Length > 0 && !description.Contains(".description"), $"description '{description}'");
			return $"act {Integration.ActLikeIt2Bridge.ActNumber}: {description}";
		});

		Check("generate rooms", () =>
		{
			ActModel act = canonical.ToMutable();
			act.GenerateRooms(new Rng(42), UnlockState.all, isMultiplayer: false);
			Require(act.BossEncounter != null, "no boss");
			return $"boss={act.BossEncounter!.Id.Entry}, ancient={act.Ancient?.Id.Entry}, rooms={act.GetNumberOfRooms(false)}";
		});

		Check("act encounter pools", () =>
		{
			int weak = canonical.AllWeakEncounters.Count();
			int regular = canonical.AllRegularEncounters.Count();
			int elite = canonical.AllEliteEncounters.Count();
			int boss = canonical.AllBossEncounters.Count();
			Require(weak == 4 && regular == 10 && elite == 4 && boss == 1, $"weak={weak} regular={regular} elite={elite} boss={boss}");
			// Boss 固定为爱国者：本幕只有这一场 Boss，发现顺序里也只有它。
			Require(canonical.AllBossEncounters.Single() is ReunionPatriotBoss, canonical.AllBossEncounters.Single().Id.Entry);
			Require(canonical.BossDiscoveryOrder.SequenceEqual(canonical.AllBossEncounters), "boss discovery order");
			return $"weak={weak} regular={regular} elite={elite} boss={canonical.AllBossEncounters.Single().Id.Entry}";
		});

		foreach (EncounterModel encounter in canonical.AllEncounters)
		{
			Check($"act encounter {encounter.Id.Entry}", () =>
			{
				Require(encounter is ReunionEncounter, "not a ReunionEncounter");
				Require(encounter.Title.Exists(), "missing encounters loc title");
				string kind = encounter.RoomType switch { RoomType.Boss => "boss", RoomType.Elite => "elite", _ => encounter.IsWeak ? "weak" : "normal" };
				HashSet<MonsterModel> possible = encounter.AllPossibleMonsters.ToHashSet();
				// 随机阵容：多个种子各抽一次，列出出现过的全部组合；每个组合的起手招应与 docs/tools/opening_sim.py 的输出一致。
				SortedSet<string> lineups = [];
				for (ulong seed = 1; seed <= 40; seed++)
				{
					IReadOnlyList<MonsterModel> monsters = ((ReunionEncounter)encounter).CreateMonsters(new Rng(seed));
					Require(monsters.All(m => m is ReunionMonster), "lineup has non-Reunion monster");
					Require(monsters.All(m => possible.Contains(m.CanonicalInstance)), "rolled a monster missing from AllPossibleMonsters");
					List<string> openings = [];
					foreach (ReunionMonster monster in monsters.Cast<ReunionMonster>())
					{
						monster.SetUpForCombat();
						openings.Add($"{monster.Id.Entry}:{monster.OpeningStateId}");
					}

					// 同一场里的同种怪起手必须错开。
					Require(openings.Count == openings.Distinct().Count(), $"duplicate openings: {string.Join(" + ", openings)}");

					lineups.Add($"hp={monsters.Sum(m => m.MinInitialHp)}-{monsters.Sum(m => m.MaxInitialHp)}: {string.Join(" + ", openings)}");
				}

				string tags = string.Join(",", encounter.Tags.Select(t => (int)t));
				return $"{kind} tags=[{tags}] {string.Join(" | ", lineups)}";
			});
		}

		Check("vertical strike staggers dive", () =>
		{
			// 组长先降落重击，普通突袭战士先起飞、第二回合才落地，两次重击错开。
			IReadOnlyList<MonsterModel> monsters = ModelDb.Encounter<ReunionVerticalStrikeElite>().CreateMonsters(new Rng(1));
			Require(monsters[0] is ReunionGuerrillaAssaulterLeader, $"first is {monsters[0].Id.Entry}");
			string?[] openings = monsters.Cast<ReunionMonster>().Select(m =>
			{
				m.SetUpForCombat();
				return m.OpeningStateId;
			}).ToArray();
			Require(openings[0] == ReunionGuerrillaAssaulterLeader.DiveAssaultMoveId && openings[1] == ReunionGuerrillaAssaulter.TakeOffMoveId,
				string.Join(", ", openings));
			return string.Join(" + ", openings);
		});

		Check("weak/normal escalation tags", () =>
		{
			// 同一群怪的弱怪版与普通版共用标签，原版抽取时就不会前后脚连着遇到。
			(EncounterModel Weak, EncounterModel Normal)[] pairs =
			[
				(ModelDb.Encounter<ReunionHoundPackWeak>(), ModelDb.Encounter<ReunionHoundPackNormal>()),
				(ModelDb.Encounter<ReunionHostStragglersWeak>(), ModelDb.Encounter<ReunionHostHerdNormal>()),
				(ModelDb.Encounter<ReunionParatrooperWeak>(), ModelDb.Encounter<ReunionParatroopersNormal>()),
				(ModelDb.Encounter<ReunionSpecOpsRemnantsWeak>(), ModelDb.Encounter<ReunionSpecOpsTeamNormal>()),
			];
			foreach ((EncounterModel weak, EncounterModel normal) in pairs)
			{
				Require(weak.SharesTagsWith(normal), $"{weak.Id.Entry} / {normal.Id.Entry} share no tag");
			}

			Require(!ModelDb.Encounter<ReunionHoundPackWeak>().SharesTagsWith(ModelDb.Encounter<ReunionHostHerdNormal>()), "unrelated encounters share a tag");
			return $"{pairs.Length} pairs";
		});

		Check("host remnant only in normal herd", () =>
		{
			// 弱怪版牧群关掉残存（不要求群体伤害），普通版保留；开关在怪物加入战斗时才读，这里核对遭遇战生成的可变怪物。
			bool[] weak = ((ReunionEncounter)ModelDb.Encounter<ReunionHostStragglersWeak>()).CreateMonsters(new Rng(1))
				.Cast<ReunionHostMonster>().Select(m => m.HasRemnant).ToArray();
			bool[] normal = ((ReunionEncounter)ModelDb.Encounter<ReunionHostHerdNormal>()).CreateMonsters(new Rng(1))
				.Cast<ReunionHostMonster>().Select(m => m.HasRemnant).ToArray();
			Require(weak.Length == 2 && weak.All(h => !h), $"weak {string.Join(",", weak)}");
			Require(normal.Length == 3 && normal.All(h => h), $"normal {string.Join(",", normal)}");
			// 原型不能被遭遇战改到（ConfigureMonsters 只动可变副本）。
			Require(ModelDb.Monster<ReunionHostSoldier>().HasRemnant, "canonical host soldier lost remnant");
			return $"weak {weak.Length}x off, normal {normal.Length}x on";
		});

		Check("herald summons reinforcements", () =>
		{
			// 照原版卵翼虫：开场先呼叫增援；战旗之后按遭遇战有没有空槽分支到再呼叫或号令。
			ReunionGuerrillaHeraldLeader herald = (ReunionGuerrillaHeraldLeader)ModelDb.Monster<ReunionGuerrillaHeraldLeader>().ToMutable();
			herald.SetUpForCombat();
			Require(herald.OpeningStateId == ReunionGuerrillaHeraldLeader.CallReinforcementsMoveId, $"opening {herald.OpeningStateId}");
			Require(herald.MoveStateMachine!.States.ContainsKey("REINFORCE_BRANCH_STATE"), "no reinforce branch");
			Require(herald.MoveStateMachine.States.ContainsKey(ReunionGuerrillaHeraldLeader.RallyMoveId), "no rally fallback");
			// 战旗叠领袖气质只在组长最后出手时不改变已亮出的意图：原版按槽位顺序重排敌方列表，组长必须占最后一个槽。
			Require(ReunionGuerrillaHeraldLeader.FormationSlots[^1] == ReunionGuerrillaHeraldLeader.HeraldSlot, "herald is not the last slot");

			// 召唤靠遭遇战场景里的同名 Marker2D 定位；缺一个，NCombatRoom.AddCreature 就会在召唤时抛异常。
			List<string> checkedScenes = [];
			foreach (EncounterModel encounter in new EncounterModel[] { ModelDb.Encounter<ReunionReinforcementsNormal>(), ModelDb.Encounter<ReunionGuerrillaHeraldLeaderTest>() })
			{
				Require(encounter.HasScene && encounter.Slots.SequenceEqual(ReunionGuerrillaHeraldLeader.FormationSlots), $"{encounter.Id.Entry} slots");
				Control scene = encounter.CreateScene();
				try
				{
					List<string> missing = encounter.Slots.Where(slot => scene.GetNodeOrNull<Marker2D>(slot) == null).ToList();
					Require(missing.Count == 0, $"{encounter.Id.Entry} scene lacks {string.Join(", ", missing)}");
				}
				finally
				{
					scene.Free();
				}

				IReadOnlyList<(MonsterModel Monster, string? Slot)> lineup = ((ReunionEncounter)encounter).CreateMonstersWithSlots(new Rng(1));
				Require(lineup.All(m => m.Slot != null && encounter.Slots.Contains(m.Slot)), $"{encounter.Id.Entry} lineup slots");
				checkedScenes.Add($"{encounter.Id.Entry}[{string.Join(",", lineup.Select(m => m.Slot))}]");
			}

			return $"opens with {herald.OpeningStateId}; {string.Join("; ", checkedScenes)}";
		});

		Check("combat background", () =>
		{
			// 经 RitsuLib 的 ActAssetProfile 从模组目录挑图层；任何一条路径落回原版命名空间都说明接线失效。
			BackgroundAssets assets = canonical.GenerateBackgroundAssets(new Rng(7));
			Require(assets.BgLayers.Count > 0, "no bg layers");
			foreach (string path in assets.AssetPaths)
			{
				Require(path.StartsWith(ChernobogAssets.Root), $"not in mod namespace: {path}");
				Require(ResourceLoader.Load<PackedScene>(path) != null, $"cannot load {path}");
			}

			Node root = ResourceLoader.Load<PackedScene>(assets.BackgroundScenePath).Instantiate();
			string rootType = root.GetType().Name;
			root.Free();
			return $"{assets.BgLayers.Count} layers, fg={assets.FgLayer}, root={rootType}";
		});

		Check("rest site", () =>
		{
			Require(canonical.RestSiteBackgroundPath.StartsWith(ChernobogAssets.Root), canonical.RestSiteBackgroundPath);
			Node root = ResourceLoader.Load<PackedScene>(canonical.RestSiteBackgroundPath).Instantiate();
			string rootType = root.GetType().Name;
			Require(root is Control, rootType);
			root.Free();
			return $"{canonical.RestSiteBackgroundPath} -> {rootType}";
		});

		Check("map backgrounds", () =>
		{
			string[] paths = [canonical.MapTopBgPath, canonical.MapMidBgPath, canonical.MapBotBgPath];
			foreach (string path in paths)
			{
				Require(path.StartsWith(ChernobogAssets.Root), $"not in mod namespace: {path}");
				Texture2D? texture = ResourceLoader.Load<Texture2D>(path);
				Require(texture != null && texture.GetWidth() > 0, $"cannot load {path}");
			}

			return string.Join(", ", paths);
		});

		Check("map bg color matches paper", () =>
		{
			// 节点着色器和描边光晕都用 MapBgColor，它与纸面平均色不一致时每个节点外面会多一圈色晕。
			Image image = ResourceLoader.Load<Texture2D>(canonical.MapMidBgPath).GetImage();
			if (image.IsCompressed())
			{
				image.Decompress();
			}

			double r = 0, g = 0, b = 0;
			int count = 0;
			for (int y = 0; y < image.GetHeight(); y += 8)
			{
				for (int x = 0; x < image.GetWidth(); x += 8)
				{
					Color pixel = image.GetPixel(x, y);
					if (pixel.A < 0.8f)
					{
						continue;
					}

					r += pixel.R;
					g += pixel.G;
					b += pixel.B;
					count++;
				}
			}

			Color paper = new((float)(r / count), (float)(g / count), (float)(b / count));
			Color bg = canonical.MapBgColor;
			float diff = Math.Max(Math.Abs(paper.R - bg.R), Math.Max(Math.Abs(paper.G - bg.G), Math.Abs(paper.B - bg.B)));
			Require(diff <= 8f / 255f, $"paper #{paper.ToHtml(false)} vs MapBgColor #{bg.ToHtml(false)}");
			return $"paper #{paper.ToHtml(false)} ~ MapBgColor #{bg.ToHtml(false)}";
		});

		foreach (Type powerType in ModEntry.ModelTypes<PowerModel>())
		{
			Check($"power {powerType.Name}", () =>
			{
				PowerModel power = ModelDb.GetById<PowerModel>(ModelDb.GetId(powerType));
				Require(power.Title.Exists() && power.HasSmartDescription, "missing powers loc");
				// 图标经 RitsuLib 的 PowerAssetProfile 指到模组目录；路径不存在时 RitsuLib 退回原版加载器，得到 NOPE 占位而不是 null。
				string expected = ((STS2RitsuLib.Scaffolding.Content.ModPowerTemplate)power).AssetProfile.BigIconPath!;
				Require(power.ResolvedBigIconPath == expected, $"big icon {power.ResolvedBigIconPath}, expected {expected}");
				Texture2D icon = power.Icon;
				Require(icon.GetWidth() == 256, $"icon {icon.ResourcePath} {icon.GetWidth()}px (NOPE placeholder?)");
				return $"{power.Id.Entry}: title={power.Title.GetFormattedText()}, icon {icon.GetWidth()}px";
			});
		}

		foreach (Type hostType in ReunionMonsterSelfTest.MonsterTypes.Where(t => t.IsSubclassOf(typeof(ReunionHostMonster))))
		{
			Check($"host revive chain {hostType.Name}", () =>
			{
				// 残存把倒地的宿主切到 DownedState，之后必须经过“重新站起”再回到正常循环的某一招。
				ReunionHostMonster host = (ReunionHostMonster)ModelDb.GetById<MonsterModel>(ModelDb.GetId(hostType)).ToMutable();
				host.SetUpForCombat();
				MoveState downed = host.DownedState;
				Require(downed.Id == ReunionHostMonster.DownedMoveId, downed.Id);
				MoveState? revive = downed.FollowUpState as MoveState;
				Require(revive?.Id == ReunionHostMonster.ReviveMoveId, $"downed -> {revive?.Id}");
				string resume = revive!.FollowUpState?.Id ?? "(none)";
				Require(resume is not (ReunionHostMonster.DownedMoveId or ReunionHostMonster.ReviveMoveId), $"revive -> {resume}");
				return $"DOWNED -> REVIVE -> {resume}";
			});
		}

		Check("random move machines", () =>
		{
			// 随机出招的怪：每招之后回到同一个随机分支，分支恰好覆盖这几招且都不能连用；与 docs/tools/opening_sim.py 的 RANDOM 对应。
			Type[] randomTypes =
			[
				typeof(ReunionGuerrillaHound), typeof(ReunionHostSoldier), typeof(ReunionHostScavenger),
				typeof(ReunionHostWanderer), typeof(ReunionRagingHostSoldier), typeof(ReunionRagingHostThrower),
			];
			List<string> report = [];
			foreach (Type type in randomTypes)
			{
				MonsterModel monster = ModelDb.GetById<MonsterModel>(ModelDb.GetId(type)).ToMutable();
				monster.SetUpForCombat();
				RandomBranchState? loop = monster.MoveStateMachine!.States.Values.OfType<RandomBranchState>().SingleOrDefault();
				Require(loop != null, $"{type.Name}: no random branch");
				List<MoveState> moves = monster.MoveStateMachine.States.Values.OfType<MoveState>()
					.Where(m => m.Id is not (ReunionHostMonster.DownedMoveId or ReunionHostMonster.ReviveMoveId)).ToList();
				Require(moves.All(m => m.FollowUpState == loop), $"{type.Name}: a move does not return to the branch");
				Require(loop!.States.Select(s => s.stateId).OrderBy(id => id).SequenceEqual(moves.Select(m => m.Id).OrderBy(id => id)),
					$"{type.Name}: branch covers {string.Join(",", loop.States.Select(s => s.stateId))}");
				Require(loop.States.All(s => s.repeatType == MoveRepeatType.CannotRepeat), $"{type.Name}: repeat rule");
				report.Add($"{type.Name}[{string.Join("/", moves.Select(m => m.Id))}]");
			}

			return string.Join(" ", report);
		});

		Check("act events", () =>
		{
			// 本幕事件经 RitsuLib 按幕注册；事件池必须恰好包含它们各一次，且标题、立绘都能取到。
			List<EventModel> pool = ModelDb.Act<ChernobogAct>().AllEvents.ToList();
			List<string> ids = pool.Select(e => e.Id.Entry).ToList();
			Require(ids.Distinct().Count() == ids.Count, $"duplicate events: {string.Join(",", ids)}");
			foreach (Type type in ModEntry.ActEventTypes)
			{
				EventModel? model = pool.FirstOrDefault(e => e.GetType() == type);
				Require(model != null, $"{type.Name} missing from act pool [{string.Join(",", ids)}]");
				Require(model!.Title.Exists(), $"{model.Id.Entry} missing events loc title");
				string? portrait = (model as Events.ReunionEventModel)?.CustomInitialPortraitPath;
				Require(portrait != null && ResourceLoader.Load<Texture2D>(portrait) != null, $"{model.Id.Entry} portrait {portrait}");
			}

			CardModel oripathy = ModelDb.Card<Cards.Oripathy>();
			Require(oripathy.TitleLocString.Exists(), $"{oripathy.Id.Entry} missing cards loc title");
			Require(ResourceLoader.Load<Texture2D>(oripathy.PortraitPath) != null, $"oripathy portrait {oripathy.PortraitPath}");
			Require(ModelDb.CardPool<MegaCrit.Sts2.Core.Models.CardPools.CurseCardPool>().AllCards.Contains(oripathy), "oripathy not in curse pool");
			Require(ModelDb.Encounter<ReunionSupplyAmbushEvent>().Title.Exists(), "ambush encounter loc title");
			return $"{string.Join(",", ids)}; curse {oripathy.Id.Entry}";
		});

		Check("patriot boss", () =>
		{
			// 地图节点用静态占位图：剪影与描边都要能按原版路径加载。
			EncounterModel boss = ModelDb.Encounter<ReunionPatriotBoss>();
			Require(boss.BossNodeSpineResource == null, "unexpected boss node spine");
			foreach (string path in new[] { boss.BossNodePath + ".png", boss.BossNodePath + "_outline.png" })
			{
				Require(ResourceLoader.Load<Texture2D>(path) is { } tex && tex.GetWidth() == 352, $"cannot load {path}");
			}
			// 对局历史按模型 id 取 Boss 头像，缺图时原版只报加载错误、历史里留空。
			foreach (string? path in new[]
			{
				ImageHelper.GetRoomIconPath(MapPointType.Boss, RoomType.Boss, boss.Id),
				ImageHelper.GetRoomIconOutlinePath(MapPointType.Boss, RoomType.Boss, boss.Id),
			})
			{
				Require(path != null && ResourceLoader.Load<Texture2D>(path) is { } tex && tex.GetWidth() == 88, $"cannot load run history icon {path}");
			}
			Require(boss.BossNodePath.StartsWith(ChernobogAssets.Root) && ImageHelper.GetRoomIconPath(MapPointType.Boss, RoomType.Boss, boss.Id)!.StartsWith(ChernobogAssets.Root),
				"boss node / run history icon not routed to the mod namespace");
			// 专属背景由 RitsuLib 按 AssetProfile 的背景根场景 + 图层目录挂上；目录打不开时它会退回幕背景，所以逐个核对。
			var bossProfile = ((ReunionPatriotBoss)boss).AssetProfile;
			Require(ResourceLoader.Load<PackedScene>(bossProfile.BackgroundScenePath!) != null, $"cannot load {bossProfile.BackgroundScenePath}");
			List<string> bossLayers = DirAccess.GetFilesAt(bossProfile.BackgroundLayersDirectoryPath!).Select(f => f.Replace(".remap", "")).Distinct().ToList();
			Require(bossLayers.Count == 1 && bossLayers[0].Contains("_bg_00_"), $"boss bg layers [{string.Join(",", bossLayers)}]");
			Require(ResourceLoader.Load<PackedScene>($"{bossProfile.BackgroundLayersDirectoryPath}/{bossLayers[0]}") != null, "cannot load boss bg layer");
			// Boss 音乐由 FMOD 读原文件（导入设为 keep），包里必须有原始 mp3。
			foreach (string track in new[] { Audio.ReunionBossMusic.PhaseOneTrackPath, Audio.ReunionBossMusic.PhaseTwoTrackPath })
			{
				Require(Godot.FileAccess.FileExists(track), $"boss music raw file missing {track}");
			}

			// 站位靠场景里的同名 Marker2D；重生召唤的空降兵要在 AllPossibleMonsters 里才会随房间预载。
			Require(boss.HasScene && boss.Slots.SequenceEqual(ReunionPatriot.FormationSlots), "boss slots");
			Control bossScene = boss.CreateScene();
			try
			{
				List<string> missing = boss.Slots.Where(slot => bossScene.GetNodeOrNull<Marker2D>(slot) == null).ToList();
				Require(missing.Count == 0, $"boss scene lacks {string.Join(", ", missing)}");
			}
			finally
			{
				bossScene.Free();
			}
			IReadOnlyList<(MonsterModel Monster, string? Slot)> bossLineup = ((ReunionEncounter)boss).CreateMonstersWithSlots(new Rng(1));
			Require(bossLineup.Select(m => m.Slot).SequenceEqual(ReunionPatriot.FormationSlots), "boss lineup slots");
			// 原版预载只看实际怪物与 ExtraAssetPaths，重生才召唤的空降兵场景必须在 ExtraAssetPaths 里。
			Require(boss.AllPossibleMonsters.Contains(ModelDb.Monster<ReunionGuerrillaAssaulter>()), "paratrooper missing from AllPossibleMonsters");
			Require(bossProfile.ExtraAssetPaths?.Contains(ModelDb.Monster<ReunionGuerrillaAssaulter>().CreatureScenePath) == true, "paratrooper scene not preloaded");
			// 重生补位的空降兵落地入场，第一招起飞；普通空降兵仍先降落重击。
			ReunionGuerrillaAssaulter dropper = (ReunionGuerrillaAssaulter)ModelDb.Monster<ReunionGuerrillaAssaulter>().ToMutable();
			dropper.DropsIn = true;
			dropper.SetUpForCombat();
			Require(dropper.OpeningStateId == ReunionGuerrillaAssaulter.TakeOffMoveId, $"drop-in opening {dropper.OpeningStateId}");

			// 一阶段 行军 → 长戟四连 → 盾击；不屈把下一招强制成重生，重生后进入二阶段循环。
			ReunionPatriot patriot = (ReunionPatriot)ModelDb.Monster<ReunionPatriot>().ToMutable();
			patriot.SetUpForCombat();
			Require(patriot.OpeningStateId == ReunionPatriot.MarchMoveId, $"opening {patriot.OpeningStateId}");
			Require(patriot.MoveStateMachine!.States.TryGetValue(ReunionPatriot.RebirthMoveId, out MonsterState? rebirth), "no rebirth state");
			string next = ((MoveState)rebirth!).FollowUpState?.Id ?? "(none)";
			Require(next == ReunionPatriot.SweepMoveId, $"rebirth -> {next}");
			return $"node {boss.BossNodePath}; MARCH ... REBIRTH -> {next}";
		});

		Check("arts master takeoff chain", () =>
		{
			// 坠落后的击晕以起飞为下一招（按 id 查状态），起飞之后回到法术射线。
			ReunionArtsMaster drone = (ReunionArtsMaster)ModelDb.Monster<ReunionArtsMaster>().ToMutable();
			drone.SetUpForCombat();
			Require(drone.MoveStateMachine!.States.TryGetValue(ReunionArtsMaster.TakeoffMoveId, out MonsterState? state), "no take-off state");
			MoveState takeoff = (MoveState)state!;
			Require(takeoff.FollowUpState?.Id == ReunionArtsMaster.ArtsBeamMoveId, $"take-off -> {takeoff.FollowUpState?.Id}");
			return "STUNNED -> TAKE_OFF -> ARTS_BEAM";
		});

		Check("chest spine", () =>
		{
			Require(ResourceLoader.Exists(canonical.ChestSpineResourcePath), canonical.ChestSpineResourcePath);
			return canonical.ChestSpineResourcePath;
		});

		foreach (Type monster in ReunionMonsterSelfTest.MonsterTypes)
		{
			Check($"monster {monster.Name}", () => ReunionMonsterSelfTest.CheckMonster(monster));
		}

		foreach (Type encounter in ReunionMonsterSelfTest.EncounterTypes)
		{
			Check($"test encounter {encounter.Name}", () => ReunionMonsterSelfTest.CheckEncounter(encounter));
		}

		Log.Info($"{Prefix} Finished with {_failures} failure(s).");
	}

	private static void Check(string name, Func<string> body)
	{
		try
		{
			Log.Info($"{Prefix} PASS {name}: {body()}");
		}
		catch (Exception ex)
		{
			_failures++;
			Log.Error($"{Prefix} FAIL {name}: {ex.GetType().Name}: {ex.Message}");
		}
	}

	private static void Require(bool condition, string detail)
	{
		if (!condition)
		{
			throw new InvalidOperationException(detail);
		}
	}
}
