using ArknightsChernobog.Monsters;
using STS2RitsuLib.Scaffolding.Content;
using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Encounters;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;

namespace ArknightsChernobog.Encounters;

// 苦难摇篮的战斗池，编组照原版蜂巢：一场只围绕一种兵（单体招牌、同种成群、头目带同族、召唤型），
// 或者一对有明确机制关系的兵（盾卫掩护炮手、术师献祭强化战士）；同一群怪有弱怪版和普通版，打同一个遭遇标签。
// 名字取自第七章关卡与剧情，不再按关卡出兵表凑队。萨卡兹雇佣军是游击队萨卡兹的换皮，不进池。
// 数值跟着遭遇走（同一兵种只出现在一两场），组成、总血量与对照原版的难度见 docs/战斗设计.md。
// 起手偏移（SetOpenings）由 docs/tools/opening_sim.py --search 挑出：对齐原版蜂巢同类型遭遇战的平均开局伤害
// （docs/tools/vanilla_hive.py），不是对齐原版的最高值；改阵容或数值后要重跑，并同步模拟器里的 OPENINGS。
// 同一场里的同种怪起手一律错开（模拟器里是硬约束），战斗中召来的增援也按同一原则挑起手。

// ---- 弱怪 ----

/// <summary>猎犬 ×2：游击队的先头猎犬，一只扑咬一只撕咬起手、之后随机出招（普通版见 <see cref="ReunionHoundPackNormal"/>）。</summary>
public sealed class ReunionHoundPackWeak : ReunionEncounter
{
	public override RoomType RoomType => RoomType.Monster;

	public override bool IsWeak => true;

	public override IEnumerable<EncounterTag> Tags => [ReunionEncounterTags.Hounds];

	protected override IReadOnlyList<MonsterModel> Lineup => [M<ReunionGuerrillaHound>(), M<ReunionGuerrillaHound>()];

	protected override void ConfigureMonsters(IReadOnlyList<MonsterModel> monsters) => SetOpenings(monsters, 0, 1);
}

/// <summary>
/// 7-3 变节之刃：失控的牧群，宿主士兵 ×2（一劈砍一溃烂撕咬起手、之后随机出招），只带永续再生、不带残存：
/// 弱怪战在第二幕最先遇到，不该要求群体伤害（普通版见 <see cref="ReunionHostHerdNormal"/>）。
/// </summary>
public sealed class ReunionHostStragglersWeak : ReunionEncounter
{
	public override RoomType RoomType => RoomType.Monster;

	public override bool IsWeak => true;

	public override IEnumerable<EncounterTag> Tags => [ReunionEncounterTags.Hosts];

	protected override IReadOnlyList<MonsterModel> Lineup => [M<ReunionHostSoldier>(), M<ReunionHostSoldier>()];

	protected override void ConfigureMonsters(IReadOnlyList<MonsterModel> monsters)
	{
		SetOpenings(monsters, 0, 2);
		foreach (ReunionHostMonster host in monsters.OfType<ReunionHostMonster>())
		{
			host.HasRemnant = false;
		}
	}
}

/// <summary>单个突袭战士：起飞 ↔ 降落重击（普通版见 <see cref="ReunionParatroopersNormal"/>）。</summary>
public sealed class ReunionParatrooperWeak : ReunionEncounter
{
	public override RoomType RoomType => RoomType.Monster;

	public override bool IsWeak => true;

	public override IEnumerable<EncounterTag> Tags => [ReunionEncounterTags.Paratroopers];

	protected override IReadOnlyList<MonsterModel> Lineup => [M<ReunionGuerrillaAssaulter>()];
}

/// <summary>7-2 别离之夜：龙门街头的整合运动残党，特战士兵先隐蔽，法术大师A1 开火（普通版见 <see cref="ReunionSpecOpsTeamNormal"/>）。</summary>
public sealed class ReunionSpecOpsRemnantsWeak : ReunionEncounter
{
	public override RoomType RoomType => RoomType.Monster;

	public override bool IsWeak => true;

	public override IEnumerable<EncounterTag> Tags => [ReunionEncounterTags.SpecOps];

