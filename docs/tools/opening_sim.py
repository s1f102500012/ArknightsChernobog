"""苦难摇篮遭遇战的开局伤害，与原版蜂巢（vanilla_hive.py）同一口径对照（引擎与口径见 sim_core.py）。

用法：
  python3 opening_sim.py          逐场列出 A0 / A10 的期望与最大值，并和原版蜂巢同类型的均值对照
  python3 opening_sim.py --search 为每场重新挑起手位置（按下面的 TARGET 打分），改阵容或数值后用

起手写在 OPENINGS 里，必须与 src/Encounters/ChernobogEncounters.cs 的 SetOpenings 一致（值 = 招式序号，即 OpeningOffset）。
随机出招的怪（RandomMachine）第一招取 moves[偏移]，之后在其余招里等权随机、不连用。
"""
import sys
from itertools import combinations, product

from sim_core import V, Move, Kind, cycle, random_kind, simulate, summarize
import vanilla_hive

A = Move  # 简写


def fixed(*moves):
    return ("cycle", moves)


def rand(*moves):
    return ("random", moves)


# 招式：(招式名, Move)。数值 (A0, A10)，与 src/Monsters 的 DeadlyValue(A10, A0) 一致。
M = {
    "猎犬": rand(("扑咬", A(V(4, 5), 2)), ("撕咬", A(V(7, 8), 1)), ("环伺", A(self_str=V(1)))),
    "猎犬pro": fixed(("扑咬", A(V(4, 5), 2)), ("撕咬", A(V(8, 9), 1)), ("嚎叫", A(self_str=V(2, 3)))),
    "战士": fixed(("劈砍", A(V(8, 9), 1)), ("全力冲锋", A(V(6, 7), 1, self_str=V(2))), ("压制", A(V(5, 6), 1))),
    "战士组长": fixed(("劈砍", A(V(10, 11), 1)), ("全力冲锋", A(V(7, 8), 1, self_str=V(2))), ("督战", A())),
    "狙击手": fixed(("狙击", A(V(7, 8), 1)), ("狙击2", A(V(7, 8), 1)), ("瞄准", A(self_str=V(2, 3)))),
    "狙击手组长": fixed(("狙击", A(V(9, 10), 1)), ("标定目标", A(vuln=1)), ("双重狙击", A(V(4, 5), 2))),
    "传令兵组长": fixed(("呼叫增援", A(summon="战士")), ("殴打", A(V(5, 6), 1)), ("战旗", A(self_lead=V(1, 2)))),
    "盾卫": fixed(("推进", A(V(9, 10), 1)), ("盾击", A(V(15, 16), 1)), ("盾墙", A())),
    "盾卫组长": fixed(("推进", A(V(10, 11), 1)), ("盾击", A(V(15, 16), 1)), ("盾墙", A())),
    "迫击炮兵": fixed(("装填", A()), ("炮击", A(self_str=V(3, 4))), ("急速射", A(V(14, 16), 1))),
    "迫击炮兵组长": fixed(("装填", A()), ("炮击", A(V(20, 22), 1)), ("急速射", A(V(5, 6), 3))),
    "突袭战士": fixed(("降落重击", A(V(14, 16), 1)), ("起飞", A(self_str=V(2, 3)))),
    "突袭战士组长": fixed(("降落重击", A(V(18, 20), 1)), ("起飞", A(self_str=V(3, 4)))),
    "萨卡兹战士": fixed(("斩击", A(V(12, 13), 1)), ("斩击2", A(V(12, 13), 1)), ("仪式强化", A(self_str=V(3, 4)))),
    "萨卡兹术师": fixed(("源石法术", A(V(9, 10), 1)), ("献祭仪式", A(self_str=V(3, 4))), ("源石洪流", A(V(3, 4), 3))),
    "萨卡兹战士组长": fixed(("斩击", A(V(14, 15), 1)), ("斩击2", A(V(14, 15), 1)), ("仪式强化", A(self_str=V(4, 5)))),
    "萨卡兹术师组长": fixed(("源石法术", A(V(11, 12), 1)), ("献祭仪式", A(self_str=V(3, 4))), ("源石洪流", A(V(4, 5), 3))),
    "宿主士兵": rand(("劈砍", A(V(9, 10), 1)), ("猛扑", A(V(5, 6), 2)), ("溃烂撕咬", A(V(6, 7), 1))),
    "宿主拾荒者": rand(("乱砸", A(V(8, 9), 1)), ("撕扯", A(V(5, 6), 1)), ("翻找", A(self_str=V(1)))),
    "宿主流浪者": rand(("重击", A(V(9, 10), 1)), ("嘶吼", A(self_str=V(2, 3))), ("蹒跚冲撞", A(V(5, 6), 1))),
    "宿主士兵组长": fixed(("劈砍", A(V(6, 7), 1)), ("牧群号令", A()), ("连斩", A(V(4, 5), 2))),
    "狂暴宿主士兵": rand(("狂斩", A(V(7, 8), 3)), ("撕裂", A(V(18, 20), 1)), ("狂嚎", A(self_str=V(3, 4)))),
    "狂暴宿主投掷手": rand(("投掷", A(V(14, 15), 1)), ("乱掷", A(V(5, 6), 3)), ("砸石", A(V(8, 9), 1))),
    "狂暴宿主组长": fixed(("狂暴连斩", A(V(9, 10), 3)), ("狂乱之嚎", A(self_str=V(2, 3))), ("处决", A(V(26, 28), 1))),
    "特战士兵": fixed(("伏击", A(V(11, 12), 1)), ("连刺", A(V(5, 6), 2)), ("隐蔽", A())),
    "特战术师": fixed(("源石冲击", A(V(8, 9), 1)), ("腐蚀法术", A(V(6, 7), 1)), ("源石屏障", A())),
    "法术大师A1": fixed(("法术射线", A(V(3, 4), 3)), ("法术射线2", A(V(3, 4), 3)), ("超载", A(self_str=V(2, 3)))),
    # 爱国者一阶段（二阶段在重生之后，开局 4 回合内打不到）。残甲只影响玩家打它，不影响它的出伤。
    "爱国者": fixed(("行军", A()), ("长戟四连", A(V(3, 4), 4)), ("盾击", A(V(14, 15), 1))),
}
LEADERSHIP = {"传令兵组长": V(2, 3)}
SUMMON_CAP = {"传令兵组长": 2}


