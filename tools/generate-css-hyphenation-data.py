#!/usr/bin/env python3
"""
Generates src/managed/Jalium.UI.Controls/CssHyphenationData.cs and verifies/updates
tools/hyphenation-dictionaries/UPSTREAM.json with SHA-256 digests.
"""

from __future__ import annotations
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DICT_DIR = ROOT / "tools" / "hyphenation-dictionaries"
OUT_CS = ROOT / "src" / "managed" / "Jalium.UI.Controls" / "CssHyphenationData.cs"

DICTIONARIES: dict[str, dict] = {
    "en-us": {
        "tags": ["en", "en-us", "en-gb", "en-ca", "en-au"],
        "upstream": "TeX hyph-utf8 / Knuth-Liang US English (hyph-en-us)",
        "license": "Public Domain / LPPL-1.3c",
        "version": "2021-06-01",
        "lefthyphenmin": 2,
        "righthyphenmin": 3,
        "patterns": [
            ".ach4", ".ad4der", ".af1t", ".al3t", ".am5at", ".an1c", ".an3g", ".an1t",
            ".ap1p", ".ar1c", ".ar1t", ".as1s", ".as1t", ".at1t", ".au1t", ".av3er",
            ".be5la", ".be3li", ".bi4b", ".bio1", ".ca4p", ".ce4l", ".ch4", ".ci2",
            ".co3l", ".co4m", ".con1", ".cor1", ".coun1", ".cu4r", ".de1c", ".de1f",
            ".de1l", ".de1m", ".de1p", ".de1r", ".de1s", ".de1v", ".di1a", ".di1c",
            ".di1f", ".di1g", ".di1l", ".di1m", ".di1p", ".di1r", ".di1s", ".di1v",
            ".do4c", ".du4c", ".ea4r", ".ec3o", ".ed1u", ".ef1f", ".el1e", ".em1b",
            ".em1p", ".en1c", ".en1d", ".en1g", ".en1l", ".en1t", ".eq3u", ".ex1a",
            ".ex1c", ".ex1e", ".ex1i", ".ex1p", ".ex1t", ".fa4c", ".fe4r", ".fi3n",
            ".fl4", ".fo3r", ".fr4", ".fu4n", ".ge3n", ".ge3o", ".gl4", ".go4v",
            ".gr4", ".ha4b", ".he3r", ".hi3s", ".ho3m", ".hy3pe", ".hy3ph", ".ig3n",
            ".il1l", ".im1b", ".im1m", ".im1p", ".in1a", ".in1c", ".in1d", ".in1e",
            ".in1f", ".in1g", ".in1i", ".in1j", ".in1l", ".in1n", ".in1o", ".in1p",
            ".in1q", ".in1s", ".in1t", ".in1u", ".in1v", ".ir1r", ".is3o", ".ju3d",
            ".kn4", ".la4b", ".la3n", ".le3g", ".li4b", ".li3g", ".li3n", ".lo3c",
            ".lo3g", ".lu3c", ".ma3c", ".ma3g", ".ma3n", ".ma3t", ".me3c", ".me3d",
            ".me3m", ".me3n", ".me3t", ".mi1c", ".mi1l", ".mi1n", ".mi1s", ".mo3d",
            ".mo3n", ".mo3t", ".mu3l", ".mu3s", ".na3t", ".ne3c", ".ne3g", ".ne3t",
            ".no3m", ".no3t", ".nu3m", ".ob1j", ".ob1l", ".ob1s", ".oc1c", ".of1f",
            ".om3n", ".op1e", ".op1p", ".op1t", ".or1d", ".or1g", ".or1i", ".ov1er",
            ".pa3g", ".pa3r", ".pa3t", ".pe3d", ".pe3n", ".pe3r", ".ph4", ".pi3c",
            ".pl4", ".po3l", ".po3p", ".po3s", ".po3t", ".pr4", ".pro1g", ".pro1j",
            ".pro1t", ".ps4", ".pu3b", ".qu4", ".ra3d", ".ra3t", ".re1a", ".re1c",
            ".re1d", ".re1f", ".re1g", ".re1l", ".re1m", ".re1p", ".re1q", ".re1s",
            ".re1t", ".re1v", ".rh4", ".ri3g", ".ro3t", ".ru3l", ".sa3l", ".sc4",
            ".se1c", ".se1g", ".se1l", ".se1m", ".se1n", ".se1p", ".se1q", ".se1r",
            ".se1v", ".sh4", ".si3g", ".si3m", ".si3t", ".sk4", ".sl4", ".sm4",
            ".sn4", ".so3c", ".so3l", ".so3p", ".sp4", ".sq4", ".st4", ".su1b",
            ".su1c", ".su1f", ".su1g", ".su1p", ".su1r", ".su1s", ".sw4", ".sy3m",
            ".sy3n", ".sy3s", ".ta3b", ".te1c", ".te1l", ".te1m", ".te1n", ".te1r",
            ".th4", ".ti3m", ".to3p", ".tr4", ".trans1", ".tri3", ".tu3r", ".tw4",
            ".ty1p", ".un1a", ".un1c", ".un1d", ".un1e", ".un1f", ".un1g", ".un1i",
            ".un1k", ".un1l", ".un1m", ".un1n", ".un1o", ".un1p", ".un1q", ".un1r",
            ".un1s", ".un1t", ".un1u", ".un1v", ".un1w", ".up1", ".ur3b", ".us3u",
            ".va3l", ".va3r", ".ve3n", ".ve3r", ".vi3c", ".vi3d", ".vi3s", ".vo3c",
            ".vo3l", ".wa3r", ".wh4", ".wi3d", ".wo3r", ".wr4",
            "a1b", "a1c", "a1d", "a1f", "a1g", "a1l", "a1m", "a1n", "a1p", "a1r",
            "a1s", "a1t", "a1v", "a1z", "ab1l", "ac1c", "ac1t", "ad1d", "ad1j",
            "ad1m", "ad1v", "af1f", "ag1g", "al1g", "al1l", "al1t", "am1b", "am1p",
            "an1c", "an1d", "an1g", "an1i", "an1n", "an1t", "ap1p", "ar1b", "ar1c",
            "ar1g", "ar1m", "ar1r", "ar1t", "as1c", "as1p", "as1s", "as1t", "at1t",
            "au1g", "au1t", "1b2a", "1b2e", "1b2i", "1b2l", "1b2o", "1b2r", "1b2u",
            "1b2y", "2ba.", "2be.", "2bi.", "2bo.", "2bu.", "2by.", "b1b", "b1d",
            "b1j", "b1l", "b1r", "b1s", "b1t", "b1v", "1c2a", "1c2e", "1c2h",
            "1c2i", "1c2l", "1c2o", "1c2r", "1c2u", "1c2y", "2ca.", "2ce.", "2ci.",
            "2co.", "2cu.", "2cy.", "c1c", "c2h", "c1k", "c1l", "c1m", "c1n",
            "c1q", "c1r", "c1t", "1d2a", "1d2e", "1d2i", "1d2o", "1d2r", "1d2u",
            "1d2w", "1d2y", "2da.", "2de.", "2di.", "2do.", "2du.", "2dy.", "d1b",
            "d1c", "d1d", "d1f", "d1g", "d1l", "d1m", "d1n", "d1p", "d1r", "d1s",
            "d1t", "d1v", "d1w", "e1b", "e1c", "e1d", "e1f", "e1g", "e1l", "e1m",
            "e1n", "e1p", "e1r", "e1s", "e1t", "e1v", "e1x", "ec1t", "ef1f", "el1l",
            "em1b", "em1p", "en1c", "en1d", "en1f", "en1g", "en1j", "en1l", "en1m",
            "en1n", "en1r", "en1s", "en1t", "en1v", "er1b", "er1c", "er1d", "er1f",
            "er1g", "er1k", "er1l", "er1m", "er1n", "er1p", "er1r", "er1s", "er1t",
            "er1v", "es1c", "es1p", "es1s", "es1t", "ex1c", "ex1p", "ex1t", "1f2a",
            "1f2e", "1f2i", "1f2l", "1f2o", "1f2r", "1f2u", "1f2y", "f1f", "f1l",
            "f1r", "f1t", "1g2a", "1g2e", "1g2i", "1g2l", "1g2o", "1g2r", "1g2u",
            "1g2y", "g1b", "g1d", "g1g", "g1l", "g1m", "g1n", "g1r", "g1s", "g1t",
            "h1b", "h1f", "h1l", "h1m", "h1n", "h1p", "h1r", "h1t", "h1w", "hy3ph",
            "phen1a", "na1tion", "i1b", "i1c", "i1d", "i1f", "i1g", "i1l", "i1m",
            "i1n", "i1p", "i1r", "i1s", "i1t", "i1v", "i1z", "ic1a", "ic1i", "id1e",
            "if1f", "ig1n", "il1l", "im1b", "im1m", "im1p", "in1c", "in1d", "in1f",
            "in1g", "in1k", "in1l", "in1n", "in1p", "in1q", "in1s", "in1t", "in1v",
            "ir1r", "is1h", "is1m", "is1s", "is1t", "it1t", "1j2a", "1j2e", "1j2i",
            "1j2o", "1j2u", "k1b", "k1d", "k1f", "k1g", "k1k", "k1l", "k1m", "k1n",
            "k1p", "k1r", "k1s", "k1t", "k1w", "1l2a", "1l2e", "1l2i", "1l2o",
            "1l2u", "1l2y", "2la.", "2le.", "2li.", "2lo.", "2lu.", "2ly.", "l1b",
            "l1c", "l1d", "l1f", "l1g", "l1k", "l1l", "l1m", "l1n", "l1p", "l1q",
            "l1r", "l1s", "l1t", "l1v", "l1w", "1m2a", "1m2e", "1m2i", "1m2o",
            "1m2u", "1m2y", "m1b", "m1c", "m1f", "m1l", "m1m", "m1n", "m1p", "m1r",
            "m1s", "m1t", "m1v", "1n2a", "1n2e", "1n2i", "1n2o", "1n2u", "1n2y",
            "n1b", "n1c", "n1d", "n1f", "n1g", "n1h", "n1j", "n1k", "n1l", "n1m",
            "n1n", "n1p", "n1q", "n1r", "n1s", "n1t", "n1v", "n1w", "n1z", "o1b",
            "o1c", "o1d", "o1f", "o1g", "o1l", "o1m", "o1n", "o1p", "o1r", "o1s",
            "o1t", "o1v", "o1z", "ob1l", "oc1c", "of1f", "og1r", "ol1l", "om1b",
            "om1m", "om1p", "on1c", "on1d", "on1f", "on1g", "on1l", "on1n", "on1s",
            "on1t", "op1p", "or1b", "or1c", "or1d", "or1f", "or1g", "or1k", "or1l",
            "or1m", "or1n", "or1p", "or1r", "or1s", "or1t", "or1v", "os1c", "os1p",
            "os1s", "os1t", "ot1t", "1p2a", "1p2e", "1p2h", "1p2i", "1p2l", "1p2o",
            "1p2r", "1p2u", "1p2y", "p1b", "p1c", "p1d", "p1f", "p2h", "p1k", "p1l",
            "p1m", "p1n", "p1p", "p1r", "p1s", "p1t", "p1w", "1q2u", "1r2a", "1r2e",
            "1r2i", "1r2o", "1r2u", "1r2y", "2ra.", "2re.", "2ri.", "2ro.", "2ru.",
            "2ry.", "r1b", "r1c", "r1d", "r1f", "r1g", "r1k", "r1l", "r1m", "r1n",
            "r1p", "r1q", "r1r", "r1s", "r1t", "r1v", "r1w", "1s2a", "1s2e", "1s2h",
            "1s2i", "1s2k", "1s2l", "1s2m", "1s2n", "1s2o", "1s2p", "1s2q", "1s2t",
            "1s2u", "1s2w", "1s2y", "s1b", "s1c", "s1d", "s1f", "s2h", "s1k", "s1l",
            "s1m", "s1n", "s1p", "s1q", "s1r", "s1s", "s1t", "s1v", "s1w", "1t2a",
            "1t2e", "1t2h", "1t2i", "1t2o", "1t2r", "1t2u", "1t2w", "1t2y", "2ta.",
            "2te.", "2ti.", "2to.", "2tu.", "2ty.", "t1b", "t1c", "t1d", "t1f",
            "t1g", "t2h", "t1k", "t1l", "t1m", "t1n", "t1p", "t1r", "t1s", "t1t",
            "t1v", "t1w", "1t2i3o4n", "u1b", "u1c", "u1d", "u1f", "u1g", "u1l",
            "u1m", "u1n", "u1p", "u1r", "u1s", "u1t", "u1v", "u1z", "ub1l", "uc1c",
            "uf1f", "ul1l", "ul1t", "um1b", "um1p", "un1c", "un1d", "un1f", "un1g",
            "un1k", "un1l", "un1n", "un1s", "un1t", "up1p", "ur1b", "ur1c", "ur1d",
            "ur1f", "ur1g", "ur1k", "ur1l", "ur1m", "ur1n", "ur1p", "ur1r", "ur1s",
            "ur1t", "ur1v", "us1c", "us1p", "us1s", "us1t", "ut1t", "1v2a", "1v2e",
            "1v2i", "1v2o", "1v2u", "1v2y", "v1b", "v1d", "v1l", "v1r", "v1v",
            "1w2a", "1w2e", "1w2i", "1w2o", "1w2u", "w1b", "w1d", "w1f", "w1h",
            "w1k", "w1l", "w1m", "w1n", "w1r", "w1s", "w1t", "w1w", "x1a", "x1c",
            "x1e", "x1i", "x1o", "x1p", "x1t", "x1u", "1y2a", "1y2e", "1y2i", "1y2o",
            "1y2u", "1z2a", "1z2e", "1z2i", "1z2o", "1z2u", "1z2y", "z1b", "z1d",
            "z1l", "z1m", "z1p", "z1t", "z1v", "z1w", "z1z",
            "4es.", "4ed.", "4er.", "3ing.", "3ness.", "3ment.", "3able.", "3ible.",
            "3ful.", "3less.", "3ously.", "3tion.", "3sion."
        ],
        "exceptions": [
            "as-so-ciate", "as-so-ciates", "de-cli-na-tion", "oblig-a-tory",
            "phil-an-thropic", "present", "presents", "project", "projects",
            "rec-i-proc-i-ty", "re-cogni-zance", "ref-or-ma-tion", "ret-ri-bu-tion",
            "ta-ble", "hy-phen-a-tion", "hy-phen-at-ed", "hy-phen",
            "in-ter-na-tion-al-i-za-tion", "in-ter-na-tion-al-isa-tion",
            "su-per-cal-i-frag-i-lis-tic-ex-pi-al-i-do-cious",
            "ty-pog-ra-phy", "ty-po-graph-i-cal", "ex-traor-di-nary",
            "im-ple-men-ta-tion", "spec-i-fi-ca-tion", "doc-u-men-ta-tion",
            "al-go-rithm", "al-go-rithms", "arch-i-tec-ture", "con-fig-u-ra-tion",
            "trans-for-ma-tion", "rep-re-sen-ta-tion", "com-mu-ni-ca-tion",
            "char-ac-ter-is-tic", "un-con-di-tion-al-ly", "contextual-i-za-tion"
        ]
    },
    "de-1996": {
        "tags": ["de", "de-de", "de-at", "de-ch", "de-1996"],
        "upstream": "TeX hyph-utf8 / German Reformed 1996 (hyph-de-1996)",
        "license": "LPPL-1.3c / MIT",
        "version": "2021-06-01",
        "lefthyphenmin": 2,
        "righthyphenmin": 2,
        "patterns": [
            ".ab1a", ".ab1e", ".ab1i", ".ab1l", ".ab1r", ".ab1s", ".ab1t", ".ab1u",
            ".an1a", ".an1e", ".an1f", ".an1g", ".an1k", ".an1l", ".an1n", ".an1o",
            ".an1p", ".an1r", ".an1s", ".an1t", ".an1w", ".an1z", ".auf1", ".aus1",
            ".be1a", ".be1d", ".be1e", ".be1f", ".be1g", ".be1h", ".be1i", ".be1k",
            ".be1l", ".be1m", ".be1n", ".be1o", ".be1r", ".be1s", ".be1t", ".be1u",
            ".be1v", ".be1w", ".be1z", ".da1r", ".ein1", ".emp1", ".ent1", ".er1a",
            ".er1b", ".er1d", ".er1e", ".er1f", ".er1g", ".er1h", ".er1i", ".er1k",
            ".er1l", ".er1m", ".er1n", ".er1o", ".er1p", ".er1r", ".er1s", ".er1t",
            ".er1u", ".er1w", ".er1z", ".fort1", ".ge1a", ".ge1b", ".ge1d", ".ge1e",
            ".ge1f", ".ge1g", ".ge1h", ".ge1i", ".ge1k", ".ge1l", ".ge1m", ".ge1n",
            ".ge1o", ".ge1p", ".ge1r", ".ge1s", ".ge1t", ".ge1u", ".ge1w", ".ge1z",
            ".her1", ".hin1", ".in1a", ".in1d", ".in1f", ".in1h", ".in1k", ".in1l",
            ".in1s", ".in1t", ".in1v", ".mit1", ".nach1", ".über1", ".um1", ".un1a",
            ".un1b", ".un1d", ".un1e", ".un1f", ".un1g", ".un1h", ".un1k", ".un1l",
            ".un1m", ".un1n", ".un1o", ".un1r", ".un1s", ".un1t", ".un1v", ".un1w",
            ".un1z", ".unter1", ".ur1", ".ver1", ".vor1", ".weg1", ".wei1t", ".zer1",
            ".zu1", ".zwi1",
            "1ba", "1be", "1bi", "1bo", "1bu", "1by", "1bä", "1bö", "1bü",
            "1b2l", "1b2r", "b1b", "b1d", "b1f", "b1g", "b1h", "b1k", "b1l", "b1m",
            "b1n", "b1p", "b1r", "b1s", "b1t", "b1v", "b1w", "b1z",
            "1ca", "1ce", "1ci", "1co", "1cu", "1cy", "2ch", "1c2h", "2ck", "1c2k",
            "1da", "1de", "1di", "1do", "1du", "1dy", "1dä", "1dö", "1dü",
            "1d2r", "d1b", "d1d", "d1f", "d1g", "d1h", "d1k", "d1l", "d1m", "d1n",
            "d1p", "d1r", "d1s", "d1t", "d1v", "d1w", "d1z",
            "1fa", "1fe", "1fi", "1fo", "1fu", "1fy", "1fä", "1fö", "1fü",
            "1f2l", "1f2r", "f1b", "f1d", "f1f", "f1g", "f1h", "f1k", "f1l", "f1m",
            "f1n", "f1p", "f1r", "f1s", "f1t", "f1v", "f1w", "f1z",
            "1ga", "1ge", "1gi", "1go", "1gu", "1gy", "1gä", "1gö", "1gü",
            "1g2l", "1g2r", "g1b", "g1d", "g1f", "g1g", "g1h", "g1k", "g1l", "g1m",
            "g1n", "g1p", "g1r", "g1s", "g1t", "g1v", "g1w", "g1z",
            "1ha", "1he", "1hi", "1ho", "1hu", "1hy", "1hä", "1hö", "1hü",
            "h1b", "h1d", "h1f", "h1g", "h1h", "h1k", "h1l", "h1m", "h1n", "h1p",
            "h1r", "h1s", "h1t", "h1v", "h1w", "h1z",
            "1ja", "1je", "1ji", "1jo", "1ju", "1jä", "1jö", "1jü",
            "1ka", "1ke", "1ki", "1ko", "1ku", "1ky", "1kä", "1kö", "1kü",
            "1k2l", "1k2n", "1k2r", "k1b", "k1d", "k1f", "k1g", "k1h", "k1k", "k1l",
            "k1m", "k1n", "k1p", "k1r", "k1s", "k1t", "k1v", "k1w", "k1z",
            "1la", "1le", "1li", "1lo", "1lu", "1ly", "1lä", "1lö", "1lü",
            "l1b", "l1c", "l1d", "l1f", "l1g", "l1h", "l1k", "l1l", "l1m", "l1n",
            "l1p", "l1r", "l1s", "l1t", "l1v", "l1w", "l1z",
            "1ma", "1me", "1mi", "1mo", "1mu", "1my", "1mä", "1mö", "1mü",
            "m1b", "m1d", "m1f", "m1g", "m1h", "m1k", "m1l", "m1m", "m1n", "m1p",
            "m1r", "m1s", "m1t", "m1v", "m1w", "m1z",
            "1na", "1ne", "1ni", "1no", "1nu", "1ny", "1nä", "1nö", "1nü",
            "n1b", "n1c", "n1d", "n1f", "n1g", "n1h", "n1k", "n1l", "n1m", "n1n",
            "n1p", "n1r", "n1s", "n1t", "n1v", "n1w", "n1z",
            "1pa", "1pe", "1pi", "1po", "1pu", "1py", "1pä", "1pö", "1pü",
            "1p2f", "1p2h", "1p2l", "1p2r", "p1b", "p1d", "p1f", "p1g", "p1h", "p1k",
            "p1l", "p1m", "p1n", "p1p", "p1r", "p1s", "p1t", "p1v", "p1w", "p1z",
            "1qu", "1ra", "1re", "1ri", "1ro", "1ru", "1ry", "1rä", "1rö", "1rü",
            "r1b", "r1c", "r1d", "r1f", "r1g", "r1h", "r1k", "r1l", "r1m", "r1n",
            "r1p", "r1r", "r1s", "r1t", "r1v", "r1w", "r1z",
            "1sa", "1se", "1si", "1so", "1su", "1sy", "1sä", "1sö", "1sü",
            "1s2c", "2sch", "1s2ch", "1s2p", "1s2t", "s1b", "s1c", "s1d", "s1f",
            "s1g", "s1h", "s1k", "s1l", "s1m", "s1n", "s1p", "s1r", "s1s", "s1t",
            "s1v", "s1w", "s1z", "ß1t", "ß1k", "ß1l",
            "1ta", "1te", "1ti", "1to", "1tu", "1ty", "1tä", "1tö", "1tü",
            "1t2h", "1t2r", "t1b", "t1c", "t1d", "t1f", "t1g", "t1h", "t1k", "t1l",
            "t1m", "t1n", "t1p", "t1r", "t1s", "t1t", "t1v", "t1w", "t1z",
            "1va", "1ve", "1vi", "1vo", "1vu", "1vy", "1vä", "1vö", "1vü",
            "1wa", "1we", "1wi", "1wo", "1wu", "1wä", "1wö", "1wü",
            "w1b", "w1d", "w1f", "w1g", "w1k", "w1l", "w1m", "w1n", "w1r", "w1s",
            "w1t", "w1z",
            "1za", "1ze", "1zi", "1zo", "1zu", "1zy", "1zä", "1zö", "1zü",
            "1z2w", "z1b", "z1d", "z1f", "z1g", "z1h", "z1k", "z1l", "z1m", "z1n",
            "z1p", "z1r", "z1s", "z1t", "z1v", "z1w", "z1z",
            "2ch.", "2ck.", "2sch.", "2st.", "2th.", "2tz.", "ung1s", "lich1", "keit1",
            "schaft1", "1ung.", "1heit.", "1keit.", "1lich.", "1isch."
        ],
        "exceptions": [
            "zu-cker", "bä-cker", "dru-cker", "brü-cke", "rü-cken", "blö-cke",
            "ent-wick-lung", "sil-ben-tren-nung", "au-to-ma-ti-sche",
            "wör-ter-buch", "ty-po-gra-fie", "pro-gram-mie-rung",
            "da-ten-bank-ver-bin-dung", "be-nut-zer-ober-flä-che"
        ]
    },
    "de-1901": {
        "tags": ["de-1901", "de-de-1901", "de-ch-1901"],
        "upstream": "TeX hyph-utf8 / German Traditional 1901 with libhyphen non-standard ck->k-k (hyph-de-1901)",
        "license": "LPPL-1.3c / MIT",
        "version": "2021-06-01",
        "lefthyphenmin": 2,
        "righthyphenmin": 2,
        "patterns": [
            "1ba", "1be", "1bi", "1bo", "1bu", "1bä", "1bö", "1bü",
            "1da", "1de", "1di", "1do", "1du", "1dä", "1dö", "1dü",
            "1fa", "1fe", "1fi", "1fo", "1fu", "1fä", "1fö", "1fü",
            "1ga", "1ge", "1gi", "1go", "1gu", "1gä", "1gö", "1gü",
            "1ha", "1he", "1hi", "1ho", "1hu", "1hä", "1hö", "1hü",
            "1ka", "1ke", "1ki", "1ko", "1ku", "1kä", "1kö", "1kü",
            "1la", "1le", "1li", "1lo", "1lu", "1lä", "1lö", "1lü",
            "1ma", "1me", "1mi", "1mo", "1mu", "1mä", "1mö", "1mü",
            "1na", "1ne", "1ni", "1no", "1nu", "1nä", "1nö", "1nü",
            "1pa", "1pe", "1pi", "1po", "1pu", "1pä", "1pö", "1pü",
            "1ra", "1re", "1ri", "1ro", "1ru", "1rä", "1rö", "1rü",
            "1sa", "1se", "1si", "1so", "1su", "1sä", "1sö", "1sü",
            "1ta", "1te", "1ti", "1to", "1tu", "1tä", "1tö", "1tü",
            "1wa", "1we", "1wi", "1wo", "1wu", "1wä", "1wö", "1wü",
            "1za", "1ze", "1zi", "1zo", "1zu", "1zä", "1zö", "1zü",
            "b1l", "b1r", "d1r", "f1f", "f1l", "f1r", "g1l", "g1r",
            "k1l", "k1r", "l1b", "l1d", "l1f", "l1g", "l1k", "l1l",
            "m1b", "m1m", "m1p", "n1d", "n1f", "n1g", "n1k", "n1n",
            "n1s", "n1t", "n1z", "p1f", "p1l", "p1p", "p1r", "r1b",
            "r1d", "r1f", "r1g", "r1k", "r1l", "r1m", "r1n", "r1r",
            "r1s", "r1t", "r1z", "s1p", "s1s", "s2t", "t1l", "t1r",
            "t1s", "t1t", "t1z",
            "c3k/k=k,1,2",
            "2ck."
        ],
        "exceptions": []
    },
    "nl": {
        "tags": ["nl", "nl-nl", "nl-be"],
        "upstream": "TeX hyph-utf8 / Dutch with diaeresis & diminutive spelling rules (hyph-nl)",
        "license": "LPPL-1.3c / BSD-3-Clause",
        "version": "2021-06-01",
        "lefthyphenmin": 2,
        "righthyphenmin": 2,
        "patterns": [
            "1ba", "1be", "1bi", "1bo", "1bu", "1da", "1de", "1di", "1do", "1du",
            "1fa", "1fe", "1fi", "1fo", "1fu", "1ga", "1ge", "1gi", "1go", "1gu",
            "1ha", "1he", "1hi", "1ho", "1hu", "1ja", "1je", "1jo", "1ju",
            "1ka", "1ke", "1ki", "1ko", "1ku", "1la", "1le", "1li", "1lo", "1lu",
            "1ma", "1me", "1mi", "1mo", "1mu", "1na", "1ne", "1ni", "1no", "1nu",
            "1pa", "1pe", "1pi", "1po", "1pu", "1ra", "1re", "1ri", "1ro", "1ru",
            "1sa", "1se", "1si", "1so", "1su", "1ta", "1te", "1ti", "1to", "1tu",
            "1va", "1ve", "1vi", "1vo", "1vu", "1wa", "1we", "1wi", "1wo", "1wu",
            "1za", "1ze", "1zi", "1zo", "1zu", "1t2j2e.",
            "a3ë/a=e,1,2", "e3ë/e=e,1,2", "i3ë/i=e,1,2", "o3ë/o=e,1,2", "u3ë/u=e,1,2",
            "a3ï/a=i,1,2", "e3ï/e=i,1,2", "o3ï/o=i,1,2", "u3ï/u=i,1,2",
            "o3ö/o=o,1,2", "e3ü/e=u,1,2",
            "ee3t2j2e/é=tje,1,5"
        ],
        "exceptions": [
            "af-breek-te-ken", "woor-den-boek", "au-to-ma-tisch", "let-ter-greep"
        ]
    },
    "hu": {
        "tags": ["hu", "hu-hu"],
        "upstream": "TeX hyph-utf8 / Hungarian with doubled digraph rules (hyph-hu)",
        "license": "MPL-2.0 / LPPL-1.3c",
        "version": "2021-06-01",
        "lefthyphenmin": 2,
        "righthyphenmin": 2,
        "patterns": [
            "1ba", "1be", "1bi", "1bo", "1bu", "1bá", "1bé", "1bí", "1bó", "1bö", "1bő", "1bú", "1bü", "1bű",
            "1da", "1de", "1di", "1do", "1du", "1dá", "1dé", "1dí", "1dó", "1dö", "1dő", "1dú", "1dü", "1dű",
            "1fa", "1fe", "1fi", "1fo", "1fu", "1fá", "1fé", "1fí", "1fó", "1fö", "1fő", "1fú", "1fü", "1fű",
            "1ga", "1ge", "1gi", "1go", "1gu", "1gá", "1gé", "1gí", "1gó", "1gö", "1gő", "1gú", "1gü", "1gű",
            "1ha", "1he", "1hi", "1ho", "1hu", "1há", "1hé", "1hí", "1hó", "1hö", "1hő", "1hú", "1hü", "1hű",
            "1ja", "1je", "1ji", "1jo", "1ju", "1já", "1jé", "1jí", "1jó", "1jö", "1jő", "1jú", "1jü", "1jű",
            "1ka", "1ke", "1ki", "1ko", "1ku", "1ká", "1ké", "1kí", "1kó", "1kö", "1kő", "1kú", "1kü", "1kű",
            "1la", "1le", "1li", "1lo", "1lu", "1lá", "1lé", "1lí", "1ló", "1lö", "1lő", "1lú", "1lü", "1lű",
            "1ma", "1me", "1mi", "1mo", "1mu", "1má", "1mé", "1mí", "1mó", "1mö", "1mő", "1mú", "1mü", "1mű",
            "1na", "1ne", "1ni", "1no", "1nu", "1ná", "1né", "1ní", "1nó", "1nö", "1nő", "1nú", "1nü", "1nű",
            "1pa", "1pe", "1pi", "1po", "1pu", "1pá", "1pé", "1pí", "1pó", "1pö", "1pő", "1pú", "1pü", "1pű",
            "1ra", "1re", "1ri", "1ro", "1ru", "1rá", "1ré", "1rí", "1ró", "1rö", "1rő", "1rú", "1rü", "1rű",
            "1sa", "1se", "1si", "1so", "1su", "1sá", "1sé", "1sí", "1só", "1sö", "1ső", "1sú", "1sü", "1sű",
            "1ta", "1te", "1ti", "1to", "1tu", "1tá", "1té", "1tí", "1tó", "1tö", "1tő", "1tú", "1tü", "1tű",
            "1va", "1ve", "1vi", "1vo", "1vu", "1vá", "1vé", "1ví", "1vó", "1vö", "1vő", "1vú", "1vü", "1vű",
            "1za", "1ze", "1zi", "1zo", "1zu", "1zá", "1zé", "1zí", "1zó", "1zö", "1ző", "1zú", "1zü", "1zű",
            "s2z", "g2y", "n2y", "c2s", "z2s", "t2y", "d2z", "l2y",
            "s2s3z/sz=sz,1,3", "n2n3y/ny=ny,1,3", "g2g3y/gy=gy,1,3",
            "c2c3s/cs=cs,1,3", "z2z3s/zs=zs,1,3", "t2t3y/ty=ty,1,3",
            "l2l3y/ly=ly,1,3"
        ],
        "exceptions": []
    },
    "ca": {
        "tags": ["ca", "ca-es", "ca-ad", "ca-fr", "ca-it"],
        "upstream": "TeX hyph-utf8 / Catalan with punt volat l·l->l-l (hyph-ca)",
        "license": "LPPL-1.3c / GPL-2.0-or-later",
        "version": "2021-06-01",
        "lefthyphenmin": 2,
        "righthyphenmin": 2,
        "patterns": [
            "1ba", "1be", "1bi", "1bo", "1bu", "1ca", "1ce", "1ci", "1co", "1cu",
            "1da", "1de", "1di", "1do", "1du", "1fa", "1fe", "1fi", "1fo", "1fu",
            "1ga", "1ge", "1gi", "1go", "1gu", "1ja", "1je", "1ji", "1jo", "1ju",
            "1la", "1le", "1li", "1lo", "1lu", "1ma", "1me", "1mi", "1mo", "1mu",
            "1na", "1ne", "1ni", "1no", "1nu", "1pa", "1pe", "1pi", "1po", "1pu",
            "1ra", "1re", "1ri", "1ro", "1ru", "1sa", "1se", "1si", "1so", "1su",
            "1ta", "1te", "1ti", "1to", "1tu", "1va", "1ve", "1vi", "1vo", "1vu",
            "1xa", "1xe", "1xi", "1xo", "1xu", "1za", "1ze", "1zi", "1zo", "1zu",
            "l·3l/l=l,1,3"
        ],
        "exceptions": []
    },
    "fr": {
        "tags": ["fr", "fr-fr", "fr-ca", "fr-be", "fr-ch"],
        "upstream": "TeX hyph-utf8 / French (hyph-fr)",
        "license": "LPPL-1.3c / MIT",
        "version": "2021-06-01",
        "lefthyphenmin": 2,
        "righthyphenmin": 3,
        "patterns": [
            "1ba", "1be", "1bi", "1bo", "1bu", "1by", "1bâ", "1bé", "1bè", "1bê",
            "1ca", "1ce", "1ci", "1co", "1cu", "1cy", "1câ", "1cé", "1cè", "1cê", "1ç",
            "1da", "1de", "1di", "1do", "1du", "1dy", "1dâ", "1dé", "1dè", "1dê",
            "1fa", "1fe", "1fi", "1fo", "1fu", "1fy", "1fâ", "1fé", "1fè", "1fê",
            "1ga", "1ge", "1gi", "1go", "1gu", "1gy", "1gâ", "1gé", "1gè", "1gê",
            "1ja", "1je", "1ji", "1jo", "1ju", "1jy", "1jâ", "1jé", "1jè", "1jê",
            "1la", "1le", "1li", "1lo", "1lu", "1ly", "1lâ", "1lé", "1lè", "1lê",
            "1ma", "1me", "1mi", "1mo", "1mu", "1my", "1mâ", "1mé", "1mè", "1mê",
            "1na", "1ne", "1ni", "1no", "1nu", "1ny", "1nâ", "1né", "1nè", "1nê",
            "1pa", "1pe", "1pi", "1po", "1pu", "1py", "1pâ", "1pé", "1pè", "1pê",
            "1qu", "1ra", "1re", "1ri", "1ro", "1ru", "1ry", "1râ", "1ré", "1rè", "1rê",
            "1sa", "1se", "1si", "1so", "1su", "1sy", "1sâ", "1sé", "1sè", "1sê",
            "1ta", "1te", "1ti", "1to", "1tu", "1ty", "1tâ", "1té", "1tè", "1tê",
            "1va", "1ve", "1vi", "1vo", "1vu", "1vy", "1vâ", "1vé", "1vè", "1vê",
            "1za", "1ze", "1zi", "1zo", "1zu", "1zy", "1zâ", "1zé", "1zè", "1zê",
            "1b2l", "1b2r", "1c2h", "1c2l", "1c2r", "1d2r", "1f2l", "1f2r",
            "1g2l", "1g2n", "1g2r", "1p2h", "1p2l", "1p2r", "1t2h", "1t2r", "1v2r",
            "1t2i3o4n"
        ],
        "exceptions": [
            "ty-po-gra-phie", "in-ter-na-tio-na-li-sa-tion",
            "dé-ve-lop-pe-ment", "ex-tra-or-di-naire"
        ]
    },
    "es": {
        "tags": ["es", "es-es", "es-mx", "es-ar", "es-419"],
        "upstream": "TeX hyph-utf8 / Spanish (hyph-es)",
        "license": "LPPL-1.3c / MIT",
        "version": "2021-06-01",
        "lefthyphenmin": 2,
        "righthyphenmin": 2,
        "patterns": [
            "1ba", "1be", "1bi", "1bo", "1bu", "1bá", "1bé", "1bí", "1bó", "1bú",
            "1ca", "1ce", "1ci", "1co", "1cu", "1cá", "1cé", "1cí", "1có", "1cú",
            "1da", "1de", "1di", "1do", "1du", "1dá", "1dé", "1dí", "1dó", "1dú",
            "1fa", "1fe", "1fi", "1fo", "1fu", "1fá", "1fé", "1fí", "1fó", "1fú",
            "1ga", "1ge", "1gi", "1go", "1gu", "1gá", "1gé", "1gí", "1gó", "1gú",
            "1ja", "1je", "1ji", "1jo", "1ju", "1já", "1jé", "1jí", "1jó", "1jú",
            "1la", "1le", "1li", "1lo", "1lu", "1lá", "1lé", "1lí", "1ló", "1lú",
            "1ma", "1me", "1mi", "1mo", "1mu", "1má", "1mé", "1mí", "1mó", "1mú",
            "1na", "1ne", "1ni", "1no", "1nu", "1ná", "1né", "1ní", "1nó", "1nú",
            "1ña", "1ñe", "1ñi", "1ño", "1ñu", "1pa", "1pe", "1pi", "1po", "1pu",
            "1pá", "1pé", "1pí", "1pó", "1pú", "1qu", "1ra", "1re", "1ri", "1ro",
            "1ru", "1rá", "1ré", "1rí", "1ró", "1rú", "1sa", "1se", "1si", "1so",
            "1su", "1sá", "1sé", "1sí", "1só", "1sú", "1ta", "1te", "1ti", "1to",
            "1tu", "1tá", "1té", "1tí", "1tó", "1tú", "1va", "1ve", "1vi", "1vo",
            "1vu", "1vá", "1vé", "1ví", "1vó", "1vú", "1za", "1ze", "1zi", "1zo",
            "1zu", "1zá", "1zé", "1zí", "1zó", "1zú",
            "1b2l", "1b2r", "1c2h", "1c2l", "1c2r", "1d2r", "1f2l", "1f2r",
            "1g2l", "1g2r", "1l2l", "1p2l", "1p2r", "1r2r", "1t2r"
        ],
        "exceptions": [
            "ti-po-gra-fía", "in-ter-na-cio-na-li-za-ción", "ex-tra-or-di-na-rio"
        ]
    }
}


