using ArknightsChernobog.Audio;
using ArknightsChernobog.Powers;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Audio;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace ArknightsChernobog.Monsters;

/// <summary>
/// 爱国者（PRTS enemy_1506_patrt），本幕 Boss，带两名游击队战士做护卫（7-18 爱国者之死）。两阶段：
/// - 行军姿态：残甲（<see cref="ReunionPatriotArmorPower"/>，受到的所有伤害固定减层数点，受击不掉层）。
///   行军（加固残甲 + 格挡）→ 长戟四连 → 盾击（打完崩落加固的甲片，回到初始层数），盾击后的那个玩家回合是爆发窗口。
///   旧部之誓（<see cref="ReunionPatriotOathPower"/>）：护卫倒下时获得力量，四连击会把力量放大四倍。
/// - 生命归零时靠不屈（<see cref="ReunionPatriotRebirthPower"/>，照原版实验体）进入重生：下个敌方回合满血换成毁灭姿态，
///   若有护卫阵亡，空投一名空降兵补位（落地入场、第一招起飞）。护卫不挂爪牙，爱国者倒下后仍要把它们打完。
/// - 毁灭姿态：卸甲，毁灭姿态被动（<see cref="ReunionPatriotWrathPower"/>）每回合给玩家叠瓦解；横扫 → 投枪 → 战吼。
/// 动画：一阶段 Idle_1 / Attack_1（四连击，OnAttack 0.633~0.933s）/ Move_1；二阶段 Idle_2 / Attack_2（OnAttack 0.533s）/
/// Move_2 / Skill（投枪，OnAttack 1.7s）；一阶段倒下播 revive_1 → revive_2（保持倒地），重生播 revive_3 接 Idle_2；
/// 真正死亡才播 Die（5.3s）。
/// </summary>
public sealed class ReunionPatriot : ReunionMonster, IReunionRebirth
{
	public const string MarchMoveId = "MARCH_MOVE";
	public const string SpearFlurryMoveId = "SPEAR_FLURRY_MOVE";
	public const string ShieldRamMoveId = "SHIELD_RAM_MOVE";
	public const string RebirthMoveId = "REBIRTH_MOVE";
	public const string SweepMoveId = "SWEEP_MOVE";
	public const string JavelinMoveId = "JAVELIN_MOVE";
	public const string WarCryMoveId = "WAR_CRY_MOVE";

	private const string MarchTrigger = "March";
	private const string WarCryTrigger = "WarCry";
	private const string JavelinTrigger = "Javelin";
	private const string ReviveTrigger = "Revive";

	private const int FlurryHits = 4;
	private const int SweepHits = 2;
	private const int RamFrail = 1;
	private const float RebirthSeconds = 2.8f;

	// Boss 战按遭遇战场景的同名 Marker2D 站位（res_overlay/scenes/encounters/reunion_patriot_boss.tscn）：
	// 两名护卫比原版自动排开的位置各左移 80，与爱国者拉开距离；重生时召来的空降兵落在阵亡护卫空出的槽位上。
	public const string EscortFrontSlot = "escort1";
	public const string EscortBackSlot = "escort2";
	public const string PatriotSlot = "patriot";

	public static readonly IReadOnlyList<string> FormationSlots = [EscortFrontSlot, EscortBackSlot, PatriotSlot];

	private bool _phaseTwo;
	private MoveState? _rebirthState;

	public override string SceneName => "reunion_patriot";

	public override IReadOnlyList<string> RequiredAnimations =>
		["Idle_1", "Attack_1", "Move_1", "Idle_2", "Attack_2", "Move_2", "Skill", "revive_1", "revive_2", "revive_3", "Die"];

	/// <summary>一阶段（行军姿态）的生命；二阶段由重生按 <see cref="PhaseTwoHp"/> 重设。</summary>
	public override int MinInitialHp => ToughValue(150, 140);

	public override int MaxInitialHp => MinInitialHp;

	public override DamageSfxType TakeDamageSfxType => DamageSfxType.ArmorBig;

	/// <summary>是否已进入毁灭姿态。只影响动画选择；招式由状态机决定。</summary>
	public bool PhaseTwo
	{
		get => _phaseTwo;
		private set
		{
			AssertMutable();
			_phaseTwo = value;
		}
	}

	private MoveState RebirthState
	{
		get => _rebirthState!;
		set
		{
			AssertMutable();
			_rebirthState = value;
		}
	}

	private int PhaseTwoHp => ToughValue(150, 140);

	/// <summary>初始残甲，也是盾击崩落后回到的层数。</summary>
	private int ArmorAmount => ToughValue(3, 2);

	private int OathStrength => DeadlyValue(3, 2);

	private const int MarchArmor = 2;

	private int MarchBlock => ToughValue(12, 10);

	private int FlurryDamage => DeadlyValue(4, 3);