def build_kinds() -> dict:
    kinds = {}
    for name, (mode, moves) in M.items():
        kw = {"leadership": LEADERSHIP.get(name, (0, 0)), "summon_cap": SUMMON_CAP.get(name, 0)}
        if name in SUMMON_CAP:
            # 与游戏里一致：召来的战士起手用场上第一只战士本回合出的招。
            kw["summon_start"] = lambda mons, kind: next((m.history[-1] for m in mons if m.kind == kind and m.history),
                                                         M[kind][1][0][0])
            # 增援槽在传令兵组长的槽前面，原版按槽位重排后召来的战士先于组长出手（战旗叠的领袖气质只影响下回合的意图）。
            kw["summon_front"] = True
        kinds[name] = (random_kind if mode == "random" else cycle)(list(moves), **kw)
    return kinds


KINDS = build_kinds()
HOST_WORKERS = ["宿主士兵", "宿主拾荒者", "宿主流浪者"]

# 阵容 = 种类列表，或种类列表的列表（随机阵容，等概率）。
ENCOUNTERS = {
    "弱|猎犬群": ["猎犬", "猎犬"],
    "弱|失控的牧群": ["宿主士兵", "宿主士兵"],
    "弱|空降兵": ["突袭战士"],
    "弱|整合运动残党": ["特战士兵", "法术大师A1"],
    "普|猎犬群": ["猎犬", "猎犬", "猎犬pro"],
    "普|梅菲斯特的牧群": [["宿主士兵组长", a, b] for a, b in combinations(HOST_WORKERS, 2)],
    "普|狂暴宿主": [["狂暴宿主士兵"], ["狂暴宿主投掷手"]],
    "普|特战分队": ["特战士兵", "特战术师", "法术大师A1"],
    "普|游击队突击组": ["战士组长", "战士", "战士"],
    "普|狙击阵地": ["狙击手", "狙击手", "狙击手组长"],
    "普|炮击阵地": ["盾卫", "迫击炮兵"],
    "普|浸染": ["萨卡兹战士", "萨卡兹术师"],
    "普|空降小队": ["突袭战士", "突袭战士"],
    "普|增援信号": ["战士", "传令兵组长"],
    "精|感染者之盾": ["盾卫组长", "迫击炮兵组长"],
    "精|垂直打击": ["突袭战士组长", "突袭战士"],
    "精|萨卡兹仪式": ["萨卡兹战士组长", "萨卡兹术师组长"],
    "精|狂暴宿主组长": ["狂暴宿主组长"],
    "首|爱国者": ["战士", "战士", "爱国者"],
}
# 每场的起手偏移：固定阵容按站位；随机阵容按种类（{种类: 偏移}，未列出的为 0）。
OPENINGS = {
    "弱|猎犬群": (0, 1),
    "弱|失控的牧群": (0, 2),
    "弱|空降兵": (0,),
    "弱|整合运动残党": (2, 0),
    "普|猎犬群": (1, 2, 2),
    "普|梅菲斯特的牧群": {"宿主士兵组长": 1, "宿主拾荒者": 1, "宿主流浪者": 1},
    "普|狂暴宿主": {"狂暴宿主士兵": 1},
    "普|特战分队": (2, 2, 0),
    "普|游击队突击组": (2, 0, 2),
    "普|狙击阵地": (1, 2, 1),
    "普|炮击阵地": (1, 0),
    "普|浸染": (2, 2),
    "普|空降小队": (0, 1),
    "普|增援信号": (2, 0),
    "精|感染者之盾": (2, 1),
    "精|垂直打击": (0, 1),
    "精|萨卡兹仪式": (2, 2),
    "精|狂暴宿主组长": (2,),
    "首|爱国者": (0, 2, 0),
}
# 固定起手（不参与搜索）：传令兵组长先呼叫增援（同原版卵翼虫开场下蛋），组长空降兵先降落。
FIXED = {"传令兵组长": [0], "突袭战士组长": [0]}


