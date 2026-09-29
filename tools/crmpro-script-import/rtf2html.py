"""Minimal RichEdit RTF → HTML (bold/italic/underline, text colour, highlight, paragraphs) for
CRMPro ScriptBox text. Embedded <* ... *> code tags are returned as CODE placeholders so the
caller can map them to {{variables}}; `tags` lists their plain-text code in order."""
import re, sys

CTRL = re.compile(r"\\([a-z]+)(-?\d+)? ?|\\'([0-9a-fA-F]{2})|\\([{}\\])|\\~|([{}])|\r?\n|([^\\{}\r\n]+)")
SPECIAL = {'rquote': '\u2019', 'lquote': '\u2018', 'ldblquote': '\u201c', 'rdblquote': '\u201d',
           'emdash': '\u2014', 'endash': '\u2013', 'bullet': '\u2022', 'tab': ' '}


def convert(rtf):
    colors, out, tags = [], [], []
    stack = [dict(b=False, i=False, u=False, cf=0, hl=0, skip=False)]
    # colour table
    m = re.search(r'\{\\colortbl ?;?(.*?)\}', rtf, re.S)
    if m:
        colors = [None] + [f'#{int(r):02x}{int(g):02x}{int(b):02x}'
                           for r, g, b in re.findall(r'\\red(\d+)\\green(\d+)\\blue(\d+)', m.group(1))]
    para, text = [], []

    def st(): return stack[-1]

    def emit(s):
        if st()['skip'] or not s: return
        text.append((s, dict(st())))

    def flush_para():
        para.append(list(text)); text.clear()

    for m in CTRL.finditer(rtf):
        word, num, hexc, esc, brace, plain = m.groups()
        if brace == '{':
            stack.append(dict(st())); continue
        if brace == '}':
            if len(stack) > 1: stack.pop()
            continue
        if plain is not None: emit(plain); continue
        if esc: emit(esc); continue
        if hexc: emit(bytes([int(hexc, 16)]).decode('cp1252')); continue
        if word is None: continue
        n = int(num) if num is not None else None
        s = st()
        if word in ('fonttbl', 'colortbl', 'generator', 'stylesheet', 'info'): s['skip'] = True
        elif word == 'par': flush_para()
        elif word == 'line': emit('\n')
        elif word in SPECIAL: emit(SPECIAL[word])
        elif word == 'u' and n is not None: emit(chr(n % 65536))
        elif word == 'b': s['b'] = n != 0
        elif word == 'i': s['i'] = n != 0
        elif word == 'ul': s['u'] = True
        elif word == 'ulnone': s['u'] = False
        elif word == 'cf': s['cf'] = n or 0
        elif word == 'highlight': s['hl'] = n or 0
        elif word == 'plain': s.update(b=False, i=False, u=False, cf=0, hl=0)
    flush_para()

    html_paras = []
    in_code, code = False, []   # code tags may span paragraphs
    for p in para:
        html = []
        if in_code:
            code.append(chr(10))
        for s, f in p:
            k = 0
            while k < len(s):
                if not in_code:
                    j = s.find('<*', k)
                    seg = s[k:] if j < 0 else s[k:j]
                    if seg:
                        h = seg.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;').replace('\n', '<br>')
                        style = []
                        col = colors[f['cf']] if 0 < f['cf'] < len(colors) else None
                        if col and col not in ('#000000',): style.append(f'color: {col}')
                        hl = colors[f['hl']] if 0 < f['hl'] < len(colors) else None
                        if hl and hl not in ('#ffffff',): style.append(f'background-color: {hl}')
                        if style: h = f'<span style="{"; ".join(style)}">{h}</span>'
                        if f['u']: h = f'<u>{h}</u>'
                        if f['i']: h = f'<em>{h}</em>'
                        if f['b']: h = f'<strong>{h}</strong>'
                        html.append(h)
                    if j < 0: break
                    in_code, code, k = True, [], j + 2
                else:
                    j = s.find('*>', k)
                    if j < 0: code.append(s[k:]); break
                    code.append(s[k:j]); in_code = False; k = j + 2
                    tags.append(''.join(code).strip())
                    html.append(f'[[CODE{len(tags) - 1}]]')
        html_paras.append(''.join(html))
    body = ''.join(f'<p>{h}</p>' if h.strip() else '<p></p>' for h in html_paras)
    body = re.sub(r'(<p></p>){2,}', '<p></p>', body).strip()
    return body, tags


if __name__ == '__main__':
    for f in sys.argv[1:]:
        h, t = convert(open(f, encoding='utf-8').read())
        print('=====', f); print(h); [print(f'  CODE{i}:', x.replace('\n', ' | ')) for i, x in enumerate(t)]
