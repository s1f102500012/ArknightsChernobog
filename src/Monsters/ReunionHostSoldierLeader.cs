using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace ArknightsChernobog.Monsters;

/// <summary>
/// 宿主士兵组长（PRTS enemy_1043_zomsabr_2，资源目录 enemy_1043_zomsbr_2），“牧群”的头目（7-2/7-3，7-3 生命值大幅提升）。
/// PRTS 定位：被不明意识控制身体的士兵组长，能快速自然恢复生命。
/// 与宿主士兵同一套骨骼（附件网格与部分关键帧不同，skel 单独转换）：Idle / Attack（OnAttack 0.4s）/ Die；Run_Loop 未用。
/// 招式与数值见 docs/战斗设计.md：永续再生 + 残存（见 <see cref="ReunionHostMonster"/>）；劈砍 → 牧群号令（弃牌堆塞 2 张晕眩）→ 连斩。
/// </summary>
public sealed class ReunionHostSoldierLeader : ReunionHostMonster
{
	public const string HackMoveId = "HACK_MOVE";
	public const string HerdCallMoveId = "HERD_CALL_MOVE";
	public const string DoubleSlashMoveId = "DOUBLE_SLASH_MOVE";

	private const int DoubleSlashHits = 2;

	public override string SceneName => "reunion_host_soldier_leader";

	public override IReadOnlyList<string> RequiredAnimations => ["Idle", "Attack", "Die"];

	public override int MinInitialHp => ToughValue(43, 40);

	public override int MaxInitialHp => ToughValue(47, 44);

	public override DamageSfxType TakeDamageSfxType => DamageSfxType.Fur;

	private int HackDamage => DeadlyValue(7, 6);

	private int DoubleSlashDamage => DeadlyValue(5, 4);

	private const int HerdDazed = 2;

	protected override int HostRegen => 3;

	protected override int ReviveHp => ToughValue(20, 18);

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState hack = new(HackMoveId, HackMove, new SingleAttackIntent(HackDamage));
		MoveState herdCall = new(HerdCallMoveId, HerdCallMove, new StatusIntent(HerdDazed));
		MoveState doubleSlash = new(DoubleSlashMoveId, DoubleSlashMove, new MultiAttackIntent(DoubleSlashDamage, DoubleSlashHits));
		hack.FollowUpState = herdCall;
		herdCall.FollowUpState = doubleSlash;
		doubleSlash.FollowUpState = hack;
		return HostMachine([hack, herdCall, doubleSlash], hack, hack);
	}

	private async Task HackMove(IReadOnlyList<Creature> targets)
	{
		await DamageCmd.Attack(HackDamage)
			.FromMonster(this)
			.WithAttackerAnim(CreatureAnimator.attackTrigger, 0.4f)
			.WithHitFx(SlashVfx, MeleeHitSfx)
			.Execute(null);
	}

	private async Task HerdCallMove(IReadOnlyList<Creature> targets)
	{
		SfxCmd.Play(BuffSfx);
		await CreatureCmd.TriggerAnim(Creature, CreatureAnimator.attackTrigger, 0.4f);
		await AddStatusCards<Dazed>(targets, HerdDazed);
	}

	private async Task DoubleSlashMove(IReadOnlyList<Creature> targets)
	{
		await DamageCmd.Attack(DoubleSlashDamage).WithHitCount(DoubleSlashHits).OnlyPlayAnimOnce()
			.FromMonster(this)
			.WithAttackerAnim(CreatureAnimator.attackTrigger, 0.4f)
			.WithHitFx(SlashVfx, MeleeHitSfx)
			.Execute(null);
	}

	public override CreatureAnimator GenerateAnimator(MegaSprite controller)
	{
		return BuildHostAnimator(controller);
	}
}