	protected override IReadOnlyList<MonsterModel> Lineup => [M<ReunionSpecOpsSoldier>(), M<ReunionArtsMaster>()];

	protected override void ConfigureMonsters(IReadOnlyList<MonsterModel> monsters) => SetOpenings(monsters, 2, 0);
}

// ---- 普通 ----

/// <summary>
/// 猎犬群：猎犬pro 带两只猎犬（猎犬pro 首回合嚎叫强化自己；两只猎犬一撕咬一环伺起手，之后随机出招）。
/// 猎犬pro 站最右、最后出手：出手顺序就是站位顺序。嚎叫原来给全体加力量，它若先出手，两只猎犬本回合已亮出的意图会在出手前涨伤害，
/// 玩家按意图算的伤害就不准了；现在只加自己，站最右仍保留。
/// </summary>
public sealed class ReunionHoundPackNormal : ReunionEncounter
{
	public override RoomType RoomType => RoomType.Monster;

	public override IEnumerable<EncounterTag> Tags => [ReunionEncounterTags.Hounds];

	protected override IReadOnlyList<MonsterModel> Lineup =>
		[M<ReunionGuerrillaHound>(), M<ReunionGuerrillaHound>(), M<ReunionGuerrillaHoundPro>()];

	protected override void ConfigureMonsters(IReadOnlyList<MonsterModel> monsters) => SetOpenings(monsters, 1, 2, 2);
}

/// <summary>
/// 7-2 别离之夜：梅菲斯特的牧群。宿主士兵组长固定，再从宿主士兵、拾荒者、流浪者里不重复地抽两只（照原版碗虫）。
/// 组长先牧群号令（弃牌堆塞晕眩），拾荒者、流浪者先撕扯、嘶吼，宿主士兵先劈砍，之后随机出招；
/// 残存在这里生效（其他宿主活着就会站起来）。
/// </summary>
public sealed class ReunionHostHerdNormal : ReunionEncounter
{
	private static readonly MonsterModel[] Workers =
		[M<ReunionHostSoldier>(), M<ReunionHostScavenger>(), M<ReunionHostWanderer>()];

	public override RoomType RoomType => RoomType.Monster;

	public override IEnumerable<EncounterTag> Tags => [ReunionEncounterTags.Hosts];

	protected override IEnumerable<MonsterModel> Candidates => [M<ReunionHostSoldierLeader>(), .. Workers];

	protected override IReadOnlyList<MonsterModel> RollLineup(Rng rng)
	{
		List<MonsterModel> pool = [.. Workers];
		MonsterModel first = rng.NextItem(pool)!;
		pool.Remove(first);
		MonsterModel second = rng.NextItem(pool)!;
		// 站位按招牌顺序（士兵、拾荒者、流浪者）排，不按抽取顺序，免得同组合站位忽左忽右。
		return [M<ReunionHostSoldierLeader>(), .. Workers.Where(w => w == first || w == second)];
	}

	protected override void ConfigureMonsters(IReadOnlyList<MonsterModel> monsters) =>
		SetOpeningsByType(monsters,
			(typeof(ReunionHostSoldierLeader), 1), (typeof(ReunionHostScavenger), 1), (typeof(ReunionHostWanderer), 1));
}

/// <summary>7-3 变节之刃：狂暴宿主单体，士兵或投掷手随机一个（士兵先撕裂）。高血高瓦解，以防御为主撑到它自己倒下。</summary>
public sealed class ReunionRagingHostNormal : ReunionEncounter
{
	public override RoomType RoomType => RoomType.Monster;

	protected override IEnumerable<MonsterModel> Candidates => [M<ReunionRagingHostSoldier>(), M<ReunionRagingHostThrower>()];

	protected override IReadOnlyList<MonsterModel> RollLineup(Rng rng) => [rng.NextItem(Candidates)!];

	protected override void ConfigureMonsters(IReadOnlyList<MonsterModel> monsters) =>
		SetOpeningsByType(monsters, (typeof(ReunionRagingHostSoldier), 1));
}

