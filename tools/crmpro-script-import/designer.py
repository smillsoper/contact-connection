"""Parse a CRMPro .DESIGNER.VB into a control tree (parent → children in visual order)."""
import re, sys, json

F = sys.argv[1]
src = open(F, encoding='utf-8', errors='replace').read()

ctrls = {}
for m in re.finditer(r'Me\.(\w+) = New ([\w.]+)\(', src):
    ctrls.setdefault(m.group(1), {'type': m.group(2).split('.')[-1], 'props': {}, 'children': []})

for m in re.finditer(r'^\s*Me\.(\w+)\.(\w+) = (.+?)\s*$', src, re.M):
    name, prop, val = m.groups()
    if name in ctrls:
        ctrls[name]['props'][prop] = val

parent = {}
for m in re.finditer(r'^\s*Me\.(\w+)\.Controls\.Add\(Me\.(\w+)\)', src, re.M):
    p, c = m.groups()
    if p in ctrls and c in ctrls:
        ctrls[p]['children'].append(c); parent[c] = p
for m in re.finditer(r'^\s*Me\.(\w+)\.(?:TabPages|Pages)\.Add\(Me\.(\w+)\)', src, re.M):
    p, c = m.groups()
    if p in ctrls and c in ctrls:
        ctrls[p]['children'].append(c); parent[c] = p


def loc(n):
    v = ctrls[n]['props'].get('Location', '')
    m = re.search(r'Point\((-?\d+), (-?\d+)\)', v)
    return (int(m.group(2)), int(m.group(1))) if m else (0, 0)


def text(n):
    v = ctrls[n]['props'].get('Text', '')
    m = re.match(r'"(.*)"$', v)
    return m.group(1) if m else v


def show(n, depth=0):
    c = ctrls[n]; p = c['props']
    flags = []
    if 'ScriptBox' in p: flags.append('script')
    if p.get('Visible') == 'false': flags.append('HIDDEN')
    if p.get('Enabled') == 'false': flags.append('disabled')
    t = text(n)
    print('  ' * depth + f'{n} [{c["type"]}] @{loc(n)[1]},{loc(n)[0]}' + (f' "{t[:90]}"' if t else '') + (f' ({", ".join(flags)})' if flags else ''))
    kids = c['children']
    # tab pages keep insertion order; others by visual position
    if c['type'] not in ('TabStripControl',):
        kids = sorted(kids, key=loc)
    for k in kids:
        show(k, depth + 1)


roots = [n for n in ctrls if n not in parent and ctrls[n]['type'] not in ('Size', 'SizeF', 'Point', 'Container')]
for r in roots:
    show(r)