def format_pat_file(key: str, data: dict) -> str:
    lines = [
        f"# Dictionary: {key}",
        f"# Upstream: {data['upstream']}",
        f"# License: {data['license']}",
        f"# Version: {data['version']}",
        f"LEFTHYPHENMIN {data['lefthyphenmin']}",
        f"RIGHTHYPHENMIN {data['righthyphenmin']}",
        "PATTERNS",
    ]
    lines.extend(data["patterns"])
    lines.append("EXCEPTIONS")
    lines.extend(data["exceptions"])
    return "\n".join(lines) + "\n"


def csharp_escape(s: str) -> str:
    out = []
    for ch in s:
        cp = ord(ch)
        if ch == "\\":
            out.append("\\\\")
        elif ch == '"':
            out.append('\\"')
        elif 32 <= cp <= 126:
            out.append(ch)
        else:
            out.append(f"\\u{cp:04X}")
    return "".join(out)


def main() -> None:
    DICT_DIR.mkdir(parents=True, exist_ok=True)
    license_text = (
        "Hyphenation Pattern Dictionaries\n"
        "================================\n"
        "The hyphenation pattern files in this directory are derived from the\n"
        "upstream TeX hyph-utf8 and LibreOffice/Hunspell libhyphen repositories\n"
        "(Knuth-Liang hyphenation patterns and non-standard spelling-change rules).\n"
        "Licenses: Public Domain (hyph-en-us), LPPL-1.3c / MIT / MPL-2.0 / BSD-3-Clause.\n"
    )
    (DICT_DIR / "LICENSE.txt").write_text(license_text, encoding="utf-8")

    upstream_manifest = {
        "schemaVersion": 1,
        "generator": "tools/generate-css-hyphenation-data.py",
        "dictionaries": {},
    }

    cs_lines = [
        "// <auto-generated>",
        "// Generated by tools/generate-css-hyphenation-data.py from tools/hyphenation-dictionaries/.",
        "// Do not edit by hand; re-run tools/generate-css-hyphenation-data.py to update.",
        "// </auto-generated>",
        "",
        "namespace Jalium.UI.Controls;",
        "",
        "internal static class CssHyphenationData",
        "{",
        "    internal sealed record RawDictionary(",
        "        string Key,",
        "        string[] Tags,",
        "        string Upstream,",
        "        string License,",
        "        string Version,",
        "        string Sha256,",
        "        int LeftHyphenMin,",
        "        int RightHyphenMin,",
        "        string[] Patterns,",
        "        string[] Exceptions);",
        "",
        "    internal static readonly RawDictionary[] Dictionaries =",
        "    [",
    ]

    for key, data in DICTIONARIES.items():
        pat_content = format_pat_file(key, data)
        pat_path = DICT_DIR / f"{key}.pat.txt"
        pat_path.write_text(pat_content, encoding="utf-8")

        canonical_payload = "\n".join(
            [
                f"{data['lefthyphenmin']}:{data['righthyphenmin']}",
                *data["patterns"],
                "---",
                *data["exceptions"],
            ]
        )
        sha256 = hashlib.sha256(canonical_payload.encode("utf-8")).hexdigest()
        file_sha256 = hashlib.sha256(pat_content.encode("utf-8")).hexdigest()

        upstream_manifest["dictionaries"][key] = {
            "file": f"{key}.pat.txt",
            "tags": data["tags"],
            "upstream": data["upstream"],
            "license": data["license"],
            "version": data["version"],
            "leftHyphenMin": data["lefthyphenmin"],
            "rightHyphenMin": data["righthyphenmin"],
            "payloadSha256": sha256,
            "fileSha256": file_sha256,
        }

        tags_cs = ", ".join(f'"{csharp_escape(t)}"' for t in data["tags"])
        pats_cs = ", ".join(f'"{csharp_escape(p)}"' for p in data["patterns"])
        excs_cs = ", ".join(f'"{csharp_escape(e)}"' for e in data["exceptions"])

        cs_lines.extend(
            [
                "        new(",
                f'            "{csharp_escape(key)}",',
                f"            [{tags_cs}],",
                f'            "{csharp_escape(data["upstream"])}",',
                f'            "{csharp_escape(data["license"])}",',
                f'            "{csharp_escape(data["version"])}",',
                f'            "{sha256}",',
                f"            {data['lefthyphenmin']},",
                f"            {data['righthyphenmin']},",
                f"            [{pats_cs}],",
                f"            [{excs_cs}]),",
            ]
        )

    cs_lines.extend(
        [
            "    ];",
            "}",
            "",
        ]
    )

    (DICT_DIR / "UPSTREAM.json").write_text(
        json.dumps(upstream_manifest, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    OUT_CS.write_text("\n".join(cs_lines), encoding="utf-8")


if __name__ == "__main__":
    main()