def lineups_of(key):
    v = ENCOUNTERS[key]
    return v if isinstance(v[0], list) else [v]


def start_state(kind, offset):
    moves = M[kind][1]
    return moves[offset % len(moves)][0]


def run(key, openings, asc):
    dists = []
    for lu in lineups_of(key):
        if isinstance(openings, dict):
            offs = [openings.get(k, 0) for k in lu]
        else:
            offs = openings
        dists.append(simulate(KINDS, [(k, start_state(k, o)) for k, o in zip(lu, offs)], asc))
    return summarize(dists)


# 按原版蜂巢同类型遭遇战 A0 的期望值定目标（见 vanilla_hive.py 的输出与 docs/战斗设计.md）。
#   first：第 1 回合期望上限；total：前 4 回合总伤期望的目标；peak：任一回合最大值上限。
TARGET = {
    "弱": {"first": 20, "total": 62, "peak": 33},
    "普": {"first": 16, "total": 66, "peak": 33},
    "精": {"first": 24, "total": 78, "peak": 36},
    "首": {"first": 15, "total": 70, "peak": 38},
}


def score(kind, s):
    t = TARGET[kind]
    over_first = max(0.0, s["mean"][0] - t["first"])
    over_peak = sum(max(0, x - t["peak"]) for x in s["max"])
    return over_first * 3 + over_peak * 2 + abs(s["total_mean"] - t["total"])


def search(key):
    kind = key.split("|")[0]
    lus = lineups_of(key)
    randomized = len(lus) > 1
    names = sorted({n for lu in lus for n in lu}) if randomized else lus[0]
    best = None
    for combo in product(*(FIXED.get(n) or range(len(M[n][1])) for n in names)):
        if not randomized and any(names[a] == names[b] and combo[a] == combo[b]
                                  for a in range(len(names)) for b in range(a + 1, len(names))):
            continue  # 同一场里的同种怪起手必须错开
        openings = dict(zip(names, combo)) if randomized else combo
        s = run(key, openings, 0)
        sc = (round(score(kind, s), 1), sum(1 for c in combo if c))
        if best is None or sc < best[0]:
            best = (sc, openings, s)
    return best


def fmt(xs):
    return "/".join(f"{x:.1f}".rstrip("0").rstrip(".") for x in xs)


def describe(key, openings):
    lu = lineups_of(key)[0] if not isinstance(openings, dict) else None
    if lu is None:
        return " ".join(f"{k}:{start_state(k, o)}" for k, o in openings.items()) or "全部第一招"
    return " ".join(f"{k}:{start_state(k, o)}" for k, o in zip(lu, openings))


def report():
    vanilla = {0: vanilla_hive.run(0), 1: vanilla_hive.run(1)}
    for asc, label in ((0, "A0"), (1, "A10")):
        print(f"== 苦难摇篮 {label}（前 4 个敌方回合：期望 | 最大 | 总伤期望/最大）")
        groups: dict = {}
        for key in ENCOUNTERS:
            kind, title = key.split("|")
            s = run(key, OPENINGS[key], asc)
            groups.setdefault(kind, []).append(s)
            print(f"{kind} {title:8s} 期望 {fmt(s['mean']):24s} 最大 {fmt(s['max']):14s} 总 {s['total_mean']:5.1f}/{s['total_max']}")
        print(f"-- 类型均值（苦难摇篮 vs 原版蜂巢 {label}）：第 1 回合期望 / 总伤期望")
        for kind, ss in groups.items():
            vs = [s for (k, _), s in vanilla[asc].items() if k == kind]
            ours = (sum(s["mean"][0] for s in ss) / len(ss), sum(s["total_mean"] for s in ss) / len(ss))
            base = (sum(s["mean"][0] for s in vs) / len(vs), sum(s["total_mean"] for s in vs) / len(vs))
            print(f"{kind}  {ours[0]:5.1f} / {ours[1]:5.1f}    原版 {base[0]:5.1f} / {base[1]:5.1f}")


if __name__ == "__main__":
    if "--search" in sys.argv:
        for key in ENCOUNTERS:
            sc, openings, s = search(key)
            cur = run(key, OPENINGS[key], 0)
            mark = "" if openings == OPENINGS[key] else f"  （现为 {OPENINGS[key]}，分 {round(score(key.split('|')[0], cur), 1)}）"
            print(f"{key:14s} {openings}  分 {sc[0]}  期望 {fmt(s['mean'])} 总 {s['total_mean']:.1f}  {describe(key, openings)}{mark}")
    else:
        report()
