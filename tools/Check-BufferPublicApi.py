"""Check explicit public-API decisions against pinned Java and current C# declarations.

Uses Python's standard library. This checks inventory and target declarations only;
behavioral equivalence still requires the evidence recorded beside each decision.
"""
from pathlib import Path
import collections
import json
import re
import subprocess

ROOT = Path(__file__).resolve().parent.parent
TOKENS = re.compile(r'''(?s)@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|/\*.*?\*/|//[^\r\n]*''')
METHOD = re.compile(
    r'(?m)^[ \t]*public[ \t]+static[ \t]+[\w.<>,\[\]?]+[ \t]+(\w+)\s*'
    r'\(([^;{}]*?)\)\s*(?:throws\s+[\w.,\s]+)?(?=\{|=>|;)')
INSTANCE_METHOD = re.compile(
    r'(?m)^[ \t]*public[ \t]+(?!static\b)'
    r'(?:(?:abstract|final|virtual|override|sealed|new|unsafe|async)[ \t]+)*'
    r'[\w.<>,\[\]?]+[ \t]+(\w+)\s*'
    r'\(([^;{}]*?)\)\s*(?:throws\s+[\w.,\s]+)?(?=\{|=>|;)')
STATES = {'implemented', 'clr-replacement', 'implementation-gap', 'unreviewed'}


def parameter_types(parameters):
    result, start, depth = [], 0, 0
    for index, char in enumerate(parameters + ','):
        if char == '<': depth += 1
        elif char == '>': depth -= 1
        elif char == ',' and depth == 0:
            parameter = parameters[start:index].split('=')[0].strip()
            if parameter:
                words = parameter.split()
                while words[0] in {'final', 'params', 'ref', 'out', 'in', 'this'}:
                    words.pop(0)
                result.append(re.sub(r'\s+', '', ' '.join(words[:-1])))
            start = index + 1
    return result


def declarations(source, static=True):
    """Declared public methods of the outer class; omit nested helper members.

    Fail closed on unmatched public methods at the outer depth. Literal and
    comment masking preserves offsets, so brace depth cannot come from a JavaDoc.
    """
    masked = TOKENS.sub(lambda m: re.sub(r'[^\r\n]', ' ', m.group()), source)
    depths, depth = [], 0
    for char in masked:
        depths.append(depth)
        if char == '{': depth += 1
        elif char == '}': depth -= 1
    found = {}
    pattern = METHOD if static else INSTANCE_METHOD
    for match in pattern.finditer(masked):
        if depths[match.start()] != 1: continue
        signature = match[1] + '(' + ','.join(parameter_types(match[2])) + ')'
        if signature in found: raise ValueError('Duplicate declaration: ' + signature)
        found[signature] = source.count('\n', 0, match.start()) + 1
    # A different formatting/grammar must be inspected instead of silently lost.
    candidate = (r'(?m)^[ \t]*public[ \t]+static[^\r\n]*\(' if static else
                 r'(?m)^[ \t]*public[ \t]+(?!static\b)'
                 r'(?:(?:abstract|final|virtual|override|sealed|new|unsafe|async)[ \t]+)*'
                 r'[\w.<>,\[\]?]+[ \t]+\w+[ \t]*\(')
    for match in re.finditer(candidate, masked):
        if depths[match.start()] == 1 and not pattern.match(masked, match.start()):
            raise ValueError('Unparsed public method declaration at line ' +
                             str(source.count('\n', 0, match.start()) + 1))
    return found


def constructors(source, class_name):
    """Inventory public outer constructors, including CLR this/base initializers."""
    masked = TOKENS.sub(lambda m: re.sub(r'[^\r\n]', ' ', m.group()), source)
    depths, depth = [], 0
    for char in masked:
        depths.append(depth)
        if char == '{': depth += 1
        elif char == '}': depth -= 1
    candidate = re.compile(r'(?m)^[ \t]*public[ \t]+' + re.escape(class_name) + r'\s*\(')
    pattern = re.compile(r'(?m)^[ \t]*public[ \t]+(' + re.escape(class_name) +
                         r')\s*\(([^();{}]*?)\)\s*'
                         r'(?::\s*(?:this|base)\s*\([^;{}]*?\)\s*)?(?=\{|=>)')
    found = {}
    for start in candidate.finditer(masked):
        if depths[start.start()] != 1: continue
        match = pattern.match(masked, start.start())
        if not match: raise ValueError('Unparsed public constructor at line ' +
                                      str(source.count('\n', 0, start.start()) + 1))
        signature = match[1] + '(' + ','.join(parameter_types(match[2])) + ')'
        if signature in found: raise ValueError('Duplicate declaration: ' + signature)
        found[signature] = source.count('\n', 0, match.start()) + 1
    return found


