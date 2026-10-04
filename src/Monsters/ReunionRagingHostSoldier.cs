using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace ArknightsChernobog.Monsters;

/// <summary>
/// 狂暴宿主士兵（PRTS enemy_1062_rager），失去控制后的宿主（7-3 “残余的狂暴宿主”）。
/// PRTS 定位：逐渐陷入狂乱的敌方士兵，攻击力很高，会持续损失生命。
/// 动画：Idle / Attack（OnAttack 0.667s）/ Die；Move 未用。
/// 招式与数值见 docs/战斗设计.md（初版）：血比普通宿主薄、伤害更高，原版“瓦解”9 层（比投掷手的 14 低，自损慢、要多撑几回合）；狂斩（三段）、撕裂、狂嚎（自身力量 + 玩家虚弱）三招随机、不连用（照原版猎杀者）。
/// </summary>
public sealed class ReunionRagingHostSoldier : ReunionMonster
{
	public const string FrenzyMoveId = "FRENZY_MOVE";
	public const string RipMoveId = "RIP_MOVE";
	public const string RoarMoveId = "ROAR_MOVE";

	private const int RoarWeak = 1;

	private const int LossOfControl = 9;
	private const int FrenzyHits = 3;

	public override string SceneName => "reunion_raging_host_soldier";

	public override IReadOnlyList<string> RequiredAnimations => ["Idle", "Attack", "Die"];

	public override int MinInitialHp => ToughValue(158, 150);

	public override int MaxInitialHp => ToughValue(164, 156);

	public override DamageSfxType TakeDamageSfxType => DamageSfxType.Fur;

	private int FrenzyDamage => DeadlyValue(8, 7);

	private int RipDamage => DeadlyValue(20, 18);

	private int RoarStrength => DeadlyValue(4, 3);

	public override async Task AfterAddedToRoom()
	{
		await base.AfterAddedToRoom();
		await ApplyLossOfControl(LossOfControl);
	}

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState frenzy = new(FrenzyMoveId, FrenzyMove, new MultiAttackIntent(FrenzyDamage, FrenzyHits));
		MoveState rip = new(RipMoveId, RipMove, new SingleAttackIntent(RipDamage));
		MoveState roar = new(RoarMoveId, RoarMove, new BuffIntent(), new DebuffIntent());
		return RandomMachine("RAGING_SOLDIER_RANDOM", [frenzy, rip, roar]);
	}

	private async Task FrenzyMove(IReadOnlyList<Creature> targets)
	{
		await DamageCmd.Attack(FrenzyDamage).WithHitCount(FrenzyHits).OnlyPlayAnimOnce()
			.FromMonster(this)
			.WithAttackerAnim(CreatureAnimator.attackTrigger, 0.65f)
			.WithHitFx(SlashVfx, MeleeHitSfx)
			.Execute(null);
	}

	private async Task RoarMove(IReadOnlyList<Creature> targets)
	{
		SfxCmd.Play(BuffSfx);
		await ApplyStrengthToSelf(RoarStrength);
		await ApplyPower<WeakPower>(targets, RoarWeak);
	}

	private async Task RipMove(IReadOnlyList<Creature> targets)
	{
		await DamageCmd.Attack(RipDamage)
			.FromMonster(this)
			.WithAttackerAnim(CreatureAnimator.attackTrigger, 0.65f)
			.WithHitFx(HeavyBluntVfx, HeavyHitSfx)
			.Execute(null);
	}

	public override CreatureAnimator GenerateAnimator(MegaSprite controller)
	{
		return BuildAnimator(controller, "Idle", "Die", (CreatureAnimator.attackTrigger, "Attack"));
	}
}
