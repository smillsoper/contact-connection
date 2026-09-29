"""Parse a .NET binary .resources file (System.Resources.ResourceReader format).
Usage: python resparse.py FILE [--dump DIR]
Lists every resource (name, type, size, preview); --dump writes each value to DIR/<name>.<ext>
(strings as .txt / .rtf, user types as .bin with extracted strings as .strings.txt)."""
import os, re, struct, sys


def read7(b, i):
    v = s = 0
    while True:
        c = b[i]; i += 1
        v |= (c & 0x7F) << s; s += 7
        if not c & 0x80:
            return v, i


def rstr(b, i, enc='utf-8'):
    n, i = read7(b, i)
    return b[i:i + n].decode(enc, 'replace'), i + n


def parse(path):
    b = open(path, 'rb').read()
    magic, hver, skip = struct.unpack_from('<Iii', b, 0)
    assert magic == 0xBEEFCACE, hex(magic)
    i = 12 + skip
    ver, nres, ntypes = struct.unpack_from('<iii', b, i); i += 12
    types = []
    for _ in range(ntypes):
        t, i = rstr(b, i); types.append(t)
    while i % 8:  # PAD
        i += 1
    i += 4 * nres  # hashes
    i += 4 * nres  # name positions
    data_off, = struct.unpack_from('<i', b, i); i += 4
    names_start = i
    entries = []
    j = names_start
    for _ in range(nres):
        n, j = read7(b, j)
        name = b[j:j + n].decode('utf-16-le'); j += n
        off, = struct.unpack_from('<i', b, j); j += 4
        entries.append((name, off))
    offs = sorted(o for _, o in entries) + [len(b) - data_off]
    out = []
    for name, off in entries:
        p = data_off + off
        end = data_off + offs[offs.index(off) + 1]
        code, q = read7(b, p)
        if code == 1:
            val, _ = rstr(b, q)
            out.append((name, 'string', val))
        elif code >= 0x40:
            out.append((name, types[code - 0x40], b[q:end]))
        else:
            out.append((name, f'primitive:{code}', b[q:end]))
    return out


def strings_of(raw):
    return [m.decode('utf-8', 'replace') for m in re.findall(rb'[\x09\x0a\x0d\x20-\x7e\x80-\xff]{6,}', raw)]


if __name__ == '__main__':
    res = parse(sys.argv[1])
    dump = sys.argv[3] if len(sys.argv) > 3 and sys.argv[2] == '--dump' else None
    if dump:
        os.makedirs(dump, exist_ok=True)
    for name, typ, val in res:
        short = typ.split(',')[0]
        if isinstance(val, str):
            kind = 'RTF' if val.lstrip().startswith('{\\rtf') else 'text'
            print(f'{name:60s} {short:40s} {kind} {len(val)}')
            if dump:
                open(os.path.join(dump, name + ('.rtf' if kind == 'RTF' else '.txt')), 'w', encoding='utf-8').write(val)
        else:
            ss = strings_of(val)
            has_rtf = any('{\\rtf' in s for s in ss)
            print(f'{name:60s} {short:40s} bin {len(val)}{" (contains RTF)" if has_rtf else ""}')
            if dump:
                open(os.path.join(dump, name + '.bin'), 'wb').write(val)
                open(os.path.join(dump, name + '.strings.txt'), 'w', encoding='utf-8').write('\n'.join(ss))
