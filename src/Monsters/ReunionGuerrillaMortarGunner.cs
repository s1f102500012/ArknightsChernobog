using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace ArknightsChernobog.Monsters;

/// <summary>
/// 游击队迫击炮兵（PRTS enemy_1082_soticn），后排重火力。PRTS 定位：受到强化时攻击速度大幅提升。
/// 动画：Idle_1 待机 / Attack（OnAttack 0.533s）/ Die；炮击（蓄力）借用 Idle_2 放一遍再回 Idle_1。Move 未用。
/// 招式与数值见 docs/战斗设计.md：装填（往手牌塞 3 张原版“灼烧”，不播动画）→ 炮击（自身力量，预告下一发）→ 急速射（单发重炮，吃到刚叠的力量）。
/// 不再给自己格挡：格挡由同场的盾卫“掩护”提供。招式 Id 沿用旧名，本地化标题未改。
/// </summary>
public sealed class ReunionGuerrillaMortarGunner : ReunionMonster
{
	public const string ReloadMoveId = "RELOAD_MOVE";
	public const string BombardMoveId = "BOMBARD_MOVE";
	public const string BarrageMoveId = "BARRAGE_MOVE";

	private const string ReloadTrigger = "Reload";
	/// <summary>装填塞进手牌的灼烧张数（常量名沿用旧版“炮击塞灼烧”）。</summary>
	private const int BombardBurns = 3;

	public override string SceneName => "reunion_guerrilla_mortar_gunner";

	public override IReadOnlyList<string> RequiredAnimations => ["Idle_1", "Idle_2", "Attack", "Die"];

	public override int MinInitialHp => ToughValue(60, 58);

	public override int MaxInitialHp => ToughValue(64, 62);

	public override DamageSfxType TakeDamageSfxType => DamageSfxType.Armor;
	
	private int BarrageDamage => DeadlyValue(16, 14);

	private int BombardStrength => DeadlyValue(4, 3);
	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState reload = new(ReloadMoveId, ReloadMove, new StatusIntent(BombardBurns));
		MoveState bombard = new(BombardMoveId, BombardMove, new BuffIntent());
		MoveState barrage = new(BarrageMoveId, BarrageMove, new SingleAttackIntent(BarrageDamage));
		reload.FollowUpState = bombard;
		bombard.FollowUpState = barrage;
		barrage.FollowUpState = reload;
		return Machine([reload, bombard, barrage], reload);
	}

	private async Task ReloadMove(IReadOnlyList<Creature> targets)
	{
		await CardPileCmd.AddToCombatAndPreview<Burn>(targets, PileType.Hand, BombardBurns, null);
	}

	private async Task BombardMove(IReadOnlyList<Creature> targets)
	{
		SfxCmd.Play(BuffSfx);
		await CreatureCmd.TriggerAnim(Creature, ReloadTrigger, 0.4f);
		await ApplyStrengthToSelf(BombardStrength);
	}

	private async Task BarrageMove(IReadOnlyList<Creature> targets)
	{
		await DamageCmd.Attack(BarrageDamage)
			.FromMonster(this)
			.WithAttackerAnim(CreatureAnimator.attackTrigger, 0.55f)
			.WithHitFx(HeavyBluntVfx, BombHitSfx)
			.Execute(null);
	}

	public override CreatureAnimator GenerateAnimator(MegaSprite controller)
	{
		return BuildAnimator(controller, "Idle_1", "Die",
			(CreatureAnimator.attackTrigger, "Attack"),
			(ReloadTrigger, "Idle_2"));
	}
}