/// <summary>7-2 别离之夜：特战士兵 + 特战术师 + 法术大师A1（特战士兵先隐蔽、特战术师先源石屏障）。</summary>
public sealed class ReunionSpecOpsTeamNormal : ReunionEncounter
{
	public override RoomType RoomType => RoomType.Monster;

	public override IEnumerable<EncounterTag> Tags => [ReunionEncounterTags.SpecOps];

	protected override IReadOnlyList<MonsterModel> Lineup =>
		[M<ReunionSpecOpsSoldier>(), M<ReunionSpecOpsCaster>(), M<ReunionArtsMaster>()];

	protected override void ConfigureMonsters(IReadOnlyList<MonsterModel> monsters) => SetOpenings(monsters, 2, 2, 0);
}

/// <summary>7-4/7-5 并肩之约：游击队突击组，战士组长带两名战士，督战给玩家脆弱（组长先督战，两名战士分别从劈砍、压制起手）。</summary>
public sealed class ReunionAssaultSquadNormal : ReunionEncounter
{
	public override RoomType RoomType => RoomType.Monster;

	protected override IReadOnlyList<MonsterModel> Lineup =>
		[M<ReunionGuerrillaFighterLeader>(), M<ReunionGuerrillaFighter>(), M<ReunionGuerrillaFighter>()];

	protected override void ConfigureMonsters(IReadOnlyList<MonsterModel> monsters) => SetOpenings(monsters, 2, 0, 2);
}

/// <summary>
/// 7-5 并肩之约：狙击阵地，狙击手组长带两名狙击手，标定目标给玩家易伤。组长站最右、最后出手并先标定目标：
/// 新挂到玩家身上的易伤跳过第一次掉层，第 1 回合挂上后整个第 2 回合都吃加成，所以第一名狙击手从第二发狙击起手、
/// 第 2 回合轮到瞄准，避开同回合三发都吃易伤；第二名狙击手先瞄准。
/// </summary>
public sealed class ReunionSniperNestNormal : ReunionEncounter
{
	public override RoomType RoomType => RoomType.Monster;

	protected override IReadOnlyList<MonsterModel> Lineup =>
		[M<ReunionGuerrillaSniper>(), M<ReunionGuerrillaSniper>(), M<ReunionGuerrillaSniperLeader>()];

	protected override void ConfigureMonsters(IReadOnlyList<MonsterModel> monsters) => SetOpenings(monsters, 1, 2, 1);
}

/// <summary>7-13/7-14 炮击阵地：盾卫掩护迫击炮兵（照原版活体盾牌 + 高塔炮手），不先拆盾卫炮兵每回合都有格挡（盾卫先盾击，炮兵先装填）。</summary>
public sealed class ReunionMortarPositionNormal : ReunionEncounter
{
	public override RoomType RoomType => RoomType.Monster;

	protected override IReadOnlyList<MonsterModel> Lineup =>
		[M<ReunionGuerrillaShieldGuard>(), M<ReunionGuerrillaMortarGunner>()];

	protected override void ConfigureMonsters(IReadOnlyList<MonsterModel> monsters) => SetOpenings(monsters, 1, 0);
}

/// <summary>7-11/7-12 浸染：萨卡兹战士 + 萨卡兹术师，各自献祭、强化自己（战士先仪式强化，术师先源石洪流）。</summary>
public sealed class ReunionInfectionNormal : ReunionEncounter
{
	public override RoomType RoomType => RoomType.Monster;

	protected override IReadOnlyList<MonsterModel> Lineup =>
		[M<ReunionGuerrillaSarkazWarrior>(), M<ReunionGuerrillaSarkazCaster>()];

	protected override void ConfigureMonsters(IReadOnlyList<MonsterModel> monsters) => SetOpenings(monsters, 2, 2);
}

/// <summary>7-15 游击-2：空降小队，突袭战士 ×2，第二名先起飞，两次降落重击错开。</summary>
public sealed class ReunionParatroopersNormal : ReunionEncounter
{
	public override RoomType RoomType => RoomType.Monster;

	public override IEnumerable<EncounterTag> Tags => [ReunionEncounterTags.Paratroopers];

