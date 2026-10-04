using ArknightsChernobog.Powers;
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
/// 游击队猎犬pro（PRTS enemy_1077_sotihd_2），猎犬的头目版。PRTS 定位：穿戴轻量乌萨斯装甲、攻击欲望更强的高级战犬。
/// 与本体同一套骨骼和动画（附件网格随贴图重新打包，skel 单独转换）：Idle / Attack（OnAttack 0.6s）/ Die；Run_Loop 未用。
/// 招式与数值见 docs/战斗设计.md：开场带原版“覆甲”当轻甲，撕咬给 1 层虚弱，嚎叫给自己力量（不给同伴：它若先出手，同伴已亮出的意图会在出手前涨伤害）；
/// 带狼群（<see cref="ReunionPackFuryPower"/>），同伴猎犬倒下时获得力量。
/// </summary>
public sealed class ReunionGuerrillaHoundPro : ReunionMonster
{
	public const string RushMoveId = "RUSH_MOVE";
	public const string BiteMoveId = "BITE_MOVE";
	public const string HowlMoveId = "HOWL_MOVE";

	private const int RushHits = 2;
	private const int BiteWeak = 1;

	public override string SceneName => "reunion_guerrilla_hound_pro";

	public override IReadOnlyList<string> RequiredAnimations => ["Idle", "Attack", "Die"];

	public override int MinInitialHp => ToughValue(51, 48);

	public override int MaxInitialHp => ToughValue(55, 52);

	public override DamageSfxType TakeDamageSfxType => DamageSfxType.Armor;

	private int PlatingAmount => ToughValue(5, 4);

	private int RushDamage => DeadlyValue(5, 4);

	private int BiteDamage => DeadlyValue(9, 8);

	private int HowlStrength => DeadlyValue(3, 2);

	private int PackFury => DeadlyValue(3, 2);

	public override async Task AfterAddedToRoom()
	{
		await base.AfterAddedToRoom();
		await ApplyPower<PlatingPower>([Creature], PlatingAmount);
		await ApplyPower<ReunionPackFuryPower>([Creature], PackFury);
	}

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState rush = new(RushMoveId, RushMove, new MultiAttackIntent(RushDamage, RushHits));
		MoveState bite = new(BiteMoveId, BiteMove, new SingleAttackIntent(BiteDamage), new DebuffIntent());
		MoveState howl = new(HowlMoveId, HowlMove, new BuffIntent());
		rush.FollowUpState = bite;
		bite.FollowUpState = howl;
		howl.FollowUpState = rush;
		return Machine([rush, bite, howl], rush);
	}

	private async Task RushMove(IReadOnlyList<Creature> targets)
	{
		await DamageCmd.Attack(RushDamage).WithHitCount(RushHits).OnlyPlayAnimOnce()
			.FromMonster(this)
			.WithAttackerAnim(CreatureAnimator.attackTrigger, 0.55f)
			.WithHitFx(BiteVfx, MeleeHitSfx)
			.Execute(null);
	}

	private async Task BiteMove(IReadOnlyList<Creature> targets)
	{
		await DamageCmd.Attack(BiteDamage)
			.FromMonster(this)
			.WithAttackerAnim(CreatureAnimator.attackTrigger, 0.55f)
			.WithHitFx(BiteVfx, MeleeHitSfx)
			.Execute(null);
		await ApplyPower<WeakPower>(targets, BiteWeak);
	}

	private async Task HowlMove(IReadOnlyList<Creature> targets)
	{
		SfxCmd.Play(BuffSfx);
		await ApplyStrengthToSelf(HowlStrength);
	}

	public override CreatureAnimator GenerateAnimator(MegaSprite controller)
	{
		return BuildAnimator(controller, "Idle", "Die", (CreatureAnimator.attackTrigger, "Attack"));
	}
}