def property_accessors(source, static=False):
    """Declared public properties and their publicly available accessors."""
    masked = TOKENS.sub(lambda m: re.sub(r'[^\r\n]', ' ', m.group()), source)
    depths, depth = [], 0
    for char in masked:
        depths.append(depth)
        if char == '{': depth += 1
        elif char == '}': depth -= 1
    properties = {}
    prefix = r'static[ \t]+' if static else r'(?!static\b)'
    pattern = re.compile(r'(?m)^[ \t]*public[ \t]+' + prefix +
                         r'(?:(?:abstract|virtual|override|sealed|new|unsafe)[ \t]+)*'
                         r'(?!class\b|struct\b|interface\b|enum\b|record\b|event\b|delegate\b)'
                         r'[\w.<>,\[\]?]+[ \t]+(\w+)\s*(=>|\{)')
    accessor = re.compile(r'(?:(private|protected|internal)\s+)*(get|set)\s*(?==>|\{|;)')
    for match in pattern.finditer(masked):
        if depths[match.start()] != 1: continue
        name = match[1]
        if name in properties: raise ValueError('Duplicate property declaration: ' + name)
        if match[2] == '=>':
            properties[name] = {'get'}
            continue
        opening = match.end() - 1
        closing = next((i for i in range(opening + 1, len(masked))
                        if masked[i] == '}' and depths[i] == 2), None)
        if closing is None: raise ValueError('Unclosed property: ' + name)
        properties[name] = {a[2] for a in accessor.finditer(masked, opening + 1, closing)
                            if depths[a.start()] == 2 and a[1] is None}
    return properties


def check():
    manifest = json.loads((ROOT / 'docs/buffer-porting-manifest.json').read_text(encoding='utf-8-sig'))
    upstream = ROOT.parent / 'netty'
    reviewed = 0
    reviews = [(entry, entry[key]) for entry in manifest['entries']
               for key in ('publicApiReview', 'publicStaticApiReview', 'publicConstructorReview') if entry.get(key)]
    for entry, review in reviews:
        reviewed += 1
        if review['scope'] not in {'declared-public-static-methods', 'declared-public-instance-methods',
                                   'declared-public-constructors'}:
            raise ValueError('Unsupported API review scope; extend the parser explicitly')
        static = review['scope'] == 'declared-public-static-methods'
        construction = review['scope'] == 'declared-public-constructors'
        if entry['kind'] != 'source': raise ValueError('API review requires a source entry')
        java = subprocess.check_output(
            ['git', '-C', str(upstream), 'show', manifest['baseline'] + ':' + entry['upstream']]
        ).decode('utf-8')
        class_name = Path(entry['upstream']).stem
        original = constructors(java, class_name) if construction else declarations(java, static)
        decisions = review['methods']
        unreviewed = review.get('unreviewedMethods', [])
        names = [item['upstream'] for item in decisions] + unreviewed
        if len(names) != len(set(names)) or set(names) != set(original):
            raise ValueError('Missing, duplicate or unknown original API: ' + entry['upstream'])
        counts = collections.Counter()
        counts['unreviewed'] = len(unreviewed)
        for item in decisions:
            state = item['state']
            if state not in STATES | {'jvm-only'}: raise ValueError('Unknown API state: ' + state)
            counts[state] += 1
            group = review['groups'][item['group']]
            if not group['adaptation'] or not group['evidence']:
                raise ValueError('Missing decision evidence: ' + item['upstream'])
            for evidence in group['evidence']:
                if not (ROOT / evidence).is_file(): raise ValueError('Missing evidence: ' + evidence)
            targets = item['targets']
            if state == 'jvm-only' and targets:
                raise ValueError('JVM-only API cannot declare CLR targets: ' + item['upstream'])
            if state in {'implemented', 'clr-replacement'} and not targets:
                raise ValueError('Implemented API has no CLR target: ' + item['upstream'])
            for target in targets:
                path = target['path']
                if path not in entry['implementation']: raise ValueError('Unregistered CLR target: ' + path)
                source = (ROOT / path).read_text(encoding='utf-8-sig')
                accessor = target.get('accessor')
                if accessor is not None:
                    if construction or accessor not in {'get', 'set'}:
                        raise ValueError('Unsupported CLR property accessor: ' + str(accessor))
                    if accessor not in property_accessors(source, static).get(target['member'], set()):
                        raise ValueError('Missing public CLR accessor: ' + path + ':' + target['member'] + '.' + accessor)
                elif target['member'] not in (constructors(source, class_name) if construction else declarations(source, static)):
                    raise ValueError('Missing CLR declaration: ' + path + ':' + target['member'])
        summary = {'declared': len(original), **{state: counts[state] for state in sorted(STATES)}}
        if counts['jvm-only']: summary['jvm-only'] = counts['jvm-only']
        if summary != review['summary']: raise ValueError('Stale API summary: ' + entry['upstream'])
        print(Path(entry['upstream']).name + ' [' + review['scope'] + ']: ' + json.dumps(summary))
    if not reviewed: raise ValueError('No public API review is registered')
    print('Pinned public signatures and explicit CLR targets agree; behavior/fixture completion is not inferred.')


if __name__ == '__main__':
    check()
