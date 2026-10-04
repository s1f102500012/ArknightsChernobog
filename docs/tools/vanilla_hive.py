"""原版第二幕蜂巢全部遭遇战的开局伤害（口径见 sim_core.py），作为苦难摇篮调难度的基准。

招式与数值逐个取自反编译的 MegaCrit.Sts2.Core.Models.Monsters.*（数值写 (A0, A10)）。
近似：碗虫岩石“失衡”要玩家完全格挡才触发，这里玩家不出格挡，所以一直头槌；
凯撒蟹的“包围”（背后攻击 ×1.5）取决于玩家朝向，这里不计。
"""
from itertools import permutations

from sim_core import V, Move, Rand, Kind, cycle, simulate, summarize

K = {
    # —— 弱怪 ——
    "外骨骼虫": Kind(
        {"SKITTER": Move(V(1), 3), "SKITTER4": Move(V(1), 4), "MANDIBLES": Move(V(8, 9), 1), "ENRAGE": Move(self_str=V(2))},
        {}),
    "钻地虫": cycle([("BITE", Move(V(13, 15), 1)), ("BURROW", Move()), ("BELOW", Move(V(23, 26), 1))]),
    "窃贼跳蚤": cycle([("THIEVERY", Move(V(17, 19), 1)), ("FLUTTER", Move()), ("HAT_TRICK", Move(V(21, 23), 1)),
                    ("NAB", Move(V(14, 16), 1)), ("ESCAPE", Move())]),
    "岩石碗虫": cycle([("HEADBUTT", Move(V(15, 16), 1))]),
    "丝线碗虫": cycle([("SPIT", Move()), ("THRASH", Move(V(4, 5), 2))]),
    "花蜜碗虫": Kind({"THRASH": Move(V(3), 1), "BUFF": Move(self_str=V(15, 16)), "THRASH2": Move(V(3), 1)},
                 {"THRASH": "BUFF", "BUFF": "THRASH2", "THRASH2": "THRASH2"}),
    "蛋碗虫": cycle([("BITE", Move(V(7, 8), 1))]),
    # —— 普通 ——
    "咀嚼者": cycle([("CLAMP", Move(V(8, 9), 2)), ("SCREECH", Move())]),
    "猎杀者": Kind({"GOOP": Move(), "BITE": Move(V(17, 19), 1), "PUNCTURE": Move(V(7, 8), 3)}, {}),
    "虱子始祖": cycle([("WEB", Move(V(9, 10), 1)), ("CURL_GROW", Move(self_str=V(5, 7))), ("POUNCE", Move(V(14, 16), 1))]),
    "螨虫": cycle([("TOXIC", Move()), ("BITE", Move(V(13, 15), 1)), ("SUCK", Move(V(4, 6), 1, self_str=V(2, 3)))]),
    "带刺蟾蜍": cycle([("SPIKES", Move()), ("EXPLOSION", Move(V(23, 25), 1)), ("LASH", Move(V(17, 19), 1))]),
    "卵翼虫": Kind({"LAY": Move(summon="坚硬虫卵", summon_count=3), "SMASH": Move(V(16, 17), 1),
                 "TENDERIZER": Move(V(7, 8), 1, vuln=2), "PASTE": Move(self_str=V(3, 4))},
                {"LAY": "SMASH", "PASTE": "SMASH", "SMASH": "TENDERIZER",
                 # CanLay：存活的己方（含自己）≤3 只才下蛋，否则吃营养糊加力量。
                 "TENDERIZER": lambda m, w: "LAY" if len(w) <= 3 else "PASTE"},
                summon_cap=5, summon_front=True),  # 蛋槽 egg1–5 在卵翼虫槽前面
    "坚硬虫卵": Kind({"HATCH": Move(), "NIBBLE": Move(V(4, 5), 1)}, {"HATCH": "NIBBLE", "NIBBLE": "NIBBLE"}),
    "沉睡甲虫": Kind({"SNORE": Move(), "ROLL_OUT": Move(V(16, 18), 1, self_str=V(2))},
                 # 沉睡 3 层，每个敌方回合结束掉 1 层，归零醒来；玩家不打它，所以睡满 3 回合。
                 {"SNORE": lambda m, w: "SNORE" if len(m.history) < 3 else "ROLL_OUT", "ROLL_OUT": "ROLL_OUT"}),
    "窥视者": Kind({"ILLUSION": Move(summon="惊惧幻影"), "GAZE": Move(V(10, 11), 1), "WAIL": Move(team_str=V(3)),
                 "HARDENING": Move(V(6, 7), 1)}, {}, summon_cap=1, summon_front=True),  # 槽位 illusion 在 obscura 前面
    "惊惧幻影": cycle([("SLAM", Move(V(16, 17), 1))]),
    # —— 精英 ——
    "虫群术士": Kind({"BEES": Move(V(3), 7), "SPEAR": Move(V(18, 20), 1), "SPIT": Move(self_str=V(1))},
                 {"BEES": "SPEAR", "SPEAR": "SPIT", "SPIT": "BEES"}),
    "感染棱镜": cycle([("JAB", Move(V(15, 17), 1)), ("RADIATE", Move(V(11, 13), 1)), ("WHIRLWIND", Move(V(5, 6), 3)),
                    ("PULSATE", Move(V(8, 10), 1))]),
    "千足虫节": cycle([("CONSTRICT", Move(V(8, 9), 1)), ("BULK", Move(V(6, 7), 1, self_str=V(2))), ("WRITHE", Move(V(5, 6), 2))]),
    # —— Boss ——
    "暴食者": Kind({"LIQUIFY": Move(), "THRASH": Move(V(8, 9), 2), "BITE": Move(V(28, 31), 1),
                 "SALIVATE": Move(self_str=V(2, 3)), "THRASH2": Move(V(8, 9), 2)},
                {"LIQUIFY": "THRASH", "THRASH": "BITE", "BITE": "SALIVATE", "SALIVATE": "THRASH2", "THRASH2": "THRASH"}),
    "知识恶魔": cycle([("CURSE", Move()), ("SLAP", Move(V(17, 18), 1)), ("OVERWHELM", Move(V(8, 9), 3)),
                    ("PONDER", Move(V(11, 13), 1, self_str=V(2, 3)))]),
    "凯撒蟹·钳": cycle([("THRASH", Move(V(12, 14), 1)), ("ENLARGING", Move(V(4), 1)), ("STING", Move(V(6, 7), 2)),
                     ("ADAPT", Move(self_str=V(2, 3))), ("GUARDED", Move(V(12, 14), 1))]),
    "凯撒蟹·炮": cycle([("RETICLE", Move(V(3, 4), 1)), ("BEAM", Move(V(18, 20), 1)), ("CHARGE", Move(self_str=V(2, 3))),
                     ("LASER", Move(V(31, 35), 1)), ("RECHARGE", Move())]),
}
# 外骨骼虫：撕咬 → 激怒，其余进随机（快爬/撕咬，不连用）。快爬段数 A9 起 4 段。
_exo_rand = lambda asc: Rand((("SKITTER4" if asc else "SKITTER", 1.0, 1), ("MANDIBLES", 1.0, 1)))
# 猎杀者：撕咬不能连用，穿刺最多连用 2 次。
K["猎杀者"].next = {s: Rand((("BITE", 1.0, 1), ("PUNCTURE", 1.0, 2))) for s in ("GOOP", "BITE", "PUNCTURE")}
_obscura = Rand((("GAZE", 1.0, 1), ("WAIL", 1.0, 1), ("HARDENING", 1.0, 1)))
K["窥视者"].next = {s: _obscura for s in K["窥视者"].moves}


