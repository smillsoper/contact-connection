"""Layered (Sugiyama-style) auto-layout for ContactConnection flow definitions (CRM or telephony).

  python layout.py in.json out.json

1. Break cycles: DFS from the entry node (then any other roots, e.g. telephony event listeners);
   edges back to a node on the DFS stack are "back edges" (retry loops, jump-backs).
2. Rank = longest path from the roots, so every forward edge points down.
3. Long edges get invisible dummy nodes on each rank they cross (reserved lanes).
4. Order within each rank by barycenter sweeps (down/up) to minimise crossings.
5. X: relax each node toward its neighbours' average, resolving overlaps with a two-sided pack
   (left→right and right→left placements averaged) so nothing drifts to one side.
6. Edge routing via the designers' `_waypoints` (key "<source>-<handle>-<target>"): long forward
   edges follow their dummy lane; back edges leave downward, run up a dedicated lane in the left
   margin, and enter the target from above.
Only _pos/_waypoints change; the flow's behaviour is untouched."""
import json, sys
from collections import defaultdict

NODE_W, NODE_H, GAP_X, ROW_H = 210, 100, 70, 190
DUMMY_W = 30


def layout(defn):
    nodes = defn['nodes']
    succ = {n: [t for t in dict.fromkeys(v['transitions'].values()) if t in nodes] for n, v in nodes.items()}
    indeg = defaultdict(int)
    for ts in succ.values():
        for t in ts: indeg[t] += 1
    roots = [defn['entry_node']] + [n for n in nodes if indeg[n] == 0 and n != defn['entry_node']]

    # 1. back edges
    state, back = {}, set()
    for r in roots + list(nodes):
        if r in state: continue
        stack = [(r, iter(succ[r]))]; state[r] = 1
        while stack:
            n, it = stack[-1]
            nxt = next(it, None)
            if nxt is None:
                state[n] = 2; stack.pop(); continue
            if state.get(nxt) == 1: back.add((n, nxt))
            elif nxt not in state:
                state[nxt] = 1; stack.append((nxt, iter(succ[nxt])))
    fwd = {n: [t for t in succ[n] if (n, t) not in back] for n in nodes}

    # 2. ranks
    indeg2 = defaultdict(int)
    for ts in fwd.values():
        for t in ts: indeg2[t] += 1
    topo = [n for n in nodes if indeg2[n] == 0]
    rank = {n: 0 for n in topo}
    i = 0
    while i < len(topo):
        n = topo[i]; i += 1
        for t in fwd[n]:
            rank[t] = max(rank.get(t, 0), rank[n] + 1)
            indeg2[t] -= 1
            if indeg2[t] == 0: topo.append(t)

    # 3. dummies
    width = {n: NODE_W for n in nodes}
    up, down = defaultdict(list), defaultdict(list)
    chain = {}
    dc = 0
    for n in topo:
        for t in fwd[n]:
            prev, ds = n, []
            for r in range(rank[n] + 1, rank[t]):
                d = f'__d{dc}'; dc += 1
                rank[d] = r; width[d] = DUMMY_W; ds.append(d)
                down[prev].append(d); up[d].append(prev); prev = d
            down[prev].append(t); up[t].append(prev)
            chain[(n, t)] = ds
    layers = defaultdict(list)
    for n in topo + [d for d in rank if d.startswith('__d')]:
        layers[rank[n]].append(n)
    maxr = max(layers)

    # 4. ordering
    pos = {}
    def reindex(r):
        for k, n in enumerate(layers[r]): pos[n] = k
    for r in layers: reindex(r)
    def bary(n, nb):
        return sum(pos[m] for m in nb[n]) / len(nb[n]) if nb[n] else pos[n]
    for _ in range(30):
        for r in range(1, maxr + 1):
            layers[r].sort(key=lambda n: bary(n, up)); reindex(r)
        for r in range(maxr - 1, -1, -1):
            layers[r].sort(key=lambda n: bary(n, down)); reindex(r)

    # 5. x placement
    x = {}
    for L in layers.values():
        cur = 0
        for n in L:
            x[n] = cur + width[n] / 2; cur += width[n] + GAP_X

    def sep(a, b): return (width[a] + width[b]) / 2 + GAP_X

    def pack(L, want):
        lr, rl = {}, {}
        for k, n in enumerate(L):
            lr[n] = want[n] if k == 0 else max(want[n], lr[L[k - 1]] + sep(L[k - 1], n))
        for k in range(len(L) - 1, -1, -1):
            n = L[k]
            rl[n] = want[n] if k == len(L) - 1 else min(want[n], rl[L[k + 1]] - sep(n, L[k + 1]))
        return {n: (lr[n] + rl[n]) / 2 for n in L}

    for it in range(40):
        downward = it % 2 == 0
        for r in (range(1, maxr + 1) if downward else range(maxr - 1, -1, -1)):
            L = layers[r]
            nb = up if downward else down
            # neighbours on both sides count, weighted toward the sweep direction
            want = {}
            for n in L:
                ns = [x[m] for m in nb[n]] * 2 + [x[m] for m in (down if downward else up)[n]]
                want[n] = sum(ns) / len(ns) if ns else x[n]
            x.update(pack(L, want))

    minx = min(x.values())
    X = {n: x[n] - minx for n in x}          # centre x
    Y = {n: rank[n] * ROW_H for n in rank}
    for n in nodes:
        nodes[n]['_pos'] = {'x': round(X[n] - NODE_W / 2), 'y': Y[n]}

    # 6. waypoints
    wps = {}
    gutter = -80
    lane = 0
    for n, v in nodes.items():
        for handle, t in v['transitions'].items():
            if t not in nodes: continue
            key = f'{n}-{handle}-{t}'
            if (n, t) in back:
                gx = gutter - lane * 24; lane += 1
                sx, sy = X[n], Y[n] + NODE_H
                tx, ty = X[t], Y[t]
                wps[key] = [{'x': sx, 'y': sy + 30}, {'x': gx, 'y': sy + 30},
                            {'x': gx, 'y': ty - 40}, {'x': tx, 'y': ty - 40}]
            elif chain.get((n, t)):
                wps[key] = [{'x': round(X[d]), 'y': Y[d] + NODE_H / 2} for d in chain[(n, t)]]
    if wps: defn['_waypoints'] = wps
    else: defn.pop('_waypoints', None)
    return {'ranks': maxr + 1, 'back_edges': len(back), 'dummies': dc, 'routed_edges': len(wps)}


if __name__ == '__main__':
    d = json.load(open(sys.argv[1], encoding='utf-8'))
    stats = layout(d)
    json.dump(d, open(sys.argv[2], 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
    print(stats)
