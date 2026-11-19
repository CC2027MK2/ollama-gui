import struct, sys, os

def u16(b, o): return struct.unpack_from('<H', b, o)[0]
def u32(b, o): return struct.unpack_from('<I', b, o)[0]

def imports(path):
    d = open(path, 'rb').read()
    if d[:2] != b'MZ':
        return []
    pe = u32(d, 0x3C)
    if d[pe:pe+4] != b'PE\0\0':
        return []
    coff = pe + 4
    nsec = u16(d, coff + 2)
    optsz = u16(d, coff + 16)
    opt = coff + 20
    magic = u16(d, opt)
    dd = opt + (112 if magic == 0x20b else 96)
    imp_rva = u32(d, dd + 8)
    sec = opt + optsz
    secs = []
    for i in range(nsec):
        o = sec + i * 40
        va = u32(d, o + 12); raw = u32(d, o + 20); size = u32(d, o + 16)
        secs.append((va, size, raw))
    def off(rva):
        for va, size, raw in secs:
            if va <= rva < va + max(size, 1):
                return raw + (rva - va)
        return None
    names = []
    if not imp_rva:
        return names
    o = off(imp_rva)
    if o is None:
        return names
    while True:
        desc = d[o:o+20]
        if len(desc) < 20 or desc == b'\0' * 20:
            break
        nrva = u32(desc, 12)
        if nrva:
            no = off(nrva)
            if no:
                end = d.find(b'\0', no)
                names.append(d[no:end].decode('ascii', 'ignore'))
        o += 20
    return names

def closure(root, dirs):
    seen = set()
    todo = [root]
    missing = set()
    while todo:
        cur = todo.pop()
        if cur.lower() in seen:
            continue
        seen.add(cur.lower())
        p = None
        for dd in dirs:
            c = os.path.join(dd, cur)
            if os.path.exists(c):
                p = c; break
        if p is None:
            missing.add(cur); continue
        for n in imports(p):
            if n.lower() not in seen:
                todo.append(n)
    return seen, missing

print("== tesseract.exe ==")
seen, missing = closure('tesseract.exe', ['tesshome'])
for s in sorted(seen):
    print(" ", s)
print("missing:", sorted(missing))