def exo_kinds(asc: int) -> dict:
    kinds = dict(K)
    r = _exo_rand(asc)
    skitter = "SKITTER4" if asc else "SKITTER"
    kinds["外骨骼虫"] = Kind(K["外骨骼虫"].moves, {"SKITTER": r, "SKITTER4": r, "MANDIBLES": "ENRAGE", "ENRAGE": r})
    return kinds, skitter


def lineups(asc: int):
    """{(类型, 名称): [阵容, ...]}，阵容 = [(种类, 起手)]，列表里各阵容等概率。"""
    _, sk = exo_kinds(asc)
    exo_rand_start = "RAND"
    bugs_normal = [[("岩石碗虫", "HEADBUTT"), (a, START[a]), (b, START[b])]
                   for a, b in permutations(["蛋碗虫", "丝线碗虫", "花蜜碗虫"], 2)]
    deci = [[("千足虫节", s[(r + i) % 3]) for i in range(3)] for r in range(3) for s in [("WRITHE", "BULK", "CONSTRICT")]]
    return {
        ("弱", "碗虫（弱）"): [[("岩石碗虫", "HEADBUTT"), (b, START[b])] for b in ("蛋碗虫", "花蜜碗虫")],
        ("弱", "外骨骼虫 ×3"): [[("外骨骼虫", sk), ("外骨骼虫", "MANDIBLES"), ("外骨骼虫", "ENRAGE")]],
        ("弱", "窃贼跳蚤"): [[("窃贼跳蚤", "THIEVERY")]],
        ("弱", "钻地虫"): [[("钻地虫", "BITE")]],
        ("普", "碗虫"): bugs_normal,
        ("普", "咀嚼者 ×2"): [[("咀嚼者", "CLAMP"), ("咀嚼者", "SCREECH")]],
        ("普", "外骨骼虫 ×4"): [[("外骨骼虫", sk), ("外骨骼虫", "MANDIBLES"), ("外骨骼虫", "ENRAGE"), ("外骨骼虫", exo_rand_start)]],
        ("普", "猎杀者"): [[("猎杀者", "GOOP")]],
        ("普", "虱子始祖"): [[("虱子始祖", "WEB")]],
        ("普", "螨虫 ×2"): [[("螨虫", "TOXIC"), ("螨虫", "SUCK")]],
        ("普", "卵翼虫"): [[("卵翼虫", "LAY")]],
        ("普", "沉睡甲虫组"): [[("岩石碗虫", "HEADBUTT"), ("丝线碗虫", "SPIT"), ("沉睡甲虫", "SNORE")]],
        ("普", "带刺蟾蜍"): [[("带刺蟾蜍", "SPIKES")]],
        ("普", "窥视者"): [[("窥视者", "ILLUSION")]],
        ("精", "虫群术士"): [[("虫群术士", "BEES")]],
        ("精", "感染棱镜"): [[("感染棱镜", "JAB")]],
        ("精", "残杀千足虫"): deci,
        ("首", "暴食者"): [[("暴食者", "LIQUIFY")]],
        ("首", "知识恶魔"): [[("知识恶魔", "CURSE")]],
        ("首", "凯撒蟹"): [[("凯撒蟹·钳", "THRASH"), ("凯撒蟹·炮", "RETICLE")]],
    }