	protected override IReadOnlyList<MonsterModel> Lineup => [M<ReunionGuerrillaAssaulter>(), M<ReunionGuerrillaAssaulter>()];

	protected override void ConfigureMonsters(IReadOnlyList<MonsterModel> monsters) => SetOpenings(monsters, 0, 1);
}

/// <summary>
/// 7-6 遗忘之地：增援信号。传令兵组长带一名战士，开场呼叫增援，之后把战士召进预留的空位（照原版卵翼虫）；
/// 战士不挂爪牙：击倒传令兵组长后增援仍留在场上，要全部打完。
/// 槽位由前到后：战士、两个增援空位、传令兵组长；坐标按原版 PositionEnemies 对四个单位排开的结果，站满时与自动排开一致。
/// 前排战士倒下后空出的槽位也会被下一次增援填上。
/// </summary>
public sealed class ReunionReinforcementsNormal : ReunionEncounter
{
	public override RoomType RoomType => RoomType.Monster;

	public override EncounterAssetProfile AssetProfile { get; } = new(EncounterScenePath: ChernobogAssets.EncounterScene("reunion_reinforcements_normal"));

	public override IReadOnlyList<string> Slots => ReunionGuerrillaHeraldLeader.FormationSlots;

	protected override IReadOnlyList<MonsterModel> Lineup =>
		[M<ReunionGuerrillaFighter>(), M<ReunionGuerrillaHeraldLeader>()];

	protected override IReadOnlyList<string> LineupSlots =>
		[ReunionGuerrillaHeraldLeader.FrontSlot, ReunionGuerrillaHeraldLeader.HeraldSlot];

	// 前排战士先压制（传令兵组长固定先呼叫增援）。
	protected override void ConfigureMonsters(IReadOnlyList<MonsterModel> monsters) => SetOpenings(monsters, 2, 0);
}

// ---- 事件战斗（不进任何战斗池，只由事件发起） ----

/// <summary>冻土补给箱撬开铁箱后被引来的猎犬：两只猎犬，起手错开，强度同弱怪“猎犬群”。</summary>
public sealed class ReunionSupplyAmbushEvent : ReunionEncounter
{
	public override RoomType RoomType => RoomType.Monster;

	protected override IReadOnlyList<MonsterModel> Lineup => [M<ReunionGuerrillaHound>(), M<ReunionGuerrillaHound>()];

	protected override void ConfigureMonsters(IReadOnlyList<MonsterModel> monsters) => SetOpenings(monsters, 0, 1);
}

// ---- 精英 ----

/// <summary>7-13/7-17 感染者之盾：盾卫组长掩护迫击炮兵组长（盾卫组长先盾墙，炮兵组长先炮击）。</summary>
public sealed class ReunionShieldOfInfectedElite : ReunionEncounter
{
	public override RoomType RoomType => RoomType.Elite;

	protected override IReadOnlyList<MonsterModel> Lineup =>
		[M<ReunionGuerrillaShieldGuardLeader>(), M<ReunionGuerrillaMortarGunnerLeader>()];

	protected override void ConfigureMonsters(IReadOnlyList<MonsterModel> monsters) => SetOpenings(monsters, 2, 1);
}

/// <summary>7-15 游击-2：垂直打击，突袭战士组长先降落重击，突袭战士先起飞，两次重击错开。</summary>
public sealed class ReunionVerticalStrikeElite : ReunionEncounter
{
	public override RoomType RoomType => RoomType.Elite;

	protected override IReadOnlyList<MonsterModel> Lineup =>
		[M<ReunionGuerrillaAssaulterLeader>(), M<ReunionGuerrillaAssaulter>()];

	protected override void ConfigureMonsters(IReadOnlyList<MonsterModel> monsters) => SetOpenings(monsters, 0, 1);
}

/// <summary>7-17 感染者之盾-2 / H7-3：萨卡兹战士组长 + 萨卡兹术师组长的仪式（战士组长先仪式强化，术师组长先源石洪流）。</summary>
public sealed class ReunionSarkazRitualElite : ReunionEncounter
{
	public override RoomType RoomType => RoomType.Elite;

