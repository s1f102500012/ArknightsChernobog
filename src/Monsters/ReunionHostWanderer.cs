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
/// 宿主流浪者（PRTS enemy_1044_zomstr_2），梅菲斯特“牧群”的普通单位（7-3，关卡里生命值大幅提升）。
/// PRTS 定位：被不明意识控制身体的流浪者，能快速自然恢复生命。
/// 与宿主拾荒者同一套骨骼和动画（附件网格随贴图重新打包，skel 单独转换）：Idle / Attack（OnAttack 0.867s）/ Die；Move 未用。
/// 招式与数值见 docs/战斗设计.md：永续再生 + 残存（见 <see cref="ReunionHostMonster"/>）；血比拾荒者厚，重击、嘶吼（自身力量）、蹒跚冲撞（1 层脆弱）三招随机、不连用。
/// </summary>
public sealed class ReunionHostWanderer : ReunionHostMonster
{
	public const string HeavyBlowMoveId = "HEAVY_BLOW_MOVE";
	public const string SnarlMoveId = "SNARL_MOVE";
	public const string LurchMoveId = "LURCH_MOVE";

	private const int LurchFrail = 1;


	public override string SceneName => "reunion_host_wanderer";

	public override IReadOnlyList<string> RequiredAnimations => ["Idle", "Attack", "Die"];

	public override int MinInitialHp => ToughValue(55, 52);

	public override int MaxInitialHp => ToughValue(59, 56);

	public override DamageSfxType TakeDamageSfxType => DamageSfxType.Fur;

	private int HeavyBlowDamage => DeadlyValue(10, 9);

	private int SnarlStrength => DeadlyValue(3, 2);

	private int LurchDamage => DeadlyValue(6, 5);

	protected override int HostRegen => 2;

	protected override int ReviveHp => ToughValue(24, 22);

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState heavyBlow = new(HeavyBlowMoveId, HeavyBlowMove, new SingleAttackIntent(HeavyBlowDamage));
		MoveState snarl = new(SnarlMoveId, SnarlMove, new BuffIntent());
		MoveState lurch = new(LurchMoveId, LurchMove, new SingleAttackIntent(LurchDamage), new DebuffIntent());
		return HostRandomMachine("HOST_WANDERER_RANDOM", [heavyBlow, snarl, lurch]);
	}

	private async Task HeavyBlowMove(IReadOnlyList<Creature> targets)
	{
		await DamageCmd.Attack(HeavyBlowDamage)
			.FromMonster(this)
			.WithAttackerAnim(CreatureAnimator.attackTrigger, 0.85f)
			.WithHitFx(HeavyBluntVfx, HeavyHitSfx)
			.Execute(null);
	}

	private async Task LurchMove(IReadOnlyList<Creature> targets)
	{
		await DamageCmd.Attack(LurchDamage)
			.FromMonster(this)
			.WithAttackerAnim(CreatureAnimator.attackTrigger, 0.85f)
			.WithHitFx(BluntVfx, MeleeHitSfx)
			.Execute(null);
		await ApplyPower<FrailPower>(targets, LurchFrail);
	}

	private async Task SnarlMove(IReadOnlyList<Creature> targets)
	{
		SfxCmd.Play(BuffSfx);
		await ApplyStrengthToSelf(SnarlStrength);
	}

	public override CreatureAnimator GenerateAnimator(MegaSprite controller)
	{
		return BuildHostAnimator(controller);
	}
}