START = {"蛋碗虫": "BITE", "丝线碗虫": "SPIT", "花蜜碗虫": "THRASH"}


def run(asc: int) -> dict:
    kinds, _ = exo_kinds(asc)
    out = {}
    for key, lus in lineups(asc).items():
        dists = []
        for lu in lus:
            if any(s == "RAND" for _, s in lu):
                # 第 4 只的第 1 招在快爬/撕咬里等概率。
                sk = "SKITTER4" if asc else "SKITTER"
                for first in (sk, "MANDIBLES"):
                    dists.append(simulate(kinds, [(k, first if s == "RAND" else s) for k, s in lu], asc))
            else:
                dists.append(simulate(kinds, lu, asc))
        out[key] = summarize(dists)
    return out


def fmt(xs):
    return "/".join(f"{x:4.1f}".rstrip("0").rstrip(".") if isinstance(x, float) else str(x) for x in xs)


if __name__ == "__main__":
    for asc, label in ((0, "A0"), (1, "A10")):
        print(f"== 原版蜂巢 {label}（前 4 个敌方回合：期望 | 最大 | 总伤期望/最大）")
        for (kind, name), s in run(asc).items():
            print(f"{kind} {name:10s} 期望 {fmt([round(x, 1) for x in s['mean']]):24s} 最大 {fmt(s['max']):16s} "
                  f"总 {s['total_mean']:5.1f}/{s['total_max']}")
