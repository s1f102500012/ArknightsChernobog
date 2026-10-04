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
/// 狂暴宿主组长（PRTS enemy_1062_rager_2），狂暴宿主的头目（7-3）。PRTS 定位：彻底失去理智的敌方士兵，攻击力很高，会持续损失生命。
/// 与狂暴宿主士兵同一套骨骼和动画（附件网格随贴图重新打包，skel 单独转换）：Idle / Attack（OnAttack 0.667s）/ Die；Move 未用。
/// 招式与数值见 docs/战斗设计.md（初版）：原版“瓦解”9 层（与狂暴宿主士兵相同、比投掷手的 14 低，血量靠自损扣得慢，要多防几轮处决）；狂暴连斩（三段）→ 狂乱之嚎（自身力量；它总是单独出场）→ 处决 → 循环。
/// </summary>
public sealed class ReunionRagingHostLeader : ReunionMonster
{
	public const string RampageMoveId = "RAMPAGE_MOVE";
	public const string FrenziedHowlMoveId = "FRENZIED_HOWL_MOVE";
	public const string ExecuteMoveId = "EXECUTE_MOVE";

	private const int LossOfControl = 9;
	private const int RampageHits = 3;

	public override string SceneName => "reunion_raging_host_leader";

	public override IReadOnlyList<string> RequiredAnimations => ["Idle", "Attack", "Die"];

	public override int MinInitialHp => ToughValue(210, 200);

	public override int MaxInitialHp => ToughValue(216, 206);

	public override DamageSfxType TakeDamageSfxType => DamageSfxType.Fur;

	private int RampageDamage => DeadlyValue(10, 9);

	private int HowlStrength => DeadlyValue(3, 2);

	private int ExecuteDamage => DeadlyValue(28, 26);

	public override async Task AfterAddedToRoom()
	{
		await base.AfterAddedToRoom();
		await ApplyLossOfControl(LossOfControl);
	}

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState rampage = new(RampageMoveId, RampageMove, new MultiAttackIntent(RampageDamage, RampageHits));
		MoveState howl = new(FrenziedHowlMoveId, FrenziedHowlMove, new BuffIntent());
		MoveState execute = new(ExecuteMoveId, ExecuteMove, new SingleAttackIntent(ExecuteDamage));
		rampage.FollowUpState = howl;
		howl.FollowUpState = execute;
		execute.FollowUpState = rampage;
		return Machine([rampage, howl, execute], rampage);
	}

	private async Task RampageMove(IReadOnlyList<Creature> targets)
	{
		await DamageCmd.Attack(RampageDamage).WithHitCount(RampageHits).OnlyPlayAnimOnce()
			.FromMonster(this)
			.WithAttackerAnim(CreatureAnimator.attackTrigger, 0.65f)
			.WithHitFx(SlashVfx, MeleeHitSfx)
			.Execute(null);
	}

	private async Task FrenziedHowlMove(IReadOnlyList<Creature> targets)
	{
		SfxCmd.Play(BuffSfx);
		await ApplyStrengthToSelf(HowlStrength);
	}

	private async Task ExecuteMove(IReadOnlyList<Creature> targets)
	{
		await DamageCmd.Attack(ExecuteDamage)
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
