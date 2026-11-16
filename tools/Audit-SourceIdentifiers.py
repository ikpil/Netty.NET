"""Report Java identifiers that do not appear in the C# files mapped by a porting manifest.

Usage: python tools/Audit-SourceIdentifiers.py [manifest] [upstream-root] [--json out.json] [--only substring]

Rule audited (sourceIdentifierPolicy): a ported variable or method keeps its Java name; only capitalization and
underscore placement may differ, and C# keyword escaping (@in) keeps the identifier. A Task-returning port may
add the C# Async suffix; such names are listed as accepted, not as review items.

This is a report, not a gate. Java declarations are found with regular expressions, so a few names may be
misclassified; a reported name is a review item, not proof of a defect. Names that a documented CLR decision
removes (for example Java Future/Promise listener plumbing) still appear here until the code or the policy changes.
Categories per name not found verbatim:
  async-suffix   accepted: the C# files declare the same name with an Async suffix
  absent         review item: no C# identifier with the same normalized name exists in the mapped files
"""
import argparse
import json
import re
from collections import Counter, defaultdict
from pathlib import Path

JAVA_KEYWORDS = set("""abstract assert boolean break byte case catch char class const continue default do double else
enum extends final finally float for goto if implements import instanceof int interface long native new package private
protected public return short static strictfp super switch synchronized this throw throws transient try void volatile
while var record yield true false null sealed permits""".split())

LITERALS_AND_COMMENTS = re.compile(r'(?s)"""(?:.*?)"""|"(?:\\.|[^"\\\n])*"|\'(?:\\.|[^\'\\\n])*\'|/\*.*?\*/|//[^\n]*')
METHOD = re.compile(
    r'(?m)^[ \t]*(?:@\w+(?:\([^)]*\))?\s+)*'
    r'(?:(?:public|protected|private|static|final|abstract|synchronized|native|default|strictfp)\s+)*'
    r'(?:<[^>{};]+>\s+)?'
    r'(?P<type>[\w.$]+(?:\s*<[^{};()]*>)?(?:\s*\[\s*\])*)\s+'
    r'(?P<name>[A-Za-z_$][\w$]*)\s*\((?P<params>[^;{}]*?)\)\s*(?:throws\s+[\w.,\s]+)?\s*[{;]')
CTOR = re.compile(r'(?m)^[ \t]*(?:(?:public|protected|private)\s+)?(?P<name>[A-Z][\w$]*)\s*\((?P<params>[^;{}]*?)\)'
                  r'\s*(?:throws\s+[\w.,\s]+)?\s*\{')
FIELD = re.compile(
    r'(?m)^[ \t]*(?:(?:public|protected|private|static|final|volatile|transient)\s+)+'
    r'(?P<type>[\w.$]+(?:\s*<[^{};()=]*>)?(?:\s*\[\s*\])*)\s+(?P<name>[A-Za-z_$][\w$]*)\s*(?:=|;)')
LOCAL = re.compile(
    r'(?m)^[ \t]*(?:final\s+)?(?P<type>[A-Z][\w.$]*(?:\s*<[^{};()=]*>)?(?:\s*\[\s*\])*|int|long|short|byte|char|boolean'
    r'|double|float|var)\s+(?P<name>[a-z_$][\w$]*)\s*(?:=|;|:)')
CATCH = re.compile(r'catch\s*\(\s*(?:final\s+)?[\w.|\s]+\s+(?P<name>\w+)\s*\)')
# Members whose CLR names are fixed by the runtime contract.
CLR_FIXED = {'hashcode', 'iterator', 'finalize', 'main'}


def mask(source):
    return LITERALS_AND_COMMENTS.sub(lambda m: re.sub(r'[^\n]', ' ', m.group(0)), source)


def norm(name):
    return name.lstrip('@').replace('_', '').lower()