	protected override IReadOnlyList<MonsterModel> Lineup =>
		[M<ReunionGuerrillaSarkazWarriorLeader>(), M<ReunionGuerrillaSarkazCasterLeader>()];

	protected override void ConfigureMonsters(IReadOnlyList<MonsterModel> monsters) => SetOpenings(monsters, 2, 2);
}

/// <summary>7-3 变节之刃：狂暴宿主组长单体，开局就处决；血最厚、瓦解最高，防住就赢。</summary>
public sealed class ReunionRagingHostLeaderElite : ReunionEncounter
{
	public override RoomType RoomType => RoomType.Elite;

	protected override IReadOnlyList<MonsterModel> Lineup => [M<ReunionRagingHostLeader>()];

	protected override void ConfigureMonsters(IReadOnlyList<MonsterModel> monsters) => SetOpenings(monsters, 2);
}

// ---- Boss ----

/// <summary>
/// 7-18 爱国者之死：爱国者带两名游击队战士（“盾”的护卫）。护卫倒下会激怒爱国者（旧部之誓），
/// 爱国者一阶段倒下后重生为毁灭姿态，有护卫阵亡时空投一名空降兵补位；护卫不挂爪牙，爱国者倒下后仍要打完。
/// 两名战士错开起手，爱国者先行军披甲；前四回合伤害按原版第二幕 Boss 的区间校过（docs/tools/opening_sim.py）。
/// </summary>
public sealed class ReunionPatriotBoss : ReunionEncounter
{
	public override RoomType RoomType => RoomType.Boss;

	// 地图节点用静态占位图（照原版知识恶魔）：原版在 BossNodePath 后拼 .png（剪影）与 _outline.png（描边）。
	public override string BossNodePath => ChernobogAssets.PatriotBossNodePrefix;

	public override MegaSkeletonDataResource? BossNodeSpineResource => null;

	// 专属战场背景：不用幕背景，改用 AssetProfile 里的背景根场景与图层目录（一整张 3072×1440 的画，照顾下面拉远的镜头）。
	protected override bool UseActCombatBackground => false;

	// 原版进房间只预载实际生成的怪物与额外路径（不看 AllPossibleMonsters），重生时才召唤的空降兵场景要列进额外路径，
	// 否则召唤时报 “Asset not cached” 并同步加载卡一下（实机踩过）；它的意图图标爱国者都用过。
	// Boss 音乐是 FMOD 直接读的原文件（不经 Godot 资源加载），不能列进预载。
	public override EncounterAssetProfile AssetProfile =>
		ChernobogAssets.PatriotBossProfile([ModelDb.Monster<ReunionGuerrillaAssaulter>().CreatureScenePath]);

	// 镜头拉远照原版三体 Boss（TheKinBoss 0.85）。场景容器以屏幕中心为轴缩放，背景也在容器里：
	// 专属背景按“0.85、下移 30”构图（脚底线、可见范围见 art/codex/make_boss_bg_refs.py），改这两个数要一起重画背景。
	public override float GetCameraScaling() => 0.85f;

	public override Vector2 GetCameraOffset() => Vector2.Down * 30f;

	// 按场景标记点站位（照原版卵翼虫，场景在 AssetProfile 里）：重生时补位的空降兵要落在阵亡护卫的槽位上。
	public override IReadOnlyList<string> Slots => ReunionPatriot.FormationSlots;

	protected override IReadOnlyList<MonsterModel> Lineup =>
		[M<ReunionGuerrillaFighter>(), M<ReunionGuerrillaFighter>(), M<ReunionPatriot>()];

	protected override IReadOnlyList<string> LineupSlots => ReunionPatriot.FormationSlots;

	// 空降兵只在重生时召唤，列进 AllPossibleMonsters 供图鉴与自检；预载靠 AssetProfile 的额外路径。
	protected override IEnumerable<MonsterModel> Candidates => [.. Lineup, M<ReunionGuerrillaAssaulter>()];

	protected override void ConfigureMonsters(IReadOnlyList<MonsterModel> monsters) => SetOpenings(monsters, 0, 2, 0);
}
