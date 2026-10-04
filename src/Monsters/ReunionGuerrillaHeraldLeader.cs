using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace ArknightsChernobog.Monsters;

/// <summary>
/// 游击队传令兵组长（PRTS enemy_1080_sotidp_2），传令兵的头目版。PRTS 定位：在场时强化所有敌军的攻击力与防御力。
/// 与本体同一套骨骼和动画（附件网格随贴图重新打包，skel 单独转换）：Idle / Attack（OnAttack 0.4s）/ Die；Move 未用。
/// 招式与数值见 docs/战斗设计.md：召唤型（照原版卵翼虫）。开场原版“领袖气质”；呼叫增援（召来一名游击队战士）→ 殴打 → 战旗
/// （给自己再叠领袖气质，加的是其他盟友的攻击；它站最后一个槽、最后出手，叠上时同伴本回合都出过手，不会让已亮出的意图涨伤害。
/// 召来的增援也在它之前出手：原版 CombatManager.AddCreature 按遭遇战槽位重排敌方列表，增援槽排在 <see cref="HeraldSlot"/> 前面，
/// 所以 <see cref="FormationSlots"/> 必须以 <see cref="HeraldSlot"/> 结尾）
/// → 遭遇战还有空槽时再呼叫增援，没有了改为号令（全体格挡）。
/// 站位也照卵翼虫：遭遇战用 <see cref="FormationSlots"/> 预留槽位（场景里有同名 Marker2D），召唤进
/// EncounterModel.GetNextSlot 给出的第一个空槽，已有怪物不移动；遭遇战没有槽位时不召唤。召来的战士与场上战士错开起手（见 <see cref="RecruitOpening"/>）。
/// 战士（开场的和召来的）不挂“爪牙”：传令兵组长倒下后它们留在场上，要全部打完才结束战斗。
/// 领袖气质随持有者死亡由原版移除，不需要自己清理。
/// </summary>
public sealed class ReunionGuerrillaHeraldLeader : ReunionMonster
{
	public const string CallReinforcementsMoveId = "CALL_REINFORCEMENTS_MOVE";
	public const string StrikeMoveId = "STRIKE_MOVE";
	public const string RallyMoveId = "RALLY_MOVE";
	public const string BannerMoveId = "BANNER_MOVE";

	public const string FrontSlot = "fighter";
	public const string HeraldSlot = "herald";

	/// <summary>带传令兵组长的遭遇战共用的槽位，由前到后；两个增援槽开场空着。GetNextSlot 按这个顺序找空槽。</summary>
	public static readonly IReadOnlyList<string> FormationSlots = [FrontSlot, "recruit1", "recruit2", HeraldSlot];

	public override string SceneName => "reunion_guerrilla_herald_leader";

	public override IReadOnlyList<string> RequiredAnimations => ["Idle", "Attack", "Die"];

	public override int MinInitialHp => ToughValue(104, 100);

	public override int MaxInitialHp => ToughValue(108, 104);

	public override DamageSfxType TakeDamageSfxType => DamageSfxType.Fur;

	private int LeadershipAmount => DeadlyValue(3, 2);

	private int StrikeDamage => DeadlyValue(6, 5);

	private int RallyBlock => ToughValue(10, 9);

	/// <summary>战旗每次再叠的领袖气质。</summary>
	private int ExtraLeadershipAmount => DeadlyValue(2, 1);

	/// <summary>遭遇战里下一个空槽；没有槽位或已站满时为 null。</summary>
	private string? FreeSlot => CombatState.Encounter?.GetNextSlot(CombatState) is { Length: > 0 } slot ? slot : null;

	public override async Task AfterAddedToRoom()
	{
		await base.AfterAddedToRoom();
		await ApplyPower<LeadershipPower>([Creature], LeadershipAmount);
	}

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState call = new(CallReinforcementsMoveId, CallReinforcementsMove, new SummonIntent());
		MoveState strike = new(StrikeMoveId, StrikeMove, new SingleAttackIntent(StrikeDamage));
		MoveState banner = new(BannerMoveId, BannerMove, new BuffIntent());
		MoveState rally = new(RallyMoveId, RallyMove, new DefendIntent());
		ConditionalBranchState next = new("REINFORCE_BRANCH_STATE");
		call.FollowUpState = strike;
		rally.FollowUpState = strike;
		strike.FollowUpState = banner;
		banner.FollowUpState = next;
		next.AddState(call, () => FreeSlot != null);
		next.AddState(rally, () => FreeSlot == null);
		return Machine([call, strike, banner, rally, next], call);
	}

	private async Task CallReinforcementsMove(IReadOnlyList<Creature> targets)
	{
		SfxCmd.Play(BuffSfx);
		await CreatureCmd.TriggerAnim(Creature, CreatureAnimator.attackTrigger, 0.4f);
		if (FreeSlot is not { } slot)
		{
			return;
		}

		ReunionGuerrillaFighter recruit = (ReunionGuerrillaFighter)ModelDb.Monster<ReunionGuerrillaFighter>().ToMutable();
		recruit.OpeningOffset = RecruitOpening();
		await CreatureCmd.Add(recruit, CombatState, CombatSide.Enemy, slot);
	}

	/// <summary>
	/// 召来的战士下个敌方回合才出手；让它起手用场上第一名战士本回合这一招，下回合两者就错开（战士是三招固定循环）。
	/// 敌方回合里 NextMove 就是本回合的招式（回合结束后原版才重新掷），不管那名战士这回合出过手没有。
	/// </summary>
	private int RecruitOpening()
	{
		string? current = CombatState.GetTeammatesOf(Creature)
			.FirstOrDefault(c => c.IsAlive && c.Monster is ReunionGuerrillaFighter)?.Monster?.NextMove.Id;
		return current switch
		{
			ReunionGuerrillaFighter.ChargeMoveId => 1,
			ReunionGuerrillaFighter.SuppressMoveId => 2,
			_ => 0,
		};
	}

	private async Task StrikeMove(IReadOnlyList<Creature> targets)
	{
		await DamageCmd.Attack(StrikeDamage)
			.FromMonster(this)
			.WithAttackerAnim(CreatureAnimator.attackTrigger, 0.4f)
			.WithHitFx(BluntVfx, MeleeHitSfx)
			.Execute(null);
	}

	private async Task RallyMove(IReadOnlyList<Creature> targets)
	{
		SfxCmd.Play(BuffSfx);
		await GiveBlock(LivingTeammates(), RallyBlock);
	}

	private async Task BannerMove(IReadOnlyList<Creature> targets)
	{
		SfxCmd.Play(BuffSfx);
		await CreatureCmd.TriggerAnim(Creature, CreatureAnimator.attackTrigger, 0.4f);
		await ApplyPower<LeadershipPower>([Creature], ExtraLeadershipAmount);
	}

	public override CreatureAnimator GenerateAnimator(MegaSprite controller)
	{
		return BuildAnimator(controller, "Idle", "Die", (CreatureAnimator.attackTrigger, "Attack"));
	}
}
