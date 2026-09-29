"""Render a flow definition's layout to PNG with Pillow (approximating the designer: 210-wide boxes,
edges bottom-centre → top-centre; back edges red) and report straight-line crossings.
python render.py flow.json out.png [scale]"""
import itertools, json, sys
from PIL import Image, ImageDraw, ImageFont

COLORS = {'input': '#10b981', 'script': '#3b82f6', 'branch': '#f59e0b', 'set_variable': '#8b5cf6', 'section': '#e5e7eb',
          'add_to_cart': '#059669', 'reset_cart': '#dc2626', 'address': '#f97316', 'phone': '#0d9488', 'email': '#0891b2',
          'api_call': '#6366f1', 'authorize_payment': '#16a34a', 'set_custom_field': '#65a30d', 'end': '#ef4444',
          'trigger_telephony_event': '#e11d48', 'send_email': '#be185d'}
d = json.load(open(sys.argv[1], encoding='utf-8'))
S = float(sys.argv[3]) if len(sys.argv) > 3 else 0.35
nodes = d['nodes']
W, H = 210, 90
xs = [n['_pos']['x'] for n in nodes.values()] + [0]; ys = [n['_pos']['y'] for n in nodes.values()]
img = Image.new('RGB', (int((max(xs) + W + 400) * S), int((max(ys) + H + 100) * S)), '#030712')
g = ImageDraw.Draw(img)
try: font = ImageFont.truetype('arial.ttf', max(8, int(22 * S)))
except OSError: font = ImageFont.load_default()
OX = -min([0] + [w['x'] for v in d.get('_waypoints', {}).values() for w in v]) + 50
P = lambda x, y: (int((x + OX) * S), int((y + 50) * S))
segs = []
for k, n in nodes.items():
    a = n['_pos']
    for h, t in n['transitions'].items():
        b = nodes[t]['_pos']
        pts = [(a['x'] + W / 2, a['y'] + H)] + [(w['x'], w['y']) for w in d.get('_waypoints', {}).get(f'{k}-{h}-{t}', [])]               + [(b['x'] + W / 2, b['y'])]
        g.line([P(*pt) for pt in pts], fill='#f87171' if b['y'] <= a['y'] else '#6b7280', width=max(1, int(3 * S)))
        segs.extend(zip(pts, pts[1:]))
for k, n in nodes.items():
    x, y = n['_pos']['x'], n['_pos']['y']
    c = COLORS.get(n['type'], '#9ca3af')
    g.rectangle([P(x, y), P(x + W, y + H)], fill='#1f2937', outline=c, width=max(1, int(4 * S)))
    g.rectangle([P(x, y), P(x + W, y + 22)], fill=c)
    g.text(P(x + 6, y + 30), n['label'][:26], fill='white', font=font)
img.save(sys.argv[2])


def cross(s1, s2):
    if len({s1[0], s1[1], s2[0], s2[1]}) < 4: return False
    def ccw(A, B, C): return (C[1] - A[1]) * (B[0] - A[0]) > (B[1] - A[1]) * (C[0] - A[0])
    return ccw(s1[0], s2[0], s2[1]) != ccw(s1[1], s2[0], s2[1]) and ccw(s1[0], s1[1], s2[0]) != ccw(s1[0], s1[1], s2[1])


print('canvas', max(xs) + W, 'x', max(ys) + H, 'crossings', sum(cross(a, b) for a, b in itertools.combinations(segs, 2)))
