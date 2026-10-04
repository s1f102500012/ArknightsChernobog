"""开局伤害模拟的公共引擎：原版蜂巢（vanilla_hive.py）与苦难摇篮（opening_sim.py）用同一口径。

口径：
- 玩家不出格挡、不打怪、不死怪，只算前 N 个敌方回合每回合玩家要承受的攻击伤害。
- 难度 0 = A0（无进阶），1 = A10（含“更强的敌人”A8 与“更致命的敌人”A9）。数值写成 (A0, A10)。
- 攻击伤害 =（基础 + 自身力量 + 其他盟友的领袖气质（开场层数 + 招式叠加的层数））× 段数；玩家带易伤时每段 ×1.5 向下取整。
  易伤层数叠加，在敌方回合结束时掉 1 层（原版 VulnerablePower 在敌方回合结束 TickDownDuration）；
  新挂到玩家身上的减益跳过第一次掉层（PowerCmd.Apply 设 SkipNextDurationTick，叠到已有层数上不跳），
  所以 1 层易伤会覆盖挂上的这个敌方回合剩下的出手，再加完整的下一个敌方回合。
- 出手顺序 = 敌方列表顺序。原版敌方回合开始时把列表拷一份逐个出手，刚召唤的怪当回合不行动，从下一个敌方回合起出手。
  召唤物默认排在队尾；遭遇战有槽位时原版 CombatManager.AddCreature 会按槽位顺序重排敌方列表（SortEnemiesBySlotName），
  召唤者占最后一个槽的（卵翼虫、窥视者、传令兵组长）召唤物排到召唤者前面，用 Kind.summon_front 表示。
- 随机分支照原版 RandomBranchState：按权重在允许的招里抽；rep=1 是“不能连用”，rep=n 是“最多连用 n 次”，rep=0 不限。
- 随机分支与随机阵容都按真实概率展开，所以同时给出逐回合最大值（最差情况）与期望值（平均情况）。
- 只模拟出伤：格挡、虚弱、脆弱、状态牌、荆棘这类不改变敌方出伤的效果不计。
"""
from __future__ import annotations

from dataclasses import dataclass, field, replace
from typing import Callable, Union


def V(a0: int, a10: int | None = None) -> tuple:
    """(A0, A10) 数值；只给一个表示两档相同。"""
    return (a0, a0 if a10 is None else a10)


@dataclass(frozen=True)
class Move:
    dmg: tuple = (0, 0)
    hits: int = 0
    self_str: tuple = (0, 0)
    team_str: tuple = (0, 0)  # 全体盟友（含自己）加力量，原版 GetTeammatesOf 含自身。
    self_lead: tuple = (0, 0)  # 给自己叠领袖气质（加给其他盟友的攻击，不加自己）
    vuln: int = 0
    summon: str | None = None  # 召唤的怪物种类
    summon_count: int = 1


@dataclass(frozen=True)
class Rand:
    """随机分支：[(招式, 权重, rep)]，rep 含义见模块说明。"""
    branches: tuple


# 下一招：招式名 / Rand / 条件函数 (monster, world) -> 招式名或 Rand。
Next = Union[str, Rand, Callable]


@dataclass
class Kind:
    moves: dict
    next: dict
    leadership: tuple = (0, 0)
    summon_cap: int = 0  # 这种怪最多同时召唤几只（受遭遇战空槽限制）
    summon_start: Callable | None = None  # (world, summoned_kind) -> 召唤物起手招
    summon_front: bool = False  # 召唤物按槽位排到召唤者前面（召唤者占遭遇战最后一个槽）


def cycle(moves: list[tuple[str, Move]], **kw) -> Kind:
    names = [n for n, _ in moves]
    return Kind(dict(moves), {n: names[(i + 1) % len(names)] for i, n in enumerate(names)}, **kw)


def random_kind(moves: list[tuple[str, Move]], **kw) -> Kind:
    """苦难摇篮的 RandomMachine：起手固定，之后在其余招里等权随机、不连用。"""
    rand = Rand(tuple((n, 1.0, 1) for n, _ in moves))
    return Kind(dict(moves), {n: rand for n, _ in moves}, **kw)


@dataclass(frozen=True)
class Mon:
    kind: str
    state: str
    history: tuple = ()
    strength: int = 0
    start: int = 0
    summoned: int = 0
    lead: int = 0  # 招式叠加的领袖气质（开场层数在 Kind.leadership）
    order: float = 0.0  # 出手顺序键，敌方回合结束时按它重排（模拟原版按槽位排序）