def split_params(params):
    parts, depth, cur = [], 0, ''
    for ch in params:
        depth += ch == '<'
        depth -= ch == '>'
        if ch == ',' and depth == 0:
            parts.append(cur)
            cur = ''
        else:
            cur += ch
    if cur.strip():
        parts.append(cur)
    names = []
    for p in parts:
        p = re.sub(r'@\w+(?:\([^)]*\))?', '', p).strip()
        m = re.search(r'([A-Za-z_$][\w$]*)\s*$', p)
        if m and m.group(1) not in JAVA_KEYWORDS:
            names.append(m.group(1))
    return names


def java_identifiers(source):
    s = mask(source)
    methods, fields, params, local_names = set(), set(), set(), set()
    for m in METHOD.finditer(s):
        if m.group('name') in JAVA_KEYWORDS or m.group('type') in ('new', 'return', 'throw', 'else'):
            continue
        methods.add(m.group('name'))
        params.update(split_params(m.group('params')))
    for m in CTOR.finditer(s):
        if m.group('name') not in JAVA_KEYWORDS:
            params.update(split_params(m.group('params')))
    fields.update(m.group('name') for m in FIELD.finditer(s) if m.group('name') not in JAVA_KEYWORDS)
    for m in LOCAL.finditer(s):
        if m.group('type') not in ('return', 'throw', 'new', 'else', 'case') and m.group('name') not in JAVA_KEYWORDS:
            local_names.add(m.group('name'))
    local_names.update(m.group('name') for m in CATCH.finditer(s))
    params -= fields
    local_names -= fields | params
    return {'method': methods, 'field': fields, 'param': params, 'local': local_names}


def csharp_identifiers(repo, paths):
    names = set()
    for p in paths:
        f = repo / p
        if f.exists():
            names.update(norm(t) for t in re.findall(r'@?[A-Za-z_][A-Za-z0-9_]*', mask(f.read_text(encoding='utf-8'))))
    return names


def main():
    repo = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser()
    parser.add_argument('manifest', nargs='?', default='docs/transport-porting-manifest.json')
    parser.add_argument('upstream', nargs='?', default=str(repo.parent / 'netty'))
    parser.add_argument('--json')
    parser.add_argument('--only')
    args = parser.parse_args()
    manifest = json.loads((repo / args.manifest).read_text(encoding='utf-8'))
    upstream = Path(args.upstream)

    totals = defaultdict(Counter)
    report = []
    for entry in manifest['entries']:
        targets = [p for p in entry.get('implementation', []) if p.endswith('.cs')]
        if not targets or not entry['upstream'].endswith('.java') or (args.only and args.only not in entry['upstream']):
            continue
        ids = java_identifiers((upstream / entry['upstream']).read_text(encoding='utf-8'))
        cs = csharp_identifiers(repo, targets)
        missing = defaultdict(dict)
        for kind, names in ids.items():
            totals[kind]['declared'] += len(names)
            for name in sorted(names):
                key = norm(name)
                if key in cs or key in CLR_FIXED:
                    continue
                if key + 'async' in cs:
                    totals[kind]['async-suffix'] += 1
                    continue
                missing[kind][name] = 'absent'
                totals[kind]['absent'] += 1
        report.append({'upstream': entry['upstream'], 'status': entry['status'], 'kind': entry['kind'],
                       'declared': {k: len(v) for k, v in ids.items()}, 'missing': missing})

    print(f'{"kind":8} {"declared":>8} {"absent":>7} {"async-suffix (accepted)":>24}')
    for kind, c in totals.items():
        print(f'{kind:8} {c["declared"]:8} {c["absent"]:7} {c["async-suffix"]:24}')
    flagged = [r for r in report if r['missing']]
    print(f'files with review items: {len(flagged)} / {len(report)}')
    for r in sorted(flagged, key=lambda r: -sum(len(v) for v in r['missing'].values()))[:20]:
        counts = {k: len(v) for k, v in r['missing'].items()}
        print(f"{sum(counts.values()):4} {r['status']:11} {r['upstream'].split('/io/netty/')[-1]} {counts}")
    if args.json:
        Path(args.json).write_text(json.dumps(report, indent=1), encoding='utf-8')


if __name__ == '__main__':
    main()