	private int RamDamage => DeadlyValue(15, 14);

	private int WrathAmount => DeadlyValue(3, 2);

	private int SweepDamage => DeadlyValue(8, 7);

	private int JavelinDamage => DeadlyValue(23, 21);

	private int WarCryStrength => DeadlyValue(4, 3);

	private const int WarCryBlock = 12;

	public override async Task AfterAddedToRoom()
	{
		await base.AfterAddedToRoom();
		await ApplyPower<ReunionPatriotArmorPower>([Creature], ArmorAmount);
		await ApplyPower<ReunionPatriotOathPower>([Creature], OathStrength);
		await ApplyPower<ReunionPatriotRebirthPower>([Creature], 1);
	}

	protected override MonsterMoveStateMachine GenerateMoveStateMachine()
	{
		MoveState march = new(MarchMoveId, MarchMove, new BuffIntent(), new DefendIntent());
		MoveState flurry = new(SpearFlurryMoveId, SpearFlurryMove, new MultiAttackIntent(FlurryDamage, FlurryHits));
		MoveState ram = new(ShieldRamMoveId, ShieldRamMove, new SingleAttackIntent(RamDamage), new DebuffIntent());
		RebirthState = new MoveState(RebirthMoveId, RebirthMove, new HealIntent(), new BuffIntent()) { MustPerformOnceBeforeTransitioning = true };
		MoveState sweep = new(SweepMoveId, SweepMove, new MultiAttackIntent(SweepDamage, SweepHits));
		MoveState javelin = new(JavelinMoveId, JavelinMove, new SingleAttackIntent(JavelinDamage));
		MoveState warCry = new(WarCryMoveId, WarCryMove, new BuffIntent(), new DefendIntent());
		march.FollowUpState = flurry;
		flurry.FollowUpState = ram;
		ram.FollowUpState = march;
		RebirthState.FollowUpState = sweep;
		sweep.FollowUpState = javelin;
		javelin.FollowUpState = warCry;
		warCry.FollowUpState = sweep;
		return Machine([march, flurry, ram, RebirthState, sweep, javelin, warCry], march);
	}

	/// <summary>不屈在一阶段倒下时调用：下一招强制为重生，一阶段音乐淡出。</summary>
	public Task BeginRebirth()
	{
		SetMoveImmediate(RebirthState, forceTransition: true);
		if (CombatState.IsLiveCombat())
		{
			ReunionBossMusic.FadeOutForRebirth();
		}

		return Task.CompletedTask;
	}

	private async Task MarchMove(IReadOnlyList<Creature> targets)
	{
		SfxCmd.Play(BuffSfx);
		await CreatureCmd.TriggerAnim(Creature, MarchTrigger, 0.6f);
		await ApplyPower<ReunionPatriotArmorPower>([Creature], MarchArmor);
		await GainBlock(MarchBlock);
	}

	private async Task SpearFlurryMove(IReadOnlyList<Creature> targets)
	{
		await DamageCmd.Attack(FlurryDamage).WithHitCount(FlurryHits).OnlyPlayAnimOnce()
			.FromMonster(this)
			.WithAttackerAnim(CreatureAnimator.attackTrigger, 0.63f)
			.WithHitFx(SlashVfx, MeleeHitSfx)
			.Execute(null);
	}

	private async Task ShieldRamMove(IReadOnlyList<Creature> targets)
	{
		await DamageCmd.Attack(RamDamage)
			.FromMonster(this)
			.WithAttackerAnim(CreatureAnimator.attackTrigger, 0.63f)
			.WithHitFx(HeavyBluntVfx, HeavyHitSfx)
			.Execute(null);
		await ApplyPower<FrailPower>(targets, RamFrail);
		// 盾击把行军时加固的甲片撞落，最多回到初始层数，不会低于它。
		if (Creature.GetPower<ReunionPatriotArmorPower>() is { } armor && armor.Amount > ArmorAmount)
		{
			await PowerCmd.ModifyAmount(new ThrowingPlayerChoiceContext(), armor, -Math.Min(MarchArmor, armor.Amount - ArmorAmount), Creature, null);
		}
	}