def _branch(kinds: dict, mon: Mon, world: tuple, nxt: Next) -> list[tuple[float, str]]:
    if callable(nxt):
        nxt = nxt(mon, world)
    if isinstance(nxt, str):
        return [(1.0, nxt)]
    allowed = []
    for name, weight, rep in nxt.branches:
        if rep > 0 and len(mon.history) >= rep and all(h == name for h in mon.history[-rep:]):
            continue
        allowed.append((name, weight))
    total = sum(w for _, w in allowed)
    return [(w / total, n) for n, w in allowed]


def simulate(kinds: dict, lineup: list[tuple[str, str]], asc: int, turns: int = 4) -> dict:
    """lineup：[(种类, 起手招)]。返回 {伤害序列: 概率}。"""
    start = tuple(Mon(k, s, order=float(i)) for i, (k, s) in enumerate(lineup))
    # 易伤状态 = (层数, 本回合新挂上、跳过下一次掉层)。
    worlds = {(start, (0, False), ()): 1.0}
    for t in range(turns):
        nxt_worlds: dict = {}
        for (mons, vuln, dmg), p in worlds.items():
            for q, (mons2, (stacks, skip), total) in _turn(kinds, list(mons), vuln, t, asc):
                mons2 = tuple(sorted(mons2, key=lambda m: m.order))
                key = (mons2, (stacks if skip else max(0, stacks - 1), False), dmg + (total,))
                nxt_worlds[key] = nxt_worlds.get(key, 0.0) + p * q
        worlds = nxt_worlds
    out: dict = {}
    for (_, _, dmg), p in worlds.items():
        out[dmg] = out.get(dmg, 0.0) + p
    return out


def _turn(kinds, mons, vuln, t, asc):
    """一个敌方回合，按阵容顺序出手；返回 [(概率, (怪物, 易伤, 本回合伤害))]。"""
    results = []
    actors = [i for i, m in enumerate(mons) if m.start <= t]

    def step(k, mons, vuln, total, p):
        if k == len(actors):
            results.append((p, (tuple(mons), vuln, total)))
            return
        i = actors[k]
        m = mons[i]
        kind = kinds[m.kind]
        mv = kind.moves[m.state]
        mons = list(mons)
        if mv.hits:
            lead = sum(kinds[o.kind].leadership[asc] + o.lead for j, o in enumerate(mons) if j != i and o.start <= t)
            per = mv.dmg[asc] + m.strength + lead
            if vuln[0] > 0:
                per = int(per * 1.5)
            total += max(0, per) * mv.hits
        strength = m.strength + mv.self_str[asc]
        if mv.team_str[asc]:
            mons = [o if j == i else replace(o, strength=o.strength + mv.team_str[asc])
                    for j, o in enumerate(mons)]
            strength += mv.team_str[asc]
        if mv.vuln:
            # 玩家身上原来没有易伤才是新挂上的实例，跳过下一次掉层；叠层走 ModifyAmount，不跳。
            vuln = (vuln[0] + mv.vuln, vuln[1] or vuln[0] == 0)
        summoned = m.summoned
        if mv.summon:
            for _ in range(mv.summon_count):
                if summoned >= kind.summon_cap:
                    break
                summoned += 1
                sk = kinds[mv.summon]
                first = kind.summon_start(mons, mv.summon) if kind.summon_start else next(iter(sk.moves))
                # 排到召唤者前面：夹在前一个怪与召唤者之间，按召唤先后排。
                order = m.order - 0.5 + summoned * 0.01 if kind.summon_front else max(o.order for o in mons) + 1
                mons.append(Mon(mv.summon, first, (), 0, t + 1, order=order))
        acted = replace(m, history=m.history + (m.state,), strength=strength, summoned=summoned, lead=m.lead + mv.self_lead[asc])
        mons[i] = acted
        world = tuple(mons)
        for q, state in _branch(kinds, acted, world, kind.next[m.state]):
            mons2 = list(mons)
            mons2[i] = replace(acted, state=state)
            step(k + 1, mons2, vuln, total, p * q)

    step(0, mons, vuln, 0, 1.0)
    return results


def summarize(dist_list: list[dict]) -> dict:
    """多个阵容（等概率）合并后的统计：逐回合期望、逐回合最大、总伤期望/最大。"""
    merged: dict = {}
    for dist in dist_list:
        for seq, p in dist.items():
            merged[seq] = merged.get(seq, 0.0) + p / len(dist_list)
    turns = len(next(iter(merged)))
    mean = [sum(seq[t] * p for seq, p in merged.items()) for t in range(turns)]
    peak = [max(seq[t] for seq in merged) for t in range(turns)]
    return {
        "mean": mean,
        "max": peak,
        "total_mean": sum(mean),
        "total_max": max(sum(seq) for seq in merged),
    }
