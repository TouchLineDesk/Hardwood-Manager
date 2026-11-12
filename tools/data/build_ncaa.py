"""Builds src/Hardwood.Core/RealWorld/Data/ncaa_programs.json from ncaa_rows.txt (R3, run 24).
Run: python3 tools/data/build_ncaa.py"""
import json, os
here = os.path.dirname(__file__)
CONFS = [  # key, name, short, power
 ("ae","America East Conference","America East",False),("aac","American Athletic Conference","American",False),
 ("acc","Atlantic Coast Conference","ACC",True),("asun","ASUN Conference","ASUN",False),("a10","Atlantic 10 Conference","A-10",False),
 ("bige","Big East Conference","Big East",True),("bsky","Big Sky Conference","Big Sky",False),("bsou","Big South Conference","Big South",False),
 ("b1g","Big Ten Conference","Big Ten",True),("b12","Big 12 Conference","Big 12",True),("bw","Big West Conference","Big West",False),
 ("caa","Coastal Athletic Association","CAA",False),("cusa","Conference USA","C-USA",False),("hl","Horizon League","Horizon",False),
 ("ivy","Ivy League","Ivy",False),("maac","Metro Atlantic Athletic Conference","MAAC",False),("mac","Mid-American Conference","MAC",False),
 ("meac","Mid-Eastern Athletic Conference","MEAC",False),("mvc","Missouri Valley Conference","MVC",False),("mwc","Mountain West Conference","Mountain West",False),
 ("nec","Northeast Conference","NEC",False),("ovc","Ohio Valley Conference","OVC",False),("pac","Pac-12 Conference","Pac-12",False),
 ("pat","Patriot League","Patriot",False),("sec","Southeastern Conference","SEC",True),("socon","Southern Conference","SoCon",False),
 ("slc","Southland Conference","Southland",False),("swac","Southwestern Athletic Conference","SWAC",False),("summit","Summit League","Summit",False),
 ("sbc","Sun Belt Conference","Sun Belt",False),("uac","United Athletic Conference","UAC",False),("wcc","West Coast Conference","WCC",False)]
# NCAA tournament appearances 2016-2026 (3 or more; from the same page's year lists).
RECENT = {"Vermont":5,"Memphis":3,"Wichita State":4,"Clemson":5,"Duke":9,"Florida State":4,"Louisville":4,"Miami (FL)":6,"North Carolina":9,
 "NC State":4,"Notre Dame":3,"Syracuse":4,"Virginia":8,"Virginia Tech":5,"Dayton":3,"Loyola Chicago":3,"VCU":7,"Butler":3,"Creighton":7,
 "Marquette":6,"Providence":5,"St. John's":3,"Seton Hall":5,"UConn":7,"Villanova":7,"Xavier":5,"Montana":3,"Montana State":3,"UCLA":7,
 "Illinois":6,"Indiana":3,"Iowa":6,"Maryland":6,"Michigan":8,"Michigan State":10,"Northwestern":3,"Ohio State":5,"Oregon":6,"Purdue":10,
 "USC":5,"Wisconsin":8,"Arizona":8,"Arizona State":3,"Baylor":8,"BYU":4,"Cincinnati":4,"Colorado":3,"Houston":8,"Iowa State":8,"Kansas":10,
 "Kansas State":4,"TCU":5,"Texas Tech":8,"West Virginia":5,"Charleston":3,"UNC Wilmington":3,"Liberty":3,"New Mexico State":4,
 "Northern Kentucky":3,"Wright State":3,"Yale":5,"Iona":6,"Mount St. Mary's":3,"Akron":4,"Buffalo":3,"Howard":3,"Norfolk State":3,
 "North Carolina Central":3,"Drake":4,"Murray State":3,"Grand Canyon":4,"Nevada":5,"Fairleigh Dickinson":3,"Boise State":3,"Colorado State":3,
 "Gonzaga":10,"San Diego State":6,"Utah State":6,"Colgate":5,"Alabama":7,"Arkansas":7,"Auburn":6,"Florida":7,"Kentucky":8,"LSU":3,
 "Mississippi State":4,"Missouri":5,"Oklahoma":5,"Tennessee":8,"Texas":8,"Texas A&M":6,"Vanderbilt":4,"McNeese":3,"Texas Southern":5,
 "South Dakota State":5,"Georgia State":3,"Troy":3,"Saint Mary's":7}
programs = []
for line in open(os.path.join(here, "ncaa_rows.txt")):
    line = line.strip()
    if not line or line.startswith("#"): continue
    s, nick, conf, state, arena, a, f, t, p1, p2 = line.split("|")
    programs.append(dict(school=s, nickname=nick, conference=conf, state=state, arena=arena,
                         appearances=int(a), finalFours=int(f), titles=int(t), recent=RECENT.get(s, 0), primary=p1, secondary=p2))
assert len(programs) == len({p["school"] for p in programs}), "duplicate school"
assert set(RECENT) <= {p["school"] for p in programs}, set(RECENT) - {p["school"] for p in programs}
out = dict(
    checked="2026-11-12",
    source="Wikipedia, 'List of NCAA Division I men's basketball programs' (2026-27 conference alignment; school, nickname, conference, home arena, "
           "NCAA tournament appearances, Final Fours, titles, and the years used for 'recent' = appearances 2016-2026) and 'List of NCAA Division I "
           "institutions' (state), both read Nov 12, 2026.",
    notes="Colours are the schools' usual primary and secondary colours from general knowledge (not fetched this run; school colours rarely change). "
          "Arena capacities are not in the data: the game sizes arenas from prestige. 'recent' counts only programs with 3+ appearances in 2016-2026.",
    conferences=[dict(key=k, name=n, short=sh, power=pw) for k, n, sh, pw in CONFS],
    programs=programs)
dst = os.path.join(here, "..", "..", "src", "Hardwood.Core", "RealWorld", "Data", "ncaa_programs.json")
with open(dst, "w") as fh: json.dump(out, fh, indent=1, ensure_ascii=False)
from collections import Counter
print(len(programs), "programs;", Counter(p["conference"] for p in programs))
