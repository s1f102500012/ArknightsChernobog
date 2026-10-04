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
/// 宿主拾荒者（PRTS enemy_1044_zomstr），梅菲斯特“牧群”的普通单位（7-2/7-3）。
/// PRTS 定位：被不明意识控制身体的拾荒者，能快速自然恢复生命。
/// 动画：Idle / Attack（OnAttack 0.867s）/ Die；Move 未用。
/// 招式与数值见 docs/战斗设计.md：永续再生 + 残存（见 <see cref="ReunionHostMonster"/>）；乱砸、撕扯（1 层虚弱）、翻找（格挡 + 力量）三招随机、不连用。
/// </summary>
public sealed class ReunionHostScavenger : ReunionHostMonster
{
	public const string SmashMoveId = "SMASH_MOVE";
	public const string RendMoveId = "REND_MOVE";
	public const string ScavengeMoveId = "SCAVENGE_MOVE";

	private const int RendWeak = 1;

	private const int ScavengeBlock = 6;

	private const int ScavengeStrength = 1;

	public override string SceneName => "reunion_host_scavenger";

	public override IReadOnlyList<string> RequiredAnimations => ["Idle", "Attack", "Die"];

	public override int MinInitialHp => ToughValue(41, 38);

	public override int MaxInitialHp => ToughValue(45, 42);

	public override DamageSfxType TakeDamageSfxType => DamageSfxType.Fur;

	private int SmashDamage => DeadlyValue(9, 8);

	private int RendDamage => DeadlyValue(6, 5);

	protected override int HostRegen => 3;

	protected override int ReviveHp => ToughValue(18, 16);

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState smash = new(SmashMoveId, SmashMove, new SingleAttackIntent(SmashDamage));
		MoveState rend = new(RendMoveId, RendMove, new SingleAttackIntent(RendDamage), new DebuffIntent());
		MoveState scavenge = new(ScavengeMoveId, ScavengeMove, new BuffIntent(), new DefendIntent());
		return HostRandomMachine("HOST_SCAVENGER_RANDOM", [smash, rend, scavenge]);
	}

	private async Task SmashMove(IReadOnlyList<Creature> targets)
	{
		await DamageCmd.Attack(SmashDamage)
			.FromMonster(this)
			.WithAttackerAnim(CreatureAnimator.attackTrigger, 0.85f)
			.WithHitFx(BluntVfx, MeleeHitSfx)
			.Execute(null);
	}

	private async Task ScavengeMove(IReadOnlyList<Creature> targets)
	{
		SfxCmd.Play(BuffSfx);
		await GainBlock(ScavengeBlock);
		await ApplyStrengthToSelf(ScavengeStrength);
	}

	private async Task RendMove(IReadOnlyList<Creature> targets)
	{
		await DamageCmd.Attack(RendDamage)
			.FromMonster(this)
			.WithAttackerAnim(CreatureAnimator.attackTrigger, 0.85f)
			.WithHitFx(SlashVfx, MeleeHitSfx)
			.Execute(null);
		await ApplyPower<WeakPower>(targets, RendWeak);
	}

	public override CreatureAnimator GenerateAnimator(MegaSprite controller)
	{
		return BuildHostAnimator(controller);
	}
}
