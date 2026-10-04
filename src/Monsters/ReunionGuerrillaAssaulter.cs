using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace ArknightsChernobog.Monsters;

/// <summary>
/// 游击队突袭战士（PRTS enemy_1083_sotiab），精英近战。PRTS 定位：能从战场中降落，受到强化时攻击力大幅提升。
/// 动画：Idle / Die / Start（空降，悬空、起飞、降落都由它截取，见 <see cref="ReunionAirborneAnimation"/>）；Attack、Move 未用。
/// 招式与数值见 docs/战斗设计.md：开场悬空（原版“翱翔”），降落突袭（触地重击，移除翱翔）↔ 起飞（重新翱翔 + 力量 + 格挡）交替。
/// 玩家在它落地的那个回合能打满伤害。同场两名时遭遇战用起手偏移让第二名先起飞（它本来就悬空，等于先强化一回合），错开两次重击。
/// 爱国者重生时召来的那名用 <see cref="DropsIn"/>：播完整的空降落地、站在地上入场，不带翱翔，第一招起飞。
/// </summary>
public sealed class ReunionGuerrillaAssaulter : ReunionMonster
{
	public const string DiveAssaultMoveId = "DIVE_ASSAULT_MOVE";
	public const string TakeOffMoveId = "TAKE_OFF_MOVE";

	private const int TakeOffBlock = 6;

	private bool _dropsIn;

	/// <summary>
	/// 空投入场：动画机开场播一遍完整的 Start（从画面上方落下、触地收势）后站立待机，入场时不挂翱翔，起手招改为起飞。
	/// 召唤前在可变实例上设置：动画机与起手都在 CreatureCmd.Add 建立生物节点和状态机时读取它。
	/// </summary>
	public bool DropsIn
	{
		get => _dropsIn;
		set
		{
			AssertMutable();
			_dropsIn = value;
			OpeningOffset = value ? 1 : 0;
		}
	}

	public override string SceneName => "reunion_guerrilla_assaulter";

	public override IReadOnlyList<string> RequiredAnimations => ["Idle", ReunionAirborneAnimation.StartAnimation, "Die"];

	public override int MinInitialHp => ToughValue(78, 74);

	public override int MaxInitialHp => ToughValue(82, 78);

	public override DamageSfxType TakeDamageSfxType => DamageSfxType.Armor;

	private int DiveDamage => DeadlyValue(16, 14);

	private int TakeOffStrength => DeadlyValue(3, 2);

	public override async Task AfterAddedToRoom()
	{
		await base.AfterAddedToRoom();
		if (!DropsIn)
		{
			await ApplyPower<SoarPower>([Creature], 1);
		}
	}

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState dive = new(DiveAssaultMoveId, DiveAssaultMove, new SingleAttackIntent(DiveDamage));
		MoveState takeOff = new(TakeOffMoveId, TakeOffMove, new BuffIntent(), new DefendIntent());
		dive.FollowUpState = takeOff;
		takeOff.FollowUpState = dive;
		return Machine([dive, takeOff], dive);
	}

	private async Task DiveAssaultMove(IReadOnlyList<Creature> targets)
	{
		await PowerCmd.Remove<SoarPower>(Creature);
		await Cmd.Wait(ReunionAirborneAnimation.Land(Creature));
		await DamageCmd.Attack(DiveDamage)
			.FromMonster(this)
			.WithHitFx(HeavyBluntVfx, HeavyHitSfx)
			.Execute(null);
	}

	private async Task TakeOffMove(IReadOnlyList<Creature> targets)
	{
		SfxCmd.Play(BuffSfx);
		await Cmd.Wait(ReunionAirborneAnimation.TakeOff(Creature));
		await ApplyPower<SoarPower>([Creature], 1);
		await ApplyStrengthToSelf(TakeOffStrength);
		await GainBlock(TakeOffBlock);
	}

	public override Task AfterDeath(PlayerChoiceContext choiceContext, Creature creature, bool wasRemovalPrevented, float deathAnimLength)
	{
		if (creature == Creature)
		{
			ReunionAirborneAnimation.Stop(Creature);
		}

		return base.AfterDeath(choiceContext, creature, wasRemovalPrevented, deathAnimLength);
	}

	public override CreatureAnimator GenerateAnimator(MegaSprite controller)
	{
		return ReunionAirborneAnimation.BuildAnimator(controller, dropIn: DropsIn);
	}
}