	/// <summary>
	/// 重生：满血换成毁灭姿态。原版 CreatureCmd.Heal 对已死亡生物会复活并触发 Revive（revive_3 接 Idle_2）。
	/// 一阶段倒下时原版已移除残甲、旧部之誓等能力（只有不屈保留），这里重新挂二阶段需要的，最后移除不屈，二阶段倒下即真正死亡。
	/// </summary>
	private async Task RebirthMove(IReadOnlyList<Creature> targets)
	{
		if (!CombatState.IsLiveCombat() || Creature.GetPower<ReunionPatriotRebirthPower>() is not { IsReviving: true } rebirth)
		{
			return;
		}

		PhaseTwo = true;
		ReunionBossMusic.PlayPhaseTwo();
		rebirth.Revive();
		decimal hp = Creature.ScaleHpForMultiplayer(PhaseTwoHp, CombatState.Encounter, CombatState.Players.Count, CombatState.RunState.CurrentActIndex);
		await CreatureCmd.SetMaxHp(Creature, hp);
		await CreatureCmd.Heal(Creature, hp);
		await Cmd.Wait(RebirthSeconds);
		await PowerCmd.Remove<ReunionPatriotRebirthPower>(Creature);
		await ApplyPower<ReunionPatriotWrathPower>([Creature], WrathAmount);
		await ApplyPower<ReunionPatriotOathPower>([Creature], OathStrength);
		// 有护卫阵亡时呼叫一名空降兵补位，最多一名：原版阵亡的生物会移出战斗，槽位随之空出，
		// GetNextSlot 按 FormationSlots 顺序给出第一个空槽（爱国者自己靠不屈留在场上，占着自己的槽）；两名护卫都在时没有空槽。
		// 空降兵从画面上方落地入场、不带翱翔，第一招起飞，推迟一回合才重击，避开与横扫同回合叠出的伤害峰值。
		// 护卫与空降兵都不挂爪牙：爱国者倒下后它们留在场上，要打完才结束战斗。
		if (CombatState.Encounter?.GetNextSlot(CombatState) is { Length: > 0 } slot)
		{
			ReunionGuerrillaAssaulter paratrooper = (ReunionGuerrillaAssaulter)ModelDb.Monster<ReunionGuerrillaAssaulter>().ToMutable();
			paratrooper.DropsIn = true;
			await CreatureCmd.Add(paratrooper, CombatState, CombatSide.Enemy, slot);
			await Cmd.Wait(ReunionAirborneAnimation.DropInSeconds);
		}
	}

	private async Task SweepMove(IReadOnlyList<Creature> targets)
	{
		await DamageCmd.Attack(SweepDamage).WithHitCount(SweepHits).OnlyPlayAnimOnce()
			.FromMonster(this)
			.WithAttackerAnim(CreatureAnimator.attackTrigger, 0.53f)
			.WithHitFx(SlashVfx, MeleeHitSfx)
			.Execute(null);
	}

	private async Task JavelinMove(IReadOnlyList<Creature> targets)
	{
		await DamageCmd.Attack(JavelinDamage)
			.FromMonster(this)
			.WithAttackerAnim(JavelinTrigger, 1.7f)
			.WithHitFx(HeavyBluntVfx, HeavyHitSfx)
			.Execute(null);
	}

	private async Task WarCryMove(IReadOnlyList<Creature> targets)
	{
		SfxCmd.Play(BuffSfx);
		await CreatureCmd.TriggerAnim(Creature, WarCryTrigger, 0.6f);
		await ApplyStrengthToSelf(WarCryStrength);
		await GainBlock(WarCryBlock);
	}

	/// <summary>一阶段倒下且还没重生时，死亡触发器播倒地而不是 Die。死亡动画在原版移除能力之前开始，这时不屈还在。</summary>
	private bool RebirthPending => IsMutable && !PhaseTwo && Creature.HasPower<ReunionPatriotRebirthPower>();

	public override CreatureAnimator GenerateAnimator(MegaSprite controller)
	{
		AnimState idle1 = new("Idle_1", isLooping: true);
		AnimState idle2 = new("Idle_2", isLooping: true);
		AnimState downed = new("revive_2", isLooping: true);
		CreatureAnimator animator = new(idle1, controller);
		animator.AddAnyState(CreatureAnimator.idleTrigger, idle1, () => !PhaseTwo);
		animator.AddAnyState(CreatureAnimator.idleTrigger, idle2, () => PhaseTwo);
		animator.AddAnyState(CreatureAnimator.attackTrigger, new AnimState("Attack_1") { NextState = idle1 }, () => !PhaseTwo);
		animator.AddAnyState(CreatureAnimator.attackTrigger, new AnimState("Attack_2") { NextState = idle2 }, () => PhaseTwo);
		animator.AddAnyState(MarchTrigger, new AnimState("Move_1") { NextState = idle1 });
		animator.AddAnyState(WarCryTrigger, new AnimState("Move_2") { NextState = idle2 });
		animator.AddAnyState(JavelinTrigger, new AnimState("Skill") { NextState = idle2 });
		animator.AddAnyState(CreatureAnimator.deathTrigger, new AnimState("revive_1") { NextState = downed }, () => RebirthPending);
		animator.AddAnyState(CreatureAnimator.deathTrigger, new AnimState("Die"), () => !RebirthPending);
		animator.AddAnyState(ReviveTrigger, new AnimState("revive_3") { NextState = idle2 });
		return animator;
	}
}
